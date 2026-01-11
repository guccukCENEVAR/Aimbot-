# Admin Aimbot Snap Pro

Counter-Strike 2 sunucuları için gelişmiş admin aim assist (nişan yardımı) eklentisi. Hareket tahminleme (prediction), akıllı hedef seçimi ve otomatik eğilme algılama özelliklerine sahiptir.

## 📋 İçindekiler

- [Özellikler](#-özellikler)
- [Gereksinimler](#-gereksinimler)
- [Kurulum](#-kurulum)
- [Kullanım](#-kullanım)
- [Teknik Detaylar](#-teknik-detaylar)
- [Yapılandırma](#-yapılandırma)
- [Derleme](#-derleme)
- [Uyarılar](#-uyarılar)
- [Lisans](#-lisans)

## ✨ Özellikler

- **🎯 Hareket Tahminleme (Movement Prediction)**: Hedefin hızını (`AbsVelocity`) analiz ederek, 64 tick sunucu hızına göre optimize edilmiş prediction algoritması ile hareketli hedefleri doğru şekilde hesaplar
- **🔍 Akıllı Hedef Seçimi**: Sadece mesafeye değil, oyuncunun bakış açısına (FOV) en yakın düşmanı seçen gelişmiş algoritma
- **🦆 Otomatik Eğilme Algılama**: Hem kullanıcının hem de hedefin eğilme durumunu algılayarak kafa yüksekliğini dinamik olarak ayarlar
- **👁️ Face-Focus Sistemi**: Hedefin baktığı yöne göre nişan noktasını 4 birim kaydırarak daha gerçekçi bir kilitlenme sağlar
- **🛡️ Admin Yetki Sistemi**: Sadece `@css/generic` yetkisine sahip adminler tarafından kullanılabilir
- **⚡ Anlık Kilitlenme (Snap)**: Teleport metodu ile görüş açısını anında hedefe yönlendirir

## 📦 Gereksinimler

- Counter-Strike 2 Dedicated Server
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) v1.0.355 veya üzeri
- .NET 8.0 Runtime
- Minimum API Version: 80

## 🚀 Kurulum

1. Bu repository'yi klonlayın veya ZIP olarak indirin:
   ```bash
   git clone https://github.com/kullaniciadi/aimbot.git
   cd aimbot
   ```

2. Release klasöründeki `Aimbot.dll` dosyasını sunucunuzun plugins klasörüne kopyalayın:
   ```
   game/csgo/addons/counterstrikesharp/plugins/Aimbot/
   ```

3. `Aimbot.json` dosyasını aynı klasöre kopyalayın (varsa)

4. Sunucuyu yeniden başlatın veya plugin'i manuel olarak yükleyin:
   ```
   css_plugins load Aimbot
   ```

5. Konsolda şu mesajı görmelisiniz:
   ```
   [Aimbot] V1.2.1 - Hareket Tahminleme (Prediction) Yuklendi!
   ```

## 🎮 Kullanım

### Komutlar

| Komut | Açıklama |
|-------|----------|
| `css_aim` konsola | Aim assist'i açıp kapatır (sadece adminler) |
| `!aim` chate | Aim assist'i açıp kapatır (sadece adminler) |

### Kullanım Adımları

1. Oyun içerisinde konsola veya sohbete `css_aim` yazın
2. Özellik aktif olduğunda chat'te şu mesajı göreceksiniz:
   ```
   [Admin] Aim Assist: ACIK (by guccukCENEVAR)
   [Not] Kilitlemek icin 'E' tusuna basili tutun.
   ```
3. Hedefe kilitlenmek için **'E' (Use)** tuşuna basılı tutun
4. Tekrar `css_aim` yazarak özelliği kapatabilirsiniz

### Yetki Gereksinimleri

Bu eklenti sadece `@css/generic` yetkisine sahip adminler tarafından kullanılabilir. Yetkisi olmayan oyuncular şu hatayı alır:
```
[Hata] Bu komutu kullanmak icin yetkiniz yok.
```

## 🔧 Teknik Detaylar

### Algoritma Açıklamaları

#### Movement Prediction (Hareket Tahminleme)
```csharp
Vector velocity = targetPawn.AbsVelocity ?? new Vector(0,0,0);
Vector predictedOrigin = new Vector(
    targetPawn.AbsOrigin.X + (velocity.X * PredictionFactor),
    targetPawn.AbsOrigin.Y + (velocity.Y * PredictionFactor),
    targetPawn.AbsOrigin.Z + (velocity.Z * PredictionFactor)
);
```
- Prediction Factor: `0.015625` (64 tick için 1/64)
- Hedefin mevcut hızını kullanarak bir sonraki tick'teki konumunu tahmin eder
- Bu sayede hareketli hedeflere karşı daha isabetli sonuçlar alınır

#### Hedef Seçim Algoritması
```csharp
float score = angle + (dist / 5000.0f);
```
- En düşük skora sahip hedef seçilir
- Skor = Açı mesafesi + (Gerçek mesafe / 5000)
- Bu sayede hem açı hem de mesafe faktörleri dengelenir

#### Eğilme Algılama
```csharp
float currentEyeHeight = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 
    ? CrouchEyeHeight : StandEyeHeight;
```
- Stand (Ayakta): Göz yüksekliği 64.0, Kafa yüksekliği 65.0
- Crouch (Eğilmiş): Göz yüksekliği 46.0, Kafa yüksekliği 46.0

### Sabitler

| Sabit | Değer | Açıklama |
|-------|-------|----------|
| `FOV` | 360.0° | Görüş açısı (her yöndeki düşmanları algılar) |
| `StandEyeHeight` | 64.0 | Ayakta dururken göz yüksekliği |
| `CrouchEyeHeight` | 46.0 | Eğilirken göz yüksekliği |
| `StandHeadHeight` | 65.0 | Ayakta dururken kafa yüksekliği |
| `CrouchHeadHeight` | 46.0 | Eğilirken kafa yüksekliği |
| `PredictionFactor` | 0.015625 | 64 tick için prediction faktörü (1/64) |
| Max Distance | 5000.0 | Maksimum hedef algılama mesafesi |
| Face-Focus Offset | 4.0 | Hedefin baktığı yöne göre kaydırma mesafesi |

## ⚙️ Yapılandırma

`Aimbot.json` dosyası ile (eğer varsa) eklenti ayarlarını yapılandırabilirsiniz. Şu an için kod içerisinde sabit değerler kullanılmaktadır.

## 🔨 Derleme

Kendi binary'nizi oluşturmak için:

1. .NET 8.0 SDK'nın yüklü olduğundan emin olun
2. Projeyi klonlayın
3. Gerekli paketleri restore edin:
   ```bash
   dotnet restore
   ```
4. Release modunda derleyin:
   ```bash
   dotnet build -c Release
   ```
5. Çıktı dosyası: `bin/Release/net8.0/Aimbot.dll`

### Windows için Hızlı Derleme
```bash
build.bat
```

## ⚠️ Uyarılar

- ⚠️ **Bu eklenti sadece ADMIN kullanımı için tasarlanmıştır**
- ⚠️ Normal oyunculara yetki verilmesi oyun dengesini bozabilir
- ⚠️ Hile koruma sistemleri (VAC, Faceit Anti-Cheat vb.) tarafından tespit edilebilir
- ⚠️ **Sadece özel/topluluk sunucularında kullanılması önerilir**
- ⚠️ Resmi maçlarda veya rekabetçi ortamlarda kullanmayın
- ⚠️ Sunucu kurallarına ve Counter-Strike kullanım şartlarına uygun kullanın

## 📝 Sürüm Notları

### v1.2.1
- ✅ Hareket tahminleme (prediction) özelliği eklendi
- ✅ Face-focus sistemi eklendi
- ✅ Otomatik eğilme algılama iyileştirildi
- ✅ Özel kullanıcı ID yetki sistemi kaldırıldı (sadece admin yetkisi)

### v1.0.0
- İlk sürüm

## 🤝 Katkıda Bulunma

1. Fork edin
2. Feature branch oluşturun (`git checkout -b feature/AmazingFeature`)
3. Değişikliklerinizi commit edin (`git commit -m 'Add some AmazingFeature'`)
4. Branch'inizi push edin (`git push origin feature/AmazingFeature`)
5. Pull Request açın

## 📄 Lisans

Bu proje eğitim amaçlıdır. Kullanımı kullanıcının sorumluluğundadır.

## 👤 Geliştirici

**guccukCENEVAR**

- GitHub: [@guccukCENEVAR](https://github.com/guccukCENEVAR)

## 🙏 Teşekkürler

- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) - API için
- Counter-Strike 2 topluluğu - Test ve geri bildirim için

## 📞 Destek

Sorun bildirimi için [Issues](https://github.com/kullaniciadi/aimbot/issues) sayfasını kullanın.

---

⭐ Bu projeyi beğendiyseniz yıldız vermeyi unutmayın!

