using System;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Drawing.Printing;

namespace TestPlatform
{
    public static class ZplPrinterHelper
    {
        #region 公共打印方法

        /// <summary>
        /// 打印ZPL到默认打印机（异步，WPF推荐使用）
        /// </summary>
        public static async Task<bool> PrintZplAsync(string zplData, Action<string> logAction = null, CancellationToken cancellationToken = default)
        {
            return await PrintZplToPrinterAsync(zplData, logAction, cancellationToken);
        }

        /// <summary>
        /// 打印ZPL到默认打印机
        /// </summary>
        public static async Task<bool> PrintZplToPrinterAsync(string zplData, Action<string> logAction = null, CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. 获取默认打印机名称
                string printerName = GetDefaultPrinterSimple(logAction);
                if (string.IsNullOrEmpty(printerName))
                {
                    logAction?.Invoke("错误: 未找到默认打印机");
                    MessageBox.Show("未找到默认打印机，请检查打印机设置！", "打印错误",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                logAction?.Invoke($"使用打印机: {printerName}");

                // 2. 检查打印机硬件状态（关键改进）
                bool isPrinterReady = await CheckPrinterHardwareStatusAsync(printerName, logAction);
                if (!isPrinterReady)
                {
                    logAction?.Invoke("打印机硬件未就绪，无法打印");
                    MessageBox.Show($"打印机 '{printerName}' 未就绪（离线/缺纸/卡纸等），请检查后重试。",
                                  "打印机错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                logAction?.Invoke("打印机硬件状态正常，开始发送数据...");

                // 3. 发送ZPL到打印机（使用原有Win32 API）
                bool success = await SendZplToPrinterAsync(printerName, zplData, logAction, cancellationToken);

                if (success)
                {
                    logAction?.Invoke("ZPL数据发送成功");
                    return true;
                }
                else
                {
                    logAction?.Invoke("ZPL数据发送失败");
                    if (printerName.ToUpper().Contains("ZEBRA"))
                    {
                        MessageBox.Show("ZPL打印失败！\n\n建议：\n1. 确认打印机已连接\n2. 检查打印机驱动是否正确安装\n3. 尝试重启打印服务",
                                      "Zebra打印机错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"打印错误: {ex.Message}");
                MessageBox.Show($"打印过程中发生错误：{ex.Message}", "打印错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        #endregion

        #region 辅助方法

        private static string GetDefaultPrinterSimple(Action<string> logAction = null)
        {
            try
            {
                var settings = new PrinterSettings();
                return !string.IsNullOrEmpty(settings.PrinterName) ? settings.PrinterName : string.Empty;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"获取默认打印机失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 检查打印机硬件状态（是否在线、无错误、无缺纸等）
        /// </summary>
        private static async Task<bool> CheckPrinterHardwareStatusAsync(string printerName, Action<string> logAction = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var server = new LocalPrintServer())
                    {
                        var printQueue = server.GetPrintQueue(printerName);
                        if (printQueue == null)
                        {
                            logAction?.Invoke($"无法获取打印机队列: {printerName}");
                            return false;
                        }

                        // 刷新队列状态
                        printQueue.Refresh();
                        var status = printQueue.QueueStatus;

                        // 详细状态检查
                        bool isOffline = printQueue.IsOffline;
                        bool isPaused = printQueue.IsPaused;
                        bool isOutOfPaper = printQueue.IsOutOfPaper;
                        bool hasPaperProblem = printQueue.HasPaperProblem;
                        bool needUserIntervention = printQueue.NeedUserIntervention;
                        bool isError = (status & PrintQueueStatus.Error) != 0;

                        if (isOffline)
                            logAction?.Invoke("打印机状态: 离线");
                        if (isPaused)
                            logAction?.Invoke("打印机状态: 已暂停");
                        if (isOutOfPaper)
                            logAction?.Invoke("打印机状态: 缺纸");
                        if (hasPaperProblem)
                            logAction?.Invoke("打印机状态: 纸张问题");
                        if (needUserIntervention)
                            logAction?.Invoke("打印机状态: 需要用户干预");
                        if (isError)
                            logAction?.Invoke("打印机状态: 错误");

                        bool isReady = !isOffline && !isPaused && !isOutOfPaper && !hasPaperProblem && !needUserIntervention && !isError;
                        if (isReady)
                            logAction?.Invoke("打印机状态: 就绪");
                        else
                            logAction?.Invoke($"打印机状态: {status}");

                        return isReady;
                    }
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"检查打印机硬件状态失败: {ex.Message}");
                    return false;
                }
            });
        }

        private static async Task<bool> SendZplToPrinterAsync(string printerName, string zplData, Action<string> logAction = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    logAction?.Invoke("开始发送ZPL打印数据...");
                    bool success = SendRawDataToPrinter(printerName, zplData, logAction);
                    if (!success)
                    {
                        logAction?.Invoke("原始打印失败，尝试备用方法...");
                        success = SendZplViaFile(printerName, zplData, logAction);
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"打印失败: {ex.GetType().Name} - {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        #endregion

        #region 原始打印方法（Win32 API）

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDataType;

            public DOCINFOA()
            {
                pDocName = "ZPL Label";
                pOutputFile = null;
                pDataType = "RAW";
            }
        }

        private static bool SendRawDataToPrinter(string printerName, string zplData, Action<string> logAction = null)
        {
            IntPtr hPrinter = IntPtr.Zero;
            DOCINFOA docInfo = new DOCINFOA();
            bool success = false;

            try
            {
                logAction?.Invoke($"尝试打开打印机: {printerName}");

                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    logAction?.Invoke($"无法打开打印机: {printerName}, Win32错误代码: {error}");
                    return false;
                }

                logAction?.Invoke("打印机已打开");

                if (StartDocPrinter(hPrinter, 1, docInfo))
                {
                    logAction?.Invoke("开始打印作业...");

                    if (StartPagePrinter(hPrinter))
                    {
                        logAction?.Invoke("开始打印页...");

                        byte[] data = Encoding.ASCII.GetBytes(zplData);
                        int bytesWritten = 0;

                        IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, pUnmanagedBytes, data.Length);

                        try
                        {
                            if (WritePrinter(hPrinter, pUnmanagedBytes, data.Length, out bytesWritten))
                            {
                                logAction?.Invoke($"成功发送 {bytesWritten}/{data.Length} 字节到打印机");
                                success = (bytesWritten == data.Length);
                                if (!success)
                                    logAction?.Invoke($"警告：只发送了 {bytesWritten} 字节，但需要发送 {data.Length} 字节");
                            }
                            else
                            {
                                int error = Marshal.GetLastWin32Error();
                                logAction?.Invoke($"写入打印机失败，Win32错误代码: {error}");
                            }
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(pUnmanagedBytes);
                        }

                        EndPagePrinter(hPrinter);
                        logAction?.Invoke("打印页结束");
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        logAction?.Invoke($"无法开始打印页，Win32错误代码: {error}");
                    }

                    EndDocPrinter(hPrinter);
                    logAction?.Invoke("打印作业结束");
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    logAction?.Invoke($"无法开始打印作业，Win32错误代码: {error}");
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"发送原始数据时发生异常: {ex.Message}");
                success = false;
            }
            finally
            {
                if (hPrinter != IntPtr.Zero)
                {
                    ClosePrinter(hPrinter);
                    logAction?.Invoke("打印机已关闭");
                }
            }

            return success;
        }

        #endregion

        #region 备用方法：通过文件打印

        private static bool SendZplViaFile(string printerName, string zplData, Action<string> logAction = null)
        {
            try
            {
                logAction?.Invoke("尝试通过文件方式打印...");

                string tempFile = Path.Combine(Path.GetTempPath(), $"ZPL_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                try
                {
                    File.WriteAllText(tempFile, zplData, Encoding.ASCII);
                    logAction?.Invoke($"ZPL数据已写入临时文件: {tempFile}");

                    string printerPath = $"\\\\{Environment.MachineName}\\{printerName}";
                    using (var rawWriter = new StreamWriter(printerPath, false, Encoding.ASCII))
                    {
                        rawWriter.Write(zplData);
                        rawWriter.Flush();
                    }

                    logAction?.Invoke("文件方式打印成功");
                    return true;
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"文件方式打印失败: {ex.Message}");
                    return false;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempFile))
                            File.Delete(tempFile);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"文件方式打印总体失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 调试方法

        public static void SaveZplForDebug(string zplData, string filePath = null, Action<string> logAction = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZPL_Debug.txt");

                File.WriteAllText(filePath, zplData, Encoding.ASCII);
                logAction?.Invoke($"ZPL数据已保存到: {filePath}");

                string preview = zplData.Length > 200 ? zplData.Substring(0, 200) + "..." : zplData;
                logAction?.Invoke($"ZPL预览: {preview}");

                if (!zplData.Contains("^FD") || !zplData.Contains("^FS"))
                    logAction?.Invoke("警告：ZPL数据可能缺少字段数据(^FD)或字段结束(^FS)标记");
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"保存ZPL调试文件失败: {ex.Message}");
            }
        }

        #endregion
    }
}