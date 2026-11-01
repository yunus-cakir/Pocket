---
trigger: always_on
---

# Her kod değişikliğinden sonra iki emülatöre deploy

- `Pocket.Client` üzerinde yapılan **her değişiklikten sonra**, [TESTING.md](../../TESTING.md) içindeki "Her İki Emülatöre Sıralı Hızlı Rebuild & Run" komutuyla uygulamayı **her iki cihaza** deploy et:

  ```powershell
  dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5554 --no-restore; dotnet build Pocket.Client/Pocket.Client.csproj -t:Run -f net10.0-android -p:AdbTarget=-s%20emulator-5556 --no-restore
  ```

- Önce `adb devices -l` ile `emulator-5554` ve `emulator-5556` cihazlarının bağlı olduğunu kontrol et. Biri yoksa deploy etmeden kullanıcıya bildir.
- Deploy başarısız olursa hatayı düzelt ve tekrar deploy et; düzeltilemiyorsa kullanıcıya bildir.
- **Testi kullanıcıya bırak:** Deploy sonrası ekran görüntüsü alma, `input tap`/swipe ile UI otomasyonu, fotoğraf çekme veya herhangi bir fonksiyonel test YAPMA. Sadece deploy'un başarılı olduğunu ve kullanıcının neyi test etmesi gerektiğini kısaca bildir.

