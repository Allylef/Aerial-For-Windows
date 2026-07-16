using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AerialWindows
{
    public class CacheManager
    {
        private HttpClient HttpClient => HttpClientFactory.GetClient(Settings.BypassSslValidation);
        
        // Tracks active downloads so we don't download the same file twice simultaneously
        private readonly ConcurrentDictionary<string, Task<string>> _activeDownloads = new();

        public AppSettings Settings { get; }

        public CacheManager(AppSettings settings)
        {
            Settings = settings;
            EnsureCacheFolderExists();
        }

        public void EnsureCacheFolderExists()
        {
            try
            {
                if (!Directory.Exists(Settings.CacheFolder))
                {
                    Directory.CreateDirectory(Settings.CacheFolder);
                }
            }
            catch { }
        }

        public bool IsVideoCached(string videoId)
        {
            string localPath = GetLocalPath(videoId);
            return File.Exists(localPath) && new FileInfo(localPath).Length > 0;
        }

        public string GetLocalPath(string videoId)
        {
            return Path.Combine(Settings.CacheFolder, $"{videoId}.mov");
        }

        public string GetVideoPlaybackUrl(AerialVideo video)
        {
            if (Settings.EnableCaching && IsVideoCached(video.Id))
            {
                return GetLocalPath(video.Id);
            }
            
            // Return remote streaming URL
            return video.GetUrlForFormat(Settings.PreferredFormat);
        }

        public async Task<string> DownloadVideoAsync(AerialVideo video, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            string url = video.GetUrlForFormat(Settings.PreferredFormat);
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("Video has no valid URL");
            }

            string localPath = GetLocalPath(video.Id);
            
            // Check if already cached
            if (IsVideoCached(video.Id))
            {
                return localPath;
            }

            // Return active download task if one is running
            return await _activeDownloads.GetOrAdd(video.Id, async id =>
            {
                try
                {
                    EnsureCacheFolderExists();
                    string tempPath = Path.Combine(Settings.CacheFolder, $"{video.Id}.tmp");

                    using (var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            int read;

                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read, cancellationToken);
                                totalRead += read;

                                if (totalBytes.HasValue)
                                {
                                    progress?.Report((double)totalRead / totalBytes.Value);
                                }
                            }
                        }
                    }

                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }
                    File.Move(tempPath, localPath);

                    // Clean cache after successful download to stay within limits
                    EnforceCacheLimit();

                    return localPath;
                }
                finally
                {
                    _activeDownloads.TryRemove(video.Id, out _);
                }
            });
        }

        public void EnforceCacheLimit()
        {
            try
            {
                EnsureCacheFolderExists();
                var dirInfo = new DirectoryInfo(Settings.CacheFolder);
                var files = dirInfo.GetFiles("*.mov")
                                   .OrderBy(f => f.LastAccessTime)
                                   .ToList();

                long totalSizeBytes = files.Sum(f => f.Length);
                long limitBytes = (long)(Settings.CacheSizeLimitGb * 1024 * 1024 * 1024);

                int fileIdx = 0;
                while (totalSizeBytes > limitBytes && fileIdx < files.Count)
                {
                    var file = files[fileIdx];
                    totalSizeBytes -= file.Length;
                    try
                    {
                        file.Delete();
                    }
                    catch { }
                    fileIdx++;
                }
            }
            catch { }
        }

        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(Settings.CacheFolder))
                {
                    foreach (string file in Directory.GetFiles(Settings.CacheFolder, "*.mov"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (string file in Directory.GetFiles(Settings.CacheFolder, "*.tmp"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public long GetCacheSizeInBytes()
        {
            try
            {
                if (!Directory.Exists(Settings.CacheFolder)) return 0;
                return new DirectoryInfo(Settings.CacheFolder)
                    .GetFiles("*.mov")
                    .Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }
    }
}
