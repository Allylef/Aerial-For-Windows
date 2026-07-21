using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace AerialWindows
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly CacheManager _cacheManager;
        private readonly ManifestManager _manifestManager;
        private AerialVideo? _selectedVideo;

        private readonly string[] _positions = { "TopLeft", "TopRight", "BottomLeft", "BottomRight", "Center" };
        private readonly string[] _fonts = {
            "Segoe UI", "Arial", "Times New Roman", "Georgia", "Verdana", "Courier New", "Consolas", 
            "Trebuchet MS", "Impact", "Century Gothic", "Garamond", "Calibri", 
            "Montserrat", "Inter", "Roboto", "Outfit", "Lora", "Playfair Display"
        };

        public SettingsWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();
            _cacheManager = new CacheManager(_settings);
            _manifestManager = new ManifestManager();
            _manifestManager.LoadCachedVideos();

            LoadPositions();
            LoadSettingsToUi();
            PopulateVideoTree();
            UpdateCacheUsageDisplay();
        }

        private void LoadPositions()
        {
            ComboClockPos.ItemsSource = _positions;
            ComboLocationPos.ItemsSource = _positions;
            ComboWeatherPos.ItemsSource = _positions;
            ComboNowPlayingPos.ItemsSource = _positions;

            ComboClockFont.ItemsSource = _fonts;
            ComboLocationFont.ItemsSource = _fonts;
            ComboWeatherFont.ItemsSource = _fonts;
            ComboNowPlayingFont.ItemsSource = _fonts;
        }

        private void LoadSettingsToUi()
        {
            // Playback speed
            foreach (ComboBoxItem item in ComboPlaybackSpeed.Items)
            {
                if (double.TryParse(item.Tag?.ToString(), out double speedVal) && Math.Abs(speedVal - _settings.PlaybackSpeed) < 0.01)
                {
                    ComboPlaybackSpeed.SelectedItem = item;
                    break;
                }
            }
            if (ComboPlaybackSpeed.SelectedItem == null) ComboPlaybackSpeed.SelectedIndex = 2; // Default 1.0x

            // App Theme
            foreach (ComboBoxItem item in ComboAppTheme.Items)
            {
                if (item.Tag?.ToString() == _settings.AppTheme)
                {
                    ComboAppTheme.SelectedItem = item;
                    break;
                }
            }
            if (ComboAppTheme.SelectedItem == null) ComboAppTheme.SelectedIndex = 0; // Default Dark
            ApplyTheme(_settings.AppTheme);

            // Custom Video Streams settings
            ChkUseCustomUrls.IsChecked = _settings.UseCustomVideoUrls;
            ListCustomUrls.Items.Clear();
            foreach (var url in _settings.CustomVideoUrls)
            {
                ListCustomUrls.Items.Add(url);
            }

            // Format Quality
            foreach (ComboBoxItem item in ComboPreferredFormat.Items)
            {
                if (item.Tag?.ToString() == _settings.PreferredFormat.ToString())
                {
                    ComboPreferredFormat.SelectedItem = item;
                    break;
                }
            }

            // Caching settings
            ChkEnableCaching.IsChecked = _settings.EnableCaching;
            TxtCacheFolder.Text = _settings.CacheFolder;
            SliderCacheLimit.Value = _settings.CacheSizeLimitGb;
            TxtCacheLimitVal.Text = $"{_settings.CacheSizeLimitGb} GB";

            // Multi-monitor settings
            ChkMultiMonitor.IsChecked = _settings.PlayDifferentVideoPerScreen;
            ChkBypassSsl.IsChecked = _settings.BypassSslValidation;

            // Overlays configurations
            ChkShowClock.IsChecked = _settings.ShowClock;
            ComboClockPos.SelectedItem = _settings.ClockPosition;
            TxtClockSize.Text = _settings.ClockFontSize.ToString();
            ComboClockFont.SelectedItem = _settings.ClockFontFamily ?? "Segoe UI";
            if (ComboClockFont.SelectedItem == null) ComboClockFont.SelectedIndex = 0;
            TxtClockColor.Text = _settings.ClockFontColor ?? "#FFFFFF";

            ChkShowLocation.IsChecked = _settings.ShowLocationPOI;
            ComboLocationPos.SelectedItem = _settings.LocationPosition;
            TxtLocationSize.Text = _settings.LocationFontSize.ToString();
            ComboLocationFont.SelectedItem = _settings.LocationFontFamily ?? "Segoe UI";
            if (ComboLocationFont.SelectedItem == null) ComboLocationFont.SelectedIndex = 0;
            TxtLocationColor.Text = _settings.LocationFontColor ?? "#FFFFFF";

            ChkShowWeather.IsChecked = _settings.ShowWeather;
            ComboWeatherPos.SelectedItem = _settings.WeatherPosition;
            TxtWeatherSize.Text = _settings.WeatherFontSize.ToString();
            TxtWeatherLocation.Text = _settings.WeatherLocation;
            TxtWeatherApiKey.Text = _settings.WeatherApiKey;
            ComboWeatherFont.SelectedItem = _settings.WeatherFontFamily ?? "Segoe UI";
            if (ComboWeatherFont.SelectedItem == null) ComboWeatherFont.SelectedIndex = 0;
            TxtWeatherColor.Text = _settings.WeatherFontColor ?? "#FFFFFF";

            ChkShowNowPlaying.IsChecked = _settings.ShowNowPlaying;
            ComboNowPlayingPos.SelectedItem = _settings.NowPlayingPosition;
            TxtNowPlayingSize.Text = _settings.NowPlayingFontSize.ToString();
            ComboNowPlayingFont.SelectedItem = _settings.NowPlayingFontFamily ?? "Segoe UI";
            if (ComboNowPlayingFont.SelectedItem == null) ComboNowPlayingFont.SelectedIndex = 0;
            TxtNowPlayingColor.Text = _settings.NowPlayingFontColor ?? "#FFFFFF";

            // Video Zoom
            SliderVideoZoom.Value = (_settings.VideoZoom > 0 ? _settings.VideoZoom : 1.0) * 100.0;
            TxtVideoZoomVal.Text = $"{(int)SliderVideoZoom.Value}%";

            // Update preview borders and labels
            UpdatePreviewLabels();

            // Setup listeners
            SliderCacheLimit.ValueChanged += (s, e) => {
                TxtCacheLimitVal.Text = $"{(int)SliderCacheLimit.Value} GB";
            };
            SliderVideoZoom.ValueChanged += (s, e) => {
                TxtVideoZoomVal.Text = $"{(int)SliderVideoZoom.Value}%";
            };
        }

        private void PopulateVideoTree()
        {
            VideosTreeView.Items.Clear();

            if (_manifestManager.Videos.Count == 0)
            {
                var noVideosItem = new TreeViewItem { Header = "No videos loaded. Click 'Update Video Manifests' above." };
                VideosTreeView.Items.Add(noVideosItem);
                return;
            }

            TreeViewItem? firstVideoItem = null;
            AerialVideo? firstVideo = null;

            // Group videos by Scene/Category
            var grouped = _manifestManager.Videos
                .GroupBy(v => v.Scene)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var categoryItem = new TreeViewItem
                {
                    Header = $"{group.Key} ({group.Count()} videos)",
                    IsExpanded = false
                };

                foreach (var video in group.OrderBy(v => v.Name))
                {
                    var videoPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    
                    var videoItem = new TreeViewItem
                    {
                        Tag = video
                    };

                    if (firstVideoItem == null)
                    {
                        firstVideoItem = videoItem;
                        firstVideo = video;
                        categoryItem.IsExpanded = true;
                    }

                    var checkBox = new System.Windows.Controls.CheckBox
                    {
                        Content = $"{video.Name} - {video.SecondaryName}",
                        Tag = video,
                        IsChecked = !_settings.HasConfiguredVideos || _settings.EnabledVideoIds.Contains(video.Id),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    checkBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimary");

                    checkBox.Checked += VideoCheckBox_Changed;
                    checkBox.Unchecked += VideoCheckBox_Changed;

                    videoItem.PreviewMouseLeftButtonDown += (s, ev) => {
                        videoItem.IsSelected = true;
                        videoItem.Focus();
                        SelectVideo(video);
                    };

                    videoPanel.Children.Add(checkBox);
                    videoItem.Header = videoPanel;

                    categoryItem.Items.Add(videoItem);
                }

                VideosTreeView.Items.Add(categoryItem);
            }

            // Auto-select the first video so details and buttons are active immediately
            if (firstVideoItem != null && firstVideo != null)
            {
                firstVideoItem.IsSelected = true;
                firstVideoItem.Focus();
                SelectVideo(firstVideo);
            }
        }

        private void VideoCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.Tag is AerialVideo video)
            {
                bool isChecked = checkBox.IsChecked == true;
                if (isChecked)
                {
                    if (!_settings.EnabledVideoIds.Contains(video.Id))
                        _settings.EnabledVideoIds.Add(video.Id);
                }
                else
                {
                    _settings.EnabledVideoIds.Remove(video.Id);
                }
            }
        }

        private void SidebarMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageVideos == null || PageOverlays == null || PageCache == null) return;
            PageVideos.Visibility = SidebarMenu.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageOverlays.Visibility = SidebarMenu.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageCache.Visibility = SidebarMenu.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            _settings.HasConfiguredVideos = true;
            _settings.EnabledVideoIds.Clear();
            foreach (var video in _manifestManager.Videos)
            {
                _settings.EnabledVideoIds.Add(video.Id);
            }
            PopulateVideoTree();
            TxtStatus.Text = "All videos selected.";
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            _settings.HasConfiguredVideos = true;
            _settings.EnabledVideoIds.Clear();
            PopulateVideoTree();
            TxtStatus.Text = "All videos deselected.";
        }

        private void SelectVideo(AerialVideo video)
        {
            _selectedVideo = video;
            TxtVideoName.Text = video.Name;
            TxtVideoDesc.Text = video.SecondaryName;
            TxtVideoScene.Text = video.Scene;
            VideoDetailsPanel.Visibility = Visibility.Visible;

            UpdateSelectedVideoCacheStatus();

            BtnPreviewVideo.IsEnabled = true;
            BtnDownloadVideo.IsEnabled = true;
        }

        private void ClearSelectedVideo()
        {
            _selectedVideo = null;
            TxtVideoName.Text = "No Video Selected";
            TxtVideoDesc.Text = "Select a video from the list on the left to view details and options.";
            VideoDetailsPanel.Visibility = Visibility.Collapsed;

            BtnPreviewVideo.IsEnabled = false;
            BtnDownloadVideo.IsEnabled = false;
        }

        private void VideosTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is AerialVideo video)
            {
                SelectVideo(video);
            }
            else
            {
                ClearSelectedVideo();
            }
        }

        private void UpdateSelectedVideoCacheStatus()
        {
            if (_selectedVideo == null) return;

            bool isCached = _cacheManager.IsVideoCached(_selectedVideo.Id);
            if (isCached)
            {
                TxtCacheStatus.Text = "Cached (Local Playback)";
                TxtCacheStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                BtnDownloadVideo.Content = "Re-download Video";
            }
            else
            {
                TxtCacheStatus.Text = "Not Cached (Will stream over Internet)";
                TxtCacheStatus.Foreground = System.Windows.Media.Brushes.Orange;
                BtnDownloadVideo.Content = "Download/Cache Video";
            }
        }

        private void UpdateCacheUsageDisplay()
        {
            double bytes = _cacheManager.GetCacheSizeInBytes();
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            TxtCurrentCacheUsage.Text = $"{gb:F2} GB used";
        }

        private async void BtnUpdateManifests_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Updating manifests from Apple TV...";
            BtnUpdateManifests.IsEnabled = false;

            try
            {
                await _manifestManager.UpdateManifestsAsync((msg, val) => {
                    Dispatcher.Invoke(() => {
                        TxtStatus.Text = $"{msg} ({(val * 100):0}%)";
                    });
                });
                PopulateVideoTree();
                TxtStatus.Text = "Video playlists updated successfully!";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Failed to download manifests: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnUpdateManifests.IsEnabled = true;
            }
        }

        private void BtnPreviewVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVideo == null) return;

            string url = _cacheManager.GetVideoPlaybackUrl(_selectedVideo);
            if (string.IsNullOrEmpty(url)) return;

            LocalVideoProxy? proxy = null;
            if (_settings.BypassSslValidation && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var client = HttpClientFactory.GetClient(true);
                proxy = new LocalVideoProxy(client);
                proxy.Start();
                url = $"{proxy.BaseUrl}?url={Uri.EscapeDataString(url)}";
            }

            // Spawn preview player window
            var playerWindow = new Window
            {
                Title = $"Preview - {_selectedVideo.Name}",
                Width = 800,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = System.Windows.Media.Brushes.Black
            };

            var grid = new Grid();
            var media = new MediaElement
            {
                Source = new Uri(url),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                Volume = 0 // Mute previews
            };

            media.MediaEnded += (s, ev) => {
                media.Position = TimeSpan.Zero;
                media.Play();
            };

            grid.Children.Add(media);
            playerWindow.Content = grid;

            playerWindow.Loaded += (s, ev) => media.Play();
            playerWindow.Closed += (s, ev) => {
                media.Close();
                proxy?.Dispose();
            };

            playerWindow.ShowDialog();
        }

        private async void BtnDownloadVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedVideo == null) return;
            await DownloadVideosWithProgressAsync(new List<AerialVideo> { _selectedVideo });
        }

        private async void BtnDownloadAllChecked_Click(object sender, RoutedEventArgs e)
        {
            var checkedVideos = _manifestManager.Videos
                .Where(v => _settings.EnabledVideoIds.Contains(v.Id))
                .ToList();

            if (checkedVideos.Count == 0)
            {
                MessageBox.Show("No videos are currently checked in the playlist.", "No Videos Checked", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to download all {checkedVideos.Count} checked video(s)? This may take some time depending on your connection.", "Confirm Download", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                await DownloadVideosWithProgressAsync(checkedVideos);
            }
        }

        private void BtnAddCustomUrl_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtCustomUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Please enter a valid HTTP/HTTPS URL.", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_settings.CustomVideoUrls.Contains(url))
            {
                MessageBox.Show("This URL has already been added.", "Duplicate URL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings.CustomVideoUrls.Add(url);
            ListCustomUrls.Items.Add(url);
            TxtCustomUrl.Clear();
            TxtStatus.Text = "Custom URL added successfully.";
        }

        private void BtnRemoveCustomUrl_Click(object sender, RoutedEventArgs e)
        {
            if (ListCustomUrls.SelectedItem is string selectedUrl)
            {
                _settings.CustomVideoUrls.Remove(selectedUrl);
                ListCustomUrls.Items.Remove(selectedUrl);
                TxtStatus.Text = "Custom URL removed.";
            }
            else
            {
                MessageBox.Show("Please select a URL to remove.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task DownloadVideosWithProgressAsync(List<AerialVideo> videos)
        {
            if (videos.Count == 0) return;

            IsEnabled = false;
            var cts = new CancellationTokenSource();

            var progressWindow = new Window
            {
                Title = videos.Count == 1 ? "Downloading Video Asset" : "Downloading Checked Video Assets",
                Width = 450,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x22, 0x2B)),
                Foreground = System.Windows.Media.Brushes.White,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            progressWindow.Closed += (s, e) =>
            {
                cts.Cancel();
                IsEnabled = true;
                UpdateSelectedVideoCacheStatus();
                UpdateCacheUsageDisplay();
            };

            var mainGrid = new Grid { Margin = new Thickness(20, 15, 20, 15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var txtStatusLabel = new TextBlock
            {
                Text = $"Preparing download of {videos.Count} video(s)...",
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, 8),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(txtStatusLabel, 0);

            var progressBar = new System.Windows.Controls.ProgressBar
            {
                Height = 15,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1)),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x34, 0x40)),
                BorderThickness = new Thickness(0)
            };
            Grid.SetRow(progressBar, 1);

            bool isDone = false;
            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "Cancel Download",
                Width = 120,
                Height = 28,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Style = (Style)Resources["SecondaryButton"]
            };
            btnCancel.Click += (s, e) => {
                if (isDone)
                {
                    progressWindow.Close();
                }
                else
                {
                    cts.Cancel();
                    txtStatusLabel.Text = "Cancelling downloads...";
                    btnCancel.IsEnabled = false;
                }
            };
            Grid.SetRow(btnCancel, 3);

            mainGrid.Children.Add(txtStatusLabel);
            mainGrid.Children.Add(progressBar);
            mainGrid.Children.Add(btnCancel);
            progressWindow.Content = mainGrid;

            progressWindow.Show();

            int completedCount = 0;
            bool wasCancelled = false;

            try
            {
                for (int i = 0; i < videos.Count; i++)
                {
                    if (cts.Token.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        break;
                    }

                    var video = videos[i];
                    string prefix = videos.Count == 1 ? "" : $"[{i + 1}/{videos.Count}] ";
                    txtStatusLabel.Text = $"{prefix}Downloading {video.Name}...";
                    progressBar.Value = 0;

                    var progress = new Progress<double>(percent => {
                        progressBar.Value = percent * 100;
                        txtStatusLabel.Text = $"{prefix}Downloading {video.Name}... {(percent * 100):0}%";
                    });

                    try
                    {
                        await _cacheManager.DownloadVideoAsync(video, progress, cts.Token);
                        completedCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        wasCancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        App.Log($"Error downloading {video.Name}: {ex.Message}");
                        if (videos.Count == 1)
                        {
                            txtStatusLabel.Text = $"Failed: {ex.Message}";
                        }
                    }
                }
            }
            finally
            {
                isDone = true;
                btnCancel.Content = "Close";
                btnCancel.IsEnabled = true;

                if (wasCancelled)
                {
                    txtStatusLabel.Text = $"Cancelled. Cached {completedCount} of {videos.Count} videos.";
                    TxtStatus.Text = $"Downloads cancelled. Cached {completedCount} of {videos.Count} videos.";
                }
                else
                {
                    txtStatusLabel.Text = $"Success! Cached {completedCount} of {videos.Count} videos.";
                    progressBar.Value = 100;
                    TxtStatus.Text = $"Downloads complete! Successfully cached {completedCount} of {videos.Count} videos.";
                }
            }
        }

        private void BtnChangeCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = TxtCacheFolder.Text;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtCacheFolder.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnClearCache_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to delete all cached videos?", "Clear Cache", 
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _cacheManager.ClearCache();
                UpdateCacheUsageDisplay();
                UpdateSelectedVideoCacheStatus();
                TxtStatus.Text = "Local cache cleared.";
            }
        }

        private void BtnSetDefaultScreensaver_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string scrPath = Path.ChangeExtension(exePath, ".scr");
                string targetPath = File.Exists(scrPath) ? scrPath : exePath;

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    if (key != null)
                    {
                        key.SetValue("SCRNSAVE.EXE", targetPath);
                        key.SetValue("ScreenSaveActive", "1");
                    }
                }

                MessageBox.Show($"Aerial for Windows has been set as your default Windows screensaver!\n\nTarget path registered:\n{targetPath}", "Default Screensaver Set", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatus.Text = "Registered as default Windows screensaver.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set default screensaver in registry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktopPath, "Start Aerial Screensaver.lnk");
                string targetPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";

                if (string.IsNullOrEmpty(targetPath))
                {
                    MessageBox.Show("Could not resolve current executable path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.Arguments = "/s";
                    shortcut.Description = "Instantly trigger Aerial for Windows Screensaver";
                    shortcut.Save();

                    MessageBox.Show("Instant play shortcut created on your Desktop!\n\nTo set a keyboard hotkey (e.g. Ctrl+Alt+S):\n1. Right-click the new shortcut on your Desktop.\n2. Choose Properties.\n3. Click in the 'Shortcut key' box and press your preferred hotkey combination.\n4. Click OK.", "Shortcut Created", MessageBoxButton.OK, MessageBoxImage.Information);
                    TxtStatus.Text = "Shortcut created on Desktop.";
                }
                else
                {
                    MessageBox.Show("Could not initialize Windows Script Host to create shortcut.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create shortcut: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Save state to settings
            if (ComboPreferredFormat.SelectedItem is ComboBoxItem qualityItem &&
                Enum.TryParse(qualityItem.Tag?.ToString(), out VideoFormat preferredFormat))
            {
                _settings.PreferredFormat = preferredFormat;
            }

            _settings.EnableCaching = ChkEnableCaching.IsChecked == true;
            _settings.CacheFolder = TxtCacheFolder.Text;
            _settings.CacheSizeLimitGb = (int)SliderCacheLimit.Value;
            _settings.PlayDifferentVideoPerScreen = ChkMultiMonitor.IsChecked == true;
            _settings.BypassSslValidation = ChkBypassSsl.IsChecked == true;
            _settings.UseCustomVideoUrls = ChkUseCustomUrls.IsChecked == true;

            if (ComboPlaybackSpeed.SelectedItem is ComboBoxItem speedItem &&
                double.TryParse(speedItem.Tag?.ToString(), out double parsedSpeed))
            {
                _settings.PlaybackSpeed = parsedSpeed;
            }

            _settings.VideoZoom = SliderVideoZoom.Value / 100.0;

            if (ComboAppTheme.SelectedItem is ComboBoxItem themeItem)
            {
                _settings.AppTheme = themeItem.Tag?.ToString() ?? "Dark";
            }

            _settings.ShowClock = ChkShowClock.IsChecked == true;
            _settings.ClockPosition = ComboClockPos.SelectedItem?.ToString() ?? "TopRight";
            if (double.TryParse(TxtClockSize.Text, out double clockSize)) _settings.ClockFontSize = clockSize;
            _settings.ClockFontFamily = ComboClockFont.SelectedItem?.ToString() ?? "Segoe UI";
            _settings.ClockFontColor = TxtClockColor.Text;

            _settings.ShowLocationPOI = ChkShowLocation.IsChecked == true;
            _settings.LocationPosition = ComboLocationPos.SelectedItem?.ToString() ?? "BottomLeft";
            if (double.TryParse(TxtLocationSize.Text, out double locSize)) _settings.LocationFontSize = locSize;
            _settings.LocationFontFamily = ComboLocationFont.SelectedItem?.ToString() ?? "Segoe UI";
            _settings.LocationFontColor = TxtLocationColor.Text;

            _settings.ShowWeather = ChkShowWeather.IsChecked == true;
            _settings.WeatherPosition = ComboWeatherPos.SelectedItem?.ToString() ?? "TopLeft";
            if (double.TryParse(TxtWeatherSize.Text, out double weatherSize)) _settings.WeatherFontSize = weatherSize;
            _settings.WeatherLocation = TxtWeatherLocation.Text;
            _settings.WeatherApiKey = TxtWeatherApiKey.Text;
            _settings.WeatherFontFamily = ComboWeatherFont.SelectedItem?.ToString() ?? "Segoe UI";
            _settings.WeatherFontColor = TxtWeatherColor.Text;

            _settings.ShowNowPlaying = ChkShowNowPlaying.IsChecked == true;
            _settings.NowPlayingPosition = ComboNowPlayingPos.SelectedItem?.ToString() ?? "BottomRight";
            if (double.TryParse(TxtNowPlayingSize.Text, out double musicSize)) _settings.NowPlayingFontSize = musicSize;
            _settings.NowPlayingFontFamily = ComboNowPlayingFont.SelectedItem?.ToString() ?? "Segoe UI";
            _settings.NowPlayingFontColor = TxtNowPlayingColor.Text;

            _settings.HasConfiguredVideos = true;
            _settings.Save();
            Close();
        }

        public void ApplyTheme(string themeName)
        {
            string activeTheme = themeName;
            if (activeTheme.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                activeTheme = GetWindowsPersonalizationTheme();
            }

            bool isDark = activeTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase);

            // Theme Brushes
            var bgDarkColor = isDark ? System.Windows.Media.Color.FromRgb(0x12, 0x14, 0x1A) : System.Windows.Media.Color.FromRgb(0xF3, 0xF4, 0xF6);
            var bgCardColor = isDark ? System.Windows.Media.Color.FromRgb(0x1E, 0x22, 0x2B) : System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
            var bgHoverColor = isDark ? System.Windows.Media.Color.FromRgb(0x2A, 0x30, 0x3D) : System.Windows.Media.Color.FromRgb(0xE5, 0xE7, 0xEB);
            var borderBrushColor = isDark ? System.Windows.Media.Color.FromRgb(0x2E, 0x34, 0x40) : System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB);
            var textPrimaryColor = isDark ? System.Windows.Media.Color.FromRgb(0xF3, 0xF4, 0xF6) : System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x27);
            var textSecondaryColor = isDark ? System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF) : System.Windows.Media.Color.FromRgb(0x4B, 0x55, 0x63);

            // Update Resource Dictionary in SettingsWindow
            Resources["BgDark"] = new System.Windows.Media.SolidColorBrush(bgDarkColor);
            Resources["BgCard"] = new System.Windows.Media.SolidColorBrush(bgCardColor);
            Resources["BgHover"] = new System.Windows.Media.SolidColorBrush(bgHoverColor);
            Resources["BorderBrush"] = new System.Windows.Media.SolidColorBrush(borderBrushColor);
            Resources["TextPrimary"] = new System.Windows.Media.SolidColorBrush(textPrimaryColor);
            Resources["TextSecondary"] = new System.Windows.Media.SolidColorBrush(textSecondaryColor);

            // Update window level values
            Foreground = new System.Windows.Media.SolidColorBrush(textPrimaryColor);
            Background = new System.Windows.Media.SolidColorBrush(bgDarkColor);
        }

        private string GetWindowsPersonalizationTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        if (value is int i && i == 1)
                        {
                            return "Light";
                        }
                    }
                }
            }
            catch { }
            return "Dark"; // Default fallback
        }

        private void ComboAppTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboAppTheme == null || ComboAppTheme.SelectedItem == null) return;
            if (ComboAppTheme.SelectedItem is ComboBoxItem item)
            {
                ApplyTheme(item.Tag?.ToString() ?? "Dark");
            }
        }

        private void ChooseColor(System.Windows.Controls.TextBox txtBox)
        {
            using (var dialog = new System.Windows.Forms.ColorDialog())
            {
                var existingColor = ParseHexColor(txtBox.Text);
                if (existingColor != null)
                {
                    dialog.Color = System.Drawing.Color.FromArgb(existingColor.Value.R, existingColor.Value.G, existingColor.Value.B);
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtBox.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                }
            }
        }

        private void BtnChooseClockColor_Click(object sender, RoutedEventArgs e) => ChooseColor(TxtClockColor);
        private void BtnChooseLocationColor_Click(object sender, RoutedEventArgs e) => ChooseColor(TxtLocationColor);
        private void BtnChooseWeatherColor_Click(object sender, RoutedEventArgs e) => ChooseColor(TxtWeatherColor);
        private void BtnChooseNowPlayingColor_Click(object sender, RoutedEventArgs e) => ChooseColor(TxtNowPlayingColor);

        private void ColorHexChanged(object sender, TextChangedEventArgs e) => UpdatePreviewLabels();

        private void OverlaySettingChanged(object sender, SelectionChangedEventArgs e) => UpdatePreviewLabels();
        private void OverlaySettingChanged(object sender, TextChangedEventArgs e) => UpdatePreviewLabels();

        private void UpdatePreviewLabels()
        {
            if (LblClockPreview == null) return; // UI not fully initialized

            UpdateColorPreview();

            // Clock Preview
            try
            {
                if (double.TryParse(TxtClockSize.Text, out double size)) LblClockPreview.FontSize = size;
                if (ComboClockFont.SelectedItem != null) LblClockPreview.FontFamily = new System.Windows.Media.FontFamily(ComboClockFont.SelectedItem.ToString());
                var clockColor = ParseHexColor(TxtClockColor.Text);
                if (clockColor != null) LblClockPreview.Foreground = new System.Windows.Media.SolidColorBrush(clockColor.Value);
            } catch {}

            // Location Preview
            try
            {
                if (double.TryParse(TxtLocationSize.Text, out double size)) LblLocationPreview.FontSize = size;
                if (ComboLocationFont.SelectedItem != null) LblLocationPreview.FontFamily = new System.Windows.Media.FontFamily(ComboLocationFont.SelectedItem.ToString());
                var locColor = ParseHexColor(TxtLocationColor.Text);
                if (locColor != null) LblLocationPreview.Foreground = new System.Windows.Media.SolidColorBrush(locColor.Value);
            } catch {}

            // Weather Preview
            try
            {
                if (double.TryParse(TxtWeatherSize.Text, out double size)) LblWeatherPreview.FontSize = size;
                if (ComboWeatherFont.SelectedItem != null) LblWeatherPreview.FontFamily = new System.Windows.Media.FontFamily(ComboWeatherFont.SelectedItem.ToString());
                var weatherColor = ParseHexColor(TxtWeatherColor.Text);
                if (weatherColor != null) LblWeatherPreview.Foreground = new System.Windows.Media.SolidColorBrush(weatherColor.Value);
            } catch {}

            // Now Playing Preview
            try
            {
                if (double.TryParse(TxtNowPlayingSize.Text, out double size)) LblNowPlayingPreview.FontSize = size;
                if (ComboNowPlayingFont.SelectedItem != null) LblNowPlayingPreview.FontFamily = new System.Windows.Media.FontFamily(ComboNowPlayingFont.SelectedItem.ToString());
                var npColor = ParseHexColor(TxtNowPlayingColor.Text);
                if (npColor != null) LblNowPlayingPreview.Foreground = new System.Windows.Media.SolidColorBrush(npColor.Value);
            } catch {}
        }

        private void UpdateColorPreview()
        {
            try { BorderClockColor.Background = new System.Windows.Media.SolidColorBrush(ParseHexColor(TxtClockColor.Text) ?? System.Windows.Media.Colors.White); } catch {}
            try { BorderLocationColor.Background = new System.Windows.Media.SolidColorBrush(ParseHexColor(TxtLocationColor.Text) ?? System.Windows.Media.Colors.White); } catch {}
            try { BorderWeatherColor.Background = new System.Windows.Media.SolidColorBrush(ParseHexColor(TxtWeatherColor.Text) ?? System.Windows.Media.Colors.White); } catch {}
            try { BorderNowPlayingColor.Background = new System.Windows.Media.SolidColorBrush(ParseHexColor(TxtNowPlayingColor.Text) ?? System.Windows.Media.Colors.White); } catch {}
        }

        private System.Windows.Media.Color? ParseHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            try
            {
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return null;
            }
        }
    }
}
