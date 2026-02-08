using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using System.Text.Json.Serialization;
using System.Text.Json;
using RayTrace;

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
    public override string ModuleVersion => "3.1.0";
    public override string ModuleAuthor => "guccukCENEVAR";

    private HashSet<ulong> _authorizedPlayers = new HashSet<ulong>();
    private Dictionary<ulong, QAngle> _lastAimAngles = new Dictionary<ulong, QAngle>();
    
    // CCSBot::SnapViewAngles - Engine'in bot bakış açısı fonksiyonu
    // Teleport yerine kullanılır: sadece view angle değişir, model glitch yok
    // Kaynak: https://github.com/leopaulgg/CS2-Aimbot
    private MemoryFunctionVoid<CCSPlayerPawn, QAngle>? _snapViewAngles;
    private bool _snapViewAnglesLoaded = false;

    private float GetFOV() => _config?.FOV ?? 360.0f;
    private float GetMaxDistance() => _config?.MaxDistance ?? 5000.0f;
    
    private const float PredictionFactor = 0f;
    
    private AimbotConfig? _config;
    private const string ConfigFileName = "aimbot_config.json";

    public override void Load(bool hotReload)
    {
        LoadConfig();

        // SnapViewAngles yükle
        try
        {
            _snapViewAngles = new MemoryFunctionVoid<CCSPlayerPawn, QAngle>(GameData.GetSignature("CCSBot_SnapViewAngles"));
            _snapViewAnglesLoaded = true;
        }
        catch
        {
            _snapViewAnglesLoaded = false;
        }

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        
        RayTrace.RayTrace.Initialize();
    }

    // ============================================================
    // OnTick - Aim mantığı (Teleport yöntemi)
    // ============================================================
    private void OnTick()
    {
        // RayTrace lazy init
        if (!RayTrace.RayTrace.IsInitialized)
            RayTrace.RayTrace.Initialize();

        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.PawnIsAlive != true || !_authorizedPlayers.Contains(player.SteamID))
                continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || playerPawn.AbsOrigin == null) continue;

            // Silah kontrolü
            var activeWeapon = playerPawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && IsIgnoredWeapon(activeWeapon.DesignerName ?? ""))
                continue;

            // Hedef bul
            var target = GetBestTarget(player);
            if (target == null || target.PlayerPawn.Value == null)
                continue;

            // Recoil + Spread sıfırlama
            if (playerPawn.AimPunchAngle != null) { playerPawn.AimPunchAngle.X = 0; playerPawn.AimPunchAngle.Y = 0; playerPawn.AimPunchAngle.Z = 0; }
            if (playerPawn.AimPunchAngleVel != null) { playerPawn.AimPunchAngleVel.X = 0; playerPawn.AimPunchAngleVel.Y = 0; playerPawn.AimPunchAngleVel.Z = 0; }
            playerPawn.AimPunchTickBase = -1;
            playerPawn.AimPunchTickFraction = 0;

            // Silah yayılımını (accuracy penalty) sıfırla - sağ/sol sekmeyi engeller
            var weapon = playerPawn.WeaponServices?.ActiveWeapon?.Value;
            if (weapon != null)
            {
                var csWeapon = weapon.As<CCSWeaponBase>();
                if (csWeapon != null)
                    csWeapon.AccuracyPenalty = 0;
            }

            // Mevcut açı
            QAngle currentAngles;
            if (_lastAimAngles.TryGetValue(player.SteamID, out var lastAngle))
                currentAngles = lastAngle;
            else
                currentAngles = playerPawn.V_angle ?? playerPawn.EyeAngles ?? new QAngle(0, 0, 0);

            // Göz pozisyonu (sabit yükseklik)
            float myEyeZ = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;
            Vector eyePos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + myEyeZ);

            var targetPawn = target.PlayerPawn.Value;
            float targetEyeZ = targetPawn.ViewOffset.Z;
            if (targetEyeZ < 30.0f) targetEyeZ = 64.0f;

            Vector velocity = targetPawn.AbsVelocity ?? new Vector(0, 0, 0);
            Vector targetPos = targetPawn.AbsOrigin!;
            Vector predictedPos = new Vector(
                targetPos.X + velocity.X * PredictionFactor,
                targetPos.Y + velocity.Y * PredictionFactor,
                targetPos.Z + velocity.Z * PredictionFactor
            );

            Vector targetHead = new Vector(predictedPos.X, predictedPos.Y, predictedPos.Z + targetEyeZ);
            QAngle targetAngle = CalculateAngle(eyePos, targetHead);

            // Smooth
            float smoothFactor = _config?.SmoothFactor ?? 0.5f;
            float pitchDiff = MathF.Abs(targetAngle.X - currentAngles.X);
            if (pitchDiff > 30f)
                smoothFactor = MathF.Min(1.0f, smoothFactor + (pitchDiff - 30f) / 60f * 0.5f);

            QAngle finalAngle = LerpAngle(currentAngles, targetAngle, smoothFactor);

            // Açı uygula
            if (_snapViewAnglesLoaded && _snapViewAngles != null)
            {
                // SnapViewAngles: engine'in bot fonksiyonu, model glitch yok
                _snapViewAngles.Invoke(playerPawn, finalAngle);
            }
            else
            {
                // Fallback: Teleport (model glitch olabilir)
                playerPawn.Teleport(playerPawn.AbsOrigin!, finalAngle, playerPawn.AbsVelocity!);
                if (playerPawn.AbsRotation != null)
                    playerPawn.AbsRotation.X = 0;
            }

            _lastAimAngles[player.SteamID] = new QAngle(finalAngle.X, finalAngle.Y, 0);
        }
    }

    // ============================================================
    // Duvar Kontrolü
    // ============================================================
    private bool SingleTrace(Vector from, Vector to, IntPtr skipHandle)
    {
        if (!RayTrace.RayTrace.TraceWall(from, to, skipHandle, out var result))
            return false;

        if (result.IsAllSolid) return false;
        return result.Fraction < 0.97f;
    }

    private bool IsWallBetween(CCSPlayerController player, CCSPlayerController target)
    {
        if (!RayTrace.RayTrace.IsInitialized)
            return false;

        var playerPawn = player.PlayerPawn.Value;
        var targetPawn = target.PlayerPawn.Value;

        if (playerPawn?.AbsOrigin == null || targetPawn?.AbsOrigin == null) return true;

        float eyeZ = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;

        float targetEyeZ = targetPawn.ViewOffset.Z;
        if (targetEyeZ < 30.0f) targetEyeZ = 64.0f;

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
            // 3 noktaya trace: baş, gövde, bel
            Vector headPos = new Vector(tx, ty, tz + targetEyeZ);
            if (!SingleTrace(startPos, headPos, skipHandle))
                return false;

            Vector chestPos = new Vector(tx, ty, tz + targetEyeZ * 0.6f);
            if (!SingleTrace(startPos, chestPos, skipHandle))
                return false;

            Vector waistPos = new Vector(tx, ty, tz + targetEyeZ * 0.35f);
            if (!SingleTrace(startPos, waistPos, skipHandle))
                return false;

            return true;
        }
        catch { return false; }
    }

    // ============================================================
    // Hedef Seçimi
    // ============================================================
    private CCSPlayerController? GetBestTarget(CCSPlayerController player)
    {
        CCSPlayerController? bestTarget = null;
        float bestScore = float.MaxValue;

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn?.AbsOrigin == null) return null;

        QAngle currentAngles = playerPawn.V_angle ?? playerPawn.EyeAngles ?? new QAngle(0, 0, 0);
        float myEyeZ = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;
        Vector eyePos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + myEyeZ);

        Vector forward = AngleToForward(currentAngles);
        int playerTeam = player.TeamNum;

        foreach (var enemy in Utilities.GetPlayers()
            .Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != playerTeam))
        {
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn?.AbsOrigin == null) continue;

            float enemyEyeZ = enemyPawn.ViewOffset.Z;
            if (enemyEyeZ < 30.0f) enemyEyeZ = 64.0f;
            Vector enemyHead = new Vector(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z + enemyEyeZ);

            float dist = GetDistance(eyePos, enemyHead);
            if (dist > GetMaxDistance()) continue;

            // Duvar kontrolü
            if (IsWallBetween(player, enemy))
                continue;

            Vector dir = Normalize(enemyHead - eyePos);
            float dotProduct = Dot(forward, dir);
            float angle = MathF.Acos(Math.Clamp(dotProduct, -1.0f, 1.0f)) * (180.0f / MathF.PI);

            if (angle > GetFOV() / 2.0f) continue;

            float score = angle + (dist / GetMaxDistance());
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = enemy;
            }
        }
        return bestTarget;
    }

    // ============================================================
    // Komutlar
    // ============================================================
    [ConsoleCommand("css_tracetest", "RayTrace teshis komutu")]
    public void OnTraceTestCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn?.AbsOrigin == null) return;

        player.PrintToChat($" \x01[\x04Trace\x01] FUNPLAY Ray-Trace: {(RayTrace.RayTrace.IsInitialized ? "\x04AKTIF" : "\x02DEVRE DISI")}");

        if (!RayTrace.RayTrace.IsInitialized)
        {
            player.PrintToChat($" \x02[Hata]\x01 {RayTrace.RayTrace.InitError ?? "Bilinmeyen"}");
            return;
        }

        Vector eyePos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
        Vector forward = new Vector();
        var viewAngle = pawn.V_angle ?? pawn.EyeAngles;
        NativeAPI.AngleVectors(viewAngle!.Handle, forward.Handle, 0, 0);
        Vector endPos = new Vector(eyePos.X + forward.X * 3000, eyePos.Y + forward.Y * 3000, eyePos.Z + forward.Z * 3000);

        bool ok = RayTrace.RayTrace.TraceWallDebug(eyePos, endPos, pawn.Handle, out var r);

        string hitEntity = "-";
        if (ok && r.DidHit && r.HitEntity != nint.Zero)
        {
            try
            {
                var ent = new CEntityInstance(r.HitEntity);
                if (ent != null && ent.IsValid) hitEntity = ent.DesignerName ?? "?";
            }
            catch { hitEntity = "err"; }
        }

        player.PrintToChat($" \x04--- TRACE SONUCLARI ---");
        player.PrintToChat($" \x01Fraction: \x04{(ok ? r.Fraction.ToString("F4") : "HATA")}");
        player.PrintToChat($" \x01Hit Entity: \x04{hitEntity}");
        player.PrintToChat($" \x01AllSolid: \x04{(ok ? r.IsAllSolid.ToString() : "-")}");
        player.PrintToChat($" \x01Frac < 1.0 = \x02duvar \x01| >= 1.0 = \x04serbest");
    }

    [ConsoleCommand("css_aim", "Aimbot ac/kapat")]
    public void OnAimCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (!AdminManager.PlayerHasPermissions(player, "@css/generic"))
        {
            player.PrintToChat(" \x02[Hata] \x01Yetkiniz yok.");
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
            player.PrintToChat($" \x01Yontem: \x04{(_snapViewAnglesLoaded ? "SnapViewAngles" : "Teleport (fallback)")}");


            if (!RayTrace.RayTrace.IsInitialized)
                RayTrace.RayTrace.Initialize();

            if (RayTrace.RayTrace.IsInitialized)
                player.PrintToChat(" \x01[\x04RayTrace\x01] Duvar kontrolu: \x04AKTIF (FUNPLAY)");
            else
                player.PrintToChat(" \x01[\x02RayTrace\x01] Duvar kontrolu: \x02DEVRE DISI");
        }
    }

    // ============================================================
    // Matematik
    // ============================================================
    private float GetDistance(Vector a, Vector b) =>
        MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2) + MathF.Pow(a.Z - b.Z, 2));

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
        return l > 0 ? new Vector(v.X / l, v.Y / l, v.Z / l) : new Vector(0, 0, 1);
    }

    private float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private QAngle LerpAngle(QAngle from, QAngle to, float t) => new QAngle(
        LerpFloat(from.X, to.X, t),
        LerpAngleFloat(from.Y, to.Y, t),
        0
    );

    private float LerpFloat(float from, float to, float t) => from + (to - from) * t;

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
        if (name.Contains("taser") || name.Contains("c4")) return true;
        return false;
    }

    // ============================================================
    // Config
    // ============================================================
    private void LoadConfig()
    {
        var configPath = GetConfigPath();
        if (File.Exists(configPath))
        {
            try
            {
                string raw = File.ReadAllText(configPath);
                var lines = raw.Split('\n').Where(l => !l.TrimStart().StartsWith("//"));
                _config = JsonSerializer.Deserialize<AimbotConfig>(string.Join("\n", lines));
                if (_config == null) _config = new AimbotConfig();
            }
            catch { _config = new AimbotConfig(); }
        }
        else
        {
            _config = new AimbotConfig();
            SaveConfig();
        }

        _config.SmoothFactor = Math.Clamp(_config.SmoothFactor, 0f, 1f);
        _config.FOV = Math.Clamp(_config.FOV, 0f, 360f);
        if (_config.MaxDistance < 100f) _config.MaxDistance = 100f;
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
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"SmoothFactor\": {_config!.SmoothFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"FOV\": {_config.FOV.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.AppendLine($"  \"MaxDistance\": {_config.MaxDistance.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine("}");
            File.WriteAllText(GetConfigPath(), sb.ToString());
        }
        catch { }
    }

    // ============================================================
    // Temizlik
    // ============================================================
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
