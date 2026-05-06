using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using System.Runtime.InteropServices;
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
    private MemoryFunctionVoid<CCSPlayerPawn, QAngle>? _snapViewAngles;
    private bool _snapViewAnglesLoaded = false;
    
    // Debug modu
    private HashSet<ulong> _debugPlayers = new HashSet<ulong>();
    private int _debugTickCounter = 0;

    private float GetFOV() => _config?.FOV ?? 360.0f;
    private float GetMaxDistance() => _config?.MaxDistance ?? 5000.0f;
    
    private const float PredictionFactor = 0f;
    
    private AimbotConfig? _config;
    private const string ConfigFileName = "aimbot_config.json";

    public override void Load(bool hotReload)
    {
        LoadConfig();

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Linux";
        Console.WriteLine($"[Aimbot] Platform: {platform}");

        // SnapViewAngles yükle
        try
        {
            _snapViewAngles = new MemoryFunctionVoid<CCSPlayerPawn, QAngle>(GameData.GetSignature("CCSBot_SnapViewAngles"));
            _snapViewAnglesLoaded = true;
            Console.WriteLine($"[Aimbot] SnapViewAngles: YUKLENDI");
        }
        catch (Exception ex)
        {
            _snapViewAnglesLoaded = false;
            Console.WriteLine($"[Aimbot] SnapViewAngles: BASARISIZ - {ex.Message}");
            Console.WriteLine($"[Aimbot] Teleport fallback kullanilacak");
        }

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        
        RayTrace.RayTrace.Initialize();
    }

    // ============================================================
    // OnTick - Aim mantığı
    // ============================================================
    private void OnTick()
    {
        // RayTrace lazy init
        if (!RayTrace.RayTrace.IsInitialized)
            RayTrace.RayTrace.Initialize();

        _debugTickCounter++;

        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.PawnIsAlive != true || !_authorizedPlayers.Contains(player.SteamID))
                continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || playerPawn.AbsOrigin == null) continue;

            bool showDebug = _debugPlayers.Contains(player.SteamID) && (_debugTickCounter % 64 == 0);

            // Silah kontrolü
            var activeWeapon = playerPawn.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && IsIgnoredWeapon(activeWeapon.DesignerName ?? ""))
            {
                if (showDebug) player.PrintToChat($" \x02[D] Silah ignored: {activeWeapon.DesignerName}");
                continue;
            }

            // Hedef bul
            var target = GetBestTarget(player, out float chosenAimZ);
            if (target == null || target.PlayerPawn.Value == null)
            {
                if (showDebug)
                {
                    // Neden hedef bulunamadığını göster
                    int enemyCount = 0, wallBlocked = 0, fovBlocked = 0, distBlocked = 0;
                    int myTeam = player.TeamNum;
                    float myEyeZDbg = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;
                    Vector eyePosDbg = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + myEyeZDbg);
                    QAngle curDbg = playerPawn.V_angle ?? playerPawn.EyeAngles ?? new QAngle(0, 0, 0);
                    Vector fwdDbg = AngleToForward(curDbg);
                    
                    foreach (var e in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != myTeam))
                    {
                        var ep = e.PlayerPawn.Value;
                        if (ep?.AbsOrigin == null) continue;
                        enemyCount++;
                        float eez = ep.ViewOffset.Z; if (eez < 30f) eez = 64f;
                        Vector eh = new Vector(ep.AbsOrigin.X, ep.AbsOrigin.Y, ep.AbsOrigin.Z + eez);
                        float dist = GetDistance(eyePosDbg, eh);
                        if (dist > GetMaxDistance()) { distBlocked++; continue; }
                        if (IsWallBetween(player, e)) { wallBlocked++; continue; }
                        Vector dir = Normalize(eh - eyePosDbg);
                        float dot = Dot(fwdDbg, dir);
                        float ang = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * (180f / MathF.PI);
                        if (ang > GetFOV() / 2f) { fovBlocked++; }
                    }
                    player.PrintToChat($" \x02[D] Hedef yok! Dusman:{enemyCount} Duvar:{wallBlocked} FOV:{fovBlocked} Dist:{distBlocked}");
                }
                continue;
            }

            if (showDebug) player.PrintToChat($" \x04[D] Hedef: {target.PlayerName}");

            // Recoil + Spread sıfırlama (CS2 son güncellemeleriyle AimPunch özellikleri kaldırıldı)
            playerPawn.ShotsFired = 0;

            // Silah yayılımını (accuracy penalty) sıfırla
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

            // Göz pozisyonu
            float myEyeZ = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;
            Vector eyePos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + myEyeZ);

            var targetPawn = target.PlayerPawn.Value;

            Vector velocity = targetPawn.AbsVelocity ?? new Vector(0, 0, 0);
            Vector targetPos = targetPawn.AbsOrigin!;
            Vector predictedPos = new Vector(
                targetPos.X + velocity.X * PredictionFactor,
                targetPos.Y + velocity.Y * PredictionFactor,
                targetPos.Z + velocity.Z * PredictionFactor
            );

            // GetBestTarget'in dondurdugu Z: kafa acikssa kafa Z, kafa kapali gogus acikssa gogus Z
            Vector targetHead = new Vector(predictedPos.X, predictedPos.Y, predictedPos.Z + chosenAimZ);
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
                try
                {
                    _snapViewAngles.Invoke(playerPawn, finalAngle);
                    if (showDebug) player.PrintToChat($" \x04[D] SnapViewAngles OK p:{finalAngle.X:F1} y:{finalAngle.Y:F1}");
                }
                catch (Exception ex)
                {
                    // SnapViewAngles çalışmıyorsa Teleport'a düş
                    if (showDebug) player.PrintToChat($" \x02[D] SnapViewAngles HATA: {ex.Message}");
                    playerPawn.Teleport(playerPawn.AbsOrigin!, finalAngle, playerPawn.AbsVelocity!);
                    if (playerPawn.AbsRotation != null)
                        playerPawn.AbsRotation.X = 0;
                }
            }
            else
            {
                playerPawn.Teleport(playerPawn.AbsOrigin!, finalAngle, playerPawn.AbsVelocity!);
                if (playerPawn.AbsRotation != null)
                    playerPawn.AbsRotation.X = 0;
                if (showDebug) player.PrintToChat($" \x04[D] Teleport p:{finalAngle.X:F1} y:{finalAngle.Y:F1}");
            }

            _lastAimAngles[player.SteamID] = new QAngle(finalAngle.X, finalAngle.Y, 0);
        }
    }

    // ============================================================
    // Duvar Kontrolü
    // InteractsExclude=0 kullanıyoruz (resmi FUNPLAY API uyumlu)
    // Trace her şeye çarpar, filtreleme C# tarafında yapılır
    // ============================================================
    
    
    // Bilinen duvar entity'leri (sadece bunlar duvar sayılır)
    private static bool IsWallEntity(string name)
    {
        // Dünya geometrisi
        if (name.StartsWith("worldent", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("world", StringComparison.OrdinalIgnoreCase)) return true;
        // Brush entity'ler
        if (name.StartsWith("func_wall", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("func_brush", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("func_breakable", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("func_door", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("func_rotating", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("func_lod", StringComparison.OrdinalIgnoreCase)) return true;
        // Statik prop'lar (duvar gibi davranır)
        if (name.StartsWith("prop_static", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    
    
    /// <summary>
    /// Tek bir ışın trace'i yapar ve sonucun duvar olup olmadığını döndürür.
    /// true = duvar var, false = duvar yok (yol açık)
    /// 
    /// Strateji: Sadece BILINEN duvar entity'leri "duvar" sayılır.
    /// Bilinmeyen her şey "duvar değil" olarak geçer (güvenli taraf).
    /// </summary>
    private bool SingleTrace(Vector from, Vector to, IntPtr skipHandle)
    {
        if (!RayTrace.RayTrace.TraceWall(from, to, skipHandle, out var result))
            return false; // Trace başarısız = duvar yok say

        // AllSolid: başlangıç noktası katı içinde
        if (result.IsAllSolid) return false;
        
        // Fraction >= 0.97 = hedefe neredeyse ulaştı, engel yok
        if (result.Fraction >= 0.97f) return false;
        
        // Fraction < 0.97 = bir şeye çarptı
        
        // HitEntity sıfırsa → dünya geometrisi (brush/worldspawn) → DUVAR
        if (result.HitEntity == nint.Zero)
            return true;
        
        // HitEntity varsa → entity tipine bak
        try
        {
            var hitEnt = new CEntityInstance(result.HitEntity);
            if (hitEnt == null || !hitEnt.IsValid)
                return false; // Geçersiz entity → duvar değil say
            
            string name = hitEnt.DesignerName ?? "";
            
            // Boş isim → muhtemelen dünya geometrisi → DUVAR
            if (string.IsNullOrEmpty(name))
                return true;
            
            // Sadece bilinen duvar entity'leri → DUVAR
            if (IsWallEntity(name))
                return true;
            
            // Geri kalan HER ŞEY → DUVAR DEĞİL (oyuncu, trigger, prop, silah, vs)
            return false;
        }
        catch 
        { 
            return false; // Exception → duvar değil say
        }
    }

    // Gogus yuksekligi: kafa Z'sinin %60'i (Z eksenine gore omuz/gogus seviyesi)
    private const float ChestFactor = 0.6f;

    /// <summary>
    /// Hedefin gorunen nisan noktasi Z offset'ini dondurur.
    /// Once kafaya trace, gorunurse kafa Z; kapaliysa goguse trace, gorunurse gogus Z.
    /// Ikisi de kapaliysa null = hedef alinmaz.
    /// RayTrace yuklu degilse permissive: kafa Z dondur (eski davranis).
    /// </summary>
    private float? GetVisibleAimZ(CCSPlayerController player, CCSPlayerController target)
    {
        var playerPawn = player.PlayerPawn.Value;
        var targetPawn = target.PlayerPawn.Value;

        if (playerPawn?.AbsOrigin == null || targetPawn?.AbsOrigin == null) return null;

        float targetEyeZ = targetPawn.ViewOffset.Z;
        if (targetEyeZ < 30.0f) targetEyeZ = 64.0f;

        // RayTrace yoksa eski davranis: kafaya nisan al, duvar kontrolsuz
        if (!RayTrace.RayTrace.IsInitialized)
            return targetEyeZ;

        float eyeZ = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;

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
            // 1) Kafa: gorunurse direkt kafaya nisanlan (1 trace)
            Vector headPos = new Vector(tx, ty, tz + targetEyeZ);
            if (!SingleTrace(startPos, headPos, skipHandle))
                return targetEyeZ;

            // 2) Kafa kapali, goguse dene (2. trace)
            float chestZ = targetEyeZ * ChestFactor;
            Vector chestPos = new Vector(tx, ty, tz + chestZ);
            if (!SingleTrace(startPos, chestPos, skipHandle))
                return chestZ;

            // Ikisi de kapali → hedef gorunmuyor
            return null;
        }
        catch { return null; }
    }

    // Eski API: sadece debug istatistikleri icin (OnTick debug branch)
    private bool IsWallBetween(CCSPlayerController player, CCSPlayerController target)
        => GetVisibleAimZ(player, target) == null;

    // ============================================================
    // Hedef Seçimi
    // ============================================================
    private CCSPlayerController? GetBestTarget(CCSPlayerController player, out float aimZ)
    {
        CCSPlayerController? bestTarget = null;
        float bestScore = float.MaxValue;
        float bestAimZ = 64.0f;
        aimZ = 64.0f;

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

            // FOV kontrolu (raytrace'den ucuz, once bunu yap)
            Vector dir = Normalize(enemyHead - eyePos);
            float dotProduct = Dot(forward, dir);
            float angle = MathF.Acos(Math.Clamp(dotProduct, -1.0f, 1.0f)) * (180.0f / MathF.PI);
            if (angle > GetFOV() / 2.0f) continue;

            // Gorunurluk: kafa veya gogus acik mi?
            float? visibleZ = GetVisibleAimZ(player, enemy);
            if (visibleZ == null) continue;

            float score = angle + (dist / GetMaxDistance());
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = enemy;
                bestAimZ = visibleZ.Value;
            }
        }
        aimZ = bestAimZ;
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
        NativeAPI.AngleVectors(viewAngle!.Handle, forward.Handle, nint.Zero, nint.Zero);
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

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WIN" : "LNX";
        player.PrintToChat($" \x04--- TRACE SONUCLARI [{platform}] ---");
        player.PrintToChat($" \x01Fraction: \x04{(ok ? r.Fraction.ToString("F4") : "HATA")}");
        player.PrintToChat($" \x01Hit Entity: \x04{hitEntity}");
        player.PrintToChat($" \x01AllSolid: \x04{(ok ? r.IsAllSolid.ToString() : "-")}");
        if (ok) player.PrintToChat($" \x01EndPos: \x04{r.EndPosX:F0},{r.EndPosY:F0},{r.EndPosZ:F0}");
        player.PrintToChat($" \x01Frac < 1.0 = \x02duvar \x01| >= 1.0 = \x04serbest");
    }

    [ConsoleCommand("css_traceenemy", "En yakin dusmana trace yap - debug")]
    public void OnTraceEnemyCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn?.AbsOrigin == null) return;

        if (!RayTrace.RayTrace.IsInitialized)
        {
            player.PrintToChat($" \x02[Hata]\x01 RayTrace yuklu degil");
            return;
        }

        float myEyeZ = (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;
        Vector eyePos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + myEyeZ);
        int myTeam = player.TeamNum;

        // En yakın düşmanı bul
        CCSPlayerController? nearest = null;
        float nearestDist = float.MaxValue;
        foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != myTeam))
        {
            var ep = enemy.PlayerPawn.Value;
            if (ep?.AbsOrigin == null) continue;
            float d = GetDistance(eyePos, ep.AbsOrigin);
            if (d < nearestDist) { nearestDist = d; nearest = enemy; }
        }

        if (nearest == null)
        {
            player.PrintToChat($" \x02Dusman bulunamadi");
            return;
        }

        var targetPawn = nearest.PlayerPawn.Value!;
        float targetEyeZ = targetPawn.ViewOffset.Z;
        if (targetEyeZ < 30.0f) targetEyeZ = 64.0f;
        float tx = targetPawn.AbsOrigin!.X, ty = targetPawn.AbsOrigin.Y, tz = targetPawn.AbsOrigin.Z;

        player.PrintToChat($" \x04=== TRACE TO ENEMY ({nearest.PlayerName}) ===");
        player.PrintToChat($" \x01Mesafe: \x04{nearestDist:F0}\x01 birim");

        // 3 trace: baş, gövde, bel
        string[] labels = { "BAS", "GOVDE", "BEL" };
        float[] multipliers = { 1.0f, 0.6f, 0.35f };

        for (int i = 0; i < 3; i++)
        {
            Vector target = new Vector(tx, ty, tz + targetEyeZ * multipliers[i]);
            
            bool ok = RayTrace.RayTrace.TraceWall(eyePos, target, pawn.Handle, out var r);

            string entName = "-";
            if (ok && r.HitEntity != nint.Zero)
            {
                try
                {
                    var ent = new CEntityInstance(r.HitEntity);
                    if (ent != null && ent.IsValid) entName = ent.DesignerName ?? "(bos)";
                    else entName = "(gecersiz)";
                }
                catch { entName = "(err)"; }
            }
            else if (ok && r.HitEntity == nint.Zero && r.Fraction < 0.97f)
            {
                entName = "(worldspawn)";
            }

            string fracStr = ok ? r.Fraction.ToString("F3") : "FAIL";
            bool isWall = ok && r.Fraction < 0.97f && (r.HitEntity == nint.Zero || IsWallEntity(entName));
            string wallStr = isWall ? "\x02DUVAR" : "\x04ACIK";
            
            player.PrintToChat($" \x01{labels[i]}: frac=\x04{fracStr}\x01 ent=\x04{entName}\x01 → {wallStr}");
        }

        bool wallBetween = IsWallBetween(player, nearest);
        player.PrintToChat($" \x01Sonuc: {(wallBetween ? "\x02DUVAR VAR - kilitlenmez" : "\x04YOL ACIK - kilitlenir")}");
    }

    [ConsoleCommand("css_tracediag", "RayTrace diagnostik - farkli maskeleri test eder")]
    public void OnTraceDiagCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn?.AbsOrigin == null) return;

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WIN" : "LNX";
        player.PrintToChat($" \x04=== TRACE DIAGNOSTIK [{platform}] ===");

        if (!RayTrace.RayTrace.IsInitialized)
        {
            player.PrintToChat($" \x02[Hata]\x01 RayTrace yuklu degil: {RayTrace.RayTrace.InitError ?? "?"}");
            return;
        }

        Vector eyePos = new Vector(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
        Vector forward = new Vector();
        var viewAngle = pawn.V_angle ?? pawn.EyeAngles;
        NativeAPI.AngleVectors(viewAngle!.Handle, forward.Handle, nint.Zero, nint.Zero);
        Vector endPos = new Vector(eyePos.X + forward.X * 3000, eyePos.Y + forward.Y * 3000, eyePos.Z + forward.Z * 3000);

        // Test 1: MASK_SHOT_PHYSICS (resmi örnek)
        TestMask(player, "SHOT_PHYS", (ulong)InteractionLayers.MASK_SHOT_PHYSICS, 0, eyePos, endPos, pawn.Handle);
        
        // Test 2: MASK_SHOT_FULL (hitbox dahil)
        TestMask(player, "SHOT_FULL", (ulong)InteractionLayers.MASK_SHOT_FULL, 0, eyePos, endPos, pawn.Handle);
        
        // Test 3: Sadece Solid
        TestMask(player, "SOLID", (ulong)InteractionLayers.Solid, 0, eyePos, endPos, pawn.Handle);
        
        // Test 4: Solid + WorldGeometry
        TestMask(player, "SOLID+WG", (ulong)(InteractionLayers.Solid | InteractionLayers.WorldGeometry), 0, eyePos, endPos, pawn.Handle);
        
        // Test 5: Tüm katmanlar
        TestMask(player, "ALL", 0xFFFFFFFFFFFFFFFF, 0, eyePos, endPos, pawn.Handle);
    }

    private unsafe void TestMask(CCSPlayerController player, string label, ulong interactsWith, ulong interactsExclude, Vector start, Vector end, IntPtr skipHandle)
    {
        var options = new TraceOptions
        {
            InteractsWith = interactsWith,
            InteractsExclude = interactsExclude,
            DrawBeam = 0
        };

        CBaseEntity? skipEntity = null;
        if (skipHandle != IntPtr.Zero)
        {
            try { skipEntity = new CBaseEntity(skipHandle); }
            catch { }
        }

        bool ok = RayTrace.RayTrace.TraceEndShape(start, end, skipEntity, options, out var r);

        string entityName = "-";
        if (ok && r.DidHit && r.HitEntity != nint.Zero)
        {
            try
            {
                var ent = new CEntityInstance(r.HitEntity);
                if (ent != null && ent.IsValid) entityName = ent.DesignerName ?? "?";
            }
            catch { entityName = "err"; }
        }

        string fracStr = ok ? r.Fraction.ToString("F3") : "FAIL";
        string color = (ok && r.Fraction < 1.0f) ? "\x04" : "\x02";
        player.PrintToChat($" \x01{label}: {color}{fracStr}\x01 ent:\x04{entityName}");
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

    [ConsoleCommand("css_aimdebug", "Aim debug modunu ac/kapat")]
    public void OnAimDebugCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid || player.IsBot) return;
        if (!AdminManager.PlayerHasPermissions(player, "@css/generic"))
        {
            player.PrintToChat(" \x02[Hata] \x01Yetkiniz yok.");
            return;
        }

        if (_debugPlayers.Contains(player.SteamID))
        {
            _debugPlayers.Remove(player.SteamID);
            player.PrintToChat(" \x01[\x04Debug\x01] Aim debug: \x02KAPALI");
        }
        else
        {
            _debugPlayers.Add(player.SteamID);
            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "WIN" : "LNX";
            player.PrintToChat(" \x01[\x04Debug\x01] Aim debug: \x04ACIK");
            player.PrintToChat($" \x01Platform: \x04{platform}");
            player.PrintToChat($" \x01Yontem: \x04{(_snapViewAnglesLoaded ? "SnapViewAngles" : "Teleport")}");
            player.PrintToChat($" \x01RayTrace: \x04{(RayTrace.RayTrace.IsInitialized ? "AKTIF" : "KAPALI")}");
            player.PrintToChat($" \x01FOV: \x04{GetFOV()} \x01MaxDist: \x04{GetMaxDistance()}");
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
