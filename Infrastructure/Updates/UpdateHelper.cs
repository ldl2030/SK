using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace TestPlatform
{
    public class UpdateInfo
    {
        public string appName { get; set; }
        public string displayName { get; set; }
        public string downloadUrl { get; set; }
        public long fileSize { get; set; }
        public bool forceUpdate { get; set; }
        public bool hasVersion { get; set; }
        public string md5 { get; set; }
        public string minVersion { get; set; }
        public string releaseNotes { get; set; }
        public string version { get; set; }
        public int versionCode { get; set; }
    }

    public static class UpdateHelper
    {
        /// <summary>
        /// 检查服务器版本信息
        /// </summary>
        public static async Task<UpdateInfo> CheckForUpdateAsync(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                string json = await client.GetStringAsync(url);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateInfo>(json);
            }
        }

        /// <summary>
        /// 下载更新包，并报告进度，同时校验MD5
        /// </summary>
        /// <param name="downloadUrl">下载地址</param>
        /// <param name="progress">进度回调</param>
        /// <param name="expectedMd5">期望的MD5值（可选）</param>
        /// <returns>下载的临时文件完整路径</returns>
        public static async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<int> progress, long expectedFileSize, string expectedMd5 = null)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"update_{Guid.NewGuid()}.zip");
            using (HttpClient client = new HttpClient())
            {
                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    // 优先使用 Content-Length，否则使用传入的 expectedFileSize
                    long totalBytes = response.Content.Headers.ContentLength ?? expectedFileSize;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            if (totalBytes > 0)
                            {
                                int percent = (int)(totalRead * 100 / totalBytes);
                                progress?.Report(percent);
                            }
                        }
                    }
                }
            }

            // MD5 校验
            if (!string.IsNullOrEmpty(expectedMd5))
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(tempFile))
                {
                    var hash = md5.ComputeHash(stream);
                    string actualMd5 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    if (actualMd5 != expectedMd5.ToLowerInvariant())
                        throw new InvalidDataException($"MD5校验失败：期望 {expectedMd5}，实际 {actualMd5}");
                }
            }

            return tempFile;
        }

        /// <summary>
        /// 启动更新程序（Updater.exe）
        /// </summary>
        public static void LaunchUpdater(string zipPath, string targetDir, string mainExePath)
        {
            string updaterPath = Path.Combine(targetDir, "Updater.exe");
            if (!File.Exists(updaterPath))
                throw new FileNotFoundException($"更新程序未找到: {updaterPath}");

            // 去除路径末尾可能存在的反斜杠，避免转义问题
            targetDir = targetDir.TrimEnd('\\');
            mainExePath = mainExePath.TrimEnd('\\');

            // 将每个参数用双引号包裹，并转义内部的双引号（虽然路径中通常没有，但为了安全）
            string args = $"\"{zipPath.Replace("\"", "\\\"")}\" \"{targetDir.Replace("\"", "\\\"")}\" \"{mainExePath.Replace("\"", "\\\"")}\"";

            // 可选：记录日志以便调试
            // AppendLog($"启动更新程序，参数串: {args}", LogInfo);

            Process.Start(updaterPath, args);
        }
    }
}