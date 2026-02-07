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
using TraceMask = RayTrace.TraceMask;
using Contents = RayTrace.Contents;

namespace AimbotPlugin;

public class AimbotConfig
{
    [JsonPropertyName("SmoothFactor")]
    public float SmoothFactor { get; set; } = 0.5f;

    [JsonPropertyName("FOV")]
    public float FOV { get; set; } = 360.0f;

    [JsonPropertyName("MaxDistance")]
    public float MaxDistance { get; set; } = 5000.0f;
}

[MinimumApiVersion(80)]
public class AimbotPlugin : BasePlugin
{
    public override string ModuleName => "Admin Aimbot Snap Pro";
    public override string ModuleVersion => "2.3.0";
    public override string ModuleAuthor => "guccukCENEVAR";

    private HashSet<ulong> _authorizedPlayers = new HashSet<ulong>();
    
    // Son uygulanan açıyı sakla - smooth interpolasyon için gerekli
    // (V_angle client açısını döndürür, Teleport'un ayarladığı açıyı değil)
    private Dictionary<ulong, QAngle> _lastAimAngles = new Dictionary<ulong, QAngle>();

    // FOV ve MaxDistance artık config'ten okunuyor
    private float GetFOV() => _config?.FOV ?? 360.0f;
    private float GetMaxDistance() => _config?.MaxDistance ?? 5000.0f;
    
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

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        
        Console.WriteLine($"[Aimbot] V2.3.0 Yuklendi. FOV: {_config.FOV}, MaxDist: {_config.MaxDistance}, Smooth: {_config.SmoothFactor}");
        
        // RayTrace durumunu logla
        if (RayTrace.RayTrace.IsInitialized)
        {
            Console.WriteLine("[Aimbot] RayTrace AKTIF - Duvar kontrolu calisiyor.");
        }
        else
        {
            Console.WriteLine($"[Aimbot] UYARI: RayTrace BASLATILMADI! Duvar kontrolu devre disi.");
            Console.WriteLine($"[Aimbot] Hata: {RayTrace.RayTrace.InitError ?? "Bilinmeyen hata"}");
            Console.WriteLine("[Aimbot] Aimbot.json dosyasini csgo/addons/counterstrikesharp/gamedata/ klasorune kopyalayin.");
        }
    }

    private string GetConfigPath()
    {
        var dir = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "configs", "plugins", "Aimbot");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, ConfigFileName);
    }

    private void SaveConfig()
    {
        try
        {
            var configPath = GetConfigPath();
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"SmoothFactor\": {_config!.SmoothFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine("  // SmoothFactor: Aimbot yumuşaklık faktörü (0.0 = çok yavaş, 1.0 = anında snap), varsayılan 0.5");
            sb.AppendLine();
            sb.AppendLine($"  \"FOV\": {_config.FOV.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine("  // FOV: Görüş açısı (derece), 0-360 arası, varsayılan 360 (tüm yönler)");
            sb.AppendLine();
            sb.AppendLine($"  \"MaxDistance\": {_config.MaxDistance.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine("  // MaxDistance: Maksimum hedef algılama mesafesi (unit), minimum 100, varsayılan 5000");
            sb.AppendLine("}");
            
            File.WriteAllText(configPath, sb.ToString());
            Console.WriteLine($"[Aimbot] Config dosyasi olusturuldu: {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Aimbot] Config kaydedilirken hata: {ex.Message}");
        }
    }


    [ConsoleCommand("css_tracetest", "RayTrace teshis komutu - duvara/spawn bariyerine bakarak test et")]
    public void OnTraceTestCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid) return;
        
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || pawn.AbsOrigin == null) return;

        player.PrintToChat($" \x01[\x04Trace\x01] IsInitialized: {RayTrace.RayTrace.IsInitialized}");
        
        if (!RayTrace.RayTrace.IsInitialized)
        {
            player.PrintToChat($" \x02[Hata]\x01 {RayTrace.RayTrace.InitError ?? "Bilinmeyen"}");
            return;
        }

        // Göz pozisyonu
        Vector eyePos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
        
        // Baktığı yöne 3000 birim ışın
        Vector forward = new Vector();
        var viewAngle = pawn.V_angle ?? pawn.EyeAngles;
        NativeAPI.AngleVectors(viewAngle!.Handle, forward.Handle, 0, 0);
        Vector endPos = new Vector(eyePos.X + forward.X * 3000, eyePos.Y + forward.Y * 3000, eyePos.Z + forward.Z * 3000);
        
        // Test 1: TraceWall (aimbot'un kullandığı trace)
        // mask=WorldGeometry, content=Solid → buyzone/trigger/spawn bariyerinden geçer
        var r1 = RayTrace.RayTrace.TraceWall(eyePos, endPos, pawn.Handle);

        // Test 2: Eski yöntem (karşılaştırma için) - mask=content=WorldGeometry
        var r2 = TraceShape(eyePos, endPos, (ulong)Contents.WorldGeometry, (ulong)Contents.WorldGeometry, pawn.Handle);

        // Çarpılan entity bilgisi (eski yöntemden - daha fazla şeye çarpar)
        string r2Entity = "-";
        if (r2.HasValue && r2.Value.Fraction < 1.0f && r2.Value.HitEntity != IntPtr.Zero)
        {
            try
            {
                var ent = new CEntityInstance(r2.Value.HitEntity);
                if (ent != null && ent.IsValid) r2Entity = ent.DesignerName ?? "?";
            }
            catch { r2Entity = "err"; }
        }

        player.PrintToChat($" \x04--- TRACE SONUCLARI ---");
        player.PrintToChat($" \x01TraceWall:  Frac=\x04{r1?.Fraction ?? -1:F4} \x01(aimbot kullanir)");
        player.PrintToChat($" \x01Eski:       Frac=\x04{r2?.Fraction ?? -1:F4} \x01Entity=\x04{r2Entity}");
        player.PrintToChat($" \x01---");
        player.PrintToChat($" \x01TraceWall >= 1.0 = \x04gecis serbest (buyzone/spawn/cam)");
        player.PrintToChat($" \x01TraceWall < 1.0 = \x02gercek duvar");
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
            _lastAimAngles.Remove(player.SteamID);
            player.PrintToChat(" \x01[\x02Admin\x01] Aim Assist: \x02KAPALI");
        }
        else
        {
            _authorizedPlayers.Add(player.SteamID);
            player.PrintToChat(" \x01[\x02Admin\x01] Aim Assist: \x04ACIK");
            
            if (RayTrace.RayTrace.IsInitialized)
            {
                player.PrintToChat(" \x01[\x04RayTrace\x01] Duvar kontrolu: \x04AKTIF");
            }
            else
            {
                player.PrintToChat(" \x01[\x02RayTrace\x01] Duvar kontrolu: \x02DEVRE DISI");
                player.PrintToChat($" \x01[\x02Hata\x01] {RayTrace.RayTrace.InitError ?? "Imzalar bulunamadi"}");
            }
        }
    }

    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            // 1. Temel Kontroller
            if (player == null || !player.IsValid || player.PawnIsAlive != true || !_authorizedPlayers.Contains(player.SteamID))
                continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || playerPawn.AbsOrigin == null) continue;

            // 2. Silah Kontrolü (Bıçak/Bomba vb.)
            var activeWeapon = playerPawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && IsIgnoredWeapon(activeWeapon.DesignerName ?? ""))
                continue;

            // 3. Hedef Bulma
            var target = GetBestTarget(player);
            if (target == null || target.PlayerPawn.Value == null)
                continue;

            // --- Recoil (AimPunch) Sıfırlama ---
            if (playerPawn.AimPunchAngle != null) { playerPawn.AimPunchAngle.X = 0; playerPawn.AimPunchAngle.Y = 0; playerPawn.AimPunchAngle.Z = 0; }
            if (playerPawn.AimPunchAngleVel != null) { playerPawn.AimPunchAngleVel.X = 0; playerPawn.AimPunchAngleVel.Y = 0; playerPawn.AimPunchAngleVel.Z = 0; }

            // 4. Hedef Açı Hesaplama
            // Son uygulanan açıdan başla (V_angle client açısı döndürür, Teleport sonrasını yansıtmaz)
            QAngle currentAngles;
            if (_lastAimAngles.TryGetValue(player.SteamID, out var lastAngle))
                currentAngles = lastAngle;
            else
                currentAngles = playerPawn.V_angle ?? playerPawn.EyeAngles ?? new QAngle(0, 0, 0);
            
            // Göz pozisyonu: Sabit yükseklik kullan (64 ayakta, 46 eğilmiş)
            // ViewOffset.Z yukarı bakarken model yaslandığı için değişir → geri besleme döngüsü oluşturur
            // Sabit değer bu döngüyü kırar, mermi çıkış noktasıyla da uyumludur
            float myEyeZ = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;
            Vector eyePos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + myEyeZ);
            
            var targetPawn = target.PlayerPawn.Value;
            
            // Hedef kafa pozisyonu: ViewOffset.Z ile dinamik
            float targetEyeZ = targetPawn.ViewOffset.Z;
            if (targetEyeZ < 30.0f) targetEyeZ = 64.0f;
            
            Vector velocity = targetPawn.AbsVelocity ?? new Vector(0,0,0);
            Vector targetPos = targetPawn.AbsOrigin!;
            Vector predictedPos = new Vector(
                targetPos.X + (velocity.X * PredictionFactor),
                targetPos.Y + (velocity.Y * PredictionFactor),
                targetPos.Z + (velocity.Z * PredictionFactor)
            );
            
            // Hedef kafası - doğrudan göz seviyesi
            Vector targetHead = new Vector(predictedPos.X, predictedPos.Y, predictedPos.Z + targetEyeZ);

            QAngle targetAngle = CalculateAngle(eyePos, targetHead);

            // 5. Açıyı Uygula
            float smoothFactor = _config?.SmoothFactor ?? 0.5f;
            
            // Dik açılarda smooth factor'ü artır → daha hızlı takip
            float pitchDiff = MathF.Abs(targetAngle.X - currentAngles.X);
            if (pitchDiff > 30f)
                smoothFactor = MathF.Min(1.0f, smoothFactor + (pitchDiff - 30f) / 60f * 0.5f);
            
            QAngle finalAngle = LerpAngle(currentAngles, targetAngle, smoothFactor);
            
            // Teleport ile açı ayarla (CS2'de çalışan standart yöntem)
            playerPawn.Teleport(playerPawn.AbsOrigin!, finalAngle, playerPawn.AbsVelocity!);
            
            // Son açıyı sakla - sonraki tick'te buradan devam eder
            _lastAimAngles[player.SteamID] = new QAngle(finalAngle.X, finalAngle.Y, 0);
        }
    }


    /// <summary>
    /// RayTrace.TraceWall ile duvar kontrolü.
    /// Mask/content ayarları RayTrace.cs'te yapılır:
    ///   - Sadece gerçek dünya duvarlarına çarpar
    ///   - Oyuncular, spawn bariyerleri, buyzone, camlar, trigger → geçer
    /// true = gerçek duvar var, false = duvar yok
    /// </summary>
    private bool SingleTrace(Vector from, Vector to, IntPtr skipHandle)
    {
        var result = RayTrace.RayTrace.TraceWall(from, to, skipHandle);
        if (!result.HasValue) return false;

        var trace = result.Value;
        if (trace.AllSolid) return false;
        return trace.Fraction < 0.97f;
    }

    /// <summary>
    /// RayTrace ile duvar kontrolü - çoklu nokta trace.
    /// 3 farklı hedef noktasına ışın gönderir (baş, gövde, bel).
    /// Herhangi biri geçerse = hedef görünür (duvar yok).
    /// </summary>
    private bool IsWallBetween(CCSPlayerController player, CCSPlayerController target)
    {
        if (!RayTrace.RayTrace.IsInitialized)
            return false;

        if (player == null || target == null) return true;

        var playerPawn = player.PlayerPawn.Value;
        var targetPawn = target.PlayerPawn.Value;

        if (playerPawn == null || targetPawn == null) return true;
        if (playerPawn.AbsOrigin == null || targetPawn.AbsOrigin == null) return true;

        float eyeZ = playerPawn.ViewOffset.Z;
        if (eyeZ < 30.0f) eyeZ = 64.0f;

        float targetEyeZ = targetPawn.ViewOffset.Z;
        if (targetEyeZ < 30.0f) targetEyeZ = 64.0f;

        // Kaynak: oyuncunun göz pozisyonu
        Vector startPos = new Vector(
            playerPawn.AbsOrigin.X,
            playerPawn.AbsOrigin.Y,
            playerPawn.AbsOrigin.Z + eyeZ
        );

        float tx = targetPawn.AbsOrigin.X;
        float ty = targetPawn.AbsOrigin.Y;
        float tz = targetPawn.AbsOrigin.Z;

        IntPtr skipHandle = playerPawn.Handle;

        try
        {
            // Trace 1: Göz → Hedef Baş (en yüksek nokta)
            Vector headPos = new Vector(tx, ty, tz + targetEyeZ);
            if (!SingleTrace(startPos, headPos, skipHandle))
                return false;

            // Trace 2: Göz → Hedef Gövde (göğüs hizası, ~%60 yükseklik)
            Vector chestPos = new Vector(tx, ty, tz + targetEyeZ * 0.6f);
            if (!SingleTrace(startPos, chestPos, skipHandle))
                return false;

            // Trace 3: Göz → Hedef Bel (~%35 yükseklik)
            Vector waistPos = new Vector(tx, ty, tz + targetEyeZ * 0.35f);
            if (!SingleTrace(startPos, waistPos, skipHandle))
                return false;

            // 3 trace de engellendi = gerçekten duvar var
            return true;
        }
        catch
        {
            return false;
        }
    }

    private CCSPlayerController? GetBestTarget(CCSPlayerController player)
    {
        CCSPlayerController? bestTarget = null;
        float bestScore = float.MaxValue;

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return null;

        QAngle currentAngles = playerPawn.V_angle ?? playerPawn.EyeAngles ?? new QAngle(0,0,0);
        float myEyeZ = playerPawn.ViewOffset.Z;
        if (myEyeZ < 30.0f) myEyeZ = 64.0f;
        Vector eyePos = new Vector(
            playerPawn.AbsOrigin!.X, 
            playerPawn.AbsOrigin.Y, 
            playerPawn.AbsOrigin.Z + myEyeZ
        );
        
        Vector forward = AngleToForward(currentAngles);
        int playerTeam = player.TeamNum;

        foreach (var enemy in Utilities.GetPlayers()
            .Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != playerTeam))
        {
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn == null || enemyPawn.AbsOrigin == null) continue;

            float enemyEyeZ = enemyPawn.ViewOffset.Z;
            if (enemyEyeZ < 30.0f) enemyEyeZ = 64.0f;
            Vector enemyHead = new Vector(
                enemyPawn.AbsOrigin.X, 
                enemyPawn.AbsOrigin.Y, 
                enemyPawn.AbsOrigin.Z + enemyEyeZ
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

    /// <summary>
    /// İki açı arasında smooth interpolasyon yapar
    /// </summary>
    private QAngle LerpAngle(QAngle from, QAngle to, float t)
    {
        return new QAngle(
            LerpFloat(from.X, to.X, t),           // Pitch - normal lerp
            LerpAngleFloat(from.Y, to.Y, t),      // Yaw - wrap-around lerp
            LerpFloat(from.Z, to.Z, t)            // Roll - normal lerp
        );
    }

    private float LerpFloat(float from, float to, float t)
    {
        return from + (to - from) * t;
    }

    /// <summary>
    /// Açı interpolasyonu - wrap-around durumunu handle eder
    /// </summary>
    private float LerpAngleFloat(float from, float to, float t)
    {
        float diff = to - from;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        return from + diff * t;
    }

    private bool IsIgnoredWeapon(string weaponName)
    {
        if (string.IsNullOrEmpty(weaponName)) return false;
        string name = weaponName.ToLower();

        if (name.Contains("knife") || name.Contains("bayonet")) return true;
        if (name.Contains("grenade") || name.Contains("flash") || name.Contains("smoke") || 
            name.Contains("molotov") || name.Contains("incgrenade") || name.Contains("decoy")) return true;
        if (name.Contains("taser")) return true;
        if (name.Contains("c4")) return true;

        return false;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid != null)
        {
            _authorizedPlayers.Remove(@event.Userid.SteamID);
            _lastAimAngles.Remove(@event.Userid.SteamID);
        }
        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        _authorizedPlayers.Clear();
        _lastAimAngles.Clear();
        base.Unload(hotReload);
    }
}
