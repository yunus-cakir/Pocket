---
trigger: always_on
---

# Her kod değişikliğinden sonra emülatörlere ve bağlı fiziksel cihaza deploy

- `Pocket.Client` üzerinde yapılan **her değişiklikten sonra**, [TESTING.md](../../TESTING.md) doğrultusunda uygulamayı bağlı cihazlara deploy et:

  ```powershell
  dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5554 --no-restore; dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5556 --no-restore
  ```

- Önce `adb devices -l` ile bağlı cihazları kontrol et (`emulator-5554`, `emulator-5556`).
- **Gerçek (Fiziksel) Cihaz Varsa:** Eğer `adb devices -l` çıktısında bağlı ve yetkili (`device` durumunda) gerçek bir Android cihaz da varsa (örn: `dd9fc8be`), emülatörlerin yanı sıra o fiziksel cihaza da deploy komutunu çalıştır:
  ```powershell
  dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20<cihaz_serial> --no-restore
  ```
- Deploy başarısız olursa hatayı düzelt ve tekrar deploy et; düzeltilemiyorsa kullanıcıya bildir.
- **Testi kullanıcıya bırak:** Deploy sonrası ekran görüntüsü alma, `input tap`/swipe ile UI otomasyonu, fotoğraf çekme veya herhangi bir fonksiyonel test YAPMA. Sadece deploy'un başarılı olduğunu ve kullanıcının neyi test etmesi gerektiğini kısaca bildir.

