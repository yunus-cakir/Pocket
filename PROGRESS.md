# 📸 Pocket - Project Progress & Status

## 📖 High-Level Overview
**Pocket** is a privacy-focused, local-first photo and message-sharing mobile application built with pure .NET MAUI. It prioritizes complete data sovereignty by storing all personal media and direct messages directly on the user's device, using a Transient Relay Server purely for End-to-End Encrypted (E2EE) data transmission. 

## 🏗️ Current Implementation Status
**Status: ~35% (Data Foundation Completed)**

* ✅ `Pocket.Client`: Boilerplate code cleaned up and project reset to a clean compilable state.
* ✅ `Pocket.Shared`: Created .NET 8.0 Class Library with shared E2EE DTOs (`EncryptedPayloadDto`), Enums (`SyncStatus`, `MediaType`), and SignalR interfaces (`IRelayClient`).
* ✅ `Pocket.Server`: Created ASP.NET Core SignalR Server with in-memory transient payload store (`TransientMemoryStore`) and E2EE payload routing hub (`RelayHub`).
* ✅ `Pocket.slnx`: Solution configuration updated and verified with clean multi-project builds.
* ✅ `Pocket.Client` Data Foundation: Defined SQLite entities (`User`, `Friend`, `Message`) and set up `LocalDatabase` repository infrastructure.

## ✅ Completed Features
* Comprehensive system architecture and business logic documentation established.
* Clean multi-project solution structure (`Pocket.Client`, `Pocket.Shared`, `Pocket.Server`).
* Transient Relay Server SignalR hub structure with zero-persistent cloud storage logic (BR-201, BR-202).
* E2EE Data Transfer Object (DTO) contracts and interfaces.
* Local SQLite database models (`User`, `Friend`, `Message`) and `LocalDatabase` async repository initialization.

## ⏳ Planned Features (Not Yet Implemented)
* **E2EE Cryptographic Engine:** Client-side cryptographic key generation (ECDH) and payload encryption/decryption routines.
* **Advanced Camera Integration:** Platform-specific camera handlers supporting tap-to-snap and hold-to-record capabilities without standard web views.
* **Incognito Direct Messaging:** Custom MAUI `Entry` handlers that disable OS predictive text and keyboard learning (`ImeFlags.NoPersonalizedLearning` / `UITextAutocorrectionType.No`).
* **Native OS Widgets:** Integrations with iOS WidgetKit and Android AppWidget to display the latest received photo.
* **Dual View UI:** Transitioning between a chronological "Feed View" and a grid "Gallery View" via swipe gestures.
* **Weekly Rollcall:** Aggregating and bulk-sending the past week's gallery highlights to selected mutual friends.

## 📐 Important Architectural Decisions & Conventions
* **Strict Native UI:** Explicit prohibition of Blazor/WebView technologies to ensure maximum performance for camera, gestures, and OS integrations.
* **MVVM Enforcement:** Strict adherence to the MVVM pattern utilizing `CommunityToolkit.Mvvm` (using `[ObservableProperty]`, `[RelayCommand]`). Code-behind must contain zero business logic.
* **Platform Segregation:** Platform-specific features (widgets, camera, incognito keyboard) must reside cleanly in `Platforms/Android` and `Platforms/iOS` rather than cluttering shared code with inline compiler directives.
* **Local-First Obligation:** All reads and state retrieval must happen from the local SQLite DB. Remote calls are only for exchanging encrypted transient data.
* **Compiled Bindings:** Mandatory use of `x:DataType` in XAML for performance and type safety.

## 🗺️ Suggested Roadmap for Next Steps
1. ~~**Repository Cleanup:** Delete existing task management boilerplate files within `Pocket.Client`.~~ (Completed)
2. ~~**Solution Expansion:** Scaffold `Pocket.Server` and `Pocket.Shared` projects within the solution.~~ (Completed)
3. ~~**Data Foundation:** Define the SQLite entity models (`User`, `Friend`, `Message`) and set up local database repositories in `Pocket.Client`.~~ (Completed)
4. ~~**UI Scaffolding:** Create main navigation shell and View/ViewModels for Camera, Feed, and Direct Messaging screens.~~ (Completed)
5. **Core Platform Handlers:** Implement custom platform handlers for Incognito Keyboard and Camera controls.

## ❓ Assumptions & Uncertainties
* **E2EE Library:** Cryptographic key exchange strategy (e.g. `System.Security.Cryptography.ECDiffieHellman`).
* **Widget Hand-off Mechanism:** `App Group` for iOS and `SharedPreferences` for Android widgets.
