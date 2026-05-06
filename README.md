# 🎯 Admin Aimbot Snap Pro

Counter-Strike 2 sunucuları için geliştirilmiş, **FUNPLAY Ray-Trace** entegrasyonuna sahip profesyonel admin aim assist eklentisi.

---

## ✨ Özellikler

- 👁️ **SnapViewAngles Entegrasyonu:** CS2 engine'inin yerleşik bot bakış açısı fonksiyonunu kullanır. Geleneksel Teleport yöntemlerine kıyasla model "glitch"lerini veya titremeleri engeller. Signature bulunamazsa otomatik Teleport fallback dev devreye girer.
- 🧱 **Gelişmiş Duvar Kontrolü:** FUNPLAY Ray-Trace altyapısı sayesinde oyun motoru seviyesinde kusursuz ışın izleme (ray tracing). Hedef ile aranızda fiziksel bir duvar varsa kilitlenme gerçekleşmez.
- 🎯 **Akıllı Hedef Seçimi (FOV & Mesafe):** Bakış açısına (FOV) en yakın ve mesafesi en kısa olan düşmanı hassas bir skorlama algoritması ile seçer.
- 📏 **Dinamik Yükseklik & Eğilme Algılama:** Hem kullanıcının hem de hedefin eğilme durumlarını anlık analiz ederek nişangah yüksekliğini (göz hizası) dinamik olarak ayarlar.
- 🔍 **2 Hedefli (Kafa + Göğüs) Görünürlük Testi:** Hedefin görünürlüğünü doğrulamak için optimize edilmiş 2 noktalı trace kontrolü uygular. Önce kafa kontrol edilir, kafa engellenmişse doğrudan göğüs hedeflenir.
- 🌊 **Smooth Aim:** Konfigürasyon dosyası üzerinden ayarlanabilen pürüzsüz geçiş (smooth) faktörü ile hedefe çok daha doğal kilitlenme sağlar.
- 🛡️ **Recoil (AimPunch) Sıfırlama:** Silah geri tepmesi (AimPunch / AimPunchVelocity) algılanır ve stabil bir hedefleme sunulur.
- 👑 **Admin Yetki Sistemi:** Oyuncu dengesini korumak için sadece `@css/generic` yetkisine sahip sunucu yöneticileri tarafından kullanılabilir.

---

## ⚙️ Gereksinimler

Eklentinin sorunsuz çalışabilmesi için sisteminizin aşağıdaki gereksinimleri karşıladığından emin olun:

| Bileşen | Minimum Versiyon | Notlar |
|---------|-----------------|--------|
| **Counter-Strike 2 Server** | Son Sürüm | - |
| **Metamod:Source** | v2.x | - |
| **CounterStrikeSharp** | v1.0.362+ (API v80+) | Güncel API önerilir |
| **.NET Runtime** | 8.0 | CSSharp için zorunlu |
| **FUNPLAY Ray-Trace** | v1.0.3+ | [İndir (GitHub)](https://github.com/FUNPLAY-pro-CS2/Ray-Trace/releases) |

---

## 🚀 Kurulum Adımları

### 1. FUNPLAY Ray-Trace Kurulumu (ZORUNLU)
Duvar arkası hedeflemeyi engellemek için bu modül kesinlikle gereklidir. İndirdiğiniz dosyaları aşağıdaki gibi sunucunuza yükleyin:
```text
game/csgo/addons/
├── RayTrace/
│   └── bin/linuxsteamrt64/RayTrace.so  (veya RayTrace.dll)
└── metamod/
    └── RayTrace.vdf
```

### 2. GameData (Signature) Dosyası
`Aimbot.json` adlı dosyayı oluşturup, aşağıdaki konuma yerleştirin:
`game/csgo/addons/counterstrikesharp/gamedata/Aimbot.json`

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

### 3. Eklentiyi Derleme & Yükleme
Eğer açık kaynak kodunu derleyecekseniz proje dizininde şu komutları çalıştırın:
```bash
dotnet restore
dotnet build -c Release
```
Oluşan `Aimbot.dll` dosyasını sunucunuzda aşağıdaki dizine taşıyın:
`game/csgo/addons/counterstrikesharp/plugins/Aimbot/Aimbot.dll`

**Ardından sunucunuzu yeniden başlatın.** Kurulum başarılıysa konsolda eklentinin yüklendiğine dair log mesajları göreceksiniz.

---

## 🎮 Kullanım ve Komutlar

Aşağıdaki komutlar oyun içi chat (`!`) veya konsol (`css_`) üzerinden kullanılabilir:

| Komut | Konsol Karşılığı | Açıklama |
|-------|------------------|---------|
| `!aim` | `css_aim` | Aimbot assist özelliğini aktif/pasif hale getirir. |
| `!tracetest` | `css_tracetest` | Baktığınız yöne ışın (ray) gönderir, engelleri ve hedef entity'leri test eder. |
| `!aimdebug` | `css_aimdebug` | Eklentinin durumunu, trace loglarını ve hedef seçimi kararlarını ekrana yazdırır. |

---

## 🛠️ Yapılandırma (Config)

Eklenti ilk kez çalıştığında otomatik olarak bir yapılandırma dosyası oluşturur:
`game/csgo/addons/counterstrikesharp/configs/plugins/Aimbot/aimbot_config.json`

```json
{
  "SmoothFactor": 0.5,
  "FOV": 360.0,
  "MaxDistance": 5000.0
}
```

- **`SmoothFactor`**: Kameranın hedefe kilitlenme hızıdır (`0.0` yavaş, `1.0` anında/snapping).
- **`FOV`**: Nişangahınızın kaç derecelik açısındaki hedeflerin algılanacağını belirler (`360.0` arkanızdakiler dahil her yeri kapsar).
- **`MaxDistance`**: Oyun içi birim olarak hedefin ne kadar uzaktan algılanacağını ifade eder (`5000.0` geniş bir menzildir).

---

## ⚠️ Önemli Uyarılar

> [!WARNING]
> **Bu eklenti SADECE YÖNETİCİ (ADMIN) kullanımı için tasarlanmıştır!**
> Normal oyunculara `@css/generic` yetkisi vermeyin. Topluluk sunucularında adil oyun ortamını bozmamak adına hile korumalarıyla (VAC vb.) çakışabileceğini ve dengesizlik yaratabileceğini unutmayın.

## 👨‍💻 Geliştirici

**guccukCENEVAR** - [GitHub Profilim](https://github.com/guccukCENEVAR)

*Bu proje tamamıyla eğitim ve yönetim kolaylığı sağlama amaçlıdır. Kullanımından doğacak sonuçlar sunucu sahibinin kendi sorumluluğundadır.*
