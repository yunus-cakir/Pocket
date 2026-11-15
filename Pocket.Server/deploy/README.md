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

## Birincil (PC) + yedek (Mac)

Sunucu her şeyi bellekte tutuyor. İki sunucu aynı anda açık olursa kullanıcılar ikiye bölünür ve birbirlerine mesaj gönderemez. Bu yüzden kural: **her an yalnızca bir sunucu aktif**.

| | PC (birincil) | Mac (yedek) |
|---|---|---|
| Adres | `https://yunuscakir.tail4d1102.ts.net` | `https://yunuscakir-mac.tail4d1102.ts.net` |
| Ne zaman çalışır | Her zaman (Windows servisi) | Yalnızca PC ~45 sn yanıt vermezse (bekçi script'i) |
| Kurulum | `install-service.ps1` | `mac/install.sh --secret ...` |

Nasıl çalışır:
- **Bekçi (`mac/watchdog.sh`, launchd):** PC'nin `/health` ucunu 15 sn'de bir kontrol eder. PC 3 kontrolde üst üste yanıt vermezse Mac sunucusunu başlatır. PC 2 kontrolde üst üste sağlıklı görünürse Mac sunucusunu durdurur.
- **Devir:** Mac sunucusu kapanırken önce yeni bağlantıları reddeder ve bağlı istemcileri koparır; böylece istemciler PC'ye döner. Ardından teslim edilmemiş mesajları PC'nin `/internal/handoff` ucuna aktarır. Aktarım bellekten belleğe yapılır, diske hiçbir şey yazılmaz (BR-201). Bu uç, ortak gizli anahtar (`Cluster:Secret`) ile korunur.
- **Uygulama:** Varsayılan sunucu seçiliyken uygulama önce PC'ye bağlanmayı dener, olmazsa Mac'e geçer. Mac'e bağlıyken 30 sn'de bir PC'yi yoklar ve PC geri gelince ona döner. Ayarlarda "Sunucuya Bağlandı (yedek: Mac)" yazısı görünür. Elle başka bir adres girilirse yalnızca o adres kullanılır.

Mac kurulumu:
1. Mac'e Tailscale'i kur ve aynı hesapla giriş yap.
2. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)'yı kur.
3. Repoyu Mac'e klonla.
4. PC'de `install-service.ps1` çalıştırıldığında ekrana basılan gizli anahtarı al. Daha sonra tekrar görmek için PC'de `C:\Services\PocketServer\appsettings.Production.json` dosyasında `Cluster.Secret` alanına bakabilirsin.
5. Kurulum script'ini çalıştır:
   ```bash
   cd Pocket.Server/deploy/mac
   chmod +x *.sh
   ./install.sh --secret "<gizli anahtar>"
   ```
6. Logları izlemek için: `tail -f ~/PocketServer/logs/watchdog.log`

Dikkat edilmesi gerekenler:
- Mac'te sunucuyu **elle başlatma** (`dotnet run` vb.). Her zaman bekçi script'i üzerinden çalışsın, yoksa iki sunucu aynı anda açık kalabilir.
- Mac uykudayken bekçi çalışmaz. Yedek olarak kullanılacağı zamanlarda Mac'i uyanık tut (Ayarlar → Pil/Enerji).
- PC kapanırken PC'nin kuyruğundaki teslim edilmemiş mesajlar Mac'e aktarılmaz, çünkü o anda Mac sunucusu henüz çalışmıyor. Bu mesajlar PC'nin belleğindeydi; PC servisi düzgün kapanmazsa kaybolurlar. Bu, bugünkü tek sunuculu davranışla aynı.
- Kapatmak için: `./uninstall.sh`

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

- `C:\Services\PocketServer` klasörüne yayınlar ve `PocketServer` adlı otomatik başlayan bir servis oluşturur. Servis çökerse kendiliğinden yeniden başlar.
- Varsayılan olarak yalnızca `127.0.0.1:5200` adresini dinler. LAN'dan da erişmek için: `.\install-service.ps1 -Urls "http://0.0.0.0:5200"`
- Kod değiştikçe aynı komutu tekrar çalıştır; script servisi durdurup günceller.
- Not: Servis 5200 portunu kullanırken `dotnet run` ile ikinci bir sunucu açılamaz. Geliştirme sırasında `Stop-Service PocketServer` ile servisi durdur.

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

Dosyada yalnızca public key ve kullanıcı adı bulunur, mesaj ya da medya yoktur (BR-201). PC ve Mac kendi kayıtlarını ayrı tutar. Bir kullanıcının anahtarı gerçekten değiştiyse (örneğin yedekten dönmeden yeniden kurulum), o kullanıcının kaydını iki sunucudaki dosyadan da silmek gerekir.
