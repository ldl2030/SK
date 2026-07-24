using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// 继电器控制静态类（基于Modbus RTU协议）
    /// </summary>
    internal static class RelayController
    {
        // 全局信号量，防止多线程同时访问同一个串口
        private static readonly SemaphoreSlim _serialSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 发送继电器控制命令
        /// </summary>
        /// <param name="address">设备地址（站位号）</param>
        /// <param name="relayIndex">继电器索引（从1开始）</param>
        /// <param name="isOpen">true=开启，false=关闭</param>
        /// <param name="count">连续操作数量（默认为1）</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="comPort">串口名称（默认从 ComName.rs485ComName 读取）</param>
        /// <param name="logAction">日志回调</param>
        /// <returns>响应数据的十六进制字符串（无空格）</returns>
        public static async Task<string> SendCommandAsync(
            int address,
            int relayIndex,
            bool isOpen,
            int count = 1,
            int baudRate = 38400,
            string comPort = null,
            Action<string> logAction = null)
        {
            if (string.IsNullOrEmpty(comPort))
                comPort = ComName.rs485ComName;

            if (string.IsNullOrEmpty(comPort))
            {
                logAction?.Invoke("错误：RS485串口未配置");
                return "错误：串口未配置";
            }

            if (relayIndex <= 0)
            {
                logAction?.Invoke("错误：继电器索引必须大于0");
                return "错误：继电器索引无效";
            }

            int startAddress = relayIndex - 1;
            var responseBuilder = new StringBuilder();
            const int readTimeout = 500;

            await _serialSemaphore.WaitAsync();
            try
            {
                using (var port = new SerialPort(comPort, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.Open();

                    for (int i = 0; i < count; i++)
                    {
                        port.DiscardInBuffer();

                        int currentRelay = startAddress + i;
                        byte[] command = new byte[]
                        {
                            (byte)address,
                            0x06,
                            0x00,
                            (byte)currentRelay,
                            0x00,
                            (byte)(isOpen ? 0x01 : 0x00)
                        };
                        byte[] crc = CalculateCrc(command);
                        byte[] fullCommand = command.Concat(crc).ToArray();

                        logAction?.Invoke($"发送命令：{BytesToHex(fullCommand)}");
                        port.Write(fullCommand, 0, fullCommand.Length);
                        await Task.Delay(200);

                        int bytesToRead = port.BytesToRead;
                        if (bytesToRead == 0)
                        {
                            DateTime start = DateTime.Now;
                            while (bytesToRead == 0 && (DateTime.Now - start).TotalMilliseconds < readTimeout)
                            {
                                await Task.Delay(10);
                                bytesToRead = port.BytesToRead;
                            }
                        }

                        if (bytesToRead > 0)
                        {
                            byte[] buffer = new byte[bytesToRead];
                            port.Read(buffer, 0, bytesToRead);
                            string hexResp = BytesToHex(buffer);
                            responseBuilder.Append(hexResp);
                            logAction?.Invoke($"收到响应：{hexResp}");
                        }
                        else
                        {
                            responseBuilder.Append("TIMEOUT");
                            logAction?.Invoke($"命令 {i + 1} 超时无响应");
                        }
                    }
                }

                string result = responseBuilder.ToString();
                logAction?.Invoke($"最终响应：{result}");
                return result;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"串口通信异常：{ex.Message}");
                return $"错误：{ex.Message}";
            }
            finally
            {
                _serialSemaphore.Release();
            }
        }

        /// <summary>
        /// 发送原始 Modbus 命令（自动计算并附加 CRC），并读取响应数据
        /// </summary>
        /// <param name="commandWithoutCrc">不含 CRC16 的命令字节数组</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="comPort">串口名称（默认从 ComName.rs485ComName 读取）</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="logAction">日志回调</param>
        /// <returns>响应数据的十六进制字符串（无空格），失败返回空字符串</returns>
        public static async Task<string> SendCommandWithCrcAsync(
    byte[] command,
    int baudRate = 38400,
    string comPort = null,
    int timeoutMs = 1000,
    Action<string> logAction = null)
        {
            if (string.IsNullOrEmpty(comPort))
                comPort = ComName.rs485ComName;
            if (string.IsNullOrEmpty(comPort))
            {
                logAction?.Invoke("错误：串口未配置");
                return string.Empty;
            }
            if (command == null || command.Length < 4)
            {
                logAction?.Invoke("错误：命令至少需要4个字节");
                return string.Empty;
            }

            byte[] fullCommand;
            bool hasCrc = HasValidCrc(command);
            if (hasCrc)
            {
                fullCommand = command;
                logAction?.Invoke("检测到命令已包含有效 CRC，直接发送");
            }
            else
            {
                byte[] crc = CalculateCrc(command);
                fullCommand = new byte[command.Length + crc.Length];
                Buffer.BlockCopy(command, 0, fullCommand, 0, command.Length);
                Buffer.BlockCopy(crc, 0, fullCommand, command.Length, crc.Length);
                logAction?.Invoke("命令不含 CRC，自动附加 CRC 后发送");
            }

            await _serialSemaphore.WaitAsync();
            try
            {
                using (var port = new SerialPort(comPort, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.Open();
                    port.DiscardInBuffer();
                    port.Write(fullCommand, 0, fullCommand.Length);
                    logAction?.Invoke($"发送命令: {BytesToHex(fullCommand)}");

                    DateTime startTime = DateTime.Now;
                    while (port.BytesToRead == 0 && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                        await Task.Delay(10);

                    if (port.BytesToRead == 0)
                    {
                        logAction?.Invoke("未收到响应，超时");
                        return string.Empty;
                    }

                    byte[] buffer = new byte[port.BytesToRead];
                    port.Read(buffer, 0, buffer.Length);
                    string hexResponse = BytesToHex(buffer);
                    logAction?.Invoke($"收到响应: {hexResponse}");
                    return hexResponse;
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"串口通信异常: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                _serialSemaphore.Release();
            }
        }

        /// <summary>
        /// 判断命令字节数组是否已包含有效的 CRC16（最后两字节）
        /// </summary>
        private static bool HasValidCrc(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            int crcStart = data.Length - 2;
            byte[] receivedCrc = new byte[2] { data[crcStart], data[crcStart + 1] };
            byte[] dataWithoutCrc = new byte[crcStart];
            Buffer.BlockCopy(data, 0, dataWithoutCrc, 0, crcStart);
            byte[] calculatedCrc = CalculateCrc(dataWithoutCrc);
            return receivedCrc[0] == calculatedCrc[0] && receivedCrc[1] == calculatedCrc[1];
        }
        /// <summary>
        /// 构建完整的命令（原命令 + CRC16）
        /// </summary>
        public static byte[] BuildCommandWithCrc(byte[] commandWithoutCrc)
        {
            if (commandWithoutCrc == null || commandWithoutCrc.Length == 0)
                return new byte[0];
            byte[] crc = CalculateCrc(commandWithoutCrc);
            byte[] full = new byte[commandWithoutCrc.Length + crc.Length];
            Buffer.BlockCopy(commandWithoutCrc, 0, full, 0, commandWithoutCrc.Length);
            Buffer.BlockCopy(crc, 0, full, commandWithoutCrc.Length, crc.Length);
            return full;
        }

        public enum Endianness
        {
            LittleEndian,
            BigEndian
        }
        /// <summary>
        /// 发送命令读取电阻值，返回8组浮点数（每组4字节，IEEE 754单精度），
        /// 并对有效值除以100（开路值也除以100）。
        /// </summary>
        public static async Task<List<float>> ReadResistanceValuesAsync(
    string portName,
    int baudRate,
    byte[] command,
    Action<string> logAction = null,
    int timeoutMs = 1000)
        {
            List<float> result = new List<float>();
            if (string.IsNullOrEmpty(portName))
            {
                logAction?.Invoke("串口号无效");
                return result;
            }
            if (command == null || command.Length == 0)
            {
                logAction?.Invoke("命令不能为空");
                return result;
            }

            using (SerialPort serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
            {
                try
                {
                    serialPort.ReadTimeout = timeoutMs;
                    serialPort.WriteTimeout = timeoutMs;
                    serialPort.Open();

                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();

                    serialPort.Write(command, 0, command.Length);
                    logAction?.Invoke($"发送命令: {BitConverter.ToString(command).Replace("-", " ")}");

                    // 期望响应: 地址(1) + 功能码(1) + 数据长度(1) + 数据(32) + CRC(2) = 37字节
                    int expectedBytes = 37;
                    byte[] buffer = new byte[expectedBytes];
                    int totalRead = 0;
                    DateTime startTime = DateTime.Now;

                    while (totalRead < expectedBytes && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                    {
                        if (serialPort.BytesToRead > 0)
                        {
                            int bytesToRead = Math.Min(serialPort.BytesToRead, expectedBytes - totalRead);
                            int read = serialPort.Read(buffer, totalRead, bytesToRead);
                            totalRead += read;
                        }
                        else
                        {
                            await Task.Delay(10);
                        }
                    }

                    if (totalRead < expectedBytes)
                    {
                        logAction?.Invoke($"读取超时，仅收到 {totalRead} 字节，期望 {expectedBytes}");
                        return result;
                    }

                    string hexResponse = BitConverter.ToString(buffer, 0, totalRead).Replace("-", " ");
                    logAction?.Invoke($"收到响应: {hexResponse}");

                    // 跳过前3字节：地址、功能码、数据长度
                    int dataStart = 3;
                    for (int i = 0; i < 8; i++)
                    {
                        int offset = dataStart + i * 4;
                        byte[] intBytes = new byte[4];
                        Array.Copy(buffer, offset, intBytes, 0, 4);

                        // 检查是否为开路（全0xFF）
                        bool isOpen = true;
                        foreach (byte b in intBytes)
                        {
                            if (b != 0xFF) { isOpen = false; break; }
                        }

                        if (isOpen)
                        {
                            // 开路时返回一个很大的值，您也可自定义
                            result.Add(42949670f);
                            logAction?.Invoke($"电阻组 {i + 1}: 开路 → 42949670.000 Ω");
                        }
                        else
                        {
                            // 大端转小端（因为BitConverter.ToInt32使用小端）
                            if (BitConverter.IsLittleEndian)
                                Array.Reverse(intBytes);

                            int rawInt = BitConverter.ToInt32(intBytes, 0);
                            float ohms = rawInt / 100f;
                            result.Add(ohms);
                            logAction?.Invoke($"电阻组 {i + 1}: 原始值 {rawInt} → 除以100后 {ohms:F3} Ω");
                        }
                    }
                    return result;
                }
                catch (TimeoutException ex)
                {
                    logAction?.Invoke($"串口操作超时: {ex.Message}");
                    return result;
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"串口错误: {ex.Message}");
                    return result;
                }
            }
        }
        /// <summary>
        /// 计算 Modbus CRC16（大端序，低字节在前）
        /// </summary>
        private static byte[] CalculateCrc(byte[] data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return new byte[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) };
        }

        /// <summary>
        /// 将字节数组转换为连续的十六进制字符串（大写，无分隔符）
        /// </summary>
        private static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        /// <summary>
        /// 通用 Modbus 读取寄存器方法（自动计算 CRC，解析返回数据）
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="commandWithoutCrc">不含 CRC 的命令字节数组（例如 {0x02, 0x04, 0x00, 0x00, 0x00, 0x10}）</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="logAction">日志回调</param>
        /// <returns>解析后的浮点数列表（单位：伏特），失败返回空列表</returns>
        public static async Task<List<double>> ReadModbusRegistersAsync(
            string portName,
            byte[] commandWithoutCrc,
            int baudRate = 9600,
            int timeoutMs = 2000,
            Action<string> logAction = null)
        {
            List<double> result = new List<double>();
            if (string.IsNullOrEmpty(portName))
            {
                logAction?.Invoke("串口号无效");
                return result;
            }
            if (commandWithoutCrc == null || commandWithoutCrc.Length < 6)
            {
                logAction?.Invoke("命令长度不足（至少需要6字节）");
                return result;
            }

            // 解析命令中的寄存器数量（功能码 0x04 读取输入寄存器，第5字节为数量高字节，第6字节为低字节）
            int quantity = (commandWithoutCrc[4] << 8) | commandWithoutCrc[5];
            int expectedDataBytes = quantity * 2; // 每个寄存器2字节

            // 计算 CRC 并组装完整命令
            byte[] crc = CalculateCrc(commandWithoutCrc);
            byte[] fullCommand = new byte[commandWithoutCrc.Length + crc.Length];
            Buffer.BlockCopy(commandWithoutCrc, 0, fullCommand, 0, commandWithoutCrc.Length);
            Buffer.BlockCopy(crc, 0, fullCommand, commandWithoutCrc.Length, crc.Length);

            logAction?.Invoke($"发送命令: {BytesToHex(fullCommand)}");

            using (SerialPort port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
            {
                try
                {
                    port.ReadTimeout = timeoutMs;
                    port.WriteTimeout = timeoutMs;
                    port.Open();
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();

                    port.Write(fullCommand, 0, fullCommand.Length);

                    // 预期响应长度：地址(1) + 功能码(1) + 字节数(1) + 数据字节 + CRC(2)
                    int expectedBytes = 1 + 1 + 1 + expectedDataBytes + 2;
                    byte[] buffer = new byte[expectedBytes];
                    int totalRead = 0;
                    DateTime startTime = DateTime.Now;

                    while (totalRead < expectedBytes && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                    {
                        if (port.BytesToRead > 0)
                        {
                            int bytesToRead = Math.Min(port.BytesToRead, expectedBytes - totalRead);
                            int read = port.Read(buffer, totalRead, bytesToRead);
                            totalRead += read;
                        }
                        else
                        {
                            await Task.Delay(10);
                        }
                    }

                    if (totalRead < expectedBytes)
                    {
                        logAction?.Invoke($"响应不足，收到 {totalRead} 字节，期望 {expectedBytes}");
                        return result;
                    }

                    logAction?.Invoke($"收到响应: {BytesToHex(buffer, totalRead)}");

                    // 验证地址和功能码
                    if (buffer[0] != commandWithoutCrc[0] || buffer[1] != commandWithoutCrc[1])
                    {
                        logAction?.Invoke($"响应地址/功能码错误: 地址={buffer[0]}, 功能码={buffer[1]}");
                        return result;
                    }

                    // 验证数据字节数
                    int dataBytes = buffer[2];
                    if (dataBytes != expectedDataBytes)
                    {
                        logAction?.Invoke($"数据字节数异常: {dataBytes}，期望 {expectedDataBytes}");
                        return result;
                    }

                    // 验证 CRC
                    byte[] receivedData = new byte[totalRead - 2];
                    Buffer.BlockCopy(buffer, 0, receivedData, 0, totalRead - 2);
                    byte[] receivedCrc = new byte[] { buffer[totalRead - 2], buffer[totalRead - 1] };
                    byte[] expectedCrc = CalculateCrc(receivedData);
                    if (receivedCrc[0] != expectedCrc[0] || receivedCrc[1] != expectedCrc[1])
                    {
                        logAction?.Invoke($"CRC 校验失败");
                        return result;
                    }

                    // 解析数据（每个寄存器2字节，大端序，假设单位是毫伏，转换为伏特）
                    for (int i = 0; i < quantity; i++)
                    {
                        int idx = 3 + i * 2; // 跳过地址、功能码、字节数
                        ushort rawValue = (ushort)((buffer[idx] << 8) | buffer[idx + 1]);
                        double voltage = rawValue / 1000.0; // 转换为伏特
                        result.Add(voltage);
                        logAction?.Invoke($"通道 {i + 1}: 原始值 {rawValue} -> {voltage:F3} V");
                    }

                    return result;
                }
                catch (TimeoutException)
                {
                    logAction?.Invoke("读取超时");
                    return result;
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"串口错误: {ex.Message}");
                    return result;
                }
            }
        }


        // 辅助方法：字节数组转十六进制字符串
        private static string BytesToHex(byte[] bytes, int length = -1)
        {
            if (bytes == null || bytes.Length == 0) return "";
            int len = length > 0 ? length : bytes.Length;
            return BitConverter.ToString(bytes, 0, len).Replace("-", " ");
        }
        /// <summary>
        /// 读取全部8个通道的电压值（发送十六进制命令，解析冒号后的数字）
        /// </summary>
        /// <param name="portName">串口名称</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="hexCommand">十六进制命令（如 "DA DB DC DC 02 CC"）</param>
        /// <param name="logAction">日志回调</param>
        /// <param name="timeoutMs">超时毫秒</param>
        /// <param name="multiplyFactor">乘系数，默认1.0（不乘）。如需校准可传11.09等</param>
        /// <returns>8个电压值，失败返回null</returns>
        public static async Task<double[]> ReadAllVoltagesAsync(
            string portName,
            int baudRate,
            string hexCommand,
            Action<string> logAction = null,
            int timeoutMs = 2000,
            double multiplyFactor = 1.0)  // 默认为1，不乘系数
        {
            if (string.IsNullOrEmpty(portName))
            {
                logAction?.Invoke("串口未配置");
                return null;
            }

            byte[] commandBytes = HexStringToByteArray(hexCommand);
            if (commandBytes == null || commandBytes.Length == 0)
            {
                logAction?.Invoke($"无效的命令: {hexCommand}");
                return null;
            }

            using (SerialPort port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
            {
                try
                {
                    port.ReadTimeout = timeoutMs;
                    port.WriteTimeout = 1000;
                    port.NewLine = "\r\n";
                    port.Open();
                    port.DiscardInBuffer();
                    port.Write(commandBytes, 0, commandBytes.Length);
                    logAction?.Invoke($"发送命令(hex): {hexCommand}");

                    string response = await Task.Run(() => port.ReadLine()).ConfigureAwait(false);
                    logAction?.Invoke($"原始响应: {response}");

                    // 提取冒号后的数据部分
                    string dataPart = ExtractDataPart(response, logAction);
                    if (string.IsNullOrEmpty(dataPart))
                    {
                        logAction?.Invoke("未能提取数据部分");
                        return null;
                    }

                    string[] parts = dataPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    List<double> voltages = new List<double>();
                    foreach (var part in parts)
                    {
                        if (double.TryParse(part, out double val))
                        {
                            voltages.Add(val * multiplyFactor);  // 乘以系数
                        }
                        else
                        {
                            logAction?.Invoke($"解析数值失败: {part}");
                        }
                    }

                    if (voltages.Count < 8)
                    {
                        logAction?.Invoke($"电压数据不足8个，实际{voltages.Count}");
                        return null;
                    }

                    return voltages.Take(8).ToArray();
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"读取电压失败: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// 读取指定索引的电压值（便捷方法）
        /// </summary>
        public static async Task<double?> ReadVoltageValueAsync(
            string portName,
            int baudRate,
            string hexCommand,
            int valueIndex,
            Action<string> logAction = null,
            int timeoutMs = 2000,
            double multiplyFactor = 1.0)
        {
            var all = await ReadAllVoltagesAsync(portName, baudRate, hexCommand, logAction, timeoutMs, multiplyFactor);
            if (all == null || valueIndex < 0 || valueIndex >= all.Length)
                return null;
            return all[valueIndex];
        }
        // 辅助：提取冒号后的数字部分（完全复制您已验证的方法）
        private static string ExtractDataPart(string response, Action<string> logAction)
        {
            // 匹配冒号后的所有数字和空格（直到行尾）
            var match = System.Text.RegularExpressions.Regex.Match(response, @"(?<=:\s*)([\d\s]+)$");
            if (match.Success)
                return match.Value.Trim();

            // 如果没有冒号，尝试直接取所有数字序列
            match = System.Text.RegularExpressions.Regex.Match(response, @"(\d+(?:\s+\d+)+)");
            if (match.Success)
                return match.Value.Trim();

            logAction?.Invoke("无法从响应中提取数据部分");
            return null;
        }



        // 辅助方法：十六进制字符串转字节数组
        private static byte[] HexStringToByteArray(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            if (hex.Length % 2 != 0) return null;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
    }
}