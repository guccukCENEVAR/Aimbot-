# Admin Aimbot Snap Pro

Counter-Strike 2 sunucuları için gelişmiş admin aim assist (nişan yardımı) eklentisi. Hareket tahminleme (prediction), **duvar kontrolü (ray tracing)**, akıllı hedef seçimi ve otomatik eğilme algılama özelliklerine sahiptir.

## 📋 İçindekiler

- [Özellikler](#-özellikler)
- [Yenilikler v1.3.0](#-yenilikler-v130)
- [Gereksinimler](#-gereksinimler)
- [Kurulum](#-kurulum)
- [Kullanım](#-kullanım)
- [Teknik Detaylar](#-teknik-detaylar)
- [Yapılandırma](#%EF%B8%8F-yapılandırma)
- [Derleme](#-derleme)
- [Uyarılar](#%EF%B8%8F-uyarılar)
- [Lisans](#-lisans)

## ✨ Özellikler

- **🧱 Duvar Kontrolü (Ray Tracing)**: Hedefle arada duvar veya engel varsa kilitleme yapmaz - gerçekçi görüş hattı kontrolü
- **🎯 Hareket Tahminleme (Movement Prediction)**: Hedefin hızını (`AbsVelocity`) analiz ederek, 64 tick sunucu hızına göre optimize edilmiş prediction algoritması ile hareketli hedefleri doğru şekilde hesaplar
- **🔍 Akıllı Hedef Seçimi**: Sadece mesafeye değil, oyuncunun bakış açısına (FOV) en yakın düşmanı seçen gelişmiş algoritma
- **🦆 Otomatik Eğilme Algılama**: Hem kullanıcının hem de hedefin eğilme durumunu algılayarak kafa yüksekliğini dinamik olarak ayarlar
- **👁️ Face-Focus Sistemi**: Hedefin baktığı yöne göre nişan noktasını 4 birim kaydırarak daha gerçekçi bir kilitlenme sağlar
- **🛡️ Admin Yetki Sistemi**: Sadece `@css/generic` yetkisine sahip adminler tarafından kullanılabilir
- **⚡ Anlık Kilitlenme (Snap)**: Teleport metodu ile görüş açısını anında hedefe yönlendirir

## 🆕 Yenilikler v1.3.0

### 🧱 Ray Tracing Duvar Kontrolü
- Oyuncu ile hedef arasında duvar/engel kontrolü
- `TraceEndShape` fonksiyonu ile hassas ışın izleme
- Dünya geometrisi ve solid objelerle etkileşim kontrolü
- Fraction değeri ile görüş hattı doğrulaması (≥0.99 = temiz görüş)

### 📁 Yeni Dosya Yapısı
```
aimbot/
├── Aimbot.cs           # Ana plugin sınıfı
├── RayTrace.cs         # Ray tracing implementasyonu (YENİ)
├── aimbot.csproj       # Proje dosyası
├── aimbot.sln          # Solution dosyası
├── .gitignore          # Git ignore dosyası
└── README.md           # Bu dosya
```

## 📦 Gereksinimler

| Bileşen | Minimum Versiyon |
|---------|------------------|
| Counter-Strike 2 Server | Son sürüm |
| CounterStrikeSharp | v1.0.355+ (API v80+) |
| .NET | 8.0 Runtime |

## 🚀 Kurulum

1. Bu repository'yi klonlayın veya ZIP olarak indirin:
```bash
git clone https://github.com/guccukCENEVAR/Aimbot-.git
cd Aimbot-
```

2. Release klasöründeki dosyaları sunucunuzun plugins klasörüne kopyalayın:
```
game/csgo/addons/counterstrikesharp/plugins/Aimbot/
├── Aimbot.dll
└── RayTrace.dll (gerekirse)
```

3. Sunucuyu yeniden başlatın veya plugin'i manuel olarak yükleyin:
```
css_plugins load Aimbot
```

4. Konsolda şu mesajı görmelisiniz:
```
[Aimbot] V1.3.0 - Wall Check + Prediction Yuklendi!
```

## 🎮 Kullanım

### Komutlar

| Komut | Açıklama |
|-------|----------|
| `css_aim` (konsol) | Aim assist'i açıp kapatır |
| `!aim` (chat) | Aim assist'i açıp kapatır |

### Kullanım Adımları

1. Oyun içerisinde konsola veya sohbete `css_aim` veya `!aim` yazın
2. Özellik aktif olduğunda chat'te şu mesajı göreceksiniz:
```
[Admin] Aim Assist: ACIK (by guccukCENEVAR)
```
3. Tekrar `!aim` yazarak özelliği kapatabilirsiniz

### Yetki Gereksinimleri

Bu eklenti sadece `@css/generic` yetkisine sahip adminler tarafından kullanılabilir. Yetkisi olmayan oyuncular şu hatayı alır:
```
[Hata] Bu komutu kullanmak icin yetkiniz yok.
```

## 🔧 Teknik Detaylar

### Ray Tracing Sistemi (v1.3.0)

```
Oyuncu Gözü ────── Ray ──────► Hedef Kafası
                    │
                    ├─ Duvar var mı? (WorldGeometry)
                    ├─ Solid obje var mı?
                    └─ Fraction kontrolü (≥0.99 = temiz görüş)
```

```csharp
// Ray trace seçenekleri
var traceOptions = new TraceOptions
{
    InteractsExclude = InteractionLayers.Player | InteractionLayers.NPC,
    InteractsWith = InteractionLayers.Solid | InteractionLayers.WorldGeometry,
};

// Işın gönder ve sonucu kontrol et
var result = TraceEndShape(startPos, endPos, false, player.PlayerPawn, traceOptions);
if (result.Value.Fraction >= 0.99f) // Temiz görüş hattı
```

### Movement Prediction (Hareket Tahminleme)

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

### Hedef Seçim Algoritması

```csharp
float score = angle + (dist / 5000.0f);
```

- En düşük skora sahip hedef seçilir
- Skor = Açı mesafesi + (Gerçek mesafe / 5000)
- **YENİ**: Arada duvar varsa hedef atlanır

### Sabitler

| Sabit | Değer | Açıklama |
|-------|-------|----------|
| FOV | 360.0° | Görüş açısı (her yöndeki düşmanları algılar) |
| StandEyeHeight | 64.0 | Ayakta dururken göz yüksekliği |
| CrouchEyeHeight | 46.0 | Eğilirken göz yüksekliği |
| StandHeadHeight | 65.0 | Ayakta dururken kafa yüksekliği |
| CrouchHeadHeight | 46.0 | Eğilirken kafa yüksekliği |
| PredictionFactor | 0.015625 | 64 tick için prediction faktörü (1/64) |
| Max Distance | 5000.0 | Maksimum hedef algılama mesafesi |
| Face-Focus Offset | 4.0 | Hedefin baktığı yöne göre kaydırma mesafesi |

## ⚙️ Yapılandırma

Şu an için kod içerisinde sabit değerler kullanılmaktadır.

## 🔨 Derleme

### .NET CLI ile

```bash
dotnet restore
dotnet build -c Release
```

Çıktı dosyası: `bin/Release/net8.0/aimbot.dll`

### Visual Studio ile

1. `aimbot.sln` dosyasını açın
2. NuGet paketlerini restore edin
3. Release modunda derleyin

## ⚠️ Uyarılar

> ⚠️ **Bu eklenti sadece ADMIN kullanımı için tasarlanmıştır**

- Normal oyunculara yetki verilmesi oyun dengesini bozabilir
- **Sadece özel/topluluk sunucularında kullanılması önerilir**
- Resmi maçlarda veya rekabetçi ortamlarda kullanmayın
- Sunucu kurallarına ve Counter-Strike kullanım şartlarına uygun kullanın

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

## 📞 Destek

Sorun bildirimi için [Issues](https://github.com/guccukCENEVAR/Aimbot-/issues) sayfasını kullanın.

---

⭐ Bu projeyi beğendiyseniz yıldız vermeyi unutmayın!
