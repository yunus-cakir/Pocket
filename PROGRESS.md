# 📸 Pocket - Project Progress & Status

## 📖 High-Level Overview
**Pocket** is a privacy-focused, local-first photo and message-sharing mobile application built with pure .NET MAUI. It prioritizes complete data sovereignty by storing all personal media and direct messages directly on the user's device, using a Transient Relay Server purely for End-to-End Encrypted (E2EE) data transmission. 

## 🏗️ Current Implementation Status
**Status: 0% (Project Initialization / Boilerplate Cleanup Phase)**

While the conceptual documentation (Architecture, Context, Business Rules) is well-defined, the actual codebase is currently misaligned with the project's goals. 
* The existing `Pocket.Client` folder contains boilerplate code for a Syncfusion-based Project/Task Management application, which must be entirely replaced.
* The `Pocket.Server` (backend) and `Pocket.Shared` (DTOs/Interfaces) projects have not yet been created in the solution.

## ✅ Completed Features
* Comprehensive system architecture and business logic documentation established.
* *(No code features are completed at this time).*

## ⏳ Planned Features (Not Yet Implemented)
* **Local-First Storage:** Implementation of local SQLite databases as the primary source of truth (Users, Friends, Messages).
* **Transient Relay Server:** A SignalR/gRPC backend to act as a temporary broker for E2EE payloads, dropping data upon delivery confirmation.
* **E2EE Infrastructure:** Cryptographic key generation (ECDH) and payload encryption/decryption mechanisms.
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
1. **Repository Cleanup:** Delete the existing task management boilerplate files (Models, ViewModels, Pages, Data Repositories) within the `Pocket.Client` project.
2. **Solution Expansion:** Scaffold the `Pocket.Server` (ASP.NET Core SignalR) and `Pocket.Shared` (.NET Class Library) projects within the solution.
3. **Data Foundation:** Define the SQLite entity models (`Users`, `Friends`, `Messages`) and set up the local database context in `Pocket.Client`.
4. **UI Scaffolding:** Create the new main navigation shell and bare-bones View/ViewModels for the Camera, Feed, and Direct Messaging screens.
5. **Core Platform Handlers:** Begin drafting the custom platform handlers for the Incognito Keyboard and Camera integrations.

## ❓ Assumptions & Uncertainties
* **E2EE Library:** The documentation mentions "cryptographic key pairs (e.g., ECDH)", but does not specify which cryptographic library to standardize on (e.g., standard .NET `System.Security.Cryptography`, `libsodium`, `BouncyCastle`).
* **Widget Hand-off Mechanism:** The documentation outlines "App Group" for iOS and "SharedPreferences/File System" for Android widgets. We need to verify if we'll use a MAUI plugin to bridge this communication or write pure native broadcast receivers.
* **Rollcall Trigger:** It is slightly unclear if the Weekly Rollcall aggregation happens seamlessly via a background OS task or if it's generated on-the-fly when the user opens the app on the target day.
