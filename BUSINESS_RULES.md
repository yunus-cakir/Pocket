# 📸 Pocket - Business Rules & Logic Constraints

This document defines the core business logic, user flow constraints, and operational rules for the **Pocket** application. Any new feature or module must comply with these rules to maintain the application's local-first and privacy-focused identity.

## 1. Media Capture & User Interface (Camera)
* **BR-101 (Shutter Interaction):** A single tap on the camera shutter button captures a static photograph. Pressing and holding the shutter button initiates video recording. Releasing the button stops the recording.
* **BR-102 (Navigation via Gesture):** Swiping down on the active Camera view must immediately transition the user to the chronological Feed view.
* **BR-103 (Dual View Toggle):** The historical media view must support switching between a "Feed View" (vertical scrolling, showing comments/emojis inline) and a "Gallery View" (grid layout, media only).

## 2. Privacy & Data Storage (Local-First)
* **BR-201 (Zero Persistent Cloud):** Under no circumstances should user photos, videos, or direct messages be permanently stored on the Transient Relay Server.
* **BR-202 (Delivery & Purge):** Once a payload (message or media) is confirmed as delivered to the recipient's device, the Transient Relay Server must immediately purge the payload from its memory.
* **BR-203 (Local Storage):** All received and sent media must be saved directly to the device's local database/storage (SQLite/App Data).
* **BR-204 (Gallery Export):** Photos/videos are kept within the app's isolated storage by default. They are only saved to the public OS Photo Gallery if the user explicitly triggers the "Save to Gallery" action.

## 3. Direct Messaging (DMs) & Interactions
* **BR-301 (Incognito Keyboard):** Whenever a text input field for Direct Messages is focused, the application *must* enforce Incognito Mode on the OS keyboard (e.g., `ImeFlags.NoPersonalizedLearning` on Android, `UITextAutocorrectionType.No` on iOS) to prevent predictive text tracking.
* **BR-302 (Reactions):** Users can react to any received photo using emojis or direct text replies. These replies are threaded directly to the specific photo in the DM view.

## 4. Widget Operations
* **BR-401 (Widget Syncing):** The OS Widget must always display the *most recently received* photo from a friend.
* **BR-402 (Data Hand-off):** The main application must overwrite the shared OS container (App Group / SharedPreferences) immediately upon receiving a new photo in the background, triggering a widget refresh request.

## 5. Weekly "Rollcall"
* **BR-501 (Aggregation):** The "Rollcall" feature must automatically aggregate the user's generated/saved gallery photos from the previous 7 days.
* **BR-502 (Bulk Send):** The user can review the aggregated Rollcall and send it as a bulk collection to selected friends.

## 6. Friend Network
* **BR-601 (Mutual Connection):** A mutual friend connection must be established before any media or message can be sent. This ensures the End-to-End Encryption (E2EE) key exchange is completed successfully.
