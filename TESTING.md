# 🧪 Pocket - Android Build, Multi-Emulator Deployment & Testing Guide

Bu doküman, `Pocket.Client` (.NET 10.0 MAUI) ve `Pocket.Server` projelerinin yerel Android emülatörlerinde hızlı derlenmesi (rebuild), çoklu cihaza dağıtımı (deployment), otomatik etkileşim ve hata ayıklama süreçlerinde zaman kazandıracak komutları içerir.

---

## 📱 Test Emülatör Yapılandırması

Test ortamında iki farklı Android emülatörü (karşılıklı mesajlaşma, arkadaş ekleme ve E2EE relay testleri için) eşzamanlı olarak kullanılmaktadır:

| Rol | AVD Adı / Görünen Ad | Sürüm & API | Varsayılan ADB Serial | Önerilen Teknik İsimlendirme |
| :--- | :--- | :--- | :--- | :--- |
| **Emülatör 1 (Birincil)** | `pixel_7_-_api_35` (`Pixel 7 - API 35`) | Android 15 (API 35) | `emulator-5554` | `Pixel7_A15_API35` (Uygulama içi: `@pixel7_api35`) |
| **Emülatör 2 (İkincil)** | `pixel_7_-_api_34_0` (`Pixel 7 - API 34.0`) | Android 14 (API 34) | `emulator-5556` | `Pixel7_A14_API34` (Uygulama içi: `@pixel7_api34`) |

* **Uygulama Kimliği (ApplicationId):** `com.companyname.pocket.client`
* **MainActivity Bileşeni:** `com.companyname.pocket.client/crc646dd0ab311caf03fd.MainActivity`
* **Target Framework:** `net10.0-android`
* **Relay Server Adresi (Host):** `http://0.0.0.0:5200` (Health Check: `GET http://localhost:5200/`)
* **Relay Hub Adresi (Emülatör İçi):** `http://10.0.2.2:5200/hubs/relay`

### Aktif Emülatörleri Kontrol Etme
```powershell
adb devices -l
adb -s emulator-5554 shell getprop ro.build.version.sdk
adb -s emulator-5556 shell getprop ro.build.version.sdk
```

---

## ⚡ Hızlı Rebuild ve Dağıtım Komutları

> **⚠️ Önemli (.NET MAUI Fast Deployment Notu):**
> `Debug` modunda `.NET MAUI` "Fast Deployment" kullanır; yani C# DLL'leri APK'nın içine gömülmek yerine cihazdaki `files/.__override__` dizinine doğrudan kopyalanır. Bu nedenle **sadece `adb install -r` yapmak güncel C# kodunu ikinci cihaza taşımaz**. Her iki emülatöre de `-t:Run` ve `-p:AdbTarget` ile dağıtım yapılmalıdır (`--no-restore` ile çok hızlı tamamlanır).

### Her İki Emülatöre Sıralı Hızlı Rebuild & Run (Tek Satır)
```powershell
dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5554 --no-restore; dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5556 --no-restore
```

### Tek Emülatöre Build & Run
**Emülatör 1 (API 35 - `emulator-5554`):**
```powershell
dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5554 --no-restore
```

**Emülatör 2 (API 34 - `emulator-5556`):**
```powershell
dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5556 --no-restore
```

---

## 🖥️ Pocket.Server (Relay Backend) Yönetimi

### Sunucuyu Başlatma ve Kontrol Etme
`Pocket.Server` varsayılan olarak `http://0.0.0.0:5200` portunu dinler:
```powershell
# Sunucuyu başlatma
dotnet run --project Pocket.Server/Pocket.Server.csproj

# Sunucunun ayakta olduğunu hızlıca doğrulama
Invoke-RestMethod -Uri "http://localhost:5200/" -TimeoutSec 5
```

---

## 🚀 Uygulama Başlatma, İzinler ve Otomasyon Kısayolları

Derleme yapmadan uygulamayı her iki cihazda hızlıca yeniden başlatmak, izin pencerelerini atlamak ve paneller arasında geçiş yapmak için aşağıdaki komutları kullanabilirsiniz:

### 1. Kamera İznini Otomatik Verme (Permission Dialog Atlatma)
Uygulama ilk açıldığında çıkan kamera izni penceresinin otomasyonu veya ekran görüntüsünü engellememesi için:
```powershell
adb -s emulator-5554 shell pm grant com.companyname.pocket.client android.permission.CAMERA
adb -s emulator-5556 shell pm grant com.companyname.pocket.client android.permission.CAMERA
```

### 2. İki Cihazda Uygulamayı Yeniden Başlatma (Force-Stop & Start)
```powershell
adb -s emulator-5554 shell am force-stop com.companyname.pocket.client
adb -s emulator-5554 shell am start -n com.companyname.pocket.client/crc646dd0ab311caf03fd.MainActivity

adb -s emulator-5556 shell am force-stop com.companyname.pocket.client
adb -s emulator-5556 shell am start -n com.companyname.pocket.client/crc646dd0ab311caf03fd.MainActivity
```

### 3. UI Navigasyon ve Koordinat Kısayolları (1080x2400 Ekran)
`CameraPage` 3 yatay panelden oluşur: **Sol (Index 0: Profil & Sunucu Durumu)**, **Orta (Index 1: Kamera & Feed)**, **Sağ (Index 2: Sohbetler)**.

> **💡 İpucu:** `HorizontalSwipeView` üzerinde `adb shell input swipe` yerine üst bardaki butonlara `input tap` ile tıklamak (`SnapToProfileCommand` / `SnapToChatCommand`) çok daha kararlı çalışır:

| İşlem | Komut (`emulator-5554` veya `emulator-5556`) |
| :--- | :--- |
| **Kamera -> Sol Profil Menüsünü Aç** | `adb -s <serial> shell input tap 82 258` |
| **Kamera -> Sağ Sohbet Menüsünü Aç** | `adb -s <serial> shell input tap 998 258` |
| **Sol/Sağ Panelden Kameraya Dön** | Sağ üst `➔` (`tap 970 180`) veya Sol üst `🡐` (`tap 110 180`) |
| **Fotoğraf Çek (Shutter Tıklama)** | `adb -s <serial> shell input tap 540 1438` |

---

## 📸 Ekran Görüntüsü ve UI Hiyerarşisi İnceleme

> **⚠️ PowerShell Uyarısı:** PowerShell'de `adb exec-out screencap -p > file.png` komutu çıktıyı UTF-16LE olarak kodladığı için **PNG dosyasını bozar**. Her zaman `sdcard` üzerinden `adb pull` kullanın:

```powershell
# İki emülatörden bozulmamış PNG ekran görüntüsü alma
adb -s emulator-5554 shell screencap -p /sdcard/screen.png
adb -s emulator-5554 pull /sdcard/screen.png ./emu5554.png

adb -s emulator-5556 shell screencap -p /sdcard/screen.png
adb -s emulator-5556 pull /sdcard/screen.png ./emu5556.png
```

### UI Elemanlarını ve Koordinatları XML Olarak Okuma
Ekrandaki butonların tam koordinatlarını (`bounds`) ve metinlerini görmek için:
```powershell
adb -s emulator-5554 shell uiautomator dump /sdcard/ui.xml
adb -s emulator-5554 shell cat /sdcard/ui.xml
```

---

## 🔍 Hızlı Hata Ayıklama (Crash & Logcat)

Uygulama açılışta kapanırsa veya beklenmedik bir hata verirse sadece **crash buffer**'ını okuyarak C# / Mono stack trace'ini anında görebilirsiniz:

```powershell
# Sadece uygulama çökmelerini (Fatal Exception / C# StackTrace) getirir
adb -s emulator-5554 logcat -b crash -d
adb -s emulator-5556 logcat -b crash -d

# Logcat'i temizleyip sıfırdan izlemek için
adb -s emulator-5554 logcat -c
adb -s emulator-5556 logcat -c

# Uygulamanın o an hangi pencerede/durumda olduğunu kontrol etme
adb -s emulator-5554 shell dumpsys activity activities | Select-String -Pattern "mCurrentFocus|mResumedActivity|pocket"
```

---

## 🧹 Temiz Kurulum & Veritabanı Sıfırlama

**1. Fast Deployment DLL'lerini Bozmadan Sadece Veritabanını ve Medyayı Sıfırlama (Önerilen):**
`Debug` modunda `pm clear` komutu `.NET MAUI` Fast Deployment dizinini (`files/.__override__`) sildiği için yeniden `-t:Run` gerektirir. Yeniden derleme yapmadan sadece SQLite veritabanını ve medya dosyalarını sıfırlamak için:
```powershell
adb -s emulator-5554 shell run-as com.companyname.pocket.client rm -rf files/PocketLocal.db3* files/Media
adb -s emulator-5556 shell run-as com.companyname.pocket.client rm -rf files/PocketLocal.db3* files/Media
```

**2. Tam Uygulama Verisi Sıfırlama (`pm clear`):**
Uygulamanın tüm verilerini (`SecureStorage` / `SharedPreferences` dahil) sıfırlamak için (`pm clear` sonrasında `-t:Run` ile tekrar dağıtım yapılmalıdır):
```powershell
adb -s emulator-5554 shell pm clear com.companyname.pocket.client
adb -s emulator-5556 shell pm clear com.companyname.pocket.client
```

**Uygulamayı Kaldırma (Uninstall):**
```powershell
adb -s emulator-5554 uninstall com.companyname.pocket.client
adb -s emulator-5556 uninstall com.companyname.pocket.client
```
