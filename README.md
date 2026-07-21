# 🌌 Aerial for Windows

[![Framework](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6.svg)](https://microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Aerial for Windows** is a high-performance, modern WPF screensaver for Windows 10 and 11 inspired by Apple TV's iconic Aerial screensavers. Built on **.NET 10**, it brings high-definition Apple aerial video manifests, custom web/YouTube streams, multi-monitor support, dynamic themes, and customizable overlays to your desktop.

🚀 **[Click here to jump to the Download Section](#%EF%B8%8F-download)**

---

## ✨ Features & Highlights

### 🎬 Video Libraries & Streaming
- **Official Apple Aerial Catalog**: Stream or cache 4K HDR, HEVC, and 1080p videos directly from Apple's latest tvOS and macOS manifest sources.
- **Custom YouTube & Web Streams**: Add direct YouTube links or custom video stream URLs to your rotation.
- **High-Resolution YouTube Decoder**: Automatically resolves YouTube streams and prioritizes **H.264 (AVC)** codecs for native 1080p playback without requiring external Store codec packages.
- **Local SSL Loopback Proxy**: Built-in HTTP proxy (`LocalVideoProxy.cs`) routes remote stream requests safely behind corporate proxies or SSL restrictions.
- **Cache & Disk Quota Manager**: Download videos for offline playback with custom storage size caps (5 GB to 100 GB) and single-click cache cleanup.

### 🖥️ Display & Playback Controls
- **Multi-Monitor Support**: Run unique random videos across multiple displays simultaneously or sync playback across screens.
- **Video Zoom & Crop Control**: Fine-tune display zoom from **100% to 120%** to eliminate bezel gaps and edge borders.
- **High Speed Playback Ratios**: Adjust playback speed from **1.0x up to 10.0x** (Maximum) for rapid landscape movement effects.
- **Keyboard Video Skipping**: Use the **Left Arrow** or **Right Arrow** keys during screensaver playback to instantly skip to the next video.
- **Loading Grace Period Protection**: Cursor movement checks activate only *after* video playback begins, preventing buffering shifts from dismissing the screensaver prematurely.

### 🎨 Custom Overlays & Themes
- **Dynamic App Themes**: Choose between **Dark**, **Light**, and **System** themes (matching Windows OS personalization settings).
- **Customizable Overlays**:
  - 🕒 **Digital Clock**: 12-hour / 24-hour display with optional seconds.
  - 📍 **Location Name**: Displays video origin names using Apple's `.strings` UTF-16 localization parser.
  - 🌤️ **Live Weather**: Integrated OpenWeatherMap API for live temperature and condition updates.
  - 🎵 **Now Playing**: Windows System Media Transport Controls (SMTC) integration showing active music/media titles.
- **Individual Overlay Styling**: Customize font family, font size, and RGB text colors independently for every overlay, featuring a real-time live preview panel.

### ⚡ Shortcut & Windows Integration
- **Instant Launch Shortcut**: One-click Desktop shortcut creator (`Start Aerial Screensaver.lnk`) with built-in hotkey guide (e.g., `Ctrl+Alt+S`).
- **Post-Publish MSBuild Target**: Automatically generates both `AerialWindows.exe` and `AerialWindows.scr` binaries side-by-side upon publishing.

---

## 🛠️ CLI Command Line Arguments

Aerial for Windows supports standard Windows screensaver command-line switches:

| Flag | Description |
| :--- | :--- |
| `/s` | Launches the screensaver in full-screen mode across all connected monitors. |
| `/c` | Opens the configuration Settings window. |
| `/p <HWND>` | Renders a mini live preview inside the native Windows Screen Saver dialog. |

---

## 🏗️ Technical Architecture

```
AerialWindows/
├── App.xaml / App.xaml.cs           # Entry point, CLI router (/s, /c, /p) & multi-monitor window dispatcher
├── ScreensaverWindow.xaml (.cs)     # Fullscreen WPF canvas, media element, keyboard/mouse hooks, overlay renderer
├── SettingsWindow.xaml (.cs)        # Tabbed configuration UI, video manager, color pickers, download manager
├── ManifestManager.cs               # Tar archive decoder, UTF-16/UTF-8 .strings parser, Apple JSON parser
├── CacheManager.cs                  # Local video cache controller & disk quota manager
├── LocalVideoProxy.cs               # Loopback HTTP proxy handling restricted headers & SSL bypass
├── Models.cs                        # AppSettings, AerialVideo, and JSON serialization data models
├── Win32.cs                         # Native P/Invoke Win32 API functions for monitor enum & HWND parenting
└── AerialWindows.csproj             # .NET 10 WPF project file with MSBuild post-publish .scr target
```

---

## 🚀 Building & Publishing

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### Build Debug Version
```powershell
dotnet build
```

### Publish Self-Contained Release (`.exe` + `.scr`)
To generate single-file executables bundled with the .NET runtime:
```powershell
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true -o bin\Release\net10.0-windows10.0.19041.0\publish\
```

The published output directory will contain:
- `AerialWindows.exe` (Single-file executable)
- `AerialWindows.scr` (Windows Screensaver binary ready for installation)

---

## 📥 Installing on Windows

1. Download `AerialWindows.scr` from your hosted link.
2. **Right-click** `AerialWindows.scr` and select **Install**.
3. Windows will open the *Screen Saver Settings* dialog with Aerial selected. Set your idle time and click **OK**!

---

## ⬇️ Download

Download the full-performance, uncompressed self-contained release files below:

| File | Description | Download Link |
| :--- | :--- | :--- |
| **`AerialWindows.scr`** | Recommended Windows Screensaver file (Right-click -> **Install**). | [📥 Download AerialWindows.scr (Google Drive)](https://drive.google.com/file/d/1FCa3ruqwrX195S_1gtNlrUiDScWDiNDR/view?usp=drive_link) |
| **`AerialWindows.exe`** | Standalone Executable (Double-click to run anywhere). | [📥 Download AerialWindows.exe (Google Drive)](https://drive.google.com/file/d/1EfNhH6Wkw02nBayZwRb195YAiHNBGYH_/view?usp=drive_link) |

> **Note:** Both files are full-performance, uncompressed single-file binaries (~180 MB) bundled with the full .NET runtime. No external .NET installation required!

---

## 📄 License
Distributed under the MIT License. See `LICENSE` for details.
