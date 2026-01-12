using System.Drawing;
using System.Runtime.InteropServices;
using System.Numerics;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace RayTrace;

[Flags]
public enum InteractionLayers : UInt64
{
    Solid = 0x1,
    Hitboxes = 0x2,
    Trigger = 0x4,
    Sky = 0x8,
    PlayerClip = 0x10,
    NPCClip = 0x20,
    BlockLOS = 0x40,
    BlockLight = 0x80,
    Ladder = 0x100,
    Pickup = 0x200,
    BlockSound = 0x400,
    NoDraw = 0x800,
    Window = 0x1000,
    PassBullets = 0x2000,
    WorldGeometry = 0x4000,
    Water = 0x8000,
    Slime = 0x10000,
    TouchAll = 0x20000,
    Player = 0x40000,
    NPC = 0x80000,
    Debris = 0x100000,
    Physics_Prop = 0x200000,
    NavIgnore = 0x400000,
    NavLocalIgnore = 0x800000,
    PostProcessingVolume = 0x1000000,
    UnusedLayer3 = 0x2000000,
    CarriedObject = 0x4000000,
    PushAway = 0x8000000,
    ServerEntityOnClient = 0x10000000,
    CarriedWeapon = 0x20000000,
    StaticLevel = 0x40000000,
    csgo_team1 = 0x80000000,
    csgo_team2 = 0x100000000,
    csgo_grenadeclip = 0x200000000,
    csgo_droneclip = 0x400000000,
    csgo_moveable = 0x800000000,
    csgo_opaque = 0x1000000000,
    csgo_monster = 0x2000000000,
    csgo_thrown_grenade = 0x8000000000,
}

public static class RayTrace
{
    private static readonly VirtualFunctionWithReturn<
        nint,
        nint,
        nint,
        nint,
        nint,
        nint,
        bool
    > CNavPhysicsInterface_TraceShape;

    private static readonly IntPtr CTraceFilterVtable;

    static RayTrace()
    {
        CNavPhysicsInterface_TraceShape =
            new("CNavPhysicsInterface", GameData.GetOffset("CNavPhysicsInterface_TraceShape"));
        CTraceFilterVtable = NativeAPI.FindSignature(Addresses.ServerPath, GameData.GetSignature("CTraceFilterVtable"));
    }

    public struct TraceResult
    {
        public Vector3 EndPos { get; init; }
        public IntPtr HitEntity { get; init; }
        public float Fraction { get; set; }
        public bool AllSolid { get; set; }
        public Vector3 Normal { get; init; }
    }

    public class TraceOptions
    {
        public InteractionLayers? InteractsAs { get; set; } = null;
        public InteractionLayers? InteractsWith { get; set; } = null;
        public InteractionLayers? InteractsExclude { get; init; } = null;
    }

    public static unsafe TraceResult? TraceEndShape(Vector vecStart, Vector vecEnd, bool drawResult = false,
        CHandle<CCSPlayerPawn>? ignorePlayer = null, TraceOptions? traceOptions = null)
    {
        var filter = new CTraceFilter(ignorePlayer?.Raw ?? 0xFFFFFFFF);

        if (traceOptions is not null)
        {
            if (traceOptions.InteractsAs is not null)
                filter.m_nInteractsAs = (UInt64)traceOptions.InteractsAs.Value;
            if (traceOptions.InteractsWith is not null)
                filter.m_nInteractsWith = (UInt64)traceOptions.InteractsWith.Value;
            if (traceOptions.InteractsExclude is not null)
                filter.m_nInteractsExclude = (UInt64)traceOptions.InteractsExclude.Value;
        }

        var ray = new Ray_t();
        
        CGameTrace* trace = stackalloc CGameTrace[1];
        *trace = default;

        CTraceFilter* filterPtr = stackalloc CTraceFilter[1];
        *filterPtr = filter;

        IntPtr filterVtable = Address.GetAbsoluteAddress(CTraceFilterVtable, 3, 7);
        filterPtr->Vtable = (void*)filterVtable;

        Ray_t* rayPtr = stackalloc Ray_t[1];
        *rayPtr = ray;

        CNavPhysicsInterface_TraceShape.Invoke(
            IntPtr.Zero,
            (nint)rayPtr,
            vecStart.Handle,
            vecEnd.Handle,
            (nint)filterPtr,
            (nint)trace
        );

        return new TraceResult
        {
            EndPos = trace->EndPos,
            HitEntity = trace->HitEntity,
            Fraction = trace->Fraction,
            AllSolid = trace->AllSolid,
            Normal = trace->Normal
        };
    }

    public static TraceResult? TraceShape(Vector origin, QAngle viewangles, bool drawResult = false,
        CHandle<CCSPlayerPawn>? ignorePlayer = null, TraceOptions? traceOptions = null)
    {
        var forward = new Vector();
        NativeAPI.AngleVectors(viewangles.Handle, forward.Handle, 0, 0);
        var endOrigin = new Vector(origin.X + forward.X * 8192, origin.Y + forward.Y * 8192,
            origin.Z + forward.Z * 8192);

        return TraceEndShape(origin, endOrigin, drawResult, ignorePlayer, traceOptions);
    }
}

internal static class Address
{
    public static unsafe IntPtr GetAbsoluteAddress(IntPtr addr, IntPtr offset, int size)
    {
        if (addr == IntPtr.Zero)
            throw new Exception("Failed to find RayTrace signature.");

        int code = *(int*)(addr + offset);
        return addr + code + size;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0x44)]
public unsafe struct TraceHitboxData
{
    [FieldOffset(0x38)] public int HitGroup;
    [FieldOffset(0x40)] public int HitboxId;
}

[StructLayout(LayoutKind.Explicit, Size = 0xB8)]
public unsafe struct CGameTrace
{
    [FieldOffset(0x00)] public IntPtr Surface;
    [FieldOffset(0x08)] public IntPtr HitEntity;
    [FieldOffset(0x10)] public TraceHitboxData* HitboxData;
    [FieldOffset(0x50)] public uint Contents;
    [FieldOffset(0x78)] public Vector3 StartPos;
    [FieldOffset(0x84)] public Vector3 EndPos;
    [FieldOffset(0x90)] public Vector3 Normal;
    [FieldOffset(0x9C)] public Vector3 Position;
    [FieldOffset(0xAC)] public float Fraction;
    [FieldOffset(0xB6)] public bool AllSolid;
}

[StructLayout(LayoutKind.Sequential)]
public struct Line_t
{
    public Vector3 StartOffset;
    public float Radius;
}

public enum RayType_t : byte
{
    RAY_TYPE_LINE = 0,
    RAY_TYPE_SPHERE,
    RAY_TYPE_HULL,
    RAY_TYPE_CAPSULE,
    RAY_TYPE_MESH,
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct Ray_t
{
    [FieldOffset(0)] public Line_t m_Line;
    [FieldOffset(40)] public RayType_t m_eType;

    public Ray_t()
    {
        this = default;
        m_eType = RayType_t.RAY_TYPE_LINE;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 72)]
public unsafe struct CTraceFilter
{
    public CTraceFilter(uint entityIdToIgnore)
    {
        Vtable = null;

        m_nObjectSetMask = 7;
        m_nCollisionGroup = 4;
        m_nBits = 0b01000001;
        m_nInteractsAs = 0x40000;
        m_nInteractsWith = 0x2c3011;
        m_nEntityIdsToIgnore[0] = entityIdToIgnore;
        m_nEntityIdsToIgnore[1] = 0xFFFFFFFF;
        m_nOwnerIdsToIgnore[0] = 0xFFFFFFFF;
        m_nOwnerIdsToIgnore[1] = 0xFFFFFFFF;
    }

    [FieldOffset(0x00)] internal void* Vtable;
    [FieldOffset(0x08)] public ulong m_nInteractsWith;
    [FieldOffset(0x10)] public ulong m_nInteractsExclude;
    [FieldOffset(0x18)] public ulong m_nInteractsAs;
    [FieldOffset(0x20)] public fixed uint m_nOwnerIdsToIgnore[2];
    [FieldOffset(0x28)] public fixed uint m_nEntityIdsToIgnore[2];
    [FieldOffset(0x30)] public fixed ushort m_nHierarchyIds[2];
    [FieldOffset(0x34)] public byte m_nObjectSetMask;
    [FieldOffset(0x35)] public byte m_nCollisionGroup;
    [FieldOffset(0x36)] public byte m_nBits;
    [FieldOffset(0x37)] public bool m_bHitEntities;
    [FieldOffset(0x38)] public bool m_bHitTriggers;
    [FieldOffset(0x39)] public bool m_bTestHitboxes;
    [FieldOffset(0x3A)] public bool m_bTraceComplexEntities;
    [FieldOffset(0x3B)] public bool m_bOnlyHitIfHasPhysics;
    [FieldOffset(0x3C)] public bool m_bIterateEntities;
}

// Extension methods
public static class RayTraceExtensions
{
    public static CCSPlayerPawn? GetPlayerPawn(this CBaseEntity entity)
    {
        if (entity == null || !entity.IsValid) return null;
        return entity.As<CCSPlayerPawn>();
    }

    public static Vector GetEyePosition(this CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.PlayerPawn.Value == null)
            return new Vector(0, 0, 0);

        var pawn = player.PlayerPawn.Value;
        float eyeHeight = (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 ? 46.0f : 64.0f;

        return new Vector(
            pawn.AbsOrigin!.X,
            pawn.AbsOrigin.Y,
            pawn.AbsOrigin.Z + eyeHeight
        );
    }

    public static bool CheckValid(this CCSPlayerController player, bool checkAlive = false)
    {
        if (player == null || !player.IsValid || player.IsBot)
            return false;

        if (checkAlive && player.PawnIsAlive != true)
            return false;

        return true;
    }
}