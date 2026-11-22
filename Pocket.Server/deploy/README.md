# Pocket.Server: Windows servisi ve dış erişim

Dış erişim için iki yol var:
- **Tailscale Funnel**: ücretsiz, alan adı gerekmez. Şu an kullanılan yol bu.
- **Cloudflare Tunnel**: kendi alan adınla çalışır.

Her iki yolda da önce aşağıdaki **1. adım** ile sunucuyu servis olarak kurman gerekir.

## Tailscale Funnel (ücretsiz)

```
Telefon ──HTTPS/WSS──▶ Tailscale Funnel ◀── tailscaled (servis) ──▶ http://127.0.0.1:5200 (PocketServer servisi)
```

İlk kurulum (bir kez):
```powershell
tailscale up --unattended          # tarayıcıda giriş yap; Windows oturumu kapalıyken de bağlı kalır
.\setup-tailscale-funnel.ps1       # ilk seferde çıkan linkten Funnel'ı onayla
```

- Herkese açık adres sabittir: `https://<bilgisayar>.<tailnet>.ts.net`. Uygulamada **Ayarlar → Server** alanına bu adresi yaz.
- Funnel ayarı kalıcıdır ve bilgisayar yeniden başladığında Tailscale servisiyle birlikte geri gelir.
- Durumu görmek için: `tailscale funnel status`. Kapatmak için: `.\setup-tailscale-funnel.ps1 -Off`

## Birincil (Mac) + yedek (PC): çift yönlü bekçi

Sunucu her şeyi bellekte tutuyor. İki makinede de bir bekçi çalışır ve her biri diğerini izler. İki sunucunun aynı anda açık olması sorun değil (aşağıdaki "Çoklu cihaz ve çoklu sunucu" bölümüne bak); bekçi yine de normalde yalnızca birincili açık tutar.

| | Mac (birincil) | PC (yedek) |
|---|---|---|
| Adres | `https://yunuscakir-mac.tail4d1102.ts.net` | `https://yunuscakir.tail4d1102.ts.net` |
| Bekçi | `mac/watchdog.sh` (launchd, `com.pocket.watchdog`) | `windows/watchdog.ps1` (zamanlanmış görev `PocketServerWatchdog`, SYSTEM) |
| Sunucu ne zaman çalışır | Her zaman | Yalnızca Mac ~45 sn yanıt vermezse ya da Mac planlı kapanıyorsa |
| Kurulum | `mac/install.sh --secret ...` (varsayılan `--role primary`) | `install-service.ps1` (varsayılan `-Role standby`) |

Bekçinin kuralları (iki tarafta da aynı mantık; rol `appsettings.Production.json` → `Cluster.Role`):
- **Birincil:** Kendi sunucusunu hep açık tutar, kapanırsa yeniden başlatır.
- **Yedek:** Karşı tarafın `/health` ucunu 15 sn'de bir kontrol eder.
  - Kontrol, istemcilerin kullandığı **genel Funnel yolundan** yapılır: adres genel DNS ile (DNS-over-HTTPS, `dns.google`) çözülür. Karşı taraf tailnet üzerinden yanıt verip genel adresten vermiyorsa (örneğin Mac'te VPN açıkken) kapalı sayılır (`down-public`). Genel DNS'e ulaşılamazsa normal adres kullanılır. Bekçinin gördüğü durumu görmek için PC'de: `powershell -File C:\Services\PocketServer\watchdog.ps1 -CheckOnly`
  - Karşı taraf 3 kontrolde üst üste yanıt vermezse (makine kapalı, uykuda, ağ yok) kendi sunucusunu başlatır.
  - Karşı taraf **503 (devrediyor)** derse beklemeden hemen başlatır.
  - Karşı taraf 2 kontrolde üst üste sağlıklı görünürse kendi sunucusunu durdurur.
  - Kendi sunucusu kapalıyken kimlik kaydını yaklaşık dakikada bir karşı tarafla eşitler.

Devir (iki yönde de aynı, `HandoffService`). Kapanan sunucu:
1. Yeni bağlantıları reddetmeye başlar ve `/health` ucu 503 döner. Bağlı istemciler bu sırada bağlı kalır.
2. Karşı tarafın ayağa kalkmasını en çok `Cluster:HandoffWaitSeconds` (varsayılan 90 sn) bekler. Karşı taraf zaten açıksa (yedek birincile geri verirken) beklemez.
3. Kimlik kaydını karşı tarafla eşitler, istemcileri koparır (istemciler zaten diğer sunucuya da bağlıdır) ve teslim edilmemiş mesajları karşı tarafın `/internal/handoff` ucuna aktarır. Aktarım bellekten belleğe yapılır, diske hiçbir şey yazılmaz (BR-201). Bu uç ortak gizli anahtarla (`Cluster:Secret`) korunur.

Örnekler:
- **Mac güncelleniyor ya da elle durduruluyor:** Mac 503 döner → PC bekçisi PC sunucusunu hemen başlatır → Mac kuyruğunu PC'ye aktarıp kapanır → Mac geri gelince PC ~30 sn içinde kuyruğunu Mac'e geri verip durur.
- **Mac aniden kapanıyor ya da uykuya geçiyor:** PC ~45 sn sonra devralır. Mac'in belleğindeki teslim edilmemiş mesajlar kaybolur (tek sunuculu davranışla aynı).

Mac kurulumu:
1. Mac'e Tailscale'i kur ve aynı hesapla giriş yap.
2. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)'yı kur.
3. Repoyu Mac'e klonla.
4. Gizli anahtarı al: PC'de `install-service.ps1` çalışınca ekrana basılır. Daha sonra görmek için PC'de `C:\Services\PocketServer\appsettings.Production.json` dosyasındaki `Cluster.Secret` alanına bak.
5. Kurulum script'ini çalıştır:
   ```bash
   cd Pocket.Server/deploy/mac
   chmod +x *.sh
   ./install.sh --secret "<gizli anahtar>"
   ```
   Eski yedek bekçi (`com.pocket.standby-watchdog`) varsa script onu kaldırır.
6. Logları izlemek için: `tail -f ~/PocketServer/logs/watchdog.log`
7. Güncellemek için repoyu çek (`git pull`) ve `./install.sh` komutunu çalıştır. `--secret` vermezsen mevcut gizli anahtar korunur.

PC kurulumu (Yönetici PowerShell): `.\install-service.ps1`. Servisin başlangıç türü **Manual** olur; servisi bekçi başlatıp durdurur. Bekçi logu: `C:\Services\PocketServer\logs\watchdog.log`.

Rolleri ileride tersine çevirmek için Mac'te `./install.sh --role standby`, PC'de `.\install-service.ps1 -Role primary` çalıştır. İki tarafı aynı anda birincil ya da yedek yapma.

Dikkat edilmesi gerekenler:
- Sunucuyu iki tarafta da **elle başlatma** (`dotnet run`, `Start-Service`). Her zaman bekçi üzerinden çalışsın. Bekçiyi durdurmak için Mac'te `launchctl bootout gui/$(id -u)/com.pocket.watchdog`, PC'de `Stop-ScheduledTask PocketServerWatchdog` kullan.
- Mac uykudayken birincil çalışmaz ve PC devralır. Mac'in uyanık kalması için: Ayarlar → Pil/Enerji.
- Makinenin kendisi kapanırken (yeniden başlatma, kapatma) işletim sistemi devir için 90 sn beklemeyebilir. Bu durumda devir yarım kalabilir.
- Kaldırmak için Mac'te `./uninstall.sh`, PC'de `.\uninstall-service.ps1` çalıştır (bekçi görevini de siler).

## Çoklu cihaz ve çoklu sunucu

Aynı kimlik (aynı kullanıcı ID'si ve anahtar, ör. aynı Selin veritabanı) birden fazla cihazda açık olabilir.

- **İstemci:** Ayarlardaki her sunucuya (birincil + ikincil) aynı anda bağlanır. Gönderdiği her mesajı bağlı olduğu tüm sunuculara yollar; en az biri kabul ederse mesaj gönderilmiş sayılır. Aynı mesaj iki sunucudan gelirse mesaj ID'sine göre bir kez işlenir. Teslim onayı da bağlı tüm sunuculara gider. Kapalı sunucu arka planda yoklanır (`/health`), açılınca ona da bağlanılır.
- **Cihaz kimliği:** Her kurulum rastgele bir cihaz ID'si üretir (Android'de yedeklenmeyen klasörde tutulur) ve girişte (`/auth/token`, `deviceId`) gönderir. Sunucu bunu JWT'ye `device` claim'i olarak koyar.
- **Sunucu:** Bir mesaj, alıcının tüm aktif cihazları onaylayana kadar bellekte kalır. Böylece o an kapalı olan cihaz da açılınca mesajı alır. Bağlı olan ya da son 7 gün içinde görülen cihaz aktif sayılır; 7 günden uzun süre görünmeyen cihaz mesaj bekletmez (saatlik temizlik). Gönderene teslim bilgisi ilk onaylayan cihazla gider.
- **Devir:** Kapanan sunucunun karşı tarafa aktardığı her mesajla birlikte, onu zaten onaylamış cihazlar (`X-Pocket-Confirmed-Devices`) ve alıcının aktif cihazları (`X-Pocket-Recipient-Devices`) da gider. PC mesajı yalnızca henüz almamış cihazlara gönderir; örneğin yeniden kurulumdan sonra yeni kurulum aynı fotoğrafı ikinci kez indirmez. Gövde aynı kaldığı için eski sürüm sunucu bu başlıkları yok sayar.
- **Sınır:** Sunucu yeniden başladıktan sonra henüz hiç bağlanmamış bir cihazı tanımaz. Bu sırada diğer cihaz mesajı onaylarsa mesaj silinir ve o cihaz alamaz.
- **Eski istemciler** (cihaz ID'si göndermeyen) tek bir ortak yuvayı paylaşır: eskisi gibi ilk onayda silinir.

## Cloudflare Tunnel (kendi alan adınla)

```
Telefon ──HTTPS/WSS──▶ Cloudflare ◀──giden bağlantı── cloudflared (servis) ──▶ http://127.0.0.1:5200 (PocketServer servisi)
```

`cloudflared` bağlantıyı bilgisayardan dışarıya doğru açtığı için port yönlendirmeye ve gerçek IP'ye gerek yoktur (CGNAT altında da çalışır).

## 1. Sunucuyu servis olarak kur (Yönetici PowerShell)

```powershell
cd "C:\Library\Projects\Visual Studio\Pocket\Pocket.Server\deploy"
.\install-service.ps1
```

- `C:\Services\PocketServer` klasörüne yayınlar, `PocketServer` servisini ve `PocketServerWatchdog` bekçi görevini kurar (yukarıdaki "çift yönlü bekçi" bölümü). Servis çökerse kendiliğinden yeniden başlar.
- Varsayılan olarak yalnızca `127.0.0.1:5200` adresini dinler. LAN'dan da erişmek için: `.\install-service.ps1 -Urls "http://0.0.0.0:5200"`
- Kod değiştikçe aynı komutu tekrar çalıştır; script bekçiyi ve servisi durdurup günceller. Servis o an çalışıyorsa önce karşı tarafa devreder (en çok ~3 dk).
- Not: Servis 5200 portunu kullanırken `dotnet run` ile ikinci bir sunucu açılamaz. Geliştirme sırasında önce `Stop-ScheduledTask PocketServerWatchdog`, sonra `Stop-Service PocketServer` çalıştır.

## 2. Cloudflare tarafı (tarayıcıda, bir kez)

1. Cloudflare hesabına bir alan adı ekle ve alan adı kayıt firmasındaki nameserver'ları Cloudflare'in verdikleriyle değiştir. Alan adı Cloudflare'den de alınabilir.
2. **Zero Trust → Networks → Tunnels → Create a tunnel → Cloudflared** yolunu izle ve tünele bir ad ver (örn. `pocket`).
3. "Install connector" ekranında Windows'u seç ve komuttaki `eyJ...` ile başlayan **token**'ı kopyala.
4. **Public Hostname** ekle:
   - Subdomain: `pocket`, Domain: `alanadin.com`
   - Service: `HTTP` → `localhost:5200`
5. Alan adı ayarlarında **Network → WebSockets** açık olmalı (varsayılan olarak açıktır).

## 3. cloudflared'i servis olarak kur (Yönetici PowerShell)

```powershell
.\setup-cloudflare-tunnel.ps1 -Token "eyJ..."
```

## 4. Doğrula ve uygulamayı bağla

```powershell
Invoke-RestMethod https://pocket.alanadin.com/
# → "📸 Pocket Transient Relay Server is running."
```

Uygulamada **Ayarlar → Server** alanına `https://pocket.alanadin.com` yaz ve **Save**'e bas. Alan şunları kabul eder:

| Girdi | Kullanılan adres |
|---|---|
| `192.168.1.35` | `http://192.168.1.35:5200/hubs/relay` |
| `192.168.1.35:6000` | `http://192.168.1.35:6000/hubs/relay` |
| `https://pocket.alanadin.com` | `https://pocket.alanadin.com/hubs/relay` |

## Kaldırma

```powershell
.\uninstall-service.ps1
& "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe" service uninstall
```

## Kimlik doğrulama

Token'ı sunucu verir; uygulamada hiçbir gizli anahtar bulunmaz.

1. Uygulama `GET /auth/challenge` ile tek kullanımlık bir nonce alır.
2. `pocket-auth-v1|{userId}|{nonce}` metnini kendi kimlik anahtarıyla imzalar (ECDSA P-256). Bu, arkadaşlarının E2EE için kullandığı anahtarın aynısıdır. İmzayı `POST /auth/token` ile gönderir.
3. Sunucu imzayı doğrular ve kendi **rastgele, işlem başına üretilen** anahtarıyla imzalanmış 12 saatlik bir JWT döndürür. Sunucu yeniden başlarsa eski token'lar geçersiz olur; uygulama her bağlantıda yeni token aldığı için bu sorun çıkarmaz.

**Kimlik kaydı (`data/identities.json`):** Bir kullanıcı ID'si ilk kez hangi anahtarla giriş yaptıysa artık yalnızca o anahtarı kabul eder (ilk kullanımda güven, TOFU). Kullanıcı adı da o ID'ye bağlanır. Böylece:
- Başkasının ID'siyle giriş yapılamaz.
- Başkasının kullanıcı adı kendi anahtarınla kaydedilemez, yani arkadaş eklerken araya girme saldırısı engellenir.
- Arkadaşlık isteklerinde gönderen bilgisi sunucu tarafından doldurulur.

Dosyada yalnızca public key ve kullanıcı adı bulunur, mesaj ya da medya yoktur (BR-201).

**Mac ⇄ PC eşitleme:** İki sunucu kayıtlarını `POST /internal/identities/sync` ucuyla eşitler. Bu uç `Cluster:Secret` ile korunur ve iki taraf da karşı tarafın kaydını kendi kaydıyla birleştirir.
- Her sunucu açılırken, giriş kabul etmeye başlamadan önce karşı tarafla eşitlenir (`Cluster:PeerUrl`). Karşı taraf kapalıysa 5 sn bekleyip devam eder.
- Her sunucu kapanırken, istemcileri koparmadan önce karşı tarafla eşitlenir.
- Yedeğin sunucusu kapalıyken bekçisi kayıt dosyasını yaklaşık dakikada bir karşı tarafla eşitler.
- `dotnet publish`, geliştirme sırasında oluşan `data/` klasörünü yayına koymaz. Böylece kurulum canlı kaydın üzerine yazmaz.

Çakışma kuralları (iki sunucuda da aynı sonucu verir):
- Aynı kullanıcı ID'si iki sunucuda farklı anahtarlara bağlandıysa, **ilk bağlanan** anahtar kazanır.
- Aynı kullanıcı adı farklı kullanıcılarca alındıysa, **önce alan** adı korur. Diğerinin adı boşaltılır ve uygulamada yeni bir ad seçmesi gerekir.
- Kaydı değişen ve o an bağlı olan kullanıcı bağlantıdan düşürülür; uygulama yeniden giriş yapar.

Bir kullanıcının anahtarı gerçekten değiştiyse (örneğin yedekten dönmeden yeniden kurulum), o kullanıcının kaydını önce iki sunucuda da durdurup sonra iki dosyadan birden silmek gerekir. Yoksa eşitleme eski kaydı geri getirir.
