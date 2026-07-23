# 🧠 Pocket - AI Context & Coding Standards

This `CONTEXT.md` file serves as the primary system instruction set for AI assistants and human developers contributing to the **Pocket** project. It defines the strict technology stack, architectural boundaries, and coding conventions that must be adhered to at all times.

## 1. Technology Stack
* **Framework:** .NET 8.0 MAUI (Multi-platform App UI)
* **Language:** C# 12.0, XAML
* **Architecture Pattern:** MVVM (Model-View-ViewModel)
* **Local Database:** SQLite (via `sqlite-net-pcl` or `Entity Framework Core`)
* **State Management & MVVM:** `CommunityToolkit.Mvvm`
* **UI Utilities:** `CommunityToolkit.Maui`
* **Networking:** ASP.NET Core SignalR / gRPC (Client-side)

## 2. Strict Architectural Constraints
* **NO Blazor/WebView:** Do **NOT** use `BlazorWebView`, HTML, CSS, or JavaScript. The UI must be implemented using pure MAUI XAML and C# to ensure maximum performance for the camera, gestures, and OS integrations.
* **Local-First Obligation:** Always write data to the local SQLite database first. Remote server calls should only be used for the transient exchange of encrypted payloads, not for state retrieval.
* **Platform Segregation:** Keep OS-specific implementations (e.g., Android `AppWidget`, iOS `WidgetKit`, `CameraX`, `AVFoundation`) strictly within their respective `Platforms/Android` or `Platforms/iOS` directories, or use `partial` classes with platform-specific implementations. Avoid excessive `#if MACCATALYST || IOS` inline compiler directives in the core shared logic.

## 3. Coding Standards & Conventions
* **MVVM Enforcement:** Code-behind files (`.xaml.cs`) must contain **zero** business logic. All logic must reside in ViewModels or abstracted Services.
* **CommunityToolkit.Mvvm Boilerplate:** 
  * Always use the `[ObservableProperty]` attribute on private fields instead of manually implementing `INotifyPropertyChanged`.
  * Use `[RelayCommand]` on methods instead of explicitly instantiating `ICommand` or `Command` properties.
  * Ensure ViewModels inherit from `ObservableObject`.
* **Asynchronous Programming:**
  * Always use `async` and `await`. Avoid `.Result` or `.Wait()` to prevent UI thread deadlocks.
  * Use `CancellationToken` in all long-running service operations (especially network requests and media processing).
* **Dependency Injection (DI):** All Services, Repositories, and ViewModels must be registered in `MauiProgram.cs` and injected via constructors. Do not use static singletons for services unless absolutely necessary.
* **UI/XAML Standards:**
  * Use compiled bindings (`x:DataType`) in all XAML files to improve performance and catch binding errors at compile-time.
  * Store reusable colors, fonts, and styles in `Resources/Styles/Colors.xaml` and `Styles.xaml`. Do not hardcode hex colors or font sizes directly in page XAML.

## 4. Security & Privacy Rules
* **Zero Trust:** Treat all incoming data from the Transient Relay Server as untrusted until decrypted and verified.
* **Incognito Enforcement:** Any `Entry` or `Editor` used for direct messaging must utilize custom handlers to enforce incognito mode (`ImeFlags.NoPersonalizedLearning` on Android, `UITextAutocorrectionType.No` on iOS).

## 5. Instructions for AI Code Generation
* When asked to create a UI component, provide both the `XAML` file and its accompanying `ViewModel.cs` file.
* Automatically implement compiled bindings (`x:DataType`) in any generated XAML.
* Assume the presence of `CommunityToolkit.Mvvm` and `CommunityToolkit.Maui` in all code snippets.
* If a request violates the "Local-First" or "Zero Persistent Cloud" rules, refuse the architectural change and suggest a privacy-compliant alternative.
