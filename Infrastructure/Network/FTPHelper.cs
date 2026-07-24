using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TestPlatform
{
    internal class FTPHelper
    {
        /// <summary>
        /// 上传测试报告到 FTP 服务器（使用配置参数）
        /// </summary>
        /// <param name="localFilePath">本地文件路径</param>
        /// <param name="sn">序列号</param>
        /// <param name="testResult">测试结果（true=通过）</param>
        /// <param name="ftpBasePath">远程基础路径，如 "/HD/30A电源板"</param>
        /// <param name="ftpServer">FTP服务器地址（如 "218.17.142.141"）</param>
        /// <param name="ftpPort">FTP端口（如 9055）</param>
        /// <param name="ftpUser">用户名</param>
        /// <param name="ftpPassword">密码</param>
        /// <param name="logAction">日志回调</param>
        public static async Task UploadTestReportAsync(string localFilePath, string sn, bool testResult, string ftpBasePath,
    string ftpServer, int ftpPort, string ftpUser, string ftpPassword, Action<string> logAction = null)
        {
            string normalizedSn = NormalizeFileName(sn);
            string resultFolder = testResult ? "PASS" : "NG";
            string today = DateTime.Now.ToString("yyyyMM");
            string ftpHost = $"ftp://{ftpServer}:{ftpPort}";

            // 路径不再包含 SN 目录
            string remoteDir = $"{ftpHost}{ftpBasePath}/TestReport/{resultFolder}/{today}";
            // 文件名中包含 SN，以便区分不同测试
            //string fileName = $"{normalizedSn}_{Path.GetFileName(localFilePath)}";//这里使用产品SN命名产品
            // 直接使用原文件名，不添加 SN 前缀
            string fileName = Path.GetFileName(localFilePath);
            string remoteFullPath = $"{remoteDir}/{fileName}";

            logAction?.Invoke($"开始上传 FTP，目标路径：{remoteFullPath}");

            await EnsureFtpDirectoryExists(remoteDir, ftpUser, ftpPassword);
            await UploadFileToFtpAsync(localFilePath, remoteFullPath, ftpUser, ftpPassword, logAction);

            logAction?.Invoke($"FTP 上传完成：{remoteFullPath}");
        }

        // 以下辅助方法保持不变，但改为接受动态的用户名密码
        private static async Task EnsureFtpDirectoryExists(string ftpUri, string user, string pwd)
        {
            string[] parts = ftpUri.Replace("ftp://", "").Split('/');
            string host = parts[0];
            string currentPath = "ftp://" + host;

            for (int i = 1; i < parts.Length; i++)
            {
                currentPath = currentPath + "/" + parts[i];
                try
                {
                    FtpWebRequest request = (FtpWebRequest)WebRequest.Create(currentPath);
                    request.Method = WebRequestMethods.Ftp.MakeDirectory;
                    request.Credentials = new NetworkCredential(user, pwd);
                    request.UsePassive = true;
                    request.UseBinary = true;
                    request.KeepAlive = false;
                    request.Proxy = null;

                    using (FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync())
                    {
                        // 目录创建成功
                    }
                }
                catch (WebException ex)
                {
                    FtpWebResponse response = ex.Response as FtpWebResponse;
                    if (response == null || response.StatusCode != FtpStatusCode.ActionNotTakenFileUnavailable)
                        throw;
                }
            }
        }

        private static async Task UploadFileToFtpAsync(string localFilePath, string ftpPath, string user, string pwd, Action<string> logAction = null)
        {
            FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpPath);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(user, pwd);
            request.UseBinary = true;
            request.UsePassive = true;
            request.KeepAlive = false;
            request.Proxy = null;

            using (FileStream fileStream = File.OpenRead(localFilePath))
            {
                using (Stream ftpStream = await request.GetRequestStreamAsync())
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    long total = fileStream.Length;
                    long uploaded = 0;
                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await ftpStream.WriteAsync(buffer, 0, bytesRead);
                        uploaded += bytesRead;
                        if (logAction != null && total > 0)
                        {
                            int percent = (int)(uploaded * 100 / total);
                            logAction($"上传进度：{percent}%");
                        }
                    }
                }
            }

            using (FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync())
            {
                if (response.StatusCode != FtpStatusCode.ClosingData)
                    throw new Exception($"FTP 上传失败，状态码：{response.StatusCode}，描述：{response.StatusDescription}");
            }
        }

        private static string NormalizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
            sanitized = sanitized.Replace(' ', '_');
            const int maxLength = 255;
            if (sanitized.Length > maxLength)
            {
                string extension = Path.GetExtension(sanitized);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
                int maxNameLength = maxLength - extension.Length;
                if (maxNameLength > 0)
                {
                    nameWithoutExt = nameWithoutExt.Substring(0, Math.Min(nameWithoutExt.Length, maxNameLength));
                    sanitized = nameWithoutExt + extension;
                }
            }
            return sanitized.ToLowerInvariant();
        }
    }
}
