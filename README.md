# 📸 Pocket (.NET MAUI)

[![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/en-us/apps/maui)
[![Platform](https://img.shields.io/badge/Platform-iOS%20%7C%20Android-lightgrey)](#)
[![Architecture](https://img.shields.io/badge/Architecture-Local--First-brightgreen)](#)

## 📖 Overview
Pocket is a privacy-focused, local-first photo and message-sharing mobile application built with pure **.NET MAUI**. Inspired by popular widget-based social applications, it introduces complete data sovereignty by storing all personal media and direct messages directly on the user's device, rather than relying on persistent cloud storage.

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
Pocket/
│
├── Pocket.sln                # Main solution file
│
├── Pocket.Client/            # .NET MAUI Client Application
│   ├── Models/               # Local data entities (e.g., Message, Photo, User)[cite: 1]
│   ├── ViewModels/           # Business logic and state management for UI[cite: 1]
│   ├── Views/                # XAML Pages (CameraView, DMView, FeedView)[cite: 1]
│   ├── Services/             # API communication, local storage, and Crypto logic[cite: 1]
│   ├── Controls/             # Custom MAUI controls (e.g., CustomCameraView)[cite: 1]
│   ├── Handlers/             # Platform-specific rendering instructions (e.g., IncognitoEntry)[cite: 1]
│   ├── Platforms/            # Android and iOS specific OS code[cite: 1]
│   ├── Resources/            # Custom typography, static assets, and global styles[cite: 1]
│   └── MauiProgram.cs        # Dependency injection and app configuration[cite: 1]
│
├── Pocket.Server/            # Transient Relay Server (Backend)
│   ├── Hubs/                 # SignalR / gRPC Hubs for real-time E2EE payload relay
│   ├── Services/             # Connection tracking and temporary memory management
│   └── Program.cs            # Server configuration and middleware pipeline
│
└── Pocket.Shared/            # Shared Library (.NET Class Library)
    ├── DTOs/                 # Data Transfer Objects used by both Client and Server
    ├── Enums/                # Shared enumerations (e.g., SyncStatus)
    └── Interfaces/           # Shared API contracts and interface definitions

## 🚀 Getting Started

### Prerequisites
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (or newer) with the **.NET MAUI workload** installed, or VS Code with the .NET MAUI extension.
- .NET 8.0 SDK or higher.
- iOS Mac Build Host (for iOS compilation) / Android Emulator or Physical Device.

### Installation & Run
1. **Clone the repository:**
   ```bash
   git clone https://github.com/yourusername/Pocket.git
   cd Pocket
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
