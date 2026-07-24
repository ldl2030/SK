using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TestPlatform
{
    /// <summary>
    /// SMB 共享上传辅助类（支持认证和递归创建目录）
    /// </summary>
    public static class SMBHelper
    {
        /// <summary>
        /// 异步上传文件到 SMB 共享
        /// </summary>
        /// <param name="localFilePath">本地文件完整路径</param>
        /// <param name="deviceFolder">设备文件夹名（如 LR5-2904）</param>
        /// <param name="resultFolder">结果文件夹名（PASS 或 NG）</param>
        /// <param name="smbServerPath">SMB 服务器根路径（如 \\192.168.30.7\Testresult）</param>
        /// <param name="username">用户名（可为空）</param>
        /// <param name="password">密码（可为空）</param>
        /// <param name="logAction">日志回调</param>
        /// <returns>成功返回 true，否则 false</returns>
        public static async Task<bool> UploadFileToSmbAsync(
            string localFilePath,
            string deviceFolder,
            string resultFolder,
            string smbServerPath,
            string username,
            string password,
            Action<string> logAction = null)
        {
            return await Task.Run(() => UploadFileToSmb(localFilePath, deviceFolder, resultFolder, smbServerPath, username, password, logAction));
        }

        /// <summary>
        /// 同步上传文件到 SMB 共享
        /// </summary>
        public static bool UploadFileToSmb(
            string localFilePath,
            string deviceFolder,
            string resultFolder,
            string smbServerPath,
            string username,
            string password,
            Action<string> logAction = null)
        {
            try
            {
                if (!File.Exists(localFilePath))
                {
                    logAction?.Invoke($"本地文件不存在: {localFilePath}");
                    return false;
                }

                string yearMonth = DateTime.Now.ToString("yyyyMM");
                string targetPath = Path.Combine(smbServerPath, deviceFolder, resultFolder, yearMonth);
                string fileName = Path.GetFileName(localFilePath);
                string fullTargetPath = Path.Combine(targetPath, fileName);

                logAction?.Invoke($"准备上传文件到 SMB: {fullTargetPath}");

                // 确保目标目录存在（递归创建）
                if (!EnsureDirectoryExists(targetPath, username, password, logAction))
                {
                    logAction?.Invoke($"无法创建目录: {targetPath}");
                    return false;
                }

                // 使用凭据复制文件
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    var cred = new NetworkCredential(username, password);
                    using (new NetworkConnection(GetNetworkPath(targetPath), cred))
                    {
                        File.Copy(localFilePath, fullTargetPath, true);
                    }
                }
                else
                {
                    // 匿名访问
                    Directory.CreateDirectory(targetPath);
                    File.Copy(localFilePath, fullTargetPath, true);
                }

                logAction?.Invoke($"SMB 上传成功: {fullTargetPath}");
                return true;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"SMB 上传失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 递归创建 SMB 目录（支持认证）
        /// </summary>
        private static bool EnsureDirectoryExists(string fullPath, string username, string password, Action<string> logAction = null)
        {
            if (Directory.Exists(fullPath))
                return true;

            // 获取父目录
            string parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent) && parent != fullPath)
            {
                if (!EnsureDirectoryExists(parent, username, password, logAction))
                    return false;
            }

            // 创建当前目录
            try
            {
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    var cred = new NetworkCredential(username, password);
                    // 注意：NetworkConnection 需要连接到父目录（或当前目录的根），才能创建子目录
                    // 这里连接到父目录（或共享根），然后创建子目录
                    string connectPath = GetNetworkPath(fullPath);
                    using (new NetworkConnection(connectPath, cred))
                    {
                        if (!Directory.Exists(fullPath))
                            Directory.CreateDirectory(fullPath);
                    }
                }
                else
                {
                    if (!Directory.Exists(fullPath))
                        Directory.CreateDirectory(fullPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"创建目录失败 {fullPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取适合网络连接的路径（对于 UNC 路径，返回共享根或父目录）
        /// </summary>
        private static string GetNetworkPath(string fullPath)
        {
            // 对于 \\server\share\sub\sub 这样的路径，我们需要连接到根共享 \\server\share 或父目录
            // 最简单的方法是取前两级（服务器和共享名）
            string[] parts = fullPath.TrimStart('\\').Split('\\');
            if (parts.Length >= 2)
            {
                return @"\\" + parts[0] + @"\" + parts[1];
            }
            return fullPath;
        }
    }

    /// <summary>
    /// 网络连接辅助类（用于处理 SMB 认证）
    /// </summary>
    public class NetworkConnection : IDisposable
    {
        private string _networkName;

        public NetworkConnection(string networkName, NetworkCredential credentials)
        {
            _networkName = networkName;

            var netResource = new NetResource
            {
                Scope = ResourceScope.GlobalNetwork,
                ResourceType = ResourceType.Disk,
                DisplayType = ResourceDisplaytype.Share,
                RemoteName = networkName
            };

            int result = WNetUseConnection(
                IntPtr.Zero,
                netResource,
                credentials.Password,
                credentials.UserName,
                0,
                null,
                null,
                null);

            if (result != 0)
            {
                throw new System.ComponentModel.Win32Exception(result, "连接到网络共享失败");
            }
        }

        ~NetworkConnection()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_networkName != null)
            {
                WNetCancelConnection2(_networkName, 0, true);
                _networkName = null;
            }
        }

        [DllImport("mpr.dll")]
        private static extern int WNetUseConnection(
            IntPtr hwndOwner,
            NetResource netResource,
            string password,
            string username,
            int flags,
            string accessName,
            string bufferSize,
            string result);

        [DllImport("mpr.dll")]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        [StructLayout(LayoutKind.Sequential)]
        private class NetResource
        {
            public ResourceScope Scope;
            public ResourceType ResourceType;
            public ResourceDisplaytype DisplayType;
            public int Usage;
            public string LocalName;
            public string RemoteName;
            public string Comment;
            public string Provider;
        }

        private enum ResourceScope
        {
            Connected = 1,
            GlobalNetwork,
            Remembered,
            Recent,
            Context
        }

        private enum ResourceType
        {
            Any = 0,
            Disk = 1,
            Print = 2,
            Reserved = 8,
        }

        private enum ResourceDisplaytype
        {
            Generic = 0x0,
            Domain = 0x01,
            Server = 0x02,
            Share = 0x03,
            File = 0x04,
            Group = 0x05,
            Network = 0x06,
            Root = 0x07,
            Shareadmin = 0x08,
            Directory = 0x09,
            Tree = 0x0a,
            Ndscontainer = 0x0b
        }
    }
}