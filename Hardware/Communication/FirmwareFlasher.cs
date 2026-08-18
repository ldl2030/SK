using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// STM32 ROM UART bootloader programmer used by the independent 3.3 V TTL port.
    /// Protocol: 115200, 8E1, no flow control (AN3155).
    /// </summary>
    public class FirmwareFlasher : IDisposable, IDeviceLogSource
    {
        private const byte Ack = 0x79;
        private const byte Nack = 0x1F;
        private const uint Stm32FlashBase = 0x08000000;
        private const uint Stm32F103MediumFlashEnd = 0x0801FFFF;
        private const int PageSize = 1024;
        private const int BlockSize = 256;
        private SerialPort _serialPort;
        private readonly Queue<byte> _receivedBytes = new Queue<byte>();

        public event Action<string> LogInfo;
        public event Action<string> LogError;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
        public string PortName => IsConnected ? _serialPort.PortName : null;
        public string LastError { get; private set; }

        public void Connect(string portName, int baudRate = 115200)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("[FirmwareFlasher] COM口号不能为空。", nameof(portName));

            Disconnect();
            _serialPort = new SerialPort(
                portName.Trim(),
                baudRate,
                Parity.Even,
                8,
                StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = 2000,
                WriteTimeout = 2000
            };
            try
            {
                _serialPort.Open();
                Thread.Sleep(150);
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _receivedBytes.Clear();
                LogInfo?.Invoke($"STM32 TTL已连接：{portName}，{baudRate} 8E1，无流控");
            }
            catch
            {
                Disconnect();
                throw;
            }
        }

        public void Disconnect()
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Close();
            _serialPort?.Dispose();
            _serialPort = null;
            _receivedBytes.Clear();
        }

        public Task<bool> FlashFirmwareAsync(string firmwarePath, string comPort)
        {
            return FlashFirmwareAsync(firmwarePath, comPort, CancellationToken.None);
        }

        public async Task<bool> ProbeBootloaderAsync(
            string comPort,
            CancellationToken cancellationToken)
        {
            try
            {
                LastError = null;
                if (!IsConnected || !string.Equals(
                    PortName,
                    comPort,
                    StringComparison.OrdinalIgnoreCase))
                {
                    Connect(comPort);
                }

                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    _receivedBytes.Clear();
                    LogInfo?.Invoke("STM32同步Bootloader：TX 0x7F");
                    SynchronizeBootloader();
                    byte[] commands = GetSupportedCommands();
                    ushort chipId = GetChipId();
                    LogInfo?.Invoke(
                        $"STM32 Bootloader检测成功：Chip ID=0x{chipId:X3}，" +
                        $"支持命令={string.Join(" ", commands.Select(x => x.ToString("X2")))}");
                    if (chipId != 0x0410)
                        throw new InvalidOperationException(
                            $"芯片ID不匹配：期望0x410，实际0x{chipId:X3}。");
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LogError?.Invoke("STM32 Bootloader检测失败：" + ex.Message);
                return false;
            }
        }

        public async Task<bool> FlashFirmwareAsync(
            string firmwarePath,
            string comPort,
            CancellationToken cancellationToken)
        {
            try
            {
                LastError = null;
                if (!File.Exists(firmwarePath))
                    throw new FileNotFoundException("固件文件不存在。", firmwarePath);

                Stm32FirmwareImage image = Stm32IntelHexParser.Parse(
                    firmwarePath,
                    Stm32FlashBase,
                    Stm32F103MediumFlashEnd);
                LogInfo?.Invoke(
                    $"HEX解析完成：{Path.GetFileName(firmwarePath)}，" +
                    $"地址=0x{image.StartAddress:X8}-0x{image.EndAddress:X8}，" +
                    $"数据={image.Data.Length} bytes");
                if (!image.HasEndOfFileRecord)
                    LogInfo?.Invoke("HEX未包含EOF记录：已按有效Flash地址完成解析，不修改原文件。");

                if (!IsConnected || !string.Equals(
                    PortName,
                    comPort,
                    StringComparison.OrdinalIgnoreCase))
                {
                    Connect(comPort);
                }

                return await Task.Run(
                    () => FlashCore(image, cancellationToken),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                LastError = "STM32烧录已取消。";
                LogError?.Invoke("STM32烧录已取消。");
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LogError?.Invoke("STM32烧录失败：" + ex.Message);
                return false;
            }
        }

        private bool FlashCore(Stm32FirmwareImage image, CancellationToken token)
        {
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
            _receivedBytes.Clear();

            LogInfo?.Invoke("STM32同步Bootloader：TX 0x7F");
            SynchronizeBootloader();

            byte[] commands = GetSupportedCommands();
            ushort chipId = GetChipId();
            LogInfo?.Invoke(
                $"STM32 Bootloader连接成功：Chip ID=0x{chipId:X3}，" +
                $"支持命令={string.Join(" ", commands.Select(x => x.ToString("X2")))}");
            if (chipId != 0x0410)
                throw new InvalidOperationException(
                    $"芯片ID不匹配：期望STM32F101/F102/F103 Medium-density 0x410，实际0x{chipId:X3}。");

            int firstPage = checked((int)((image.StartAddress - Stm32FlashBase) / PageSize));
            int lastPage = checked((int)((image.EndAddress - Stm32FlashBase) / PageSize));
            int[] pages = Enumerable.Range(firstPage, lastPage - firstPage + 1).ToArray();
            ErasePages(commands, pages, token);

            LogInfo?.Invoke($"STM32开始写入：{image.Data.Length} bytes，块大小={BlockSize}");
            for (int offset = 0; offset < image.Data.Length; offset += BlockSize)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(BlockSize, image.Data.Length - offset);
                byte[] block = new byte[count];
                Buffer.BlockCopy(image.Data, offset, block, 0, count);
                WriteMemory(image.StartAddress + (uint)offset, block);
                LogProgress("写入", offset + count, image.Data.Length);
            }

            LogInfo?.Invoke("STM32开始逐块回读校验...");
            for (int offset = 0; offset < image.Data.Length; offset += BlockSize)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(BlockSize, image.Data.Length - offset);
                byte[] actual = ReadMemory(image.StartAddress + (uint)offset, count);
                for (int i = 0; i < count; i++)
                {
                    byte expected = image.Data[offset + i];
                    if (actual[i] != expected)
                    {
                        uint address = image.StartAddress + (uint)offset + (uint)i;
                        throw new InvalidOperationException(
                            $"回读校验失败：地址0x{address:X8}，期望0x{expected:X2}，实际0x{actual[i]:X2}。");
                    }
                }
                LogProgress("校验", offset + count, image.Data.Length);
            }

            LogInfo?.Invoke("STM32固件写入及回读校验成功。");
            return true;
        }

        private byte[] GetSupportedCommands()
        {
            SendCommand(0x00, "GET");
            int responseLength = ReadByteWithTimeout(2000) + 1;
            byte[] response = ReadExact(responseLength, 2000);
            ExpectAck("GET结束", 2000);
            if (response.Length < 2)
                throw new InvalidOperationException("GET返回长度不足。");
            int bootloaderVersion = response[0];
            byte[] commands = response.Skip(1).ToArray();
            LogInfo?.Invoke($"STM32 Bootloader协议版本：0x{bootloaderVersion:X2}");
            return commands;
        }

        private void SynchronizeBootloader()
        {
            WriteByte(0x7F);
            int response = ReadByteWithTimeout(2500);
            if (response == Ack)
                return;
            if (response == Nack)
            {
                LogInfo?.Invoke(
                    "STM32对0x7F返回NACK：Bootloader可能已在本次上电中同步，继续发送GET确认。");
                return;
            }
            throw new InvalidOperationException(
                $"Bootloader同步响应异常：期望ACK/NACK，实际0x{response:X2}。");
        }

        private ushort GetChipId()
        {
            SendCommand(0x02, "GET ID");
            int count = ReadByteWithTimeout(2000) + 1;
            byte[] id = ReadExact(count, 2000);
            ExpectAck("GET ID结束", 2000);
            if (id.Length < 2)
                throw new InvalidOperationException("GET ID返回长度不足。");
            return (ushort)((id[id.Length - 2] << 8) | id[id.Length - 1]);
        }

        private void ErasePages(byte[] commands, int[] pages, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            LogInfo?.Invoke(
                $"STM32擦除固件覆盖页：{pages.First()}-{pages.Last()}，共{pages.Length}页");

            if (commands.Contains((byte)0x44))
            {
                SendCommand(0x44, "EXTENDED ERASE");
                var payload = new List<byte>
                {
                    (byte)((pages.Length - 1) >> 8),
                    (byte)(pages.Length - 1)
                };
                foreach (int page in pages)
                {
                    payload.Add((byte)(page >> 8));
                    payload.Add((byte)page);
                }
                SendChecksummed(payload.ToArray());
                ExpectAck("扩展页擦除", 30000);
                return;
            }

            if (!commands.Contains((byte)0x43))
                throw new InvalidOperationException("Bootloader不支持标准或扩展擦除命令。");
            if (pages.Any(page => page > 255) || pages.Length > 256)
                throw new InvalidOperationException("标准擦除命令的页号超出范围。");

            SendCommand(0x43, "ERASE");
            var standardPayload = new List<byte> { (byte)(pages.Length - 1) };
            standardPayload.AddRange(pages.Select(page => (byte)page));
            SendChecksummed(standardPayload.ToArray());
            ExpectAck("页擦除", 30000);
        }

        private void WriteMemory(uint address, byte[] data)
        {
            if (data == null || data.Length < 1 || data.Length > 256)
                throw new ArgumentOutOfRangeException(nameof(data));
            SendCommand(0x31, "WRITE MEMORY");
            SendAddress(address, "WRITE MEMORY地址");

            byte[] payload = new byte[data.Length + 1];
            payload[0] = (byte)(data.Length - 1);
            Buffer.BlockCopy(data, 0, payload, 1, data.Length);
            SendChecksummed(payload);
            ExpectAck($"写入0x{address:X8}", 5000);
        }

        private byte[] ReadMemory(uint address, int count)
        {
            if (count < 1 || count > 256)
                throw new ArgumentOutOfRangeException(nameof(count));
            SendCommand(0x11, "READ MEMORY");
            SendAddress(address, "READ MEMORY地址");
            byte length = (byte)(count - 1);
            WriteBytes(new[] { length, (byte)(length ^ 0xFF) });
            ExpectAck($"读取0x{address:X8}", 2000);
            return ReadExact(count, 3000);
        }

        private void SendCommand(byte command, string operation)
        {
            WriteBytes(new[] { command, (byte)(command ^ 0xFF) });
            ExpectAck(operation, 2000);
        }

        private void SendAddress(uint address, string operation)
        {
            byte[] bytes =
            {
                (byte)(address >> 24),
                (byte)(address >> 16),
                (byte)(address >> 8),
                (byte)address
            };
            SendChecksummed(bytes);
            ExpectAck(operation, 2000);
        }

        private void SendChecksummed(byte[] payload)
        {
            byte checksum = 0;
            foreach (byte value in payload)
                checksum ^= value;
            WriteBytes(payload.Concat(new[] { checksum }).ToArray());
        }

        private void ExpectAck(string operation, int timeoutMs)
        {
            int value = ReadByteWithTimeout(timeoutMs);
            if (value == Ack)
                return;
            if (value == Nack)
                throw new InvalidOperationException(operation + "收到NACK(0x1F)。");
            throw new InvalidOperationException(
                $"{operation}响应异常：期望ACK(0x79)，实际0x{value:X2}。");
        }

        private int ReadByteWithTimeout(int timeoutMs)
        {
            if (_receivedBytes.Count > 0)
                return _receivedBytes.Dequeue();
            int previous = _serialPort.ReadTimeout;
            try
            {
                _serialPort.ReadTimeout = timeoutMs;
                return _serialPort.ReadByte();
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"等待STM32响应超时（{timeoutMs} ms）。");
            }
            finally
            {
                _serialPort.ReadTimeout = previous;
            }
        }

        private byte[] ReadExact(int count, int timeoutMs)
        {
            byte[] result = new byte[count];
            for (int index = 0; index < count; index++)
                result[index] = (byte)ReadByteWithTimeout(timeoutMs);
            return result;
        }

        private void WriteByte(byte value)
        {
            WriteBytes(new[] { value });
        }

        private void WriteBytes(byte[] bytes)
        {
            _serialPort.Write(bytes, 0, bytes.Length);
            ConsumeOptionalEcho(bytes);
        }

        private void ConsumeOptionalEcho(byte[] transmitted)
        {
            if (transmitted == null || transmitted.Length == 0)
                return;

            int previousTimeout = _serialPort.ReadTimeout;
            var observed = new List<byte>();
            try
            {
                _serialPort.ReadTimeout = 25;
                for (int i = 0; i < transmitted.Length; i++)
                {
                    int value;
                    try
                    {
                        value = _serialPort.ReadByte();
                    }
                    catch (TimeoutException)
                    {
                        break;
                    }
                    observed.Add((byte)value);
                    if (observed[i] != transmitted[i])
                        break;
                }
            }
            finally
            {
                _serialPort.ReadTimeout = previousTimeout;
            }

            bool completeEcho = observed.Count == transmitted.Length &&
                observed.SequenceEqual(transmitted);
            if (completeEcho)
                return;
            foreach (byte value in observed)
                _receivedBytes.Enqueue(value);
        }

        private void LogProgress(string phase, int completed, int total)
        {
            if (completed == total || completed % 8192 == 0)
            {
                int percent = total == 0 ? 100 : completed * 100 / total;
                LogInfo?.Invoke($"STM32 {phase}进度：{completed}/{total} bytes ({percent}%)");
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    public sealed class Stm32FirmwareImage
    {
        public uint StartAddress { get; internal set; }
        public uint EndAddress { get; internal set; }
        public byte[] Data { get; internal set; }
        public bool HasEndOfFileRecord { get; internal set; }
    }

    public static class Stm32IntelHexParser
    {
        public static Stm32FirmwareImage Parse(
            string path,
            uint allowedStart,
            uint allowedEnd)
        {
            var memory = new SortedDictionary<uint, byte>();
            uint addressBase = 0;
            bool flashBaseSeen = false;
            bool eof = false;
            int lineNumber = 0;

            foreach (string rawLine in File.ReadLines(path))
            {
                lineNumber++;
                string line = (rawLine ?? string.Empty).Trim();
                if (line.Length == 0)
                    continue;
                byte[] record = DecodeRecord(line, lineNumber);
                int count = record[0];
                int address = (record[1] << 8) | record[2];
                int type = record[3];

                if (type == 0x00)
                {
                    uint absolute = addressBase + (uint)address;
                    if (absolute < allowedStart && flashBaseSeen)
                        absolute += allowedStart;
                    uint end = absolute + (uint)count - 1;
                    if (absolute < allowedStart || end > allowedEnd)
                        throw new InvalidDataException(
                            $"HEX第{lineNumber}行地址越界：0x{absolute:X8}-0x{end:X8}。");
                    for (int i = 0; i < count; i++)
                        memory[absolute + (uint)i] = record[4 + i];
                }
                else if (type == 0x01)
                {
                    eof = true;
                    break;
                }
                else if (type == 0x02)
                {
                    uint segment = (uint)((record[4] << 8) | record[5]) << 4;
                    // Legacy station HEX files may emit a redundant zero segment
                    // immediately after selecting 0x08000000. Preserve Flash base.
                    if (!(segment == 0 && flashBaseSeen))
                        addressBase = segment;
                }
                else if (type == 0x04)
                {
                    addressBase = (uint)((record[4] << 8) | record[5]) << 16;
                    if (addressBase == allowedStart)
                        flashBaseSeen = true;
                }
            }

            if (memory.Count == 0)
                throw new InvalidDataException("HEX文件没有可烧录的数据记录。");
            uint start = memory.Keys.First();
            uint endAddress = memory.Keys.Last();
            byte[] data = Enumerable.Repeat(
                (byte)0xFF,
                checked((int)(endAddress - start + 1))).ToArray();
            foreach (KeyValuePair<uint, byte> item in memory)
                data[checked((int)(item.Key - start))] = item.Value;

            return new Stm32FirmwareImage
            {
                StartAddress = start,
                EndAddress = endAddress,
                Data = data,
                HasEndOfFileRecord = eof
            };
        }

        private static byte[] DecodeRecord(string line, int lineNumber)
        {
            if (!line.StartsWith(":", StringComparison.Ordinal) ||
                ((line.Length - 1) % 2) != 0)
                throw new InvalidDataException($"HEX第{lineNumber}行格式错误。");
            byte[] bytes = new byte[(line.Length - 1) / 2];
            try
            {
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = byte.Parse(
                        line.Substring(1 + (i * 2), 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException($"HEX第{lineNumber}行包含非法字符。", ex);
            }
            if (bytes.Length < 5 || bytes[0] + 5 != bytes.Length)
                throw new InvalidDataException($"HEX第{lineNumber}行长度不匹配。");
            byte checksum = 0;
            foreach (byte value in bytes)
                checksum += value;
            if (checksum != 0)
                throw new InvalidDataException($"HEX第{lineNumber}行校验和错误。");
            return bytes;
        }
    }
}
