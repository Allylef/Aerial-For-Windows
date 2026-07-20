using System;
using System.Collections.Generic;
using System.IO;
using System.Formats.Tar;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text;
using System.Linq;

namespace AerialWindows
{
    public class ManifestManager
    {
        private static HttpClient HttpClient => HttpClientFactory.GetClient(AppSettings.Load().BypassSslValidation);

        private static readonly string VideosCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AerialWindows", "videos.json");

        private static readonly string StringsCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AerialWindows", "strings.json");

        public List<AerialVideo> Videos { get; private set; } = new();
        public Dictionary<string, string> Translations { get; private set; } = new();

        public void LoadCachedVideos()
        {
            try
            {
                if (File.Exists(StringsCachePath))
                {
                    string json = File.ReadAllText(StringsCachePath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        Translations = dict;
                    }
                }
                if (File.Exists(VideosCachePath))
                {
                    string json = File.ReadAllText(VideosCachePath);
                    var list = JsonSerializer.Deserialize<List<AerialVideo>>(json);
                    if (list != null)
                    {
                        foreach (var v in list)
                        {
                            if (Translations.TryGetValue(v.Name, out var translated))
                            {
                                v.Name = translated;
                            }
                            else if (v.Name.EndsWith("_NAME", StringComparison.OrdinalIgnoreCase) || v.Name.Contains("_A0") || v.Name.Contains("_C0"))
                            {
                                if (!string.IsNullOrEmpty(v.SecondaryName))
                                {
                                    v.Name = v.SecondaryName;
                                }
                            }
                        }
                        Videos = list;
                    }
                }
            }
            catch { }
        }

        public async Task UpdateManifestsAsync(Action<string, double>? progressCallback)
        {
            var sources = new[]
            {
                new { Name = "macOS 26", Url = "https://sylvan.apple.com/itunes-assets/Aerials126/v4/82/2e/34/822e344c-f5d2-878c-3d56-508d5b09ed61/resources-26-0-1.tar", IsMacFormat = true },
                new { Name = "tvOS 26", Url = "https://sylvan.apple.com/itunes-assets/Aerials126/v4/c0/45/d9/c045d9d0-9606-1535-62fe-189edb4f79eb/resources-atv-23J-2.tar", IsMacFormat = true },
                new { Name = "tvOS 13", Url = "https://sylvan.apple.com/Aerials/resources-13.tar", IsMacFormat = false }
            };

            var allVideos = new Dictionary<string, AerialVideo>();
            var newTranslations = new Dictionary<string, string>();

            double totalSources = sources.Length;
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                progressCallback?.Invoke($"Downloading and parsing {source.Name}...", i / totalSources);

                try
                {
                    using (var response = await HttpClient.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var tarReader = new TarReader(stream))
                        {
                            byte[]? entriesBytes = null;
                            
                            TarEntry? entry;
                            while ((entry = tarReader.GetNextEntry()) != null)
                            {
                                string name = entry.Name;
                                if (name.EndsWith("entries.json", StringComparison.OrdinalIgnoreCase))
                                {
                                    using (var ms = new MemoryStream())
                                    {
                                        entry.DataStream?.CopyTo(ms);
                                        entriesBytes = ms.ToArray();
                                    }
                                }
                                else if (name.Contains(".lproj/Localizable.nocache.strings", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Prefer English or default translation strings
                                    if (name.Contains("en.lproj", StringComparison.OrdinalIgnoreCase) || newTranslations.Count == 0)
                                    {
                                        byte[] fileData;
                                        using (var ms = new MemoryStream())
                                        {
                                            entry.DataStream?.CopyTo(ms);
                                            fileData = ms.ToArray();
                                        }

                                        string text;
                                        if (fileData.Length >= 2 && fileData[0] == 0xFF && fileData[1] == 0xFE)
                                        {
                                            text = Encoding.Unicode.GetString(fileData);
                                        }
                                        else if (fileData.Length >= 2 && fileData[0] == 0xFE && fileData[1] == 0xFF)
                                        {
                                            text = Encoding.BigEndianUnicode.GetString(fileData);
                                        }
                                        else if (fileData.Length >= 4 && fileData.Contains((byte)0))
                                        {
                                            text = Encoding.Unicode.GetString(fileData);
                                        }
                                        else
                                        {
                                            text = Encoding.UTF8.GetString(fileData);
                                        }

                                        ParseStringsContent(text, newTranslations);
                                    }
                                }
                            }

                            if (entriesBytes != null)
                            {
                                if (source.IsMacFormat)
                                {
                                    var macManifest = JsonSerializer.Deserialize<MacManifest>(entriesBytes);
                                    if (macManifest?.Assets != null)
                                    {
                                        foreach (var asset in macManifest.Assets)
                                        {
                                            if (allVideos.ContainsKey(asset.Id)) continue;

                                            string name = Translate(asset.LocalizedNameKey, newTranslations);
                                            if (string.IsNullOrEmpty(name))
                                            {
                                                name = GetNameFromSubcategories(asset.Subcategories, macManifest, newTranslations) 
                                                       ?? asset.AccessibilityLabel;
                                            }

                                            var video = new AerialVideo
                                            {
                                                Id = asset.Id,
                                                Name = name,
                                                SecondaryName = asset.AccessibilityLabel,
                                                Type = "video",
                                                Scene = GetSceneForLabel(asset.AccessibilityLabel),
                                                PreviewImage = asset.PreviewImage,
                                                IsLive = false
                                            };

                                            if (!string.IsNullOrEmpty(asset.Url4KSDR240FPS))
                                            {
                                                video.Urls[VideoFormat.v4KSDR240] = asset.Url4KSDR240FPS;
                                                // Create a simulated 1080p URL because Apple TV videos stream smoothly at lower resolutions too
                                                // Apple's high framerate SDR streams can be scaled by the media element
                                                video.Urls[VideoFormat.v1080pH264] = asset.Url4KSDR240FPS;
                                            }

                                            if (asset.PointsOfInterest != null)
                                            {
                                                foreach (var kvp in asset.PointsOfInterest)
                                                {
                                                    video.PointsOfInterest[kvp.Key] = Translate(kvp.Value, newTranslations);
                                                }
                                            }

                                            allVideos[video.Id] = video;
                                        }
                                    }
                                }
                                else
                                {
                                    var videoManifest = JsonSerializer.Deserialize<VideoManifest>(entriesBytes);
                                    if (videoManifest?.Assets != null)
                                    {
                                        foreach (var asset in videoManifest.Assets)
                                        {
                                            if (allVideos.ContainsKey(asset.Id)) continue;

                                            string rawTitle = asset.Title ?? "";
                                            string name = Translate(rawTitle, newTranslations);
                                            if (string.IsNullOrEmpty(name) || name == rawTitle)
                                            {
                                                name = !string.IsNullOrEmpty(asset.AccessibilityLabel) ? asset.AccessibilityLabel : (rawTitle != "" ? rawTitle : "Aerial Video");
                                            }

                                            var video = new AerialVideo
                                            {
                                                Id = asset.Id,
                                                Name = name,
                                                SecondaryName = asset.AccessibilityLabel,
                                                Type = asset.Type ?? "video",
                                                TimeOfDay = asset.TimeOfDay ?? "day",
                                                Scene = asset.Scene ?? GetSceneForLabel(asset.AccessibilityLabel),
                                                PreviewImage = asset.PreviewImage,
                                                IsLive = asset.IsLive ?? false,
                                                LivePlaybackSeconds = asset.LivePlaybackSeconds ?? 300
                                            };

                                            if (!string.IsNullOrEmpty(asset.Url1080H264)) video.Urls[VideoFormat.v1080pH264] = asset.Url1080H264;
                                            if (!string.IsNullOrEmpty(asset.Url1080SDR)) video.Urls[VideoFormat.v1080pHEVC] = asset.Url1080SDR;
                                            if (!string.IsNullOrEmpty(asset.Url1080HDR)) video.Urls[VideoFormat.v1080pHDR] = asset.Url1080HDR;
                                            if (!string.IsNullOrEmpty(asset.Url4KSDR)) video.Urls[VideoFormat.v4KHEVC] = asset.Url4KSDR;
                                            if (!string.IsNullOrEmpty(asset.Url4KHDR)) video.Urls[VideoFormat.v4KHDR] = asset.Url4KHDR;
                                            if (!string.IsNullOrEmpty(asset.Url4KSDR240FPS)) video.Urls[VideoFormat.v4KSDR240] = asset.Url4KSDR240FPS;
                                            if (!string.IsNullOrEmpty(asset.Url)) video.Urls[VideoFormat.v1080pH264] = asset.Url; // fallback

                                            if (asset.PointsOfInterest != null)
                                            {
                                                foreach (var kvp in asset.PointsOfInterest)
                                                {
                                                    video.PointsOfInterest[kvp.Key] = Translate(kvp.Value, newTranslations);
                                                }
                                            }

                                            allVideos[video.Id] = video;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed loading {source.Name}: {ex.Message}");
                }
            }

            if (allVideos.Count > 0)
            {
                Videos = allVideos.Values.ToList();
                Translations = newTranslations;

                // Cache to local storage
                try
                {
                    string? dir = Path.GetDirectoryName(VideosCachePath);
                    if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string videosJson = JsonSerializer.Serialize(Videos, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(VideosCachePath, videosJson);

                    string stringsJson = JsonSerializer.Serialize(Translations, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(StringsCachePath, stringsJson);
                }
                catch { }
            }
            else
            {
                throw new Exception("Could not download any video playlists from Apple. Please verify your connection.");
            }

            progressCallback?.Invoke("Complete!", 1.0);
        }

        private static string Translate(string key, Dictionary<string, string> translations)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (translations.TryGetValue(key, out var val)) return val;
            return key;
        }

        private static string? GetNameFromSubcategories(List<string>? subcatIds, MacManifest manifest, Dictionary<string, string> translations)
        {
            if (subcatIds == null || subcatIds.Count == 0 || manifest.Categories == null) return null;
            string id = subcatIds[0];

            foreach (var category in manifest.Categories)
            {
                if (category.Id == id) return Translate(category.LocalizedNameKey, translations);
                if (category.Subcategories != null)
                {
                    foreach (var sub in category.Subcategories)
                    {
                        if (sub.Id == id) return Translate(sub.LocalizedNameKey, translations);
                    }
                }
            }
            return null;
        }

        private static string GetSceneForLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return "Nature";
            string lower = label.ToLower();
            if (lower.Contains("space") || lower.Contains("iss") || lower.Contains("earth from")) return "Space";
            if (lower.Contains("sea") || lower.Contains("underwater") || lower.Contains("coral") || lower.Contains("fish")) return "Sea";
            if (lower.Contains("beach") || lower.Contains("coast") || lower.Contains("ocean")) return "Beach";
            if (lower.Contains("city") || lower.Contains("skyline") || lower.Contains("downtown") || lower.Contains("freeway") || lower.Contains("bridge")) return "City";
            if (lower.Contains("desert") || lower.Contains("canyon") || lower.Contains("hills") || lower.Contains("forest") || lower.Contains("mountain") || lower.Contains("valley")) return "Countryside";
            return "Nature";
        }

        private static void ParseStringsContent(string text, Dictionary<string, string> translations)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("/*") || trimmed.StartsWith("//") || !trimmed.EndsWith(";"))
                    continue;

                int equalIdx = trimmed.IndexOf('=');
                if (equalIdx > 0)
                {
                    string key = trimmed.Substring(0, equalIdx).Trim().Trim('"');
                    string val = trimmed.Substring(equalIdx + 1).Trim().TrimEnd(';').Trim().Trim('"');
                    
                    // Unescape quotes and newlines
                    val = val.Replace("\\\"", "\"").Replace("\\n", "\n");
                    translations[key] = val;
                }
            }
        }
    }
}
