using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.IO;
using System.Drawing;
using RayTrace;
using static RayTrace.RayTrace;

namespace AimbotPlugin;

public class AimbotConfig
{
    [JsonPropertyName("SmoothFactor")]
    public float SmoothFactor { get; set; } = 0.5f;

    [JsonPropertyName("FOV")]
    public float FOV { get; set; } = 360.0f;

    [JsonPropertyName("MaxDistance")]
    public float MaxDistance { get; set; } = 5000.0f;

    [JsonPropertyName("BreakLimit")]
    // DİKKAT: Artık derece değil, Mouse Hareketi (Mickeys) kullanıyoruz.
    // 5.0 = Çok hassas (Hafif dokunuş)
    // 10.0 - 15.0 = Normal (Bilinçli çevirme)
    // 30.0+ = Sert çevirme
    public float BreakLimit { get; set; } = 10.0f;

    [JsonPropertyName("BreakCooldown")]
    public float BreakCooldown { get; set; } = 1.0f; // Kırılınca 1 saniye beklesin
}

[MinimumApiVersion(80)]
public class AimbotPlugin : BasePlugin
{
    public override string ModuleName => "Admin Aimbot Snap Pro";
    public override string ModuleVersion => "2.2.0 (Mouse Delta)";
    public override string ModuleAuthor => "guccukCENEVAR";

    // Debug açık kalsın, ayar yaptıktan sonra false yaparsın.
    private const bool DebugMode = true; 

    private HashSet<ulong> _authorizedPlayers = new HashSet<ulong>();
    private Dictionary<ulong, float> _aimbotBreakCooldown = new Dictionary<ulong, float>(); 
    
    // Mouse hareketini takip etmek için önceki EyeAngles'ı sakla
    private Dictionary<ulong, QAngle> _previousAngles = new Dictionary<ulong, QAngle>();

    // Artık son açıyı saklamamıza gerek yok çünkü direkt mouse hareketine bakacağız.

    // FOV ve MaxDistance artık config'ten okunuyor
    private float GetFOV() => _config?.FOV ?? 360.0f;
    private float GetMaxDistance() => _config?.MaxDistance ?? 5000.0f;
    
    private const float StandEyeHeight = 64.0f; 
    private const float CrouchEyeHeight = 46.0f;
    private const float StandHeadHeight = 65.0f; 
    private const float CrouchHeadHeight = 46.0f;
    private const float PredictionFactor = 0.015625f; // 64 tick hızı (1/64)
    
    // Config dosyasından yüklenecek
    private AimbotConfig? _config;
    private const string ConfigFileName = "aimbot_config.json";

    public override void Load(bool hotReload)
    {
        // Config dosyasını yükle
        var configPath = GetConfigPath();
        if (File.Exists(configPath))
        {
            try
            {
                string jsonWithComments = File.ReadAllText(configPath);
                // JSON yorumlarını filtrele (// ile başlayan satırları kaldır)
                var lines = jsonWithComments.Split('\n');
                var jsonLines = new List<string>();
                foreach (var line in lines)
                {
                    var trimmed = line.TrimStart();
                    // Yorum satırını atla (// ile başlayan)
                    if (!trimmed.StartsWith("//"))
                    {
                        jsonLines.Add(line);
                    }
                }
                string json = string.Join("\n", jsonLines);
                _config = JsonSerializer.Deserialize<AimbotConfig>(json);
                
                if (_config == null)
                {
                    _config = new AimbotConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Aimbot] Config yuklenirken hata: {ex.Message}");
                _config = new AimbotConfig();
            }
        }
        else
        {
            // Config dosyası yoksa varsayılan değerlerle oluştur
            _config = new AimbotConfig();
            SaveConfig();
        }
        
        // SmoothFactor değerini kontrol et ve sınırla
        if (_config.SmoothFactor < 0.0f) _config.SmoothFactor = 0.0f;
        if (_config.SmoothFactor > 1.0f) _config.SmoothFactor = 1.0f;
        
        // FOV değerini kontrol et ve sınırla (0-360 arası)
        if (_config.FOV < 0.0f) _config.FOV = 0.0f;
        if (_config.FOV > 360.0f) _config.FOV = 360.0f;
        
        // MaxDistance değerini kontrol et (minimum 100)
        if (_config.MaxDistance < 100.0f) _config.MaxDistance = 100.0f;
        
        if (_config.SmoothFactor < 0.0f) _config.SmoothFactor = 0.0f;
        // Limit kontrolü (Mouse Delta için minimum 1.0 olsun)
        if (_config.BreakLimit < 1.0f) _config.BreakLimit = 1.0f;

        // BreakCooldown değerini kontrol et ve sınırla (0.1-5.0 arası)
        if (_config.BreakCooldown < 0.1f) _config.BreakCooldown = 0.1f;
        if (_config.BreakCooldown > 5.0f) _config.BreakCooldown = 5.0f;

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        
        Console.WriteLine($"[Aimbot] V2.2.0 Yuklendi. BreakLimit (MouseDelta): {_config.BreakLimit}");
    }

    private string GetConfigPath()
    {
        // configs/plugins/Aimbot/aimbot_config.json
        var dir = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "configs", "plugins", "Aimbot");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, ConfigFileName);
    }

    private void SaveConfig()
    {
        try
        {
            var configPath = GetConfigPath();
            
            // JSON'u manuel olarak formatla - her property'nin altına açıklama ekle
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"SmoothFactor\": {_config.SmoothFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine("  // SmoothFactor: Aimbot yumuşaklık faktörü (0.0 = çok yavaş, 1.0 = anında snap), varsayılan 0.5");
            sb.AppendLine();
            sb.AppendLine($"  \"FOV\": {_config.FOV.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine("  // FOV: Görüş açısı (derece), 0-360 arası, varsayılan 360 (tüm yönler)");
            sb.AppendLine();
            sb.AppendLine($"  \"MaxDistance\": {_config.MaxDistance.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine("  // MaxDistance: Maksimum hedef algılama mesafesi (unit), minimum 100, varsayılan 5000");
            sb.AppendLine();
             sb.AppendLine($"  \"BreakLimit\": {_config.BreakLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
             sb.AppendLine("  // BreakLimit: Mouse ile aim bozma limiti. 0.5 - 1.0 arasi idealdir.");
            sb.AppendLine();
            sb.AppendLine($"  \"BreakCooldown\": {_config.BreakCooldown.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
             sb.AppendLine("  // BreakCooldown: Kilit kırıldığında aimbot'un ne kadar süre durdurulacağı (saniye), varsayılan 1.0");
            sb.AppendLine("}");
            
            File.WriteAllText(configPath, sb.ToString());
            Console.WriteLine($"[Aimbot] Config dosyasi olusturuldu: {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Aimbot] Config kaydedilirken hata: {ex.Message}");
        }
    }


    [ConsoleCommand("css_aim", "Aimbot ac/kapat")]
    public void OnAimCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        bool isAdmin = AdminManager.PlayerHasPermissions(player, "@css/generic");

        if (!isAdmin)
        {
            player.PrintToChat(" \x02[Hata] \x01Bu komutu kullanmak icin yetkiniz yok.");
            return;
        }

        if (_authorizedPlayers.Contains(player.SteamID))
        {
            _authorizedPlayers.Remove(player.SteamID);
            _previousAngles.Remove(player.SteamID); // Hafızayı temizle
            player.PrintToChat(" \x01[\x02Admin\x01] Aim Assist: \x02KAPALI");
        }
        else
        {
            _authorizedPlayers.Add(player.SteamID);
            player.PrintToChat(" \x01[\x02Admin\x01] Aim Assist: \x04ACIK");
        }
    }

    private void OnTick()
    {
        float currentTime = Server.CurrentTime;
        
        foreach (var player in Utilities.GetPlayers())
        {
            // 1. Temel Kontroller
            if (player == null || !player.IsValid || player.PawnIsAlive != true || !_authorizedPlayers.Contains(player.SteamID))
            {
                _previousAngles.Remove(player.SteamID);
                _aimbotBreakCooldown.Remove(player.SteamID);
                continue;
            }

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || playerPawn.AbsOrigin == null) continue;

            // 2. Silah Kontrolü (Bıçak/Bomba vb.)
            var activeWeapon = playerPawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && IsIgnoredWeapon(activeWeapon.DesignerName ?? ""))
            {
                _previousAngles.Remove(player.SteamID);
                continue;
            }

            // 3. Cooldown Kontrolü
            if (_aimbotBreakCooldown.TryGetValue(player.SteamID, out float cooldownEndTime))
            {
                if (currentTime < cooldownEndTime)
                {
                    _previousAngles.Remove(player.SteamID);
                    // Debug: Kırık olduğunu göster
                    if (DebugMode) player.PrintToCenterHtml($"<font color='red'>KILIT KIRIK! Bekleniyor...</font>");
                    continue; // Cooldown bitene kadar elleme
                }
                else
                {
                    _aimbotBreakCooldown.Remove(player.SteamID);
                }
            }

            // 4. Mouse Hareketi Hesaplama (MouseX/MouseY alternatifi)
            // OnTick içinde MouseX/MouseY'ye direkt erişim yok, bu yüzden EyeAngles değişimini kullanıyoruz
            QAngle currentAngles = playerPawn.EyeAngles ?? new QAngle(0, 0, 0);
            
            // Önceki açıyı al ve mouse hareketini hesapla
            float mouseMovement = 0.0f;
            if (_previousAngles.TryGetValue(player.SteamID, out QAngle previousAngles))
            {
                // Açı farkını hesapla (mouse hareketinin bir göstergesi)
                float diffX = GetAngleDiff(currentAngles.X, previousAngles.X);
                float diffY = GetAngleDiff(currentAngles.Y, previousAngles.Y);
                // Mouse hareket gücü = açı değişimi (basitleştirilmiş)
                mouseMovement = diffX + diffY;
            }
            
            // Şu anki açıyı kaydet (bir sonraki tick için)
            _previousAngles[player.SteamID] = currentAngles;

            // 5. Hedef Bulma
            var target = GetBestTarget(player);
            if (target == null || target.PlayerPawn.Value == null)
            {
                _previousAngles.Remove(player.SteamID);
                if (DebugMode) player.PrintToCenter("Hedef Yok");
                continue;
            }

            // --- Recoil (AimPunch) Sıfırlama ---
            if (playerPawn.AimPunchAngle != null) { playerPawn.AimPunchAngle.X = 0; playerPawn.AimPunchAngle.Y = 0; playerPawn.AimPunchAngle.Z = 0; }
            if (playerPawn.AimPunchAngleVel != null) { playerPawn.AimPunchAngleVel.X = 0; playerPawn.AimPunchAngleVel.Y = 0; playerPawn.AimPunchAngleVel.Z = 0; }

            // 6. Hedef Açı Hesaplama
            float currentEyeHeight = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchEyeHeight : StandEyeHeight;
            Vector eyePos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + currentEyeHeight);
            
            var targetPawn = target.PlayerPawn.Value;
            float currentTargetHeadHeight = (targetPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchHeadHeight : StandHeadHeight;
            
            Vector velocity = targetPawn.AbsVelocity ?? new Vector(0,0,0);
            Vector targetPos = targetPawn.AbsOrigin!;
            Vector predictedPos = new Vector(
                targetPos.X + (velocity.X * PredictionFactor),
                targetPos.Y + (velocity.Y * PredictionFactor),
                targetPos.Z + (velocity.Z * PredictionFactor)
            );
            
            Vector targetHead = new Vector(predictedPos.X, predictedPos.Y, predictedPos.Z + currentTargetHeadHeight);
            Vector enemyForward = AngleToForward(targetPawn.EyeAngles);
            Vector targetHeadFinal = targetHead + (enemyForward * 4.0f);

            QAngle targetAngle = CalculateAngle(eyePos, targetHeadFinal);

            // --- 7. KİLİT KIRMA (RAW MOUSE INPUT) ---
            // Mouse hareket gücünü kontrol et
            float limit = _config?.BreakLimit ?? 10.0f;

            // DEBUG: Ekrana mouse hareket gücünü yaz
            if (DebugMode)
            {
                string color = mouseMovement > limit ? "green" : "red";
                player.PrintToCenterHtml($"Mouse Power: <font color='{color}'>{mouseMovement:F1}</font> | Limit: {limit:F1}");
            }

            // Eğer mouse hareketi limiti geçerse kilit kırılır
            if (mouseMovement > limit)
            {
                float cdTime = _config?.BreakCooldown ?? 1.0f;
                _aimbotBreakCooldown[player.SteamID] = currentTime + cdTime;
                _previousAngles.Remove(player.SteamID); // Önceki açıyı temizle
                // Aimbot uygulamasını atla (continue)
                continue;
            }

            // --- 8. AÇIYI UYGULA ---
            float smoothFactor = _config?.SmoothFactor ?? 0.5f;
            // Lerp işlemini mevcut açı üzerinden yapıyoruz
            QAngle finalAngle = LerpAngle(currentAngles, targetAngle, smoothFactor);
            
            // Teleport ile açıyı uygula
            playerPawn.Teleport(playerPawn.AbsOrigin!, finalAngle, playerPawn.AbsVelocity!);
            
            // Uyguladığımız açıyı kaydet (mouse hareketi hesaplaması için)
            _previousAngles[player.SteamID] = finalAngle;
        }
    }


    /// <summary>
    /// RayTrace kullanarak duvar kontrolü yapar
    /// </summary>
    private bool IsWallBetween(CCSPlayerController player, CCSPlayerController target)
    {
        if (player == null || target == null) return true;
        
        var playerPawn = player.PlayerPawn.Value;
        var targetPawn = target.PlayerPawn.Value;
        
        if (playerPawn == null || targetPawn == null) return true;

        // Oyuncunun göz pozisyonu
        float eyeHeight = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 
            ? CrouchEyeHeight : StandEyeHeight;
        
        Vector startPos = new Vector(
            playerPawn.AbsOrigin!.X,
            playerPawn.AbsOrigin.Y,
            playerPawn.AbsOrigin.Z + eyeHeight
        );

        // Hedefin kafa pozisyonu
        float targetHeight = (targetPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 
            ? CrouchHeadHeight : StandHeadHeight;
        
        Vector endPos = new Vector(
            targetPawn.AbsOrigin!.X,
            targetPawn.AbsOrigin.Y,
            targetPawn.AbsOrigin.Z + targetHeight
        );

        // RayTrace seçenekleri - Sadece dünya geometrisiyle etkileşim
        var traceOptions = new TraceOptions
        {
            // Oyuncuları ignore et, sadece duvarları kontrol et
            InteractsExclude = InteractionLayers.Player | InteractionLayers.NPC,
            InteractsWith = InteractionLayers.Solid | InteractionLayers.WorldGeometry,
        };

        // Işın gönder
        var result = TraceEndShape(
            startPos, 
            endPos, 
            false, // Debug çizim kapalı
            player.PlayerPawn, 
            traceOptions
        );

        if (!result.HasValue) return false;

        var trace = result.Value;
        
        // Eğer ışın tam hedefe ulaştıysa (fraction 1.0'a yakınsa) duvar yok
        // Fraction < 0.99 ise araya bir şey girmiş demektir
        if (trace.Fraction >= 0.99f)
        {
            // Tam hedefe ulaştı, duvar yok
            return false;
        }

        // Çarpan entity'yi kontrol et
        if (trace.HitEntity != IntPtr.Zero)
        {
            var hitEntity = new CBaseEntity(trace.HitEntity);
            var hitPlayer = hitEntity.GetPlayerPawn()?.Controller.Value?.As<CCSPlayerController>();
            
            // Eğer çarpan entity hedef oyuncu ise, duvar yok
            if (hitPlayer == target)
            {
                return false;
            }
        }

        // Araya bir şey girmiş (duvar, obje vb.)
        return true;
    }

    private CCSPlayerController? GetBestTarget(CCSPlayerController player)
    {
        CCSPlayerController? bestTarget = null;
        float bestScore = float.MaxValue;

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return null;

        QAngle currentAngles = playerPawn.EyeAngles ?? new QAngle(0,0,0);
        float currentEyeHeight = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 
            ? CrouchEyeHeight : StandEyeHeight;
        Vector eyePos = new Vector(
            playerPawn.AbsOrigin!.X, 
            playerPawn.AbsOrigin.Y, 
            playerPawn.AbsOrigin.Z + currentEyeHeight
        );
        
        Vector forward = AngleToForward(currentAngles);
        int playerTeam = player.TeamNum;

        foreach (var enemy in Utilities.GetPlayers()
            .Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != playerTeam))
        {
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn == null || enemyPawn.AbsOrigin == null) continue;

            float currentTargetHeadHeight = (enemyPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 
                ? CrouchHeadHeight : StandHeadHeight;
            Vector enemyHead = new Vector(
                enemyPawn.AbsOrigin.X, 
                enemyPawn.AbsOrigin.Y, 
                enemyPawn.AbsOrigin.Z + currentTargetHeadHeight
            );

            float dist = GetDistance(eyePos, enemyHead);
            float maxDist = GetMaxDistance();
            if (dist > maxDist) continue;

            // *** DUVAR KONTROLÜ - RAY TRACING ***
            if (IsWallBetween(player, enemy))
            {
                // Arada duvar var, bu hedefi atla
                continue;
            }

            Vector dir = Normalize(enemyHead - eyePos);
            float dotProduct = Dot(forward, dir);
            float angle = MathF.Acos(Math.Clamp(dotProduct, -1.0f, 1.0f)) * (180.0f / MathF.PI);
            
            // FOV kontrolü - eğer açı FOV dışındaysa hedefi atla
            float fov = GetFOV();
            if (angle > fov / 2.0f) continue;
            
            float score = angle + (dist / GetMaxDistance());

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = enemy;
            }
        }
        return bestTarget;
    }

    private float GetDistance(Vector a, Vector b)
    {
        return MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2) + MathF.Pow(a.Z - b.Z, 2));
    }

    private QAngle CalculateAngle(Vector from, Vector to)
    {
        Vector delta = to - from;
        float hyp = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        return new QAngle(
            Math.Clamp(MathF.Atan2(-delta.Z, hyp) * (180.0f / MathF.PI), -89f, 89f),
            MathF.Atan2(delta.Y, delta.X) * (180.0f / MathF.PI),
            0
        );
    }

    private Vector AngleToForward(QAngle angle) 
    {
        float p = angle.X * (MathF.PI / 180.0f);
        float y = angle.Y * (MathF.PI / 180.0f);
        return new Vector(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
    }

    private Vector Normalize(Vector v) 
    {
        float l = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return l > 0 ? new Vector(v.X / l, v.Y / l, v.Z / l) : new Vector(0,0,1);
    }

    private float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    // --- YARDIMCI VEKTÖR MATEMATİĞİ ---

    private Vector GetForwardVector(QAngle angles)
    {
        // Dereceyi radyana çevir
        float pitch = angles.X * (MathF.PI / 180.0f);
        float yaw = angles.Y * (MathF.PI / 180.0f);
        
        // Spherical to Cartesian
        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        
        return new Vector(cp * cy, cp * sy, -sp);
    }

    private Vector VectorCrossProduct(Vector a, Vector b)
    {
        return new Vector(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
    }

    /// <summary>
    /// İki açı arasında smooth interpolasyon yapar
    /// Yaw açısı için wrap-around (180/-180) durumunu düzgün handle eder
    /// </summary>
    private QAngle LerpAngle(QAngle from, QAngle to, float t)
    {
        return new QAngle(
            LerpFloat(from.X, to.X, t),           // Pitch - normal lerp
            LerpAngleFloat(from.Y, to.Y, t),      // Yaw - wrap-around lerp
            LerpFloat(from.Z, to.Z, t)            // Roll - normal lerp
        );
    }

    /// <summary>
    /// Normal float interpolasyonu
    /// </summary>
    private float LerpFloat(float from, float to, float t)
    {
        return from + (to - from) * t;
    }

    /// <summary>
    /// Açı interpolasyonu - wrap-around durumunu handle eder
    /// Örnek: 170° -> -170° = kısa yoldan (20° fark), uzun yoldan değil (340° fark)
    /// </summary>
    private float LerpAngleFloat(float from, float to, float t)
    {
        float diff = to - from;
        
        // Açı farkını -180 ile 180 arasına normalize et
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        
        return from + diff * t;
    }

    // --- YENİ YARDIMCI METOT ---
    private bool IsIgnoredWeapon(string weaponName)
    {
        if (string.IsNullOrEmpty(weaponName)) return false;
        string name = weaponName.ToLower();

        // Bıçaklar
        if (name.Contains("knife") || name.Contains("bayonet")) return true;
        // Bombalar
        if (name.Contains("grenade") || name.Contains("flash") || name.Contains("smoke") || 
            name.Contains("molotov") || name.Contains("incgrenade") || name.Contains("decoy")) return true;
        // Zeus
        if (name.Contains("taser")) return true;
        // C4
        if (name.Contains("c4")) return true;

        return false;
    }

    private float GetAngleDiff(float angle1, float angle2)
    {
        float diff = MathF.Abs(angle1 - angle2);
        if (diff > 180.0f) diff = 360.0f - diff;
        return diff;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid != null)
        {
            _authorizedPlayers.Remove(@event.Userid.SteamID);
            _previousAngles.Remove(@event.Userid.SteamID);
            _aimbotBreakCooldown.Remove(@event.Userid.SteamID);
        }
        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        _authorizedPlayers.Clear();
        _previousAngles.Clear();
        _aimbotBreakCooldown.Clear();
        base.Unload(hotReload);
    }
}