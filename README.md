# Admin Aimbot Snap Pro

Counter-Strike 2 sunucuları için gelişmiş admin aim assist (nişan yardımı) eklentisi.

## Özellikler

- **Duvar Kontrolü (Ray Tracing)** - Hedefle arada gerçek duvar varsa kilitleme yapmaz. Cam, spawn bariyeri, buyzone, trigger gibi geçirgen entity'lerden ışın geçer.
- **Hareket Tahminleme (Prediction)** - Hedefin hızını analiz ederek 64 tick sunucu hızına göre bir sonraki konumunu tahmin eder.
- **Akıllı Hedef Seçimi** - Bakış açısına (FOV) en yakın ve en kısa mesafedeki düşmanı seçen skorlama algoritması.
- **Çoklu Nokta Trace** - Baş, gövde ve bel olmak üzere 3 farklı noktaya ışın göndererek hedef görünürlüğünü doğrular.
- **Smooth Aim** - Yapılandırılabilir yumuşaklık faktörü ile hedefe doğal geçiş. Dik açılarda otomatik hızlanma.
- **Eğilme Algılama** - Hem kullanıcının hem hedefin eğilme durumunu algılayarak göz/kafa yüksekliğini dinamik ayarlar.
- **Sabit Göz Yüksekliği** - Yukarı/aşağı bakarken model animasyonunun göz seviyesini değiştirmesini engeller (geri besleme döngüsü önleme).
- **Recoil Sıfırlama** - AimPunch ve AimPunchVelocity otomatik sıfırlanır.
- **Admin Yetki Sistemi** - Sadece `@css/generic` yetkisine sahip adminler kullanabilir.
- **Config Desteği** - SmoothFactor, FOV ve MaxDistance JSON config'ten okunur.

## Gereksinimler

| Bileşen | Minimum Versiyon |
|---------|-----------------|
| Counter-Strike 2 Server | Son sürüm |
| CounterStrikeSharp | v1.0.362+ (API v80+) |
| .NET | 8.0 Runtime |

## Kurulum

### 1. Derleme

```bash
dotnet restore
dotnet build -c Release
```

Çıktı: `bin/Release/net8.0/Aimbot.dll`

### 2. Plugin Dosyalarını Kopyalama

```
game/csgo/addons/counterstrikesharp/plugins/Aimbot/
└── Aimbot.dll
```

### 3. GameData Ayarı (ZORUNLU)

`Aimbot.json` dosyasını aşağıdaki konuma kopyalayın:

```
game/csgo/addons/counterstrikesharp/gamedata/Aimbot.json
```

Bu dosya Ray Tracing için gerekli engine imzalarını içerir. **Bu adım yapılmadan duvar kontrolü çalışmaz.**

### 4. Sunucuyu Başlatma

Sunucuyu yeniden başlatın veya:

```
css_plugins load Aimbot
```

Konsolda şu mesajları görmelisiniz:

```
[Aimbot] V3.1.0 Yuklendi. FOV: 360, MaxDist: 5000, Smooth: 0.5
[Aimbot] RayTrace AKTIF - Duvar kontrolu calisiyor.
```

## Kullanım

### Komutlar

| Komut | Açıklama |
|-------|---------|
| `css_aim` / `!aim` | Aim assist aç/kapat |
| `css_tracetest` / `!tracetest` | Ray trace teşhis komutu - baktığın yöne ışın gönderir |

### Oyun İçi

1. Konsola `css_aim` veya chate `!aim` yazın
2. Aktif olduğunda: `[Admin] Aim Assist: ACIK`
3. Tekrar yazarak kapatın

## Yapılandırma

İlk çalıştırmada otomatik oluşturulur:

```
game/csgo/addons/counterstrikesharp/configs/plugins/Aimbot/aimbot_config.json
```

```json
{
  "SmoothFactor": 0.5,
  "FOV": 360.0,
  "MaxDistance": 5000.0
}
```

| Ayar | Varsayılan | Açıklama |
|------|-----------|---------|
| SmoothFactor | 0.5 | Yumuşaklık faktörü (0.0 = çok yavaş, 1.0 = anında snap) |
| FOV | 360.0 | Görüş açısı (derece), 0-360 arası |
| MaxDistance | 5000.0 | Maksimum hedef algılama mesafesi (unit) |

## Dosya Yapısı

```
aimboty/
├── Aimbot.cs           # Ana plugin - hedef bulma, açı hesaplama, aim uygulama
├── RayTrace.cs         # Ray tracing - duvar kontrolü, penetrasyon trace
├── Aimbot.csproj       # Proje dosyası (.NET 8.0)
├── Aimbot.json         # GameData - engine imzaları (gamedata klasörüne kopyalanacak)
└── README.md           # Bu dosya
```

## Teknik Detaylar

### Ray Tracing Sistemi

```
Oyuncu Gözü ───── Işın ─────► Hedef
                   │
                   ├─ Gerçek duvar? → Fraction < 0.97 → ENGEL
                   ├─ Cam/Spawn/Buyzone? → Geçer (penetrasyon trace)
                   └─ Oyuncu/Trigger? → Geçer
```

- `TraceWall` fonksiyonu iteratif penetrasyon trace yapar
- `func_brush`, `trigger_*`, `player`, `info_*`, `prop_*`, `buyzone` gibi entity'lerin içinden geçer
- Sadece gerçek dünya geometrisine (WorldGeometry) çarpar
- Çoklu nokta trace: baş (%100), gövde (%60), bel (%35) yüksekliğine ışın gönderir

### Sabitler

| Sabit | Değer | Açıklama |
|-------|-------|---------|
| StandingEyeHeight | 64.0 | Ayakta göz yüksekliği |
| CrouchingEyeHeight | 46.0 | Eğilirken göz yüksekliği |
| PredictionFactor | 0.015625 | 64 tick prediction faktörü (1/64) |

## Uyarılar

> **Bu eklenti sadece ADMIN kullanımı için tasarlanmıştır.**

- Normal oyunculara yetki verilmesi oyun dengesini bozabilir
- Sadece özel/topluluk sunucularında kullanılması önerilir
- Resmi maçlarda veya rekabetçi ortamlarda kullanmayın

## Geliştirici

**guccukCENEVAR** - [GitHub](https://github.com/guccukCENEVAR)

## Lisans

Bu proje eğitim amaçlıdır. Kullanımı kullanıcının sorumluluğundadır.
