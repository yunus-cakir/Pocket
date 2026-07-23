# 🏗️ Pocket - System Architecture

This document outlines the high-level architecture, data flow, database schema, and platform-specific integrations for **Pocket**.

## 1. Core Philosophy: Local-First & Privacy-Centric
Pocket deviates from traditional client-server architectures by adopting a **Local-First** approach. 
- **No Persistent Cloud Storage:** Messages and photos are never stored permanently on a central server.
- **End-to-End Encryption (E2EE):** All communication is encrypted on the sender's device and decrypted only on the recipient's device.
- **Transient Relay:** A lightweight backend server acts only as a temporary message broker.

## 2. High-Level Architecture

The system consists of two main components: The **Pocket Client (.NET MAUI)** and the **Transient Relay Server**.

```text
┌────────────────────────────────────────────────────────┐
│                   Pocket Client (MAUI)                 │
│                                                        │
│  ┌─────────────┐   ┌─────────────┐   ┌──────────────┐  │
│  │  UI Layer   │   │ Core Logic  │   │ Platform I/O │  │
│  │ (XAML/MVVM) │   │ (Services)  │   │ (Handlers)   │  │
│  └──────┬──────┘   └──────┬──────┘   └──────┬───────┘  │
│         │                 │                 │          │
└─────────┼─────────────────┼─────────────────┼──────────┘
          │                 │                 │
    [User Input]      [Local SQLite]    [Hardware/OS]
                      [File Storage]    (Camera, Widget,
                                         Incognito IME)
          │
          ▼
┌────────────────────────────────────────────────────────┐
│               Transient Relay Server (Backend)         │
│  - SignalR / gRPC Hub                                  │
│  - Temporarily holds E2EE payloads                     │
│  - Immediately drops payload upon delivery confirmation│
└────────────────────────────────────────────────────────┘
```

## 3. Communication & Data Flow

### Frontend-Backend Communication
- **Protocol:** Real-time bi-directional communication using **SignalR** (or gRPC).
- **Authentication:** Devices authenticate using cryptographic key pairs (e.g., ECDH) instead of traditional passwords. JWTs can be used for session management on the relay, but payload decryption is strictly client-side.
- **Message Lifecycle:**
  1. **Capture:** User takes a photo/video (Camera API).
  2. **Encrypt:** Media and text are encrypted locally using a shared secret.
  3. **Transmit:** Encrypted payload is pushed to the Transient Relay Server.
  4. **Relay:** Server holds the payload *only* if the recipient is offline.
  5. **Delivery:** Recipient's device pulls the payload, decrypts it, and saves it to the local SQLite database.
  6. **Purge:** Server receives a delivery receipt and deletes the payload from memory/cache.

## 4. Local Database Schema (SQLite)

Since Pocket is local-first, the primary source of truth is the local SQLite database (`sqlite-net-pcl` or `EF Core`).

### `Users` (Local Profile)
| Column      | Type    | Description |
| ----------- | ------- | ----------- |
| `Id`        | GUID    | Local identifier |
| `Username`  | String  | User's display name |
| `PrivateKey`| String  | Securely stored local private key (Encrypted at rest) |
| `PublicKey` | String  | User's public identity key |

### `Friends` (Contacts)
| Column        | Type    | Description |
| ------------- | ------- | ----------- |
| `FriendId`    | String  | Remote user's public identifier |
| `DisplayName` | String  | Friend's name |
| `PublicKey`   | String  | Used for deriving the shared E2EE secret |

### `Messages` (Chat & Media)
| Column           | Type     | Description |
| ---------------- | -------- | ----------- |
| `Id`             | GUID     | Unique message ID |
| `FriendId`       | String   | Foreign key to Friends table |
| `IsIncoming`     | Boolean  | True if received, False if sent |
| `MediaType`      | Enum     | None, Photo, Video |
| `LocalMediaPath` | String   | File system path to the saved media |
| `TextContent`    | String   | Attached message/emoji (Plaintext after decryption) |
| `Timestamp`      | DateTime | When the message was created |

## 5. Platform-Specific Implementations

To achieve the custom features required by Pocket, the .NET MAUI application utilizes specific native integrations:

### 5.1 Native OS Widgets
Since widgets run in a separate OS process, MAUI cannot render them directly.
- **iOS:** Built using `WidgetKit` (SwiftUI). The MAUI app writes the latest received photo to an `App Group` shared container (`NSUserDefaults` / File System). The iOS widget reads from this container.
- **Android:** Built using `AppWidgetProvider` (RemoteViews) or `Glance`. The MAUI app writes to `SharedPreferences` or a specific file directory accessible by the widget receiver.

### 5.2 Incognito Keyboard (Direct Messages)
To prevent the OS from learning typed messages, a custom MAUI `Entry` handler is used:
- **Android:** Implements `ImeOptions = ImeFlags.NoPersonalizedLearning`.
- **iOS:** Implements `UITextAutocorrectionType.No` and disables predictive text on the native `UITextField`.

### 5.3 Advanced Camera Control
A custom `PlatformHandler` wrapping native APIs instead of standard web views:
- **iOS:** `AVFoundation` - Custom gestures map to `capturePhoto` (tap) and `startRecording` (hold).
- **Android:** `CameraX` API - Uses `ImageCapture` and `VideoCapture` use cases triggered by MAUI touch behaviors.
