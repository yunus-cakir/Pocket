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

## ⚠️ Güvenlik notu

İstemci JWT'yi kendisi imzalıyor ve imza anahtarı uygulamanın içinde gömülü. Sunucu internete açıkken APK'yı inceleyen biri herhangi bir kullanıcı adına token üretebilir. İçerik E2EE olduğu için mesajlar okunamaz, ama kimlik taklidi ve sunucunun kötüye kullanılması mümkün. Gerçek kullanıcılara açmadan önce token'ı sunucunun vermesi sağlanmalı.
