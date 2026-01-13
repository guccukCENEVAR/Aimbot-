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
using RayTrace;
using static RayTrace.RayTrace;

namespace AimbotPlugin;

public class AimbotConfig
{
    [JsonPropertyName("SmoothFactor")]
    public float SmoothFactor { get; set; } = 0.5f;

    // Açıklama alanı (JSON yorum yerine)
    [JsonPropertyName("_comment")]
    public string Comment { get; set; } = "Smooth Aim ayarlari: 0.0 = cok yavas, 1.0 = aninda snap, default 0.5";
}

[MinimumApiVersion(80)]
public class AimbotPlugin : BasePlugin
{
    public override string ModuleName => "Admin Aimbot Snap Pro";
    public override string ModuleVersion => "1.4.1";
    public override string ModuleAuthor => "guccukCENEVAR";

    private HashSet<ulong> _authorizedPlayers = new HashSet<ulong>();

    private const float FOV = 360.0f; 
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
                string json = File.ReadAllText(configPath);
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

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        Console.WriteLine($"[Aimbot] V1.4.0 - Smooth Aim (Factor: {_config.SmoothFactor}) + Speed Preserve + Wall Check + Prediction!");
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
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(configPath, json);
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
            player.PrintToChat(" \x01[\x02Admin\x01] Aim Assist: \x02KAPALI \x08(by guccukCENEVAR)");
        }
        else
        {
            _authorizedPlayers.Add(player.SteamID);
            player.PrintToChat(" \x01[\x02Admin\x01] Aim Assist: \x04ACIK \x08(by guccukCENEVAR)");

        }
    }

    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            // Temel kontroller ve yetki kontrolü
            if (player == null || !player.IsValid || player.PawnIsAlive != true || !_authorizedPlayers.Contains(player.SteamID))
                continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || playerPawn.AbsOrigin == null) continue;

            // --- NO RECOIL (SEKMEME) ---
            // Silahın geri tepme açısını (Punch Angle) sıfırlıyoruz
            playerPawn.AimPunchAngle.X = 0;
            playerPawn.AimPunchAngle.Y = 0;
            playerPawn.AimPunchAngle.Z = 0;

            // Geri tepme hızını (Punch Angle Velocity) sıfırlıyoruz
            // Bu, namlunun yukarı sektiğinde geri gelme efektini de iptal eder
            playerPawn.AimPunchAngleVel.X = 0;
            playerPawn.AimPunchAngleVel.Y = 0;
            playerPawn.AimPunchAngleVel.Z = 0;
            // ------------------------------------------------

            // --- MEVCUT AIMBOT MANTIĞI ---
            var target = GetBestTarget(player);
            if (target == null || target.PlayerPawn.Value == null) continue;

            float currentEyeHeight = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchEyeHeight : StandEyeHeight;
            Vector eyePos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + currentEyeHeight);

            var targetPawn = target.PlayerPawn.Value;
            float currentTargetHeadHeight = (targetPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchHeadHeight : StandHeadHeight;
            
            // Hareket tahminleme (Prediction)
            Vector velocity = targetPawn.AbsVelocity ?? new Vector(0,0,0);
            Vector predictedOrigin = new Vector(
                targetPawn.AbsOrigin!.X + (velocity.X * PredictionFactor),
                targetPawn.AbsOrigin.Y + (velocity.Y * PredictionFactor),
                targetPawn.AbsOrigin.Z + (velocity.Z * PredictionFactor)
            );

            Vector targetHeadBase = new Vector(predictedOrigin.X, predictedOrigin.Y, predictedOrigin.Z + currentTargetHeadHeight);

            // Face-focus
            Vector enemyForward = AngleToForward(targetPawn.EyeAngles);
            Vector targetHeadFinal = targetHeadBase + (enemyForward * 4.0f);

            QAngle targetAngle = CalculateAngle(eyePos, targetHeadFinal);
            
            // Mevcut açıyı al
            QAngle currentAngle = playerPawn.EyeAngles ?? new QAngle(0, 0, 0);
            
            // Smooth interpolasyon
            float smoothFactor = _config?.SmoothFactor ?? 0.5f;
            QAngle smoothAngle = LerpAngle(currentAngle, targetAngle, smoothFactor);
            
            // View angle'ı değiştir
            SetViewAngle(playerPawn, smoothAngle, player);
        }
    }

    /// <summary>
    /// Oyuncunun view angle'ını değiştirir
    /// Mevcut pozisyon ve velocity korunarak sadece açı değiştirilir
    /// SetSpeed ile ayarlanan hız da korunur
    /// AimPunchAngle ve AimPunchAngleVel telafi edilir (no-recoil)
    /// </summary>
    private void SetViewAngle(CCSPlayerPawn playerPawn, QAngle angle, CCSPlayerController? controller = null)
    {
        if (playerPawn == null || !playerPawn.IsValid) return;
        if (playerPawn.AbsOrigin == null || playerPawn.AbsVelocity == null) return;

        // *** HIZ KORUMA ***
        // Teleport öncesi velocity'yi kaydet
        var savedVelocity = playerPawn.AbsVelocity;
        float savedVelX = savedVelocity.X;
        float savedVelY = savedVelocity.Y;
        float savedVelZ = savedVelocity.Z;
        
        // Mevcut hız büyüklüğünü hesapla (yatay düzlem)
        float currentSpeed = MathF.Sqrt(savedVelX * savedVelX + savedVelY * savedVelY);
        
        // Pozisyonu ve velocity'yi kopyala
        Vector pos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z);
        Vector vel = new Vector(savedVelX, savedVelY, savedVelZ);
        
        // Teleport yap - sadece açı değişir
        playerPawn.Teleport(pos, angle, vel);
        
        // Teleport sonrası velocity'yi kontrol et ve gerekirse düzelt
        if (playerPawn.AbsVelocity != null && currentSpeed > 0)
        {
            var newVelocity = playerPawn.AbsVelocity;
            float newSpeed = MathF.Sqrt(newVelocity.X * newVelocity.X + newVelocity.Y * newVelocity.Y);
            
            // Eğer hız değiştiyse, eski hızı geri yükle
            if (MathF.Abs(newSpeed - currentSpeed) > 1.0f)
            {
                // Yeni yöne eski hızı uygula
                if (newSpeed > 0)
                {
                    float ratio = currentSpeed / newSpeed;
                    newVelocity.X *= ratio;
                    newVelocity.Y *= ratio;
                }
            }
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
            if (dist > 5000.0f) continue;

            // *** DUVAR KONTROLÜ - RAY TRACING ***
            if (IsWallBetween(player, enemy))
            {
                // Arada duvar var, bu hedefi atla
                continue;
            }

            Vector dir = Normalize(enemyHead - eyePos);
            float dotProduct = Dot(forward, dir);
            float angle = MathF.Acos(Math.Clamp(dotProduct, -1.0f, 1.0f)) * (180.0f / MathF.PI);
            
            float score = angle + (dist / 5000.0f);

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
        return l > 0 ? new Vector(v.X / l, v.Y / l, v.Z / l) : new Vector(0,0,0);
    }

    private float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

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

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid != null) _authorizedPlayers.Remove(@event.Userid.SteamID);
        return HookResult.Continue;
    }
}