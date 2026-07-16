using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace AerialWindows
{
    public partial class App : System.Windows.Application
    {
        private List<ScreensaverWindow> _screensaverWindows = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up unhandled exception logging
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => {
                Log($"Unhandled exception: {ex.ExceptionObject}");
            };

            string argsStr = string.Join(" ", e.Args);
            Log($"App started. Arguments: {argsStr}");

            string[] args = e.Args;
            if (args.Length > 0)
            {
                string firstArg = args[0].ToLowerInvariant().Trim();

                // Start full screen screensaver
                if (firstArg.StartsWith("/s"))
                {
                    ShowScreensaver();
                    return;
                }

                // Start preview mode inside the screensaver dialog
                if (firstArg.StartsWith("/p"))
                {
                    string hwndStr = "";
                    if (firstArg.Contains(":"))
                    {
                        hwndStr = firstArg.Substring(firstArg.IndexOf(':') + 1).Trim();
                    }
                    else if (args.Length > 1)
                    {
                        hwndStr = args[1].Trim();
                    }

                    if (long.TryParse(hwndStr, out long hwndVal) && hwndVal != 0)
                    {
                        ShowPreview(new IntPtr(hwndVal));
                        return;
                    }
                }

                // Show settings window
                if (firstArg.StartsWith("/c"))
                {
                    ShowSettings();
                    return;
                }
            }

            // Default fallback is to show settings
            ShowSettings();
        }

        private void ShowSettings()
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
            Shutdown();
        }

        private void ShowScreensaver()
        {
            var settings = AppSettings.Load();
            var manifestManager = new ManifestManager();
            manifestManager.LoadCachedVideos();

            // Fetch remote videos if none are cached/loaded
            if (manifestManager.Videos.Count == 0)
            {
                // Synchronously call manifest update to make sure we have something to play
                Task.Run(async () => await manifestManager.UpdateManifestsAsync(null)).Wait(15000);
            }
            if (manifestManager.Videos.Count == 0)
            {
                System.Windows.MessageBox.Show("No videos could be loaded. Please configure the screensaver first.", "Aerial", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Shutdown();
                return;
            }
            var cacheManager = new CacheManager(settings);

            bool useCustom = settings.UseCustomVideoUrls && settings.CustomVideoUrls.Count > 0;
            var playableVideos = new List<AerialVideo>();
            if (!useCustom)
            {
                playableVideos = manifestManager.Videos
                    .Where(v => !settings.HasConfiguredVideos || settings.EnabledVideoIds.Contains(v.Id))
                    .ToList();

                if (playableVideos.Count == 0)
                {
                    playableVideos = manifestManager.Videos;
                }
            }

            var screens = System.Windows.Forms.Screen.AllScreens;
            var random = new Random();

            // Select a single video if "different video per screen" is disabled
            AerialVideo? singleVideo = null;
            if (useCustom)
            {
                if (!settings.PlayDifferentVideoPerScreen)
                {
                    string url = settings.CustomVideoUrls[random.Next(settings.CustomVideoUrls.Count)];
                    singleVideo = CreateMockVideo(url);
                }
            }
            else if (!settings.PlayDifferentVideoPerScreen && playableVideos.Count > 0)
            {
                singleVideo = playableVideos[random.Next(playableVideos.Count)];
            }

            foreach (var screen in screens)
            {
                AerialVideo videoToPlay;
                if (useCustom)
                {
                    string url = settings.CustomVideoUrls[random.Next(settings.CustomVideoUrls.Count)];
                    videoToPlay = singleVideo ?? CreateMockVideo(url);
                }
                else
                {
                    videoToPlay = singleVideo ?? playableVideos[random.Next(playableVideos.Count)];
                }

                var window = new ScreensaverWindow(videoToPlay, cacheManager, settings, isPreviewMode: false);
                _screensaverWindows.Add(window);

                // Set window positions to cover the monitor bounds
                window.Left = screen.Bounds.Left;
                window.Top = screen.Bounds.Top;
                window.Width = screen.Bounds.Width;
                window.Height = screen.Bounds.Height;
                window.WindowState = WindowState.Normal;
                window.WindowStyle = WindowStyle.None;
                window.Topmost = true;
                
                window.Show();
            }
        }

        private void ShowPreview(IntPtr parentHwnd)
        {
            var settings = AppSettings.Load();
            var manifestManager = new ManifestManager();
            manifestManager.LoadCachedVideos();

            if (manifestManager.Videos.Count == 0)
            {
                Task.Run(async () => await manifestManager.UpdateManifestsAsync(null)).Wait(5000);
            }

            var cacheManager = new CacheManager(settings);
            
            // Choose a random video to show in preview
            AerialVideo? video = null;
            if (settings.UseCustomVideoUrls && settings.CustomVideoUrls.Count > 0)
            {
                var random = new Random();
                string url = settings.CustomVideoUrls[random.Next(settings.CustomVideoUrls.Count)];
                video = CreateMockVideo(url);
            }
            else if (manifestManager.Videos.Count > 0)
            {
                var random = new Random();
                video = manifestManager.Videos[random.Next(manifestManager.Videos.Count)];
            }

            if (video == null)
            {
                Shutdown();
                return;
            }

            var window = new ScreensaverWindow(video, cacheManager, settings, isPreviewMode: true);
            _screensaverWindows.Add(window);

            // Use Win32 SetParent to anchor this window inside the preview HWND
            window.Show();

            var helper = new WindowInteropHelper(window);
            IntPtr childHwnd = helper.Handle;

            Win32.SetParent(childHwnd, parentHwnd);
            
            // Modify styles to make it a child window
            int style = Win32.GetWindowLong(childHwnd, Win32.GWL_STYLE);
            style |= Win32.WS_CHILD;
            Win32.SetWindowLong(childHwnd, Win32.GWL_STYLE, style);

            // Resize child window to fit the parent preview boundaries
            if (Win32.GetClientRect(parentHwnd, out var rect))
            {
                window.Left = 0;
                window.Top = 0;
                window.Width = rect.Right - rect.Left;
                window.Height = rect.Bottom - rect.Top;
            }
        }

        private static AerialVideo CreateMockVideo(string url)
        {
            return new AerialVideo
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Custom Stream",
                SecondaryName = url,
                Scene = "Custom",
                Urls = new Dictionary<VideoFormat, string>
                {
                    { VideoFormat.v1080pH264, url },
                    { VideoFormat.v1080pHEVC, url },
                    { VideoFormat.v4KHEVC, url }
                }
            };
        }

        public static void Log(string message)
        {
            try
            {
                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AerialWindows");
                if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                string logPath = System.IO.Path.Combine(logDir, "log.txt");
                System.IO.File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.FFF} - {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
