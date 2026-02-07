using System;
using System.Numerics;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

// Ray tracing utilities (v2.0.0) - CS2 güncel GameTraceManager yaklaşımı

namespace RayTrace;

// ============================================================
// TraceMask - Yaygın çarpışma senaryoları için önceden tanımlanmış maskeler
// ============================================================
[Flags]
public enum TraceMask : ulong
{
    /// Her şeyle eşleşir
    MaskAll = ~0ul,

    /// Normalde katı olan her şey (duvarlar, pencereler, oyuncular, NPC'ler)
    MaskSolid = Contents.Solid | Contents.Window | Contents.Player | Contents.Npc | Contents.PassBullets,

    /// Sadece fırça geometrisi (duvarlar) - oyuncular/entity'ler hariç
    MaskSolidBrushOnly = Contents.Solid | Contents.Window | Contents.PassBullets,

    /// Oyuncu hareketi engelleri
    MaskPlayerSolid = Contents.Solid | Contents.PlayerClip | Contents.Window | Contents.Player | Contents.Npc | Contents.PassBullets,

    /// Mermi çarpışması (hitbox dahil)
    MaskShot = Contents.Solid | Contents.Player | Contents.Npc | Contents.Window | Contents.Debris | Contents.Hitbox,

    /// Mermi çarpışması sadece fırça geometrisi
    MaskShotBrushOnly = Contents.Solid | Contents.Window | Contents.Debris,

    /// Mermi çarpışması hull bazlı
    MaskShotHull = Contents.Solid | Contents.Player | Contents.Npc | Contents.Window | Contents.Debris | Contents.PassBullets,

    /// LOS (görüş hattı) kontrolü - duvarlar ve engeller
    MaskVisible = Contents.Solid | Contents.Window | Contents.WorldGeometry,
}

// ============================================================
// Contents - İçerik/katman bayrakları
// ============================================================
[Flags]
public enum Contents : ulong
{
    Empty       = 0,
    Solid       = 0x1,
    Hitbox      = 0x2,
    Trigger     = 0x4,
    Sky         = 0x8,
    PlayerClip  = 0x10,
    NpcClip     = 0x20,
    BlockLos    = 0x40,
    BlockLight  = 0x80,
    Ladder      = 0x100,
    Pickup      = 0x200,
    BlockSound  = 0x400,
    NoDraw      = 0x800,
    Window      = 0x1000,
    PassBullets = 0x2000,
    WorldGeometry = 0x4000,
    Water       = 0x8000,
    Slime       = 0x10000,
    TouchAll    = 0x20000,
    Player      = 0x40000,
    Npc         = 0x80000,
    Debris      = 0x100000,
    PhysicsProp = 0x200000,
    NavIgnore   = 0x400000,
    NavLocalIgnore = 0x800000,
    PostProcessingVolume = 0x1000000,
    CarriedObject = 0x4000000,
    Pushaway    = 0x8000000,
    ServerEntityOnClient = 0x10000000,
    CarriedWeapon = 0x20000000,
    StaticLevel = 0x40000000,
}

// ============================================================
// RayType - Işın tipi
// ============================================================
public enum RayType : byte
{
    Line = 0,
    Sphere,
    Hull,
    Capsule,
    Mesh,
}

// ============================================================
// Line - Çizgi şekli
// ============================================================
[StructLayout(LayoutKind.Sequential)]
public struct Line
{
    public Vector3 StartOffset;
    public float Radius;
}

// ============================================================
// Ray - Işın yapısı (union şeklinde)
// ============================================================
[StructLayout(LayoutKind.Explicit)]
public struct Ray
{
    [FieldOffset(0)] public Line Line;
    [FieldOffset(40)] public RayType Type;

    /// Basit çizgi ışını oluşturur
    public Ray(Vector3 startOffset)
    {
        this = default;
        Line = new Line { StartOffset = startOffset, Radius = 0f };
        Type = RayType.Line;
    }
}

// ============================================================
// CTraceHitbox - Hitbox verisi
// ============================================================
[StructLayout(LayoutKind.Explicit, Size = 0x44)]
public unsafe struct CTraceHitbox
{
    [FieldOffset(0x38)] public int HitGroup;
    [FieldOffset(0x40)] public int HitboxId;
}

// ============================================================
// CGameTrace - Trace sonucu (CS2 güncel offset'ler)
// ============================================================
[StructLayout(LayoutKind.Explicit, Size = 0xB8)]
public unsafe struct CGameTrace
{
    [FieldOffset(0x00)] public IntPtr Surface;
    [FieldOffset(0x08)] public IntPtr HitEntity;
    [FieldOffset(0x10)] public CTraceHitbox* HitboxData;
    [FieldOffset(0x50)] public uint Contents;
    [FieldOffset(0x78)] public Vector3 StartPos;
    [FieldOffset(0x84)] public Vector3 EndPos;
    [FieldOffset(0x90)] public Vector3 Normal;
    [FieldOffset(0x9C)] public Vector3 Position;
    [FieldOffset(0xAC)] public float Fraction;
    [FieldOffset(0xB6)] public bool AllSolid;
}

// ============================================================
// CTraceFilter - Trace filtresi (boolean flag'ler düzeltildi)
// ============================================================
[StructLayout(LayoutKind.Explicit, Size = 72)]
public unsafe struct CTraceFilter
{
    public CTraceFilter(uint entityIdToIgnore, uint ownerId = 0xFFFFFFFF, ushort hierarchyId = 0xFFFF)
    {
        Vtable = null;

        m_nInteractsWith = 0;
        m_nInteractsExclude = 0x20311;
        m_nInteractsAs = 0x40000;

        m_nOwnerIdsToIgnore[0] = ownerId;
        m_nOwnerIdsToIgnore[1] = 0xFFFFFFFF;

        m_nEntityIdsToIgnore[0] = entityIdToIgnore;
        m_nEntityIdsToIgnore[1] = 0xFFFFFFFF;

        m_nHierarchyIds[0] = hierarchyId;
        m_nHierarchyIds[1] = 0xFFFF;

        m_nObjectSetMask = 7;
        m_nCollisionGroup = 4;
        m_nBits = 0b01000001;

        // KRİTİK: Bu flag'ler mutlaka true olmalı
        m_bHitEntities = true;
        m_bHitTriggers = true;
        m_bTestHitboxes = true;
        m_bTraceComplexEntities = false;
        m_bOnlyHitIfHasPhysics = false;
        m_bIterateEntities = true;
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

// ============================================================
// Address - Adres yardımcı sınıfı
// ============================================================
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

// ============================================================
// RayTrace - Ana trace sınıfı (CS2 güncel yaklaşım)
// ============================================================
public static class RayTrace
{
    private static IntPtr CTraceFilterVtable;
    private static IntPtr GameTraceManager;
    private static TraceShapeDelegate? _traceShape;
    private static TraceShapeRayFilterDelegate? _traceShapeRayFilter;

    /// <summary>
    /// RayTrace başarıyla başlatıldı mı?
    /// false ise duvar kontrolü devre dışı kalır (hedefler engellenmez).
    /// </summary>
    public static bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// Başlatma sırasında oluşan hata mesajı (varsa)
    /// </summary>
    public static string? InitError { get; private set; } = null;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate bool TraceShapeDelegate(
        IntPtr GameTraceManager,
        IntPtr vecStart,
        IntPtr vecEnd,
        IntPtr skip,
        ulong mask,
        ulong content,
        CGameTrace* pGameTrace
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate bool TraceShapeRayFilterDelegate(
        IntPtr GameTraceManager,
        Ray* trace,
        IntPtr vecStart,
        IntPtr vecEnd,
        CTraceFilter* traceFilter,
        CGameTrace* pGameTrace
    );

    // İmzalar doğrudan kodda gömülü - harici gamedata dosyasına gerek yok
    // CS2 güncellemesi sonrası imzalar değişirse burayı güncelle
    private const string SIG_WIN_TRACEFUNC = "4C 8B DC 49 89 5B ? 49 89 6B ? 49 89 73 ? 57 41 56 41 57 48 81 EC";
    private const string SIG_WIN_TRACESHAPE = "48 89 5C 24 ? 48 89 4C 24 ? 55 57";
    private const string SIG_WIN_CTRACEFILTERVTABLE = "4C 8D 2D ? ? ? ? 24";
    private const string SIG_WIN_GAMETRACEMANAGER = "48 8B 0D ? ? ? ? 0C";

    private const string SIG_LINUX_TRACEFUNC = "48 B8 ? ? ? ? ? ? ? ? 55 66 0F EF C0 48 89 E5 41 57 41 56 49 89 D6";
    private const string SIG_LINUX_TRACESHAPE = "55 48 89 E5 41 57 49 89 CF 41 56 49 89 F6 41 55 4D 89 C5 41 54 49 89 D4 53 4C 89 CB";
    private const string SIG_LINUX_CTRACEFILTERVTABLE = "48 8D 0D ? ? ? ? 66 89 95";
    private const string SIG_LINUX_GAMETRACEMANAGER = "4C 8D 1D ? ? ? ? BB";

    private static string GetSignature(string name)
    {
        // Önce gamedata'dan dene (kullanıcı güncel imza sağlamış olabilir)
        try
        {
            string sig = GameData.GetSignature(name);
            if (!string.IsNullOrEmpty(sig))
            {
                Console.WriteLine($"[RayTrace] {name}: gamedata'dan yuklendi.");
                return sig;
            }
        }
        catch { /* gamedata yoksa gömülü imzaları kullan */ }

        // Gömülü imzaları kullan
        bool isWindows = Addresses.ServerPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        string embedded = name switch
        {
            "TraceFunc" => isWindows ? SIG_WIN_TRACEFUNC : SIG_LINUX_TRACEFUNC,
            "TraceShape" => isWindows ? SIG_WIN_TRACESHAPE : SIG_LINUX_TRACESHAPE,
            "CTraceFilterVtable" => isWindows ? SIG_WIN_CTRACEFILTERVTABLE : SIG_LINUX_CTRACEFILTERVTABLE,
            "GameTraceManager" => isWindows ? SIG_WIN_GAMETRACEMANAGER : SIG_LINUX_GAMETRACEMANAGER,
            _ => throw new Exception($"Bilinmeyen imza: {name}")
        };

        Console.WriteLine($"[RayTrace] {name}: gomulu imza kullaniliyor ({(isWindows ? "Windows" : "Linux")}).");
        return embedded;
    }

    static RayTrace()
    {
        try
        {
            string serverPath = Addresses.ServerPath;

            IntPtr traceFunc = NativeAPI.FindSignature(serverPath, GetSignature("TraceFunc"));
            IntPtr traceShape = NativeAPI.FindSignature(serverPath, GetSignature("TraceShape"));
            CTraceFilterVtable = NativeAPI.FindSignature(serverPath, GetSignature("CTraceFilterVtable"));
            GameTraceManager = NativeAPI.FindSignature(serverPath, GetSignature("GameTraceManager"));

            // İmza kontrolü
            if (traceFunc == IntPtr.Zero) throw new Exception("TraceFunc signature bulunamadi! CS2 guncellenmis olabilir.");
            if (traceShape == IntPtr.Zero) throw new Exception("TraceShape signature bulunamadi! CS2 guncellenmis olabilir.");
            if (CTraceFilterVtable == IntPtr.Zero) throw new Exception("CTraceFilterVtable signature bulunamadi! CS2 guncellenmis olabilir.");
            if (GameTraceManager == IntPtr.Zero) throw new Exception("GameTraceManager signature bulunamadi! CS2 guncellenmis olabilir.");

            _traceShape = Marshal.GetDelegateForFunctionPointer<TraceShapeDelegate>(traceFunc);
            _traceShapeRayFilter = Marshal.GetDelegateForFunctionPointer<TraceShapeRayFilterDelegate>(traceShape);

            IsInitialized = true;
            Console.WriteLine("[RayTrace] Basariyla baslatildi. Tum imzalar bulundu.");
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            InitError = ex.Message;
            Console.WriteLine($"[RayTrace] HATA: Baslatilamadi! {ex.Message}");
            Console.WriteLine("[RayTrace] Duvar kontrolu devre disi kalacak.");
        }
    }

    // ==========================================================
    // Basit Trace API (mask + content + skip)
    // Duvar kontrolü gibi basit senaryolar için ideal
    // ==========================================================

    /// <summary>
    /// Başlangıç noktasından bitiş noktasına basit ışın trace'i yapar.
    /// Duvar/görünürlük kontrolü için idealdir.
    /// IsInitialized false ise null döner.
    /// </summary>
    public static unsafe CGameTrace? TraceShape(Vector start, Vector end, ulong mask, ulong content, IntPtr skip)
    {
        if (!IsInitialized || _traceShape == null)
            return null;

        CGameTrace* trace = stackalloc CGameTrace[1];
        IntPtr gameTraceManagerAddress = Address.GetAbsoluteAddress(GameTraceManager, 3, 7);

        _traceShape(*(IntPtr*)gameTraceManagerAddress, start.Handle, end.Handle, skip, mask, content, trace);

        return *trace;
    }

    /// <summary>
    /// TraceMask ve Contents enum'ları ile trace yapar
    /// </summary>
    public static CGameTrace? TraceShape(Vector start, Vector end, TraceMask mask, Contents content, IntPtr skip)
    {
        return TraceShape(start, end, (ulong)mask, (ulong)content, skip);
    }

    /// <summary>
    /// Oyuncu controller'ını skip ederek trace yapar
    /// </summary>
    public static CGameTrace? TraceShape(Vector start, Vector end, TraceMask mask, Contents content, CCSPlayerController skipPlayer)
    {
        IntPtr skip = skipPlayer?.PlayerPawn.Value?.Handle ?? IntPtr.Zero;
        return TraceShape(start, end, (ulong)mask, (ulong)content, skip);
    }

    /// <summary>
    /// Oyuncu pawn'ını skip ederek trace yapar
    /// </summary>
    public static CGameTrace? TraceShape(Vector start, Vector end, TraceMask mask, Contents content, CCSPlayerPawn skipPawn)
    {
        return TraceShape(start, end, (ulong)mask, (ulong)content, skipPawn.Handle);
    }

    /// <summary>
    /// Açı bazlı trace - forward vektörünü otomatik hesaplar (8192 birim mesafe)
    /// </summary>
    public static CGameTrace? TraceShape(Vector origin, QAngle angle, ulong mask, ulong content, IntPtr skip)
    {
        Vector forward = new();
        NativeAPI.AngleVectors(angle.Handle, forward.Handle, 0, 0);
        Vector endOrigin = new(origin.X + forward.X * 8192, origin.Y + forward.Y * 8192, origin.Z + forward.Z * 8192);

        return TraceShape(origin, endOrigin, mask, content, skip);
    }

    /// <summary>
    /// Açı bazlı trace - TraceMask ve Contents enum'ları ile
    /// </summary>
    public static CGameTrace? TraceShape(Vector origin, QAngle angle, TraceMask mask, Contents content, IntPtr skip)
    {
        return TraceShape(origin, angle, (ulong)mask, (ulong)content, skip);
    }

    /// <summary>
    /// Açı bazlı trace - oyuncu controller skip ile
    /// </summary>
    public static CGameTrace? TraceShape(Vector origin, QAngle angle, TraceMask mask, Contents content, CCSPlayerController skipPlayer)
    {
        IntPtr skip = skipPlayer?.PlayerPawn.Value?.Handle ?? IntPtr.Zero;
        return TraceShape(origin, angle, (ulong)mask, (ulong)content, skip);
    }

    // ==========================================================
    // Duvar/Görünürlük Kontrolü API
    // Penetrasyon trace: func_ entity'lerden geçer, sadece dünya BSP duvarlarını algılar
    // ==========================================================

    // İkisi de WorldGeometry - bu kombinasyon duvarları algıladığı KANITLANDI
    private static readonly ulong WallCheckMask = (ulong)Contents.WorldGeometry;
    private static readonly ulong WallCheckIdentity = (ulong)Contents.WorldGeometry;
    private const int MaxPenetrations = 5; // Maksimum kaç entity'den geçebilir

    /// <summary>
    /// Entity geçirgen mi kontrol eder (func_, trigger_, player, info_, prop_ → geçer)
    /// </summary>
    private static bool IsPassthroughEntity(IntPtr hitEntity)
    {
        if (hitEntity == IntPtr.Zero) return false; // Dünya BSP → geçirmez (gerçek duvar)

        try
        {
            var entity = new CEntityInstance(hitEntity);
            if (entity == null || !entity.IsValid) return false;

            string name = entity.DesignerName ?? "";
            if (string.IsNullOrEmpty(name)) return false;

            // Bu entity'lerden ışın geçer:
            if (name.StartsWith("func_"))    return true; // func_brush, func_buyzone, func_breakable vb.
            if (name.StartsWith("trigger_")) return true; // trigger_push, trigger_teleport vb.
            if (name.StartsWith("player"))   return true; // player, player_controller
            if (name.StartsWith("info_"))    return true; // info_buyzone, info_bomb_target vb.
            if (name.StartsWith("prop_"))    return true; // prop_dynamic, prop_physics vb.
            if (name == "cs_player_controller") return true;
        }
        catch { }

        return false; // Bilinmeyen entity → geçirmez
    }

    /// <summary>
    /// Duvar kontrolü - penetrasyon trace.
    /// Işın func_ entity'lere çarptığında durmaz, arkasına geçip devam eder.
    /// Sadece gerçek dünya BSP duvarına çarptığında durur.
    /// 
    /// null döner = duvar yok (yol açık veya sadece entity'ler var)
    /// CGameTrace döner = gerçek duvar bulundu
    /// </summary>
    public static CGameTrace? TraceWall(Vector start, Vector end, IntPtr skip)
    {
        float sx = start.X, sy = start.Y, sz = start.Z;
        float ex = end.X, ey = end.Y, ez = end.Z;

        // Toplam mesafedeki fraction'ı takip et
        float totalFractionUsed = 0f;

        for (int i = 0; i < MaxPenetrations; i++)
        {
            var traceStart = new Vector(
                sx + (ex - sx) * totalFractionUsed,
                sy + (ey - sy) * totalFractionUsed,
                sz + (ez - sz) * totalFractionUsed
            );

            var result = TraceShape(traceStart, end, WallCheckMask, WallCheckIdentity, skip);
            if (!result.HasValue) return null; // Trace başarısız

            var trace = result.Value;

            // Tamamen katı → gerçek duvar
            if (trace.AllSolid) return result;

            // Hiçbir şeye çarpmadı → yol açık
            if (trace.Fraction >= 1.0f) return null;

            // Bir şeye çarptı - entity mi kontrol et
            if (IsPassthroughEntity(trace.HitEntity))
            {
                // Entity'den geçir: çarpma noktasının biraz ilerisinden devam et
                float remainingFraction = 1.0f - totalFractionUsed;
                totalFractionUsed += trace.Fraction * remainingFraction + 0.01f;

                if (totalFractionUsed >= 0.99f)
                    return null; // Hedefe ulaştık, gerçek duvar yok

                continue; // Tekrar dene
            }

            // Dünya BSP veya bilinmeyen entity → gerçek duvar
            return result;
        }

        // Tüm iterasyonlar tükendi, gerçek duvar bulunamadı
        return null;
    }

    /// <summary>
    /// Oyuncu pawn'ı atlayarak duvar kontrolü trace'i yapar
    /// </summary>
    public static CGameTrace? TraceWall(Vector start, Vector end, CCSPlayerPawn skipPawn)
    {
        return TraceWall(start, end, skipPawn.Handle);
    }

    /// <summary>
    /// Oyuncu controller atlayarak duvar kontrolü trace'i yapar
    /// </summary>
    public static CGameTrace? TraceWall(Vector start, Vector end, CCSPlayerController skipPlayer)
    {
        IntPtr skip = skipPlayer?.PlayerPawn.Value?.Handle ?? IntPtr.Zero;
        return TraceWall(start, end, skip);
    }

    // ==========================================================
    // Gelişmiş Trace API (CTraceFilter + Ray ile)
    // Hull trace, hitbox kontrolü gibi karmaşık senaryolar için
    // ==========================================================

    /// <summary>
    /// CTraceFilter ve Ray ile gelişmiş hull/shape trace yapar
    /// IsInitialized false ise null döner.
    /// </summary>
    public static unsafe CGameTrace? TraceHull(Vector start, Vector end, CTraceFilter filter, Ray ray)
    {
        if (!IsInitialized || _traceShapeRayFilter == null)
            return null;

        CGameTrace* trace = stackalloc CGameTrace[1];
        CTraceFilter* filterPtr = stackalloc CTraceFilter[1];

        IntPtr vtable = Address.GetAbsoluteAddress(CTraceFilterVtable, 3, 7);
        IntPtr gameTraceManager = Address.GetAbsoluteAddress(GameTraceManager, 3, 7);

        *filterPtr = filter;
        filterPtr->Vtable = (void*)vtable;

        _traceShapeRayFilter(*(nint*)gameTraceManager, &ray, start.Handle, end.Handle, filterPtr, trace);

        return *trace;
    }
}

// ============================================================
// Extension metotlar
// ============================================================
public static class RayTraceExtensions
{
    /// <summary>
    /// Trace sonucundaki mesafeyi hesaplar
    /// </summary>
    public static float Distance(this CGameTrace trace)
    {
        var diff = trace.EndPos - trace.StartPos;
        return MathF.Sqrt(diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z);
    }

    /// <summary>
    /// Trace sonucundaki hit entity'den oyuncu pawn'ı almaya çalışır
    /// </summary>
    public static bool HitPlayer(this CGameTrace trace, out CCSPlayerController? player)
    {
        player = null;
        if (trace.HitEntity == IntPtr.Zero) return false;

        try
        {
            var entity = new CEntityInstance(trace.HitEntity);
            if (entity == null || !entity.IsValid) return false;
            if (entity.DesignerName != "player") return false;

            var pawn = new CCSPlayerPawn(trace.HitEntity);
            if (pawn == null || !pawn.IsValid) return false;

            player = pawn.Controller.Value?.As<CCSPlayerController>();
            return player != null && player.IsValid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Oyuncunun göz pozisyonunu hesaplar (ViewOffset kullanarak)
    /// </summary>
    public static Vector? GetEyePosition(this CCSPlayerController player)
    {
        return player.PlayerPawn.Value?.GetEyePosition();
    }

    /// <summary>
    /// Pawn'ın göz pozisyonunu hesaplar
    /// </summary>
    public static Vector? GetEyePosition(this CCSPlayerPawn playerPawn)
    {
        if (playerPawn.AbsOrigin is not { } absOrigin) return null;
        return new Vector(absOrigin.X, absOrigin.Y, absOrigin.Z + playerPawn.ViewOffset.Z);
    }

    /// <summary>
    /// Oyuncu doğrulama kontrolü
    /// </summary>
    public static bool CheckValid(this CCSPlayerController player, bool checkAlive = false)
    {
        if (player == null || !player.IsValid || player.IsBot)
            return false;

        if (checkAlive && player.PawnIsAlive != true)
            return false;

        return true;
    }
}
