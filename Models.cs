using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AerialWindows
{
    public enum VideoFormat
    {
        v1080pH264,
        v1080pHEVC,
        v1080pHDR,
        v4KHEVC,
        v4KHDR,
        v4KSDR240
    }

    public class AerialVideo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string SecondaryName { get; set; } = "";
        public string Type { get; set; } = "";
        public string TimeOfDay { get; set; } = "day";
        public string Scene { get; set; } = "Nature";
        public Dictionary<VideoFormat, string> Urls { get; set; } = new();
        public Dictionary<string, string> PointsOfInterest { get; set; } = new();
        public string? PreviewImage { get; set; }
        public bool IsLive { get; set; }
        public double LivePlaybackSeconds { get; set; } = 300;

        public string GetUrlForFormat(VideoFormat preferred)
        {
            // Fallback order: preferred -> 1080p H264 -> first available
            if (Urls.TryGetValue(preferred, out var url) && !string.IsNullOrEmpty(url))
                return url;

            if (Urls.TryGetValue(VideoFormat.v1080pH264, out var fallbackUrl) && !string.IsNullOrEmpty(fallbackUrl))
                return fallbackUrl;

            foreach (var kvp in Urls)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                    return kvp.Value;
            }

            return "";
        }
    }

    // --- JSON Manifest Classes ---

    public class VideoManifest
    {
        [JsonPropertyName("assets")]
        public List<VideoAsset>? Assets { get; set; }
    }

    public class VideoAsset
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("accessibilityLabel")]
        public string AccessibilityLabel { get; set; } = "";

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("timeOfDay")]
        public string? TimeOfDay { get; set; }

        [JsonPropertyName("scene")]
        public string? Scene { get; set; }

        [JsonPropertyName("pointsOfInterest")]
        public Dictionary<string, string>? PointsOfInterest { get; set; }

        [JsonPropertyName("url-4K-HDR")]
        public string? Url4KHDR { get; set; }

        [JsonPropertyName("url-4K-SDR")]
        public string? Url4KSDR { get; set; }

        [JsonPropertyName("url-1080-H264")]
        public string? Url1080H264 { get; set; }

        [JsonPropertyName("url-1080-HDR")]
        public string? Url1080HDR { get; set; }

        [JsonPropertyName("url-1080-SDR")]
        public string? Url1080SDR { get; set; }

        [JsonPropertyName("url-4K-SDR-240FPS")]
        public string? Url4KSDR240FPS { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("isLive")]
        public bool? IsLive { get; set; }

        [JsonPropertyName("livePlaybackSeconds")]
        public double? LivePlaybackSeconds { get; set; }

        [JsonPropertyName("previewImage")]
        public string? PreviewImage { get; set; }
    }

    public class MacManifest
    {
        [JsonPropertyName("assets")]
        public List<MacAsset>? Assets { get; set; }

        [JsonPropertyName("categories")]
        public List<SubcategoryElement>? Categories { get; set; }
    }

    public class MacAsset
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("previewImage")]
        public string? PreviewImage { get; set; }

        [JsonPropertyName("localizedNameKey")]
        public string LocalizedNameKey { get; set; } = "";

        [JsonPropertyName("accessibilityLabel")]
        public string AccessibilityLabel { get; set; } = "";

        [JsonPropertyName("subcategories")]
        public List<string>? Subcategories { get; set; }

        [JsonPropertyName("pointsOfInterest")]
        public Dictionary<string, string>? PointsOfInterest { get; set; }

        [JsonPropertyName("url-4K-SDR-240FPS")]
        public string? Url4KSDR240FPS { get; set; }
    }

    public class SubcategoryElement
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("localizedNameKey")]
        public string LocalizedNameKey { get; set; } = "";

        [JsonPropertyName("subcategories")]
        public List<SubcategoryElement>? Subcategories { get; set; }
    }

    // --- Settings Configuration ---

    public class AppSettings
    {
        public VideoFormat PreferredFormat { get; set; } = VideoFormat.v1080pH264;
        public bool EnableCaching { get; set; } = false;
        public string CacheFolder { get; set; } = "";
        public double CacheSizeLimitGb { get; set; } = 20.0;
        public List<string> EnabledVideoIds { get; set; } = new();
        public bool HasConfiguredVideos { get; set; } = false;
        public bool PlayDifferentVideoPerScreen { get; set; } = true;
        public bool BypassSslValidation { get; set; } = false;
        public double PlaybackSpeed { get; set; } = 1.0;
        public double VideoZoom { get; set; } = 1.0;
        public List<string> CustomVideoUrls { get; set; } = new();
        public bool UseCustomVideoUrls { get; set; } = false;
        public string AppTheme { get; set; } = "Dark";
        
        // Overlays Configuration
        public bool ShowClock { get; set; } = true;
        public string ClockPosition { get; set; } = "TopRight";
        public double ClockFontSize { get; set; } = 36.0;
        public string ClockFontFamily { get; set; } = "Segoe UI";
        public string ClockFontColor { get; set; } = "#FFFFFF";

        public bool ShowWeather { get; set; } = false;
        public string WeatherPosition { get; set; } = "TopLeft";
        public string WeatherApiKey { get; set; } = "";
        public string WeatherLocation { get; set; } = "New York,US";
        public double WeatherFontSize { get; set; } = 20.0;
        public string WeatherFontFamily { get; set; } = "Segoe UI";
        public string WeatherFontColor { get; set; } = "#FFFFFF";

        public bool ShowLocationPOI { get; set; } = true;
        public string LocationPosition { get; set; } = "BottomLeft";
        public double LocationFontSize { get; set; } = 24.0;
        public string LocationFontFamily { get; set; } = "Segoe UI";
        public string LocationFontColor { get; set; } = "#FFFFFF";

        public bool ShowNowPlaying { get; set; } = false;
        public string NowPlayingPosition { get; set; } = "BottomRight";
        public double NowPlayingFontSize { get; set; } = 20.0;
        public string NowPlayingFontFamily { get; set; } = "Segoe UI";
        public string NowPlayingFontColor { get; set; } = "#FFFFFF";

        public bool ShowBattery { get; set; } = false;
        public string BatteryPosition { get; set; } = "TopRight";
        public double BatteryFontSize { get; set; } = 16.0;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AerialWindows", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        if (string.IsNullOrEmpty(settings.CacheFolder))
                        {
                            settings.CacheFolder = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "AerialWindows", "Cache");
                        }
                        return settings;
                    }
                }
            }
            catch { }

            var defaultSettings = new AppSettings();
            defaultSettings.CacheFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AerialWindows", "Cache");
            return defaultSettings;
        }

        public void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Fallback attempt to just write it
                try
                {
                    string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                    string? dir = Path.GetDirectoryName(SettingsPath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.WriteAllText(SettingsPath, json);
                }
                catch { }
            }
        }
    }

    public static class HttpClientFactory
    {
        private static HttpClient? _bypassingClient;
        private static HttpClient? _standardClient;

        public static HttpClient GetClient(bool bypassSsl)
        {
            if (bypassSsl)
            {
                if (_bypassingClient == null)
                {
                    _bypassingClient = new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                    });
                }
                return _bypassingClient;
            }
            else
            {
                if (_standardClient == null)
                {
                    _standardClient = new HttpClient();
                }
                return _standardClient;
            }
        }
    }
}
