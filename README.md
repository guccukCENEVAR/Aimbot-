# Admin Aimbot Snap Pro

Counter-Strike 2 sunucuları için gelişmiş admin aim assist eklentisi.

## Özellikler

- **SnapViewAngles** - Engine'in bot bakış açısı fonksiyonunu kullanır. Teleport'a göre model glitch oluşmaz. Yüklenemezse otomatik Teleport fallback'e döner.
- **Duvar Kontrolü (FUNPLAY Ray-Trace)** - Engine seviyesinde ray tracing ile hedefle arada gerçek duvar varsa kilitleme yapmaz.
- **Hareket Tahminleme** - Hedefin hızını analiz ederek 64 tick hızına göre bir sonraki konumunu tahmin eder.
- **Akıllı Hedef Seçimi** - Bakış açısına (FOV) en yakın ve en kısa mesafedeki düşmanı seçen skorlama algoritması.
- **Çoklu Nokta Trace** - Baş, gövde ve bel olmak üzere 3 farklı noktaya ışın göndererek hedef görünürlüğünü doğrular.
- **Smooth Aim** - Yapılandırılabilir yumuşaklık faktörü ile hedefe doğal geçiş. Dik açılarda otomatik hızlanma.
- **Eğilme Algılama** - Hem kullanıcının hem hedefin eğilme durumunu algılayarak göz yüksekliğini dinamik ayarlar.
- **Recoil Sıfırlama** - AimPunch ve AimPunchVelocity otomatik sıfırlanır.
- **Admin Yetki Sistemi** - Sadece `@css/generic` yetkisine sahip adminler kullanabilir.
- **Config Desteği** - SmoothFactor, FOV ve MaxDistance JSON config'ten okunur.

## Gereksinimler

| Bileşen | Minimum Versiyon |
|---------|-----------------|
| Counter-Strike 2 Server | Son sürüm |
| Metamod:Source | 2.x |
| CounterStrikeSharp | v1.0.362+ (API v80+) |
| FUNPLAY Ray-Trace | v1.0.3+ |
| .NET | 8.0 Runtime |

## Kurulum

### 1. FUNPLAY Ray-Trace Metamod Modülü (ZORUNLU)

Duvar kontrolü için gereklidir. Modül olmadan duvar kontrolü çalışmaz.

**İndirme:** https://github.com/FUNPLAY-pro-CS2/Ray-Trace/releases

```
game/csgo/addons/
├── RayTrace/
│   └── bin/linuxsteamrt64/RayTrace.so
└── metamod/
    └── RayTrace.vdf
```

### 2. GameData Dosyası

`Aimbot.json` dosyasını gamedata klasörüne kopyalayın:

```
game/csgo/addons/counterstrikesharp/gamedata/Aimbot.json
```

```json
{
    "CCSBot_SnapViewAngles": {
        "signatures": {
            "library": "server",
            "windows": "48 89 5C 24 ? 48 89 74 24 ? 48 89 7C 24 ? 55 48 8D 6C 24 ? 48 81 EC ? ? ? ? 48 8B DA 48 8B F1 48 8D 55",
            "linux": "55 48 89 E5 41 57 41 56 41 55 41 54 53 48 89 FB 48 89 F7 48 81 EC ? ? ? ? E8 ? ? ? ? 48 8B 93"
        }
    }
}
```

### 3. Plugin Derleme

```bash
dotnet restore
dotnet build -c Release
```

### 4. Plugin Dosyalarını Kopyalama

```
game/csgo/addons/counterstrikesharp/plugins/Aimbot/
└── Aimbot.dll
```

### 5. Sunucuyu Başlatma

Sunucuyu yeniden başlatın. Konsolda mesajlar görünecektir.

## Kullanım

| Komut | Açıklama |
|-------|---------|
| `!aim` | Aim assist aç/kapat |
| `!tracetest` | Ray trace teşhis komutu (baktığın yöne ışın gönderir) |

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
| SmoothFactor | 0.5 | Yumuşaklık (0.0 = yavaş, 1.0 = anında snap) |
| FOV | 360.0 | Görüş açısı (derece) |
| MaxDistance | 5000.0 | Maksimum hedef mesafesi (unit) |

## Dosya Yapısı

```
aimboty/
├── Aimbot.cs           # Ana plugin
├── RayTrace.cs         # FUNPLAY Ray-Trace C# wrapper
├── Aimbot.csproj       # Proje dosyası (.NET 8.0)
└── README.md
```

## Teknik Detaylar

### Açı Uygulama: SnapViewAngles

`CCSBot::SnapViewAngles` - CS2 engine'inin bot bakış açısı fonksiyonu. GameData signature ile yüklenir.

- Sadece view angle değiştirir (pozisyon/rotasyon etkilenmez)
- Model glitch oluşmaz (Teleport'ta yaşanan "yatma" sorunu yok)
- Yüklenemezse otomatik olarak Teleport fallback'e döner

### Ray Tracing: FUNPLAY Ray-Trace

[FUNPLAY Ray-Trace](https://github.com/FUNPLAY-pro-CS2/Ray-Trace) Metamod modülü üzerinden engine seviyesinde trace.

- `InteractsWith`: Solid, Window, PassBullets, WorldGeometry
- `InteractsExclude`: Player, NPC, Trigger, Debris, Physics_Prop, Pickup, TouchAll
- 3 noktaya trace: baş, gövde (%60), bel (%35)
- Herhangi biri geçerse hedef görünür kabul edilir

## Uyarılar

> **Bu eklenti sadece ADMIN kullanımı için tasarlanmıştır.**

- Normal oyunculara yetki verilmesi oyun dengesini bozabilir
- Sadece özel/topluluk sunucularında kullanılması önerilir

## Geliştirici

**guccukCENEVAR** - [GitHub](https://github.com/guccukCENEVAR)

## Lisans

Bu proje eğitim amaçlıdır. Kullanımı kullanıcının sorumluluğundadır.
