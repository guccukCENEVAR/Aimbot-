using System.Numerics;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

// ============================================================
// FUNPLAY Ray-Trace Metamod Modülü C# Wrapper
// https://github.com/FUNPLAY-pro-CS2/Ray-Trace
// 
// Bu dosya FUNPLAY'in Metamod C++ modülüne bağlanır.
// Modül engine seviyesinde trace yapar - signature scanning GEREKMEZ.
// ============================================================

namespace RayTrace;

// ============================================================
// InteractionLayers - CS2 çarpışma katmanları
// ============================================================
[Flags]
public enum InteractionLayers : ulong
{
    Solid                   = 0x1,
    Hitboxes                = 0x2,
    Trigger                 = 0x4,
    Sky                     = 0x8,
    PlayerClip              = 0x10,
    NPCClip                 = 0x20,
    BlockLOS                = 0x40,
    BlockLight              = 0x80,
    Ladder                  = 0x100,
    Pickup                  = 0x200,
    BlockSound              = 0x400,
    NoDraw                  = 0x800,
    Window                  = 0x1000,
    PassBullets             = 0x2000,
    WorldGeometry           = 0x4000,
    Water                   = 0x8000,
    Slime                   = 0x10000,
    TouchAll                = 0x20000,
    Player                  = 0x40000,
    NPC                     = 0x80000,
    Debris                  = 0x100000,
    Physics_Prop            = 0x200000,
    NavIgnore               = 0x400000,
    NavLocalIgnore          = 0x800000,
    PostProcessingVolume    = 0x1000000,
    CarriedObject           = 0x4000000,
    PushAway                = 0x8000000,
    ServerEntityOnClient    = 0x10000000,
    CarriedWeapon           = 0x20000000,
    StaticLevel             = 0x40000000,

    // Hazır maskeler (resmi FUNPLAY örneğiyle uyumlu)
    MASK_SHOT_PHYSICS = Solid | PlayerClip | Window | PassBullets | Player | NPC | Physics_Prop,
    MASK_SHOT_HITBOX  = Hitboxes | Player | NPC,
    MASK_SHOT_FULL    = MASK_SHOT_PHYSICS | Hitboxes,
    MASK_WORLD_ONLY   = Solid | Window | PassBullets,
    MASK_BRUSH_ONLY   = Solid | Window,

    // Duvar kontrolü: MASK_SHOT_PHYSICS kullanılır (Windows/Linux uyumlu)
    // Player/NPC filtrelemesi C# SingleTrace'de yapılır, InteractsExclude değil
    MASK_WALL_CHECK   = MASK_SHOT_PHYSICS,
}

// ============================================================
// TraceOptions - Trace seçenekleri (C++ struct ile uyumlu)
// ============================================================
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct TraceOptions
{
    [FieldOffset(0)]  public ulong InteractsAs;
    [FieldOffset(8)]  public ulong InteractsWith;
    [FieldOffset(16)] public ulong InteractsExclude;
    [FieldOffset(24)] public int   DrawBeam;

    /// Duvar kontrolü: MASK_SHOT_PHYSICS ile her şeyi algılar
    /// Player/entity filtrelemesi C# tarafında yapılır (SingleTrace)
    /// Bu yaklaşım resmi FUNPLAY örneğiyle uyumludur ve Windows/Linux'ta çalışır
    public static TraceOptions WallCheck => new()
    {
        InteractsAs = 0,
        InteractsWith = (ulong)InteractionLayers.MASK_SHOT_PHYSICS,
        InteractsExclude = 0,
        DrawBeam = 0
    };

    /// Duvar kontrolü + debug ışın (tracetest komutu için)
    public static TraceOptions WallCheckDebug => new()
    {
        InteractsAs = 0,
        InteractsWith = (ulong)InteractionLayers.MASK_SHOT_PHYSICS,
        InteractsExclude = 0,
        DrawBeam = 1
    };

    /// Tam mermi trace (hitbox dahil)
    public static TraceOptions ShotFull => new()
    {
        InteractsAs = 0,
        InteractsWith = (ulong)InteractionLayers.MASK_SHOT_FULL,
        InteractsExclude = 0,
        DrawBeam = 0
    };
}

// ============================================================
// TraceResult - Trace sonucu (C++ struct ile uyumlu)
// ============================================================
[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct TraceResult
{
    [FieldOffset(0)]  public float EndPosX;
    [FieldOffset(4)]  public float EndPosY;
    [FieldOffset(8)]  public float EndPosZ;
    [FieldOffset(16)] public nint  HitEntity;
    [FieldOffset(24)] public float Fraction;
    [FieldOffset(28)] public int   AllSolid;
    [FieldOffset(32)] public float NormalX;
    [FieldOffset(36)] public float NormalY;
    [FieldOffset(40)] public float NormalZ;

    public bool DidHit => Fraction < 1.0f;
    public bool IsAllSolid => AllSolid != 0;
}

// ============================================================
// CRayTrace - Ana trace sınıfı (Metamod modülüne bağlanır)
// ============================================================
public static class RayTrace
{
    private static nint _handle = nint.Zero;
    private static bool _loaded = false;

    // VTable fonksiyon delegate'leri
    private static Func<nint, nint, nint, nint, nint, nint, bool>? _traceShape;
    private static Func<nint, nint, nint, nint, nint, nint, bool>? _traceEndShape;
    private static Func<nint, nint, nint, nint, nint, nint, nint, nint, bool>? _traceHullShape;

    public static bool IsInitialized => _loaded;
    public static string? InitError { get; private set; }
    private static int _initRetryCount = 0;
    private static bool _gaveUp = false;

    /// <summary>
    /// FUNPLAY Ray-Trace Metamod modülüne bağlan.
    /// Lazy init: OnTick'te ve komutlarda otomatik çağrılır.
    /// Limit yok - bağlanana kadar sessizce dener.
    /// </summary>
    public static void Initialize()
    {
        if (_loaded) return;
        if (_gaveUp) return;

        _initRetryCount++;

        try
        {
            object? factory = null;
            
            try
            {
                factory = Utilities.MetaFactory("CRayTraceInterface001");
            }
            catch (Exception ex)
            {
                _gaveUp = true;
                InitError = $"MetaFactory exception: {ex.Message}";
                return;
            }

            if (factory == null)
            {
                InitError = $"MetaFactory null (deneme {_initRetryCount})";
                return;
            }

            _handle = (nint)factory;

            if (_handle == nint.Zero)
            {
                _gaveUp = true;
                InitError = "CRayTraceInterface001 handle sifir.";
                return;
            }

            BindVTable();
            _loaded = true;
            InitError = null;
        }
        catch (Exception ex)
        {
            _loaded = false;
            _handle = nint.Zero;
            InitError = ex.Message;
            _gaveUp = true;
        }
    }

    private static void BindVTable()
    {
        // VTable index'leri platform'a göre değişir (C++ ABI farkı)
        int traceShapeIdx, traceEndShapeIdx, traceHullShapeIdx;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            traceShapeIdx = 1;
            traceEndShapeIdx = 2;
            traceHullShapeIdx = 3;
        }
        else // Linux (Itanium ABI)
        {
            traceShapeIdx = 2;
            traceEndShapeIdx = 3;
            traceHullShapeIdx = 4;
        }

        _traceShape = VirtualFunction.Create<nint, nint, nint, nint, nint, nint, bool>(_handle, traceShapeIdx);
        _traceEndShape = VirtualFunction.Create<nint, nint, nint, nint, nint, nint, bool>(_handle, traceEndShapeIdx);
        _traceHullShape = VirtualFunction.Create<nint, nint, nint, nint, nint, nint, nint, nint, bool>(_handle, traceHullShapeIdx);
    }

    /// <summary>
    /// Başlangıç noktasından bitiş noktasına ışın trace'i yapar.
    /// </summary>
    public static unsafe bool TraceEndShape(
        Vector origin, Vector endOrigin, 
        CBaseEntity? ignoreEntity, 
        TraceOptions options, 
        out TraceResult result)
    {
        result = default;
        if (!_loaded) Initialize();
        if (!_loaded || _traceEndShape == null) return false;

        TraceResult resultBuffer = default;
        TraceOptions optionsBuffer = options;

        bool success = _traceEndShape(
            _handle,
            origin.Handle,
            endOrigin.Handle,
            ignoreEntity?.Handle ?? nint.Zero,
            (nint)(&optionsBuffer),
            (nint)(&resultBuffer));

        result = resultBuffer;
        return success;
    }

    /// <summary>
    /// Açı bazlı trace (forward vektörü otomatik hesaplanır, 8192 birim mesafe).
    /// </summary>
    public static unsafe bool TraceShape(
        Vector origin, QAngle angles,
        CBaseEntity? ignoreEntity,
        TraceOptions options,
        out TraceResult result)
    {
        result = default;
        if (!_loaded) Initialize();
        if (!_loaded || _traceShape == null) return false;

        TraceResult resultBuffer = default;
        TraceOptions optionsBuffer = options;

        bool success = _traceShape(
            _handle,
            origin.Handle,
            angles.Handle,
            ignoreEntity?.Handle ?? nint.Zero,
            (nint)(&optionsBuffer),
            (nint)(&resultBuffer));

        result = resultBuffer;
        return success;
    }

    // ============================================================
    // Aimbot uyumlu API (Aimbot.cs'nin kullandığı fonksiyonlar)
    // ============================================================

    /// <summary>
    /// Duvar kontrolü trace'i. Sadece gerçek dünya duvarlarını algılar.
    /// Oyuncular, trigger, spawn bariyeri, cam, buyzone → geçer.
    /// 
    /// return: TraceResult (Fraction &lt; 0.97 = duvar var)
    /// </summary>
    public static bool TraceWall(Vector start, Vector end, IntPtr skipHandle, out TraceResult result)
    {
        result = default;
        
        // Lazy init: modül henüz bağlanmadıysa tekrar dene
        if (!_loaded) Initialize();
        if (!_loaded) return false;

        CBaseEntity? skipEntity = null;
        if (skipHandle != IntPtr.Zero)
        {
            try { skipEntity = new CBaseEntity(skipHandle); }
            catch { }
        }

        var options = TraceOptions.WallCheck;
        return TraceEndShape(start, end, skipEntity, options, out result);
    }

    /// <summary>
    /// Debug versiyonu - beam çizer (tracetest komutu için)
    /// </summary>
    public static bool TraceWallDebug(Vector start, Vector end, IntPtr skipHandle, out TraceResult result)
    {
        result = default;
        
        if (!_loaded) Initialize();
        if (!_loaded) return false;

        CBaseEntity? skipEntity = null;
        if (skipHandle != IntPtr.Zero)
        {
            try { skipEntity = new CBaseEntity(skipHandle); }
            catch { }
        }

        var options = TraceOptions.WallCheckDebug;
        return TraceEndShape(start, end, skipEntity, options, out result);
    }
}
