using System.Globalization;
using System.Text.Json;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>The optics that can be fitted at a gun locker (or on the raid loadout). Standard: the gun's own sights.</summary>
public enum OpticKind { Standard, RedDot, Holo, Prism4x, Acog6x, Scope2to8, Scope4to16 }

/// <summary>How a gun's sight is looked through: its kind, its power to start with, and the powers the wheel turns it through.</summary>
public sealed record OpticView(ScopeKind Kind, float Zoom, float[]? Steps = null);

/// <summary>An optic from the library (Assets/Models/optics.glb, Tools/blender/build_optics.py): what it's called and how it's seen through.</summary>
public sealed record OpticInfo(OpticKind Kind, string Name, string Note, string Part, OpticView View)
{
    /// <summary>Its line of sight above the rail it clamps to, and its length from the eyepiece's end (metres).</summary>
    public float Axis { get; set; }
    public float Length { get; set; }
}

/// <summary>
/// Where optics go on one gun (model space, metres): the top of its rail (or of the riser block on a gun with none, which
/// then fills down to <see cref="BaseY"/>), where a red dot's back end sits and where a magnified optic's eyepiece does,
/// and the optics it can take besides its own sights.
/// </summary>
public sealed record GunMount(float RailY, float DotEye, float ScopeEye, OpticKind[] Allowed)
{
    /// <summary>A gun with no rail: the riser block's bottom, and how far along the gun it can reach (null: a rail).</summary>
    public float? BaseY { get; init; }
    public float2 BlockRange { get; init; } = new(-10f, 10f);

    /// <summary>Its own sights count as this optic (the L119A1's red dot): that one isn't offered twice.</summary>
    public OpticKind StandardIs { get; init; } = OpticKind.Standard;

    /// <summary>The library part its red dot is (the pistol's is a slide-mounted mini dot).</summary>
    public string? DotPart { get; init; }
}

/// <summary>An optic fitted to a gun: what it is, which part of the library is drawn, and where (model space).</summary>
public sealed record FittedOptic(OpticInfo Info, ModelPart[] Parts, float4x4 Placement, float4x4? Block, ModelPart[] BlockParts)
{
    public OpticView View => Info.View;

    /// <summary>The holographic sight's magnifier: swung aside on its mount at 1x (<see cref="MagnifierAside"/>), in line magnified.</summary>
    public static bool IsMagnifier(ModelPart part) => part.Name.EndsWith("Magnifier", StringComparison.Ordinal);

    /// <summary>The magnifier swung out to the right about its mount's hinge (optic space: the eyepiece's end at x = 0, the rail's top at y = 0).</summary>
    public static readonly float4x4 MagnifierAside = float4x4.CreateTranslation(0f, -0.010f, -0.022f) * float4x4.CreateRotationX(float.Pi / 2f)
        * float4x4.CreateTranslation(0f, 0.010f, 0.022f);

    /// <summary>Where a part of it goes (gun space), its magnifier aside unless <paramref name="magnified"/>.</summary>
    public float4x4 PartPlacement(ModelPart part, bool magnified) => IsMagnifier(part) && !magnified ? MagnifierAside * Placement : Placement;

    /// <summary>Its line of sight's height, and its eyepiece's end (model space).</summary>
    public float SightY { get; init; }
    public float Eyepiece { get; init; }

    /// <summary>The gun's hold with this on it: the line of sight up (or down) to the optic's, the eye behind its eyepiece.</summary>
    public GunFeel Adjust(GunFeel f)
    {
        var (sightY, eyepiece) = (SightY, Eyepiece);
        var sightX = f.SightPoint.X;
        var dy = sightY - f.SightPoint.Y;
        var magnified = View.Kind is ScopeKind.Tube or ScopeKind.Acog;
        var eyeX = sightX + f.AimPosition.Z;
        eyeX = magnified ? eyepiece - 0.06f : float.Min(eyeX, eyepiece - 0.16f);   // (a reflex sight: held well back, its window small in the view)
        return f with
        {
            SightPoint = new float3(sightX, sightY, f.SightPoint.Z),
            AimPosition = new float3(f.AimPosition.X, f.AimPosition.Y, eyeX - sightX),
            // At the hip, held lower by as much as the sight is higher (the hands stay where they were), and the eyepiece
            // kept out of the face.
            HipPosition = new float3(f.HipPosition.X, f.HipPosition.Y - dy * 1.2f, float.Min(f.HipPosition.Z, eyepiece - sightX - 0.09f)),
        };
    }
}

/// <summary>
/// The optics library and which gun takes which: every gun but the shotgun takes the red dot, and the bigger sights only
/// where they suit the gun. What's fitted to each gun is kept in optics.txt next to the exe.
/// </summary>
public static class Optics
{
    private const string SaveFile = "optics.txt";

    public static readonly OpticInfo[] All =
    [
        new(OpticKind.RedDot, "Red dot", "1x reflex: a red dot, both eyes open", "RedDot", new(ScopeKind.RedDot, 1f)),
        new(OpticKind.Holo, "Holo + 3x", "1x holographic ring, the wheel flips in a 3x magnifier", "Holo", new(ScopeKind.Holo, 1f, [1f, 3f])),
        new(OpticKind.Prism4x, "4x prism", "Fixed 4x prism sight with a drop ladder", "Prism4x", new(ScopeKind.Acog, 4f)),
        new(OpticKind.Acog6x, "6x ACOG", "Fixed 6x ACOG with a mini dot on top", "Acog6x", new(ScopeKind.Acog, 6f)),
        new(OpticKind.Scope2to8, "2-8x scope", "Variable tube scope: 2x, 4x, 8x on the wheel", "Scope2to8", new(ScopeKind.Tube, 4f, [2f, 4f, 8f])),
        new(OpticKind.Scope4to16, "4-16x scope", "Long-range tube scope: 4x, 8x, 16x on the wheel", "Scope4to16", new(ScopeKind.Tube, 8f, [4f, 8f, 16f])),
    ];

    public static OpticInfo Info(OpticKind kind) => All.First(o => o.Kind == kind);

    private const OpticKind Dot = OpticKind.RedDot, Holo = OpticKind.Holo, P4 = OpticKind.Prism4x, A6 = OpticKind.Acog6x,
        S28 = OpticKind.Scope2to8, S416 = OpticKind.Scope4to16;

    /// <summary>Each gun's mount and what it takes (as chosen gun by gun). The shotgun takes none.</summary>
    private static readonly Dictionary<string, GunMount> Mounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pistol"] = new(0.0275f, -0.064f, -0.064f, [Dot]) { DotPart = "PistolDot" },
        // A side mount over the dust cover.
        ["AK-47"] = new(0.050f, -0.060f, -0.095f, [Dot, Holo, P4, A6]) { BaseY = 0.032f, BlockRange = new(-0.045f, 0.148f) },
        ["UMP40"] = new(0.0655f, -0.030f, -0.060f, [Dot, Holo, P4]),
        ["AR-15"] = new(0.056f, -0.020f, -0.110f, [Dot, Holo, P4, A6, S28]),
        ["G3"] = new(0.084f, -0.040f, -0.080f, [Dot, Holo, A6, S28, S416]),
        ["M249"] = new(0.074f, -0.040f, -0.060f, [Dot, Holo]),
        ["L85A2"] = new(0.028f, -0.110f, -0.150f, [Dot, Holo, A6, S28, S416]),
        ["L85A3"] = new(0.028f, -0.110f, -0.150f, [Dot, Holo, A6, S28, S416]),
        ["L403A1"] = new(0.056f, -0.020f, -0.118f, [Dot, S416, A6, P4, Holo]) { StandardIs = S28 },
        ["L143A2"] = new(0.056f, -0.020f, -0.110f, [Dot, P4, A6, S28, S416]) { StandardIs = Holo },
        ["L119A1"] = new(0.056f, -0.016f, -0.110f, [Holo, P4, A6, S28]) { StandardIs = Dot },
        ["L129A1"] = new(0.056f, -0.020f, -0.082f, [Dot, S28, S416, P4, Holo]) { StandardIs = A6 },
        ["L135A1"] = new(0.063f, -0.100f, -0.180f, [Dot, S28, A6]) { StandardIs = S416 },
        // The MP5s: a claw mount on the receiver.
        ["L92A1"] = new(0.046f, -0.030f, -0.060f, [Dot, Holo]) { BaseY = 0.036f, BlockRange = new(-0.090f, 0.118f) },
        ["L80A1"] = new(0.046f, -0.040f, -0.060f, [Dot, Holo]) { BaseY = 0.036f, BlockRange = new(-0.090f, 0.092f) },
    };

    private static Model? _library;
    private static readonly Dictionary<IGun, OpticKind> Fitted = [];

    /// <summary>Loads the library (null if it isn't there: then nothing can be fitted).</summary>
    public static void Load(Model? model, string pointsPath)
    {
        _library = model;
        if (model is null || !File.Exists(pointsPath))
        {
            _library = null;
            return;
        }
        using var json = JsonDocument.Parse(File.ReadAllText(pointsPath));
        foreach (var info in All)
            if (json.RootElement.TryGetProperty(info.Part, out var p))
                (info.Axis, info.Length) = (p.GetProperty("Axis"u8).GetSingle(), p.GetProperty("Length"u8).GetSingle());
        if (json.RootElement.TryGetProperty("PistolDot"u8, out var dot))
            (PistolDotAxis, PistolDotLength) = (dot.GetProperty("Axis"u8).GetSingle(), dot.GetProperty("Length"u8).GetSingle());
    }

    private static float PistolDotAxis = 0.016f, PistolDotLength = 0.044f;

    /// <summary>What can go on this gun, its own sights first (none at all: it takes no optics).</summary>
    public static IReadOnlyList<OpticKind> Choices(IGun gun) =>
        _library is not null && Mounts.TryGetValue(gun.Name, out var mount) ? [OpticKind.Standard, .. mount.Allowed] : [];

    public static OpticKind FittedTo(IGun gun) => Fitted.GetValueOrDefault(gun);

    /// <summary>What an option is called on this gun: its own sights by what they are (<paramref name="standard"/>).</summary>
    public static string NameOf(OpticKind kind, string standard) => kind == OpticKind.Standard ? standard : Info(kind).Name;

    public static string NoteOf(IGun gun, OpticKind kind) =>
        kind != OpticKind.Standard ? Info(kind).Note
        : Mounts.TryGetValue(gun.Name, out var m) && m.StandardIs != OpticKind.Standard ? "Its own " + Info(m.StandardIs).Note[..1].ToLowerInvariant() + Info(m.StandardIs).Note[1..]
        : "The sights it comes with";

    /// <summary>Puts <paramref name="kind"/> on the gun (if it takes it), and keeps it.</summary>
    public static void Fit(IGun gun, OpticKind kind, bool save = true)
    {
        if (!Choices(gun).Contains(kind))
            kind = OpticKind.Standard;
        Fitted[gun] = kind;
        if (gun is GunBase g)
            g.Fit(Make(gun.Name, Drawn(gun.Name, kind)));
        Log.Write($"[OPTIC] {gun.Name}: {kind}");
        if (save)
            Save();
    }

    /// <summary>What's drawn for an option: a gun whose own sight is a reflex sight has the library's (held well back, the holo's magnifier swinging aside).</summary>
    private static OpticKind Drawn(string gunName, OpticKind kind) =>
        kind == OpticKind.Standard && Mounts.TryGetValue(gunName, out var m) && m.StandardIs is OpticKind.Holo or OpticKind.RedDot ? m.StandardIs : kind;

    /// <summary>The optic as fitted to the named gun: its parts, where they go, and the riser block under it on a gun with no rail.</summary>
    public static FittedOptic? Make(string gunName, OpticKind kind)
    {
        if (_library is null || kind == OpticKind.Standard || !Mounts.TryGetValue(gunName, out var mount))
            return null;
        var info = Info(kind);
        var part = info.Part;
        var (axis, length) = (info.Axis, info.Length);
        if (kind == OpticKind.RedDot && mount.DotPart is { } dotPart)
            (part, axis, length) = (dotPart, PistolDotAxis, PistolDotLength);
        var eyepiece = kind switch
        {
            OpticKind.RedDot => mount.DotEye,
            OpticKind.Holo => mount.DotEye - 0.055f,   // (the magnifier sits behind it)
            _ => mount.ScopeEye,
        };
        var parts = _library.Parts.Where(p => p.Name == part || p.Name == part + "Magnifier").ToArray();
        float4x4? block = null;
        if (mount.BaseY is { } baseY)
        {
            var x0 = float.Max(eyepiece + 0.01f, mount.BlockRange.X);
            var x1 = float.Min(eyepiece + length - 0.01f, mount.BlockRange.Y);
            if (x1 > x0)
                block = float4x4.CreateScale(x1 - x0, mount.RailY - baseY, 1f) * float4x4.CreateTranslation((x0 + x1) / 2f, mount.RailY, 0f);
        }
        var placement = float4x4.CreateTranslation(eyepiece, mount.RailY, 0f);
        return new FittedOptic(info, parts, placement, block, _library.Parts.Where(p => p.Name == "Mount").ToArray())
        {
            SightY = mount.RailY + axis,
            Eyepiece = eyepiece,
        };
    }

    private static readonly Dictionary<ModelPart, Aabb> PartBounds = [];

    /// <summary>The box round some parts (model space): what a picture of them frames.</summary>
    public static Aabb Bounds(IEnumerable<ModelPart> parts)
    {
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        foreach (var part in parts)
        {
            if (!PartBounds.TryGetValue(part, out var b))
            {
                var (lo, hi) = (new float3(float.MaxValue), new float3(float.MinValue));
                var v = part.SharedVertices;
                for (var i = 0; i + 2 < v.Length; i += Mesh.FullStride)
                {
                    var p = new float3(v[i], v[i + 1], v[i + 2]);
                    (lo, hi) = (float3.Min(lo, p), float3.Max(hi, p));
                }
                PartBounds[part] = b = new Aabb(lo, hi);
            }
            (min, max) = (float3.Min(min, b.Min), float3.Max(max, b.Max));
        }
        return min.X <= max.X ? new Aabb(min, max) : new Aabb(float3.Zero, float3.Zero);
    }

    // ------------------------------------------------------------------------------------------------ keeping it

    public static void Load(IEnumerable<IGun> guns)
    {
        var path = Path.Combine(AppContext.BaseDirectory, SaveFile);
        var saved = new Dictionary<string, OpticKind>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
            foreach (var line in File.ReadAllLines(path))
                if (line.Split('=', 2) is [var name, var value] && Enum.TryParse<OpticKind>(value.Trim(), true, out var kind))
                    saved[name.Trim()] = kind;
        foreach (var gun in guns)
        {
            var kind = saved.GetValueOrDefault(gun.Name);
            if (kind != OpticKind.Standard || Drawn(gun.Name, kind) != kind)
                Fit(gun, kind, save: false);
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, SaveFile),
                Fitted.Where(kv => kv.Value != OpticKind.Standard).Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key.Name}={kv.Value}")));
        }
        catch (IOException e)
        {
            Log.Write($"[WARN] Couldn't save the optics ({e.Message})");
        }
    }
}
