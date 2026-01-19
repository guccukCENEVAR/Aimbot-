using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using System.Text.Json.Serialization;

namespace NoRecoilPlugin;

public sealed class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("permission")]
    public string Permission { get; set; } = "@css/root";

    [JsonPropertyName("ConfigVersion")]
    public override int Version { get; set; } = 1;
}

[MinimumApiVersion(304)]
public partial class Plugin : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "No Recoil";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Your Name";
    public override string ModuleDescription => "Server side No Recoil for Counter-Strike: 2";

    public required PluginConfig Config { get; set; } = new PluginConfig();

    public void OnConfigParsed(PluginConfig config)
    {
        this.Config = config;
    }

    public class PlayerRecoilState
    {
        public bool Enabled { get; set; } = false;
    }

    private readonly Dictionary<CCSPlayerController, PlayerRecoilState> RecoilStates = [];

    public override void Load(bool hotReload)
    {
        AddCommand("css_norecoil", "Toggle no recoil", CommandToggleNoRecoil);

        RegisterListener<Listeners.OnTick>(OnTick);

        RegisterEventHandler((EventPlayerDisconnect @event, GameEventInfo info) =>
        {
            CCSPlayerController? player = @event.Userid;
            if (player is null || !player.IsValid)
                return HookResult.Continue;

            RecoilStates.Remove(player);
            return HookResult.Continue;
        });
    }

    private void OnTick()
    {
        if (RecoilStates.Count == 0)
            return;

        foreach (var kvp in RecoilStates.ToList())
        {
            var player = kvp.Key;
            var state = kvp.Value;

            if (!IsValidPlayer(player))
            {
                RecoilStates.Remove(player);
                continue;
            }

            if (state.Enabled && player.PlayerPawn.Value?.IsValid == true)
            {
                // Recoil timing
                player.PlayerPawn.Value.AimPunchTickBase = 0;
                player.PlayerPawn.Value.AimPunchTickFraction = 0;

                // Recoil angles
                player.PlayerPawn.Value.AimPunchAngle.X = 0;
                player.PlayerPawn.Value.AimPunchAngle.Y = 0;
                player.PlayerPawn.Value.AimPunchAngle.Z = 0;

                // Recoil velocity
                player.PlayerPawn.Value.AimPunchAngleVel.X = 0;
                player.PlayerPawn.Value.AimPunchAngleVel.Y = 0;
                player.PlayerPawn.Value.AimPunchAngleVel.Z = 0;
            }
        }
    }

    public void CommandToggleNoRecoil(CCSPlayerController? player, CommandInfo command)
    {
        if (!ValidateCommand(player))
            return;

        ToggleRecoilState(player!);
    }

    private bool ValidateCommand(CCSPlayerController? player)
        => player != null && player.IsValid && AdminManager.PlayerHasPermissions(player, Config.Permission);

    private void ToggleRecoilState(CCSPlayerController player)
    {
        if (RecoilStates.TryGetValue(player, out var state))
        {
            state.Enabled = !state.Enabled;
            player.PrintToCenterAlert($"NO RECOIL: {(state.Enabled ? "ON" : "OFF")}");
        }
        else
        {
            var newState = new PlayerRecoilState { Enabled = true };
            RecoilStates.Add(player, newState);
            player.PrintToCenterAlert("NO RECOIL: ON");
        }
    }

    private static bool IsValidPlayer(CCSPlayerController? player) =>
        player is { IsValid: true, IsHLTV: false } &&
        player.PlayerPawn?.IsValid == true &&
        player.TeamNum > 1 &&
        player.PlayerPawn.Value!.LifeState == (byte)LifeState_t.LIFE_ALIVE;
}
