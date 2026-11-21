# 🗄️ Pocket - SQLite Veritabanı Rehberi (DATABASE.md)

Bu doküman, **Pocket** mobil uygulamasında yerel olarak kullanılan SQLite veritabanının yapısını, dosya konumlarını, emülatör/cihaz üzerinden veritabanına doğrudan erişim, yedekleme (pull), geri yükleme (push), seed veri ekleme ve sıfırlama adımlarını içerir.

---

## 📌 Genel Bakış ve Veritabanı Konumu

Pocket, **Local-First** mimari prensibiyle çalışır. Tüm kullanıcı bilgileri, arkadaşlar, medya gönderileri ve direkt mesajlar cihazın yerel SQLite veritabanında saklanır.

* **Veritabanı Motoru:** SQLite (`sqlite-net-pcl`)
* **Veritabanı Dosya Adı:** `PocketLocal.db3`
* **Android Cihaz İçi Konumu:**
  `/data/user/0/com.yunuscakir.pocket.client.debug/files/PocketLocal.db3`
  *(Uygulama kum havuzunda: `files/PocketLocal.db3`)*
* **İlgili Medya Dizinleri:**
  - `files/Media/` : Çekilen/alınan fotoğraf ve videolar (`.jpg`, `.mp4`)
  - `files/Profile/` : Kırpılmış profil fotoğrafları (`avatar_*.jpg`, `friend_*.jpg`)

---

## 📋 Tablo Şemaları (Schema)

### 1. `User` (Yerel Kullanıcı Kimliği)
| Alan | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | TEXT (PK) | Kullanıcının benzersiz GUID kimliği |
| `Username` | TEXT (Max 50) | Kullanıcı adı (`@` işareti olmadan yalın metin) |
| `DisplayName` | TEXT (Max 100) | Ekranda gösterilen isim |
| `ProfilePictureUrl` | TEXT | Yerel profil fotoğrafı dosya yolu (`files/Profile/...`) |
| `PublicKey` | TEXT | ECDH P-256 Public Key (Base64) |
| `EncryptedPrivateKey` | TEXT | ECDH P-256 Private Key (Base64) |
| `CreatedAt` | DATETIME | Hesap oluşturulma tarihi (UTC) |

### 2. `Friend` (Ekli Arkadaşlar)
| Alan | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | TEXT (PK) | Arkadaşın benzersiz GUID kimliği |
| `Username` | TEXT (Indexed) | Arkadaşın kullanıcı adı (`@` olmadan) |
| `DisplayName` | TEXT | Arkadaşın görünen adı |
| `ProfilePictureUrl` | TEXT | Arkadaşın yerel profil fotoğrafı yolu (`files/Profile/...`) |
| `PublicKey` | TEXT | Arkadaşın ECDH P-256 Public Key (E2EE ortak sırrı için) |
| `IsMuted` | INTEGER (Bool) | Bildirim sessize alma durumu |
| `AddedAt` | DATETIME | Arkadaş eklenme tarihi (UTC) |
| `LastInteractionAt` | DATETIME | Son etkileşim tarihi (UTC) |

### 3. `MediaPost` (Akış & Geçmiş Gönderileri)
| Alan | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | TEXT/GUID (PK) | Gönderi GUID kimliği |
| `FriendId` | TEXT (Indexed) | Gönderen/alıcı arkadaş ID'si (boş ise tüm arkadaşlara) |
| `IsIncoming` | INTEGER (Bool) | `1` = Arkadaştan gelen, `0` = Bizim gönderdiğimiz |
| `MediaLocalPath` | TEXT | Cihazdaki medya dosyası yolu (`files/Media/...`) |
| `MediaType` | INTEGER (Enum) | `1` = Photo, `2` = Video |
| `Caption` | TEXT | Fotoğraf alt yazısı / mesaj |
| `IsIncludedInRollcall`| INTEGER (Bool) | Haftalık Rollcall özetine dahil mi |
| `SentAt` | DATETIME | Gönderilme zamanı (UTC) |
| `SyncStatus` | INTEGER (Enum) | `0` = Pending, `1` = Sent, `2` = Delivered, `3` = Failed |

### 4. `DirectMessage` (Uçtan Uca Şifreli DM & Reaksiyonlar)
| Alan | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | TEXT/GUID (PK) | Mesaj GUID kimliği |
| `FriendId` | TEXT (Indexed) | Sohbet edilen arkadaş kimliği |
| `MediaPostId` | TEXT/GUID | Yanıtlanan veya reaksiyon verilen gönderi GUID'si (opsiyonel) |
| `IsIncoming` | INTEGER (Bool) | `1` = Gelen mesaj, `0` = Giden mesaj |
| `TextContent` | TEXT | Mesaj metni |
| `ReactionEmoji` | TEXT | Gönderiye bırakılan emoji reaksiyonu (örn: ❤️, 🔥, 😋) |
| `SentAt` | DATETIME | Gönderilme zamanı (UTC) |
| `SyncStatus` | INTEGER (Enum) | İletim durumu |

---

## ⚡ Veritabanına Erişim ve Yönetim Komutları

Uygulama `debug` modunda olduğu için Android'in `run-as com.yunuscakir.pocket.client.debug` komutu ve `/data/local/tmp` köprüsü ile emülatörden doğrudan çekme/yükleme yapılabilir.

### 1. Veritabanını Cihazdan Bilgisayara Çekme (Pull)

**Emülatör 1 (`emulator-5554`):**
```powershell
adb -s emulator-5554 shell "touch /data/local/tmp/dump.db3; chmod 777 /data/local/tmp/dump.db3; run-as com.yunuscakir.pocket.client.debug cp files/PocketLocal.db3 /data/local/tmp/dump.db3"
adb -s emulator-5554 pull /data/local/tmp/dump.db3 ./PocketLocal_5554.db3
```

**Emülatör 2 (`emulator-5556`):**
```powershell
adb -s emulator-5556 shell "touch /data/local/tmp/dump.db3; chmod 777 /data/local/tmp/dump.db3; run-as com.yunuscakir.pocket.client.debug cp files/PocketLocal.db3 /data/local/tmp/dump.db3"
adb -s emulator-5556 pull /data/local/tmp/dump.db3 ./PocketLocal_5556.db3
```

> **İnceleme Araçları:** Çektiğiniz `.db3` dosyasını [DB Browser for SQLite](https://sqlitebrowser.org/), VS Code **SQLite Viewer** eklentisi veya Python (`sqlite3`) ile doğrudan açıp verileri görüntüleyebilir ve değiştirebilirsiniz.

---

### 2. Düzenlenen / Seed Veri Eklenen Veritabanını Cihaza Geri Yükleme (Push)

Bilgisayarınızda düzenlediğiniz veya seed verisi eklediğiniz `.db3` dosyasını cihaza geri yüklemek için:

**Emülatör 1 (`emulator-5554`):**
```powershell
adb -s emulator-5554 push ./PocketLocal_5554.db3 /data/local/tmp/new.db3
adb -s emulator-5554 shell "chmod 777 /data/local/tmp/new.db3; run-as com.yunuscakir.pocket.client.debug cp /data/local/tmp/new.db3 files/PocketLocal.db3"
adb -s emulator-5554 shell am force-stop com.yunuscakir.pocket.client.debug
adb -s emulator-5554 shell am start -n com.yunuscakir.pocket.client.debug/crc646dd0ab311caf03fd.MainActivity
```

**Emülatör 2 (`emulator-5556`):**
```powershell
adb -s emulator-5556 push ./PocketLocal_5556.db3 /data/local/tmp/new.db3
adb -s emulator-5556 shell "chmod 777 /data/local/tmp/new.db3; run-as com.yunuscakir.pocket.client.debug cp /data/local/tmp/new.db3 files/PocketLocal.db3"
adb -s emulator-5556 shell am force-stop com.yunuscakir.pocket.client.debug
adb -s emulator-5556 shell am start -n com.yunuscakir.pocket.client.debug/crc646dd0ab311caf03fd.MainActivity
```

---

### 3. Hızlı Python Seed Veri Örneği

> **Önemli (`sqlite-net-pcl` Tarih Formatı):** `sqlite-net-pcl` varsayılan olarak `DateTime` alanlarını (`CreatedAt`, `AddedAt`, `LastInteractionAt`, `SentAt`) ISO metni yerine **.NET UTC Ticks** (`INTEGER`) olarak saklar (`621355968000000000 + int(dt.timestamp() * 10_000_000)`).

Bilgisayarınıza çektiğiniz bir `.db3` dosyasına otomatik mock veri enjekte etmek için örnek Python betiği:

```python
import sqlite3
import uuid
from datetime import datetime, timezone

def utc_ticks(dt=None):
    dt = dt or datetime.now(timezone.utc)
    return 621355968000000000 + int(dt.timestamp() * 10_000_000)

con = sqlite3.connect("PocketLocal_5554.db3")
cur = con.cursor()

# Mock Arkadaş Ekle
friend_id = str(uuid.uuid4())
now_ticks = utc_ticks()
cur.execute("""
    INSERT OR REPLACE INTO Friend (Id, Username, DisplayName, ProfilePictureUrl, PublicKey, IsMuted, AddedAt, LastInteractionAt)
    VALUES (?, ?, ?, ?, ?, 0, ?, ?)
""", (friend_id, "testarkadas", "Test Arkadaş", "", "", now_ticks, now_ticks))

# Mock Direkt Mesaj Ekle
cur.execute("""
    INSERT OR REPLACE INTO DirectMessage (Id, FriendId, IsIncoming, TextContent, ReactionEmoji, SentAt, SyncStatus)
    VALUES (?, ?, ?, ?, ?, ?, ?)
""", (str(uuid.uuid4()), friend_id, 1, "Merhaba! Bu bir test seed mesajıdır.", None, now_ticks, 2))

con.commit()
con.close()
print("Seed veriler başarıyla eklendi.")
```

---

### 4. Sıfırlama (Temiz Veritabanı ile Başlatma)

Hızlı deployment DLL'lerini bozmadan sadece veritabanını ve yerel medyaları sıfırlamak için:

```powershell
adb -s emulator-5554 shell run-as com.yunuscakir.pocket.client.debug rm -rf files/PocketLocal.db3* files/Media files/Profile
adb -s emulator-5556 shell run-as com.yunuscakir.pocket.client.debug rm -rf files/PocketLocal.db3* files/Media files/Profile
```
Uygulama bir sonraki açılışında temiz bir veritabanı, yeni kullanıcı kimliği ve yeni ECDH anahtarları oluşturur.

---

### 5. Hazır Seed Veritabanı ve Örnek Fotoğrafları Geri Yükleme (`SeedData/`)

Proje kök dizinindeki [`SeedData/`](SeedData/) klasöründe her iki emülatör için önceden eşleştirilmiş E2EE anahtarları, 4 arkadaş profili (`Mert Kaya`, `Selin Demir`, `Zeynep Yılmaz`, `Can Özkan`, `Elif Arslan`), 15 adet yüksek çözünürlüklü örnek fotoğraf, reaksiyonlar ve sohbet mesajları yedeklenmiştir:

* `SeedData/PocketLocal_selin.db3` : `emulator-5554` (`Selin Demir`) için hazır SQLite veritabanı
* `SeedData/PocketLocal_mert.db3` : `emulator-5556` ve bağlı Android telefon (`Mert Kaya`) için hazır SQLite veritabanı

**Debug APK'da gömülü seed:** Debug Android derlemeleri iki veritabanını ve fotoğrafları içerir. Uygulama veritabanı olmadan açıldığında (yeni kurulum veya silinen uygulama verisi) seed otomatik kurulur ve tarihler kurulum anına kaydırılır. Kullanıcı, derlemedeki `AdbTarget`'a göre seçilir: `emulator-5554` → Selin, diğer her şey (emulator-5556, telefon) → Mert. Elle seçmek için `-p:PocketSeedUser=selin|mert`. Var olan veriye dokunulmaz; Release derlemesinde seed yoktur.
* `SeedData/Media/` : 15 adet (800×800) örnek akış ve galeri fotoğrafı (`sample_01_coffee.jpg` .. `sample_15_tea.jpg`)
* `SeedData/Profile/` : 7 adet kullanıcı ve arkadaş profil fotoğrafı
* `SeedData/Restore-SeedData.ps1` : Tek komutla her iki emülatöre veritabanlarını ve fotoğrafları yükleyen PowerShell betiği
* `SeedData/generate_seed_db.py` : Veritabanlarındaki tarihleri güncel zamana yenileyen veya sıfırdan oluşturan Python betiği

**Tek Komutla Her İki Cihaza Geri Yükleme:**
```powershell
# Mevcut yedek veritabanlarını ve fotoğrafları doğrudan yükler:
powershell -ExecutionPolicy Bypass -File .\SeedData\Restore-SeedData.ps1

# Gönderi ve mesaj tarihlerini bugünün saatine tazeleyerek yükler:
powershell -ExecutionPolicy Bypass -File .\SeedData\Restore-SeedData.ps1 -RefreshTimestamps
```


