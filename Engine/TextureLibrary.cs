using System.Diagnostics;
using System.Text.Json;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Engine;

/// <summary>A tileable PBR texture set baked in Blender (Tools/blender/bake_textures.py).</summary>
/// <param name="Tile">Metres of surface one repeat of the texture covers.</param>
public sealed record TextureSet(string Name, GlTexture Albedo, GlTexture Normal, GlTexture Orm, float Tile);

/// <summary>
/// The texture library: one PBR set (albedo + paint mask, normal, occlusion/roughness/metalness) per surface
/// <see cref="Material"/>, loaded from Assets/Textures. Materials without a set (or whose files are missing) fall back
/// to the old procedural shading.
/// </summary>
public sealed class TextureLibrary : IDisposable
{
    // Material -> texture set name and how many metres one repeat covers (matches the bake script).
    private static readonly (Material Material, string Set, float Tile)[] Table =
    [
        (Material.Concrete, "concrete_wall", 2.5f),
        (Material.Floor, "concrete_floor", 4.0f),
        (Material.RustyMetal, "rusty_metal", 1.5f),
        (Material.Steel, "steel", 1.5f),
        (Material.Wood, "wood_planks", 2.0f),
        (Material.PaintedMetal, "painted_metal", 1.0f),
        (Material.Corrugated, "corrugated", 2.24f),
        (Material.Galvanized, "galvanized", 2.28f),
        (Material.Gravel, "gravel", 2.0f),
        (Material.RedDirt, "red_dirt", 3.0f),
        (Material.DirtRoad, "dirt_road", 4.0f),
        (Material.Snow, "snow", 3.0f),
        (Material.Rock, "rock", 2.0f),
        (Material.MarinePaint, "marine_paint", 3.0f),
        (Material.Bush, "foliage", 0.6f),
        (Material.Gunmetal, "gunmetal", 0.15f),
        (Material.Polymer, "polymer", 0.06f),
        (Material.Skin, "skin", 0.08f),
        (Material.Cloth, "cloth", 0.05f),
        (Material.Fleece, "fleece", 0.05f),
        (Material.Denim, "denim", 0.05f),
        (Material.Canvas, "canvas", 0.06f),
        (Material.Leather, "leather", 0.08f),
        (Material.Knit, "knit", 0.04f),
        (Material.Hair, "hair", 0.03f),
        (Material.Bark, "bark_gum", 1.4f),
    ];

    private readonly Dictionary<Material, TextureSet> _sets = [];
    private readonly Dictionary<string, TextureSet?> _custom = [];
    private string _directory = ".";

    /// <summary>The loaded library (models look up their own texture sets here while they load).</summary>
    public static TextureLibrary? Current { get; private set; }

    /// <summary>
    /// A model's own texture set, <c>{name}_albedo/_normal/_orm.png</c>, loaded on first use (null if missing).
    /// Used through the model's UVs exactly as baked (Tile = 1).
    /// </summary>
    public TextureSet? GetCustom(string name)
    {
        if (_custom.TryGetValue(name, out var set))
            return set;
        set = null;
        try
        {
            var textures = TextureKinds.Select(k => (Path.Combine(_directory, $"{name}_{k.Suffix}.png"), k.Kind)).ToArray();
            if (textures.All(t => File.Exists(t.Item1)))
            {
                var ids = textures.Select(t => Texture.TryReadCache(t.Item1, t.Kind) is { } c ? Texture.UploadCached(c) : Texture.Upload(Texture.Prepare(t.Item1, t.Kind))).ToArray();
                set = new TextureSet(name, ids[0], ids[1], ids[2], 1f);
            }
        }
        catch (IOException e)
        {
            Log.Write($"[WARN] {e.Message}");
        }

        _custom[name] = set;
        return set;
    }

    /// <summary>The material that uses texture set <paramref name="setName"/> (as named in Blender), if any.</summary>
    public static Material? MaterialForSet(string setName)
    {
        foreach (var (material, set, _) in Table)
            if (set == setName)
                return material;
        // "shell" (the helmet's moulded shell, garments_more.py) has no bake of its own: it's the stippled polymer.
        return setName == "shell" ? Material.Polymer : null;
    }

    public static float TileFor(Material material)
    {
        foreach (var (m, _, tile) in Table)
            if (m == material)
                return tile;
        return 1f;
    }

    public bool TryGet(Material material, out TextureSet set) => _sets.TryGetValue(material, out set!);

    public int Count => _sets.Count;

    /// <summary>The texture sets nothing in a factory raid uses (the factory and the police HQ): they belong to the other levels.</summary>
    public static readonly IReadOnlySet<Material> NotOnARaid = new HashSet<Material>
    {
        Material.Gravel, Material.RedDirt, Material.Snow, Material.Rock, Material.MarinePaint,
    };

    /// <param name="directory">Where the texture sets are.</param>
    /// <param name="skip">Materials not to load.</param>
    public void Load(string directory, IReadOnlySet<Material>? skip = null)
    {
        _directory = directory;
        Current = this;
        var watch = Stopwatch.StartNew();
        var wanted = new List<(Material Material, string Name, float Tile)>();
        foreach (var (material, name, tile) in Table)
        {
            if (skip?.Contains(material) == true)
                continue;
            if (TextureKinds.All(k => File.Exists(Path.Combine(directory, $"{name}_{k.Suffix}.png"))))
                wanted.Add((material, name, tile));
            else
                Log.Write($"[WARN] Texture set '{name}' not found in {directory}; {material} stays procedural.");
        }

        // Read compressed caches, or decode the PNGs and build their mip chains, on all cores; then upload on this
        // (the GL) thread.
        var jobs = wanted.SelectMany(w => TextureKinds.Select(k => (Path: Path.Combine(directory, $"{w.Name}_{k.Suffix}.png"), k.Kind))).ToArray();
        var cached = new Texture.Cached?[jobs.Length];
        var prepared = new Texture.Prepared?[jobs.Length];
        Parallel.For(0, jobs.Length, i =>
        {
            cached[i] = Texture.TryReadCache(jobs[i].Path, jobs[i].Kind);
            if (cached[i] is not null)
                return;
            try
            {
                prepared[i] = Texture.Prepare(jobs[i].Path, jobs[i].Kind);
            }
            catch (IOException e)
            {
                Log.Write($"[WARN] {e.Message}");
            }
        });
        var decoded = watch.ElapsedMilliseconds;

        GlTexture? Upload(int i) =>
            cached[i] is { } c ? Texture.UploadCached(c) : prepared[i] is { } p ? Texture.Upload(p) : null;

        for (var i = 0; i < wanted.Count; i++)
        {
            if (Upload(i * 3) is not { } albedo || Upload(i * 3 + 1) is not { } normal || Upload(i * 3 + 2) is not { } orm)
                continue;
            _sets[wanted[i].Material] = new TextureSet(wanted[i].Name, albedo, normal, orm, wanted[i].Tile);
        }

        var fromCache = cached.Count(c => c is not null);
        Array.Clear(cached);
        Array.Clear(prepared);
        GC.Collect(); // the decoded images are big; let them go now rather than whenever
        Log.Write($"Loaded {_sets.Count} texture sets in {watch.ElapsedMilliseconds} ms ({fromCache}/{jobs.Length} from the cache, reading {decoded} ms; {(Texture.UseCompression ? "block-compressed" : "uncompressed")}).");
    }

    private static readonly (string Suffix, TextureKind Kind)[] TextureKinds =
        [("albedo", TextureKind.Color), ("normal", TextureKind.Normal), ("orm", TextureKind.Data)];

    public void Dispose()
    {
        foreach (var set in _sets.Values.Concat(_custom.Values.OfType<TextureSet>()))
            DeleteTextures([set.Albedo, set.Normal, set.Orm]);
        _sets.Clear();
        _custom.Clear();
    }
}

/// <summary>
/// One outdoor area's sky: an HDR cubemap rendered in Blender (Tools/blender/render_sky.py) and the sun and sky
/// irradiance measured when it was rendered, used to light the area so it matches its sky.
/// </summary>
public sealed class SkyEnvironment : IDisposable
{
    public GlTexture Cubemap { get; }
    public int MipLevels { get; }

    /// <summary>Unit vector towards the sun.</summary>
    public float3 SunDirection { get; }

    /// <summary>Sun irradiance (rgb) in the cubemap's units.</summary>
    public float3 SunIrradiance { get; }

    /// <summary>Sky irradiance on a horizontal surface (rgb) in the cubemap's units.</summary>
    public float3 SkyIrradianceUp { get; }

    /// <summary>A night sky: no sun; the area is lit by its own lamps.</summary>
    public bool IsNight { get; }

    private SkyEnvironment(GlTexture cubemap, int mips, float3 sunDirection, float3 sun, float3 sky, bool night)
    {
        Cubemap = cubemap;
        MipLevels = mips;
        SunDirection = sunDirection;
        SunIrradiance = sun;
        SkyIrradianceUp = sky;
        IsNight = night;
    }

    /// <summary>Loads <c>{directory}/{name}_*.hdr</c> and <c>{name}.json</c>; null if they're missing.</summary>
    public static SkyEnvironment? Load(string directory, string name)
    {
        var json = Path.Combine(directory, name + ".json");
        if (!File.Exists(json) || !File.Exists(Path.Combine(directory, name + "_px.hdr")))
        {
            Log.Write($"[WARN] Sky '{name}' not found in {directory}; using the procedural sky.");
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(json));
        var root = doc.RootElement;
        float3 Vec(string key)
        {
            var a = root.GetProperty(key);
            return new float3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
        }

        var (cubemap, mips) = Texture.LoadCubemapHdr(Path.Combine(directory, name));
        var dir = Vec("sunDirection");
        dir /= float.Sqrt(float3.Dot(dir, dir));
        var night = root.TryGetProperty("night"u8, out var n) && n.ValueKind == JsonValueKind.True;
        return new SkyEnvironment(cubemap, mips, dir, Vec("sunIrradiance"), Vec("skyIrradianceUp"), night);
    }

    /// <summary>
    /// The scale that brings this sky's units to the game's: a white surface facing up in full sun ends up at
    /// <paramref name="whiteLevel"/> (linear, before tone mapping). <paramref name="sunFactor"/> dims the sun (overcast).
    /// </summary>
    public float Normalization(float whiteLevel, float sunFactor)
    {
        static float Avg(float3 v) => (v.X + v.Y + v.Z) / 3f;
        var cos = float.Max(SunDirection.Y, 0f);
        var e = Avg(SunIrradiance) * sunFactor * cos + Avg(SkyIrradianceUp);
        return whiteLevel * float.Pi / float.Max(e, 1e-6f);
    }

    public void Dispose() => DeleteTextures([Cubemap]);
}
