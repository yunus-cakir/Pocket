# 📸 LocalLocket (MAUI)

[![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/en-us/apps/maui)
[![Platform](https://img.shields.io/badge/Platform-iOS%20%7C%20Android-lightgrey)](#)
[![Architecture](https://img.shields.io/badge/Architecture-Local--First-brightgreen)](#)

## 📖 Overview
LocalLocket is a privacy-focused, local-first photo and message-sharing mobile application built with pure **.NET MAUI**. Inspired by popular widget-based social applications, it introduces complete data sovereignty by storing all personal media and direct messages directly on the user's device, rather than relying on persistent cloud storage.

## ✨ Key Features
- **Local-First Architecture:** Messages and media are transmitted via a transient relay server using End-to-End Encryption (E2EE) and saved *only* to local SQLite databases.
- **Advanced Camera Mechanics:** Custom camera handler supporting tap-to-snap for photos and hold-to-record for videos.
- **Incognito Direct Messages (DMs):** Operating system keyboard learning and predictive text are disabled (Incognito IME) during DMs to ensure maximum privacy.
- **Native OS Widgets:** Integrates directly with iOS (WidgetKit) and Android (AppWidget) to display the most recently received photo on the user's home screen.
- **Weekly "Rollcall":** An automated feature that curates and bulk-sends gallery highlights from the past week to selected friends.
- **Dual Gallery/Feed View:** Swipe down from the camera to view a chronological feed, or switch to a traditional grid gallery.

## 🏗️ Project Structure
The project follows the MVVM (Model-View-ViewModel) architectural pattern and is organized by feature and function:

```text
LocalLocket/
│
├── src/
│   ├── Models/               # Data entities (e.g., Message, Photo, User)
│   ├── ViewModels/           # Business logic and state management for UI
│   ├── Views/                # XAML Pages (CameraView, DMView, FeedView)
│   ├── Services/             # Core business and API logic
│   │   ├── Api/              # Transient relay server communication (SignalR/gRPC)
│   │   ├── Storage/          # Local SQLite database operations
│   │   └── Crypto/           # End-to-end encryption (E2EE) utilities
│   ├── Controls/             # Custom MAUI controls (e.g., CustomCameraView)
│   └── Handlers/             # Platform-specific rendering instructions (e.g., IncognitoEntry)
│
├── Platforms/
│   ├── Android/              # Android-specific code (AppWidget, MainActivity, CameraX)
│   └── iOS/                  # iOS-specific code (WidgetKit extensions, AVFoundation, AppDelegate)
│
├── Resources/
│   ├── Fonts/                # Custom typography
│   ├── Images/               # Static assets and icons
│   └── Styles/               # App-wide XAML styles and themes
│
├── App.xaml                  # Global application resources
├── AppShell.xaml             # Application routing and navigation hierarchy
└── MauiProgram.cs            # Dependency injection and app configuration
```

## 🚀 Getting Started

### Prerequisites
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (or newer) with the **.NET MAUI workload** installed, or VS Code with the .NET MAUI extension.
- .NET 8.0 SDK or higher.
- iOS Mac Build Host (for iOS compilation) / Android Emulator or Physical Device.

### Installation & Run
1. **Clone the repository:**
   ```bash
   git clone https://github.com/yourusername/LocalLocket.git
   cd LocalLocket
   ```
2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```
3. **Run the Application:**
   - **For Android:** Select your Android Emulator or physical device from the debug dropdown and hit `F5`.
   - **For iOS:** Ensure you are paired to a Mac or running locally on macOS, select the iOS Simulator, and hit `F5`.

## 🛠️ Technology Stack
- **Framework:** .NET MAUI (XAML / C#)
- **Local Database:** sqlite-net-pcl / Entity Framework Core
- **Networking:** SignalR (for Transient Data Relay)
- **Platform integrations:** Swift (iOS WidgetKit), XML/Java (Android AppWidget), CommunityToolkit.Maui

## 📜 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
