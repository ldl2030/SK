using System;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// 数字量输入控制器 (基于中盛数字量输入模块)
    /// </summary>
    public static class DigitalInputController
    {
        /// <summary>
        /// 读取中盛数字量输入模块状态 (1~16路)
        /// 通过 0x04 功能码读取 0x0032 寄存器，按位返回 16 个通道状态。
        /// </summary>
        /// <param name="address">设备站号</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="comPort">串口号</param>
        /// <returns>包含 16 个布尔值的数组，索引 0 代表通道 1。如果通信失败返回 null</returns>
        public static async Task<bool[]> ReadDigitalInputAsync(
            int address,
            int baudRate = 38400,
            string comPort = null,
            Action<string> logAction = null)
        {
            if (string.IsNullOrEmpty(comPort))
                comPort = ComName.rs485ComName;

            byte[] command = new byte[]
            {
                (byte)address,
                0x04,
                0x00,
                0x32,
                0x00,
                0x01
            };

            string responseHex = string.Empty;
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                responseHex = await RelayController.SendCommandWithCrcAsync(
                    command,
                    baudRate,
                    comPort,
                    1000,
                    logAction);
                if (!string.IsNullOrEmpty(responseHex) && responseHex.Length == 14)
                    break;

                if (attempt < maxAttempts)
                {
                    logAction?.Invoke($"数字量读取第{attempt}次失败，准备重试。");
                    await Task.Delay(50);
                }
            }

            if (string.IsNullOrEmpty(responseHex) || responseHex.Length != 14)
            {
                logAction?.Invoke("读取数字量输入失败：无响应或数据长度不足");
                return null;
            }

            try
            {
                string expectedPrefix = ((byte)address).ToString("X2") + "0402";
                if (!responseHex.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    logAction?.Invoke($"数字量响应头错误：{responseHex}");
                    return null;
                }

                // 解析返回的 16 位状态。数据部分在第 3、4 字节（索引 6 开始的 4 个字符）
                string dataHex = responseHex.Substring(6, 4);
                ushort states = Convert.ToUInt16(dataHex, 16);
                
                bool[] results = new bool[16];
                for (int i = 0; i < 16; i++)
                {
                    results[i] = (states & (1 << i)) != 0;
                }
                
                logAction?.Invoke($"成功读取16路输入原始电平(DI16→DI1): {Convert.ToString(states, 2).PadLeft(16, '0')}");
                return results;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"解析数字量输入异常：{ex.Message}");
                return null;
            }
        }
    }
}
