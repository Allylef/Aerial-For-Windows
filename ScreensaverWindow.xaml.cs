using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.Media.Control;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace AerialWindows
{
    public partial class ScreensaverWindow : Window
    {
        private AerialVideo _video;
        private readonly CacheManager _cacheManager;
        private readonly AppSettings _settings;
        private readonly bool _isPreviewMode;

        private Win32.POINT _initialPhysicalMousePos;
        private bool _isPhysicalMouseSet;
        private LocalVideoProxy? _localProxy;
        private DateTime _startTime = DateTime.Now;
        private DispatcherTimer? _overlayTimer;
        private DispatcherTimer? _nowPlayingTimer;

        // Overlay controls
        private TextBlock? _clockText;
        private TextBlock? _clockDateText;
        private TextBlock? _locationText;
        private StackPanel? _weatherPanel;
        private TextBlock? _weatherText;
        private StackPanel? _nowPlayingPanel;
        private TextBlock? _nowPlayingText;

        // Sorted list of POIs with their timestamps in seconds
        private List<(double Time, string Text)> _poiList = new();
        private string _currentPoiText = "";

        public ScreensaverWindow(AerialVideo video, CacheManager cacheManager, AppSettings settings, bool isPreviewMode)
        {
            InitializeComponent();

            _video = video;
            _cacheManager = cacheManager;
            _settings = settings;
            _isPreviewMode = isPreviewMode;

            if (_isPreviewMode)
            {
                // In preview, scale down margins and disable mouse/key hooks
                OverlaysGrid.Visibility = Visibility.Collapsed; // Hide overlays in tiny screensaver settings preview
                Player.Volume = 0; // Mute
            }
            else
            {
                // Fullscreen screensaver settings overscan
                Left = -2;
                Top = -2;
                Width = SystemParameters.PrimaryScreenWidth + 4;
                Height = SystemParameters.PrimaryScreenHeight + 4;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                Cursor = System.Windows.Input.Cursors.None;
            }

            if (_settings.BypassSslValidation)
            {
                var client = HttpClientFactory.GetClient(true);
                _localProxy = new LocalVideoProxy(client);
                _localProxy.Start();
            }

            ParsePointsOfInterest();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StartVideo();

            if (!_isPreviewMode)
            {
                SetupOverlays();
                StartOverlayTimers();
            }
        }

        private async void StartVideo()
        {
            try
            {
                // Apply Video Zoom / Crop transform from settings
                double zoom = _settings.VideoZoom > 0 ? _settings.VideoZoom : 1.0;
                PlayerScale.ScaleX = zoom;
                PlayerScale.ScaleY = zoom;

                string sourceUrl = _cacheManager.GetVideoPlaybackUrl(_video);
                App.Log($"Starting playback for Video ID: {_video.Id}, Name: {_video.Name}");

                if (IsYouTubeUrl(sourceUrl))
                {
                    App.Log($"Resolving YouTube URL: {sourceUrl}");
                    var resolvedUrl = await ResolveYouTubeUrlAsync(sourceUrl);
                    if (!string.IsNullOrEmpty(resolvedUrl))
                    {
                        App.Log($"Successfully resolved YouTube stream: {resolvedUrl}");
                        sourceUrl = resolvedUrl;
                    }
                    else
                    {
                        App.Log($"Failed to resolve YouTube stream for URL: {sourceUrl}");
                        HandlePlaybackFailure("Failed to resolve YouTube stream URL.");
                        return;
                    }
                }

                App.Log($"Source playback URL: {sourceUrl}");

                if (string.IsNullOrEmpty(sourceUrl))
                {
                    // Fallback to first available URL
                    sourceUrl = _video.GetUrlForFormat(VideoFormat.v1080pH264);
                    App.Log($"URL was empty, fallback to H264: {sourceUrl}");
                }

                if (!string.IsNullOrEmpty(sourceUrl))
                {
                    bool isYoutube = sourceUrl.Contains("googlevideo.com", StringComparison.OrdinalIgnoreCase) || 
                                     sourceUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || 
                                     sourceUrl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

                    if (_localProxy != null && sourceUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !isYoutube)
                    {
                        string originalUrl = sourceUrl;
                        sourceUrl = $"{_localProxy.BaseUrl}?url={Uri.EscapeDataString(sourceUrl)}";
                        App.Log($"Redirected playback through local proxy: {sourceUrl}");
                    }

                    Player.Source = new Uri(sourceUrl);
                    Player.Play();
                }
                else
                {
                    App.Log("No playable URL was resolved for video.");
                }
            }
            catch (Exception ex)
            {
                App.Log($"Failed to start video playback: {ex.Message}");
            }
        }

        private bool IsYouTubeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || 
                   url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> ResolveYouTubeUrlAsync(string youtubeUrl)
        {
            try
            {
                var youtube = new YoutubeClient();
                var videoId = YoutubeExplode.Videos.VideoId.TryParse(youtubeUrl);
                if (videoId == null) return null;

                var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoId.Value);
                
                // 1. Try highest quality H.264 (avc) video-only streams (perfect for muted screensaver)
                var h264VideoStreams = streamManifest.GetVideoStreams()
                    .Where(s => s.VideoCodec.Contains("avc", StringComparison.OrdinalIgnoreCase));
                if (h264VideoStreams.Any())
                {
                    var streamInfo = h264VideoStreams.GetWithHighestVideoQuality();
                    if (streamInfo != null)
                    {
                        return streamInfo.Url;
                    }
                }

                // 2. Try highest quality H.264 (avc) muxed streams
                var h264MuxedStreams = streamManifest.GetMuxedStreams()
                    .Where(s => s.VideoCodec.Contains("avc", StringComparison.OrdinalIgnoreCase));
                if (h264MuxedStreams.Any())
                {
                    var streamInfo = h264MuxedStreams.GetWithHighestVideoQuality();
                    if (streamInfo != null)
                    {
                        return streamInfo.Url;
                    }
                }

                // 3. Fallback to any muxed streams
                var muxedStreams = streamManifest.GetMuxedStreams();
                if (muxedStreams.Any())
                {
                    var streamInfo = muxedStreams.GetWithHighestVideoQuality();
                    if (streamInfo != null)
                    {
                        return streamInfo.Url;
                    }
                }

                // 4. Last resort: any video streams
                var videoStreams = streamManifest.GetVideoStreams();
                if (videoStreams.Any())
                {
                    var videoStreamInfo = videoStreams.GetWithHighestVideoQuality();
                    if (videoStreamInfo != null)
                    {
                        return videoStreamInfo.Url;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error resolving YouTube URL: {ex.Message}");
            }
            return null;
        }

        private void ParsePointsOfInterest()
        {
            _poiList.Clear();
            if (_video.PointsOfInterest == null) return;

            foreach (var kvp in _video.PointsOfInterest)
            {
                if (double.TryParse(kvp.Key, out double seconds))
                {
                    string val = kvp.Value ?? "";
                    // Only accept POI text if it is a clean, human-readable description (not a raw key with underscores)
                    if (!string.IsNullOrWhiteSpace(val) && !val.Contains("_") && !val.EndsWith("_NAME", StringComparison.OrdinalIgnoreCase))
                    {
                        _poiList.Add((seconds, val));
                    }
                }
            }
            // Sort by playback timestamp
            _poiList = _poiList.OrderBy(p => p.Time).ToList();
        }

        private System.Windows.Media.FontFamily SafeGetFontFamily(string name)
        {
            try
            {
                return new System.Windows.Media.FontFamily(name ?? "Segoe UI");
            }
            catch
            {
                return new System.Windows.Media.FontFamily("Segoe UI");
            }
        }

        private System.Windows.Media.Brush SafeGetBrush(string hexColor)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor ?? "#FFFFFF");
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch
            {
                return System.Windows.Media.Brushes.White;
            }
        }

        private void SetupOverlays()
        {
            // Clear existing panels
            PanelTopLeft.Children.Clear();
            PanelTopRight.Children.Clear();
            PanelBottomLeft.Children.Clear();
            PanelBottomRight.Children.Clear();
            PanelCenter.Children.Clear();

            // CLOCK OVERLAY
            if (_settings.ShowClock)
            {
                var clockStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical };
                var clockFont = SafeGetFontFamily(_settings.ClockFontFamily);
                var clockBrush = SafeGetBrush(_settings.ClockFontColor);
                
                _clockText = new TextBlock
                {
                    FontSize = _settings.ClockFontSize,
                    FontWeight = FontWeights.Bold,
                    FontFamily = clockFont,
                    Foreground = clockBrush,
                    Effect = (System.Windows.Media.Effects.DropShadowEffect)Resources["TextShadow"]
                };
                
                _clockDateText = new TextBlock
                {
                    FontSize = _settings.ClockFontSize * 0.4,
                    FontWeight = FontWeights.Normal,
                    FontFamily = clockFont,
                    Foreground = clockBrush,
                    Margin = new Thickness(0, -4, 0, 0),
                    Effect = (System.Windows.Media.Effects.DropShadowEffect)Resources["TextShadow"]
                };

                clockStack.Children.Add(_clockText);
                clockStack.Children.Add(_clockDateText);
                
                AddControlToPosition(clockStack, _settings.ClockPosition);
            }

            // LOCATION / POI OVERLAY
            if (_settings.ShowLocationPOI)
            {
                var locFont = SafeGetFontFamily(_settings.LocationFontFamily);
                var locBrush = SafeGetBrush(_settings.LocationFontColor);

                string displayLocation = !string.IsNullOrEmpty(_video.SecondaryName) ? _video.SecondaryName : _video.Name;
                if (displayLocation.EndsWith("_NAME", StringComparison.OrdinalIgnoreCase) || displayLocation.Contains("_A0") || displayLocation.Contains("_C0"))
                {
                    displayLocation = "Aerial Video";
                }

                _currentPoiText = displayLocation;

                _locationText = new TextBlock
                {
                    Text = displayLocation,
                    FontSize = _settings.LocationFontSize,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = locFont,
                    Foreground = locBrush,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 600,
                    Opacity = 1.0, // Permanently visible
                    Effect = (System.Windows.Media.Effects.DropShadowEffect)Resources["TextShadow"]
                };

                AddControlToPosition(_locationText, _settings.LocationPosition);
            }

            // WEATHER OVERLAY
            if (_settings.ShowWeather && !string.IsNullOrEmpty(_settings.WeatherApiKey))
            {
                _weatherPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Opacity = 0 };
                var weatherFont = SafeGetFontFamily(_settings.WeatherFontFamily);
                var weatherBrush = SafeGetBrush(_settings.WeatherFontColor);
                
                _weatherText = new TextBlock
                {
                    FontSize = _settings.WeatherFontSize,
                    FontWeight = FontWeights.Medium,
                    FontFamily = weatherFont,
                    Foreground = weatherBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = (System.Windows.Media.Effects.DropShadowEffect)Resources["TextShadow"]
                };

                _weatherPanel.Children.Add(_weatherText);
                AddControlToPosition(_weatherPanel, _settings.WeatherPosition);
                
                // Fetch weather on a background thread
                Task.Run(() => FetchWeatherAsync());
            }

            // NOW PLAYING OVERLAY
            if (_settings.ShowNowPlaying)
            {
                _nowPlayingPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Opacity = 0 };
                var npFont = SafeGetFontFamily(_settings.NowPlayingFontFamily);
                var npBrush = SafeGetBrush(_settings.NowPlayingFontColor);
                
                var musicIcon = new TextBlock
                {
                    Text = "🎵 ",
                    FontSize = _settings.NowPlayingFontSize,
                    FontFamily = npFont,
                    Foreground = npBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = (System.Windows.Media.Effects.DropShadowEffect)Resources["TextShadow"]
                };

                _nowPlayingText = new TextBlock
                {
                    FontSize = _settings.NowPlayingFontSize,
                    FontWeight = FontWeights.Medium,
                    FontFamily = npFont,
                    Foreground = npBrush,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Effect = (System.Windows.Media.Effects.DropShadowEffect)Resources["TextShadow"]
                };

                _nowPlayingPanel.Children.Add(musicIcon);
                _nowPlayingPanel.Children.Add(_nowPlayingText);
                
                AddControlToPosition(_nowPlayingPanel, _settings.NowPlayingPosition);
            }
        }

        private void AddControlToPosition(FrameworkElement element, string position)
        {
            switch (position)
            {
                case "TopLeft":
                    PanelTopLeft.Children.Add(element);
                    break;
                case "TopRight":
                    PanelTopRight.Children.Add(element);
                    break;
                case "BottomLeft":
                    PanelBottomLeft.Children.Add(element);
                    break;
                case "BottomRight":
                    PanelBottomRight.Children.Add(element);
                    break;
                case "Center":
                    PanelCenter.Children.Add(element);
                    break;
                default:
                    PanelBottomLeft.Children.Add(element);
                    break;
            }
        }

        private void StartOverlayTimers()
        {
            _overlayTimer = new DispatcherTimer();
            _overlayTimer.Interval = TimeSpan.FromMilliseconds(500);
            _overlayTimer.Tick += OverlayTimer_Tick;
            _overlayTimer.Start();

            if (_settings.ShowNowPlaying)
            {
                _nowPlayingTimer = new DispatcherTimer();
                _nowPlayingTimer.Interval = TimeSpan.FromSeconds(3);
                _nowPlayingTimer.Tick += NowPlayingTimer_Tick;
                _nowPlayingTimer.Start();
                UpdateNowPlayingAsync();
            }
        }

        private void OverlayTimer_Tick(object? sender, EventArgs e)
        {
            // Update Clock
            if (_clockText != null)
            {
                _clockText.Text = DateTime.Now.ToString("h:mm tt");
            }
            if (_clockDateText != null)
            {
                _clockDateText.Text = DateTime.Now.ToString("dddd, MMMM d");
            }

            // Update Location POI based on player position
            if (_locationText != null)
            {
                string baseLocation = !string.IsNullOrEmpty(_video.SecondaryName) ? _video.SecondaryName : _video.Name;
                if (baseLocation.EndsWith("_NAME", StringComparison.OrdinalIgnoreCase) || baseLocation.Contains("_A0") || baseLocation.Contains("_C0"))
                {
                    baseLocation = "Aerial Video";
                }
                string targetText = baseLocation;

                if (_poiList.Count > 0)
                {
                    double currentSecs = Player.Position.TotalSeconds;
                    foreach (var poi in _poiList)
                    {
                        if (currentSecs >= poi.Time)
                        {
                            targetText = poi.Text;
                        }
                    }
                }

                if (_currentPoiText != targetText)
                {
                    _currentPoiText = targetText;
                    FadePoiText(targetText);
                }
            }
        }

        private void FadePoiText(string newText)
        {
            if (_locationText == null) return;

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(1.0));
            fadeOut.Completed += (s, e) => {
                _locationText.Text = newText;
                var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(1.0));
                _locationText.BeginAnimation(OpacityProperty, fadeIn);
            };
            _locationText.BeginAnimation(OpacityProperty, fadeOut);
        }

        private async Task FetchWeatherAsync()
        {
            if (string.IsNullOrWhiteSpace(_settings.WeatherApiKey) || string.IsNullOrWhiteSpace(_settings.WeatherLocation))
                return;

            try
            {
                var client = HttpClientFactory.GetClient(_settings.BypassSslValidation);
                string protocol = _settings.BypassSslValidation ? "http" : "https";

                // Build candidate location queries (e.g., "Monsey, NY" -> "Monsey, NY", "Monsey, US", "Monsey")
                var queries = new List<string> { _settings.WeatherLocation.Trim() };
                string raw = _settings.WeatherLocation.Trim();
                if (raw.Contains(','))
                {
                    string city = raw.Split(',')[0].Trim();
                    queries.Add($"{city},US");
                    queries.Add(city);
                }

                string? json = null;
                foreach (var query in queries.Distinct())
                {
                    try
                    {
                        string url = $"{protocol}://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(query)}&appid={_settings.WeatherApiKey.Trim()}&units=imperial";
                        json = await client.GetStringAsync(url);
                        if (!string.IsNullOrEmpty(json)) break;
                    }
                    catch
                    {
                        // Try next candidate location format
                    }
                }

                if (string.IsNullOrEmpty(json))
                {
                    App.Log($"Weather query failed for all candidate locations derived from '{_settings.WeatherLocation}'.");
                    return;
                }

                var doc = JsonDocument.Parse(json);
                double temp = doc.RootElement.GetProperty("main").GetProperty("temp").GetDouble();
                string desc = doc.RootElement.GetProperty("weather")[0].GetProperty("main").GetString() ?? "";

                Dispatcher.Invoke(() => {
                    if (_weatherText != null && _weatherPanel != null)
                    {
                        _weatherText.Text = $"{temp:0}°F, {desc}";
                        
                        // Fade in weather panel
                        var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(2));
                        _weatherPanel.BeginAnimation(OpacityProperty, fadeIn);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Log($"Error fetching weather: {ex.Message}");
            }
        }

        private void NowPlayingTimer_Tick(object? sender, EventArgs e)
        {
            UpdateNowPlayingAsync();
        }

        private async void UpdateNowPlayingAsync()
        {
            if (_nowPlayingText == null || _nowPlayingPanel == null) return;

            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();
                if (session != null)
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    if (props != null && !string.IsNullOrEmpty(props.Title))
                    {
                        string infoText = $"{props.Title} - {props.Artist}";
                        if (_nowPlayingText.Text != infoText)
                        {
                            _nowPlayingText.Text = infoText;
                            if (_nowPlayingPanel.Opacity == 0)
                            {
                                var fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(1));
                                _nowPlayingPanel.BeginAnimation(OpacityProperty, fadeIn);
                            }
                        }
                        return;
                    }
                }
            }
            catch { }

            // Hide now playing if nothing is active
            if (_nowPlayingPanel.Opacity > 0)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(1));
                _nowPlayingPanel.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        // --- Video Event Handlers ---

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            App.Log($"Media opened successfully: {_video.Name} (Duration: {Player.NaturalDuration})");
            _startTime = DateTime.Now; // Reset start time to give mouse/key grace period starting now
            try
            {
                Player.SpeedRatio = _settings.PlaybackSpeed;
                App.Log($"Applied video playback speed ratio: {_settings.PlaybackSpeed}x");
            }
            catch (Exception ex)
            {
                App.Log($"Error setting playback speed ratio: {ex.Message}");
            }
            // Soft fade in for video
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.5));
            Player.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            App.Log($"Media ended, looping: {_video.Name}");
            // Loop video
            Player.Position = TimeSpan.Zero;
            Player.Play();
        }

        private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            HandlePlaybackFailure(e.ErrorException?.Message ?? "Unknown media playback error.");
        }

        private void HandlePlaybackFailure(string errorMessage)
        {
            App.Log($"Media failed: {errorMessage}");
            
            // Try to find a locally cached fallback video to run
            try
            {
                App.Log("Attempting to locate any locally cached video for fallback...");
                var manifestManager = new ManifestManager();
                manifestManager.LoadCachedVideos();

                foreach (var video in manifestManager.Videos)
                {
                    if (_cacheManager.IsVideoCached(video.Id))
                    {
                        App.Log($"Found local cached fallback video: {video.Name} ({video.Id}). Switching player...");
                        _video = video;
                        StartVideo();
                        return;
                    }
                }
                App.Log("No locally cached videos found in the cache folder for fallback.");
            }
            catch (Exception ex)
            {
                App.Log($"Error searching for fallback video: {ex.Message}");
            }

            App.Log("Unable to play stream or find a local fallback. Closing screensaver window.");
            CloseAllAndShutdown();
        }

        // --- Keyboard / Mouse Input Interceptors (Screensaver Dismissal) ---

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_isPreviewMode) return;

            // Support Left/Right arrow keys to skip/rotate video
            if (e.Key == System.Windows.Input.Key.Left || e.Key == System.Windows.Input.Key.Right)
            {
                PlayNextVideo();
                e.Handled = true;
                return;
            }

            if (DateTime.Now - _startTime > TimeSpan.FromSeconds(1.0))
            {
                App.Log($"Exit screensaver: Key pressed ({e.Key})");
                CloseAllAndShutdown();
            }
        }

        private void PlayNextVideo()
        {
            try
            {
                App.Log("Skipping to next video...");
                List<AerialVideo> videos = new();
                if (_settings.UseCustomVideoUrls && _settings.CustomVideoUrls.Count > 0)
                {
                    foreach (var url in _settings.CustomVideoUrls)
                    {
                        videos.Add(new AerialVideo
                        {
                            Id = url,
                            Name = "Custom Stream",
                            SecondaryName = url,
                            Scene = "Custom",
                            Urls = new Dictionary<VideoFormat, string>
                            {
                                { VideoFormat.v1080pH264, url },
                                { VideoFormat.v1080pHEVC, url },
                                { VideoFormat.v4KHEVC, url }
                            }
                        });
                    }
                }
                else
                {
                    var manifestManager = new ManifestManager();
                    manifestManager.LoadCachedVideos();
                    videos = manifestManager.Videos
                        .Where(v => !_settings.HasConfiguredVideos || _settings.EnabledVideoIds.Contains(v.Id))
                        .ToList();

                    if (videos.Count == 0)
                    {
                        videos = manifestManager.Videos;
                    }
                }

                if (videos.Count > 0)
                {
                    var random = new Random();
                    AerialVideo nextVideo = videos[random.Next(videos.Count)];
                    if (videos.Count > 1 && nextVideo.Id == _video.Id)
                    {
                        var alternateVideos = videos.Where(v => v.Id != _video.Id).ToList();
                        if (alternateVideos.Count > 0)
                        {
                            nextVideo = alternateVideos[random.Next(alternateVideos.Count)];
                        }
                    }
                    
                    App.Log($"Selected next video: {nextVideo.Name} ({nextVideo.Id})");
                    _video = nextVideo;
                    
                    // Reset overlay state
                    _poiList.Clear();
                    ParsePointsOfInterest();
                    if (_locationText != null)
                    {
                        string displayLoc = !string.IsNullOrEmpty(_video.SecondaryName) ? _video.SecondaryName : _video.Name;
                        _locationText.Text = displayLoc;
                        _locationText.Opacity = 1.0;
                    }
                    
                    // Reset grace period start time
                    _startTime = DateTime.Now;
                    
                    // Start playback
                    StartVideo();
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error skipping video: {ex.Message}");
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPreviewMode && (DateTime.Now - _startTime > TimeSpan.FromSeconds(1.0)))
            {
                App.Log($"Exit screensaver: Mouse clicked ({e.ChangedButton})");
                CloseAllAndShutdown();
            }
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isPreviewMode) return;

            if (Win32.GetCursorPos(out Win32.POINT currentPhysicalPos))
            {
                // During the first 5 seconds after startup, continuously update initial position to swallow cursor layout/focus shifts
                if (DateTime.Now - _startTime < TimeSpan.FromSeconds(5.0))
                {
                    _initialPhysicalMousePos = currentPhysicalPos;
                    _isPhysicalMouseSet = true;
                    return;
                }

                if (!_isPhysicalMouseSet)
                {
                    _initialPhysicalMousePos = currentPhysicalPos;
                    _isPhysicalMouseSet = true;
                    return;
                }

                double deltaX = Math.Abs(currentPhysicalPos.X - _initialPhysicalMousePos.X);
                double deltaY = Math.Abs(currentPhysicalPos.Y - _initialPhysicalMousePos.Y);

                // Requires intentional physical mouse movement (> 100 pixels) to exit
                if (deltaX > 100 || deltaY > 100)
                {
                    App.Log($"Exit screensaver: Physical Mouse moved (DeltaX: {deltaX}, DeltaY: {deltaY})");
                    CloseAllAndShutdown();
                }
            }
        }

        private void CloseAllAndShutdown()
        {
            App.Log("Shutdown process initiated for screensaver...");
            _overlayTimer?.Stop();
            _nowPlayingTimer?.Stop();
            Player.Close();
            _localProxy?.Dispose();

            // Close all windows across screens
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                try { window.Close(); } catch { }
            }

            App.Log("Shutdown complete. Exiting process.");
            System.Windows.Application.Current.Shutdown();
        }
    }
}
