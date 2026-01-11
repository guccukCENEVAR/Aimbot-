using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using System.Collections.Generic;
using System.Linq;

namespace AimbotPlugin;

[MinimumApiVersion(80)]
public class AimbotPlugin : BasePlugin
{
    public override string ModuleName => "Admin Aimbot Snap Pro";
    public override string ModuleVersion => "1.2.1";
    public override string ModuleAuthor => "guccukCENEVAR";

    private HashSet<ulong> _authorizedPlayers = new HashSet<ulong>();

    private const float FOV = 360.0f; 
    private const float StandEyeHeight = 64.0f; 
    private const float CrouchEyeHeight = 46.0f;
    private const float StandHeadHeight = 65.0f; 
    private const float CrouchHeadHeight = 46.0f;
    private const float PredictionFactor = 0.015625f; // 64 tick hızı (1/64)

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        Console.WriteLine("[Aimbot] V1.2.1 - Hareket Tahminleme (Prediction) Yuklendi!");
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
            player.PrintToChat(" \x01[\x02Not\x01] Kilitlemek icin \x04'E'\x01 tusuna basili tutun.");
        }
    }

    private void OnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.PawnIsAlive != true || !_authorizedPlayers.Contains(player.SteamID))
                continue;

            if ((player.Buttons & PlayerButtons.Use) == 0)
                continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || playerPawn.AbsOrigin == null) continue;

            var target = GetBestTarget(player);
            if (target == null || target.PlayerPawn.Value == null) continue;

            float currentEyeHeight = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchEyeHeight : StandEyeHeight;
            Vector eyePos = new Vector(playerPawn.AbsOrigin.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + currentEyeHeight);

            var targetPawn = target.PlayerPawn.Value;
            float currentTargetHeadHeight = (targetPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchHeadHeight : StandHeadHeight;
            
            // --- HAREKET TAHMİNLEME (PREDICTION) ---
            // Düşmanın hızı ile mermi gidiş süresini (basitçe 1 tick) çarpıp konuma ekliyoruz
            Vector velocity = targetPawn.AbsVelocity ?? new Vector(0,0,0);
            Vector predictedOrigin = new Vector(
                targetPawn.AbsOrigin!.X + (velocity.X * PredictionFactor),
                targetPawn.AbsOrigin.Y + (velocity.Y * PredictionFactor),
                targetPawn.AbsOrigin.Z + (velocity.Z * PredictionFactor)
            );

            Vector targetHeadBase = new Vector(predictedOrigin.X, predictedOrigin.Y, predictedOrigin.Z + currentTargetHeadHeight);

            // Düşmanın baktığı yöne doğru küçük bir kaydırma (Face-focus)
            Vector enemyForward = AngleToForward(targetPawn.EyeAngles);
            Vector targetHeadFinal = targetHeadBase + (enemyForward * 4.0f);

            QAngle targetAngle = CalculateAngle(eyePos, targetHeadFinal);
            playerPawn.Teleport(playerPawn.AbsOrigin, targetAngle, playerPawn.AbsVelocity);
        }
    }

    private CCSPlayerController? GetBestTarget(CCSPlayerController player)
    {
        CCSPlayerController? bestTarget = null;
        float bestScore = float.MaxValue;

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return null;

        QAngle currentAngles = playerPawn.EyeAngles ?? new QAngle(0,0,0);
        float currentEyeHeight = (playerPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchEyeHeight : StandEyeHeight;
        Vector eyePos = new Vector(playerPawn.AbsOrigin!.X, playerPawn.AbsOrigin.Y, playerPawn.AbsOrigin.Z + currentEyeHeight);
        
        Vector forward = AngleToForward(currentAngles);
        int playerTeam = player.TeamNum;

        foreach (var enemy in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && p.TeamNum != playerTeam))
        {
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn == null || enemyPawn.AbsOrigin == null) continue;

            float currentTargetHeadHeight = (enemyPawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? CrouchHeadHeight : StandHeadHeight;
            Vector enemyHead = new Vector(enemyPawn.AbsOrigin.X, enemyPawn.AbsOrigin.Y, enemyPawn.AbsOrigin.Z + currentTargetHeadHeight);

            float dist = GetDistance(eyePos, enemyHead);
            if (dist > 5000.0f) continue; 

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

    private Vector AngleToForward(QAngle angle) {
        float p = angle.X * (MathF.PI / 180.0f);
        float y = angle.Y * (MathF.PI / 180.0f);
        return new Vector(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
    }

    private Vector Normalize(Vector v) {
        float l = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return l > 0 ? new Vector(v.X / l, v.Y / l, v.Z / l) : new Vector(0,0,0);
    }

    private float Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid != null) _authorizedPlayers.Remove(@event.Userid.SteamID);
        return HookResult.Continue;
    }
}
