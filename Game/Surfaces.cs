using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>What a surface is made of, as far as a bullet is concerned: the hole it leaves, what flies off it.</summary>
public enum SurfaceKind
{
    Concrete,      // walls, floors, slabs: a chipped crater, grey chips and dust
    Rock,          // the Arctic's basalt: like concrete, darker chips
    Metal,         // bare or rusty steel: a bright dent, sparks
    PaintedMetal,  // paint over steel (containers, the ship, portal trim): bare metal ring, chipped paint, sparks
    SheetMetal,    // thin corrugated / galvanised iron: punched through, sparks
    Wood,          // planks, crates, pallets, bark: a splintered hole, splinters
    Glass,         // windows, lamps: a crack star, shards
    Dirt,          // red earth, the road, gravel: a puff of soil
    Snow,          // a puff of snow
    Plastic,       // polymer, plastic trim
    Rubber,        // tyres
    Fabric,        // cloth, skin
    Water,         // the sea
}

/// <summary>A surface the physics world knows about: its kind, its colour (display-space tint) and its material.</summary>
public readonly record struct SurfaceInfo(SurfaceKind Kind, float3 Tint, Material Material)
{
    /// <summary>Hard surfaces throw sparks or chips and can make a shallow shot glance off.</summary>
    public bool Hard => Kind is SurfaceKind.Concrete or SurfaceKind.Rock or SurfaceKind.Metal or SurfaceKind.PaintedMetal
        or SurfaceKind.SheetMetal or SurfaceKind.Glass;

    public bool Metallic => Kind is SurfaceKind.Metal or SurfaceKind.PaintedMetal or SurfaceKind.SheetMetal;

    /// <summary>How bouncy and grippy it is for debris landing on it (Bullet multiplies both bodies' values).</summary>
    public (float Restitution, float Friction) Contact => Kind switch
    {
        SurfaceKind.Metal or SurfaceKind.PaintedMetal or SurfaceKind.SheetMetal => (0.55f, 0.6f),
        SurfaceKind.Concrete or SurfaceKind.Rock => (0.5f, 0.8f),
        SurfaceKind.Wood => (0.4f, 0.8f),
        SurfaceKind.Glass => (0.5f, 0.4f),
        SurfaceKind.Dirt => (0.15f, 1.0f),
        SurfaceKind.Snow => (0.05f, 1.0f),
        SurfaceKind.Rubber => (0.6f, 1.0f),
        _ => (0.3f, 0.8f),
    };

    /// <summary>The surface of a level box or a code-built mesh drawn with <paramref name="material"/>.</summary>
    public static SurfaceInfo ForMaterial(Material material, float3 tint)
    {
        var kind = material switch
        {
            Material.Concrete or Material.Floor or Material.Plain or Material.WallPaint or Material.CeilingTile or Material.Brick or Material.Whitewash or Material.Blockwork or Material.RoofSlate => SurfaceKind.Concrete,
            Material.Rock => SurfaceKind.Rock,
            Material.RustyMetal or Material.Steel or Material.Gunmetal => SurfaceKind.Metal,
            Material.PaintedMetal or Material.MarinePaint => SurfaceKind.PaintedMetal,
            Material.Corrugated or Material.Galvanized => SurfaceKind.SheetMetal,
            Material.Wood or Material.Bark or Material.Veneer => SurfaceKind.Wood,
            Material.Carpet => SurfaceKind.Fabric,
            Material.Window or Material.Glass or Material.Emissive => SurfaceKind.Glass,
            Material.Gravel or Material.RedDirt or Material.DirtRoad or Material.Bush or Material.Grass => SurfaceKind.Dirt,
            Material.Snow => SurfaceKind.Snow,
            Material.Polymer => SurfaceKind.Plastic,
            Material.Skin or Material.Cloth or Material.Fleece => SurfaceKind.Fabric,
            Material.Ocean => SurfaceKind.Water,
            _ => SurfaceKind.Concrete,
        };
        return new SurfaceInfo(kind, tint, material);
    }

    /// <summary>
    /// The surface of a Blender model's part: by its texture set, or for flat materials by how metallic and glossy
    /// it is. Returns null for parts bullets pass through (leaf cards).
    /// </summary>
    public static SurfaceInfo? ForPart(in PartMaterial part, float3 instanceTint)
    {
        var tint = part.TakesInstanceTint ? instanceTint : part.Color;
        if (part.Material is Material.Leaves or Material.Bush)
            return null;
        if (part.Custom is { } set)
        {
            var name = set.Name;
            var kind = name.Contains("tyre") ? SurfaceKind.Rubber
                : name.Contains("bark") ? SurfaceKind.Wood
                : name.Contains("grille") || name.Contains("stipple") ? SurfaceKind.Plastic
                : name.Contains("wagon") || name.Contains("transit") || name.Contains("plates") ? SurfaceKind.PaintedMetal
                : name.Contains("hand") ? SurfaceKind.Fabric
                : SurfaceKind.Concrete;
            return new SurfaceInfo(kind, tint, part.Material);
        }

        if (part.Material == Material.Plain)
        {
            // Untextured: glossy and non-metallic reads as glass, metallic as steel, the rest as hard plastic.
            var kind = part.Roughness < 0.15f && part.Metalness < 0.5f ? SurfaceKind.Glass
                : part.Metalness >= 0.5f ? SurfaceKind.Metal
                : SurfaceKind.Plastic;
            return new SurfaceInfo(kind, tint, part.Material);
        }

        // A plain white factor in Blender means "the texture as baked", which the renderer tints with the set's own colour.
        if (!part.TakesInstanceTint && tint.X > 0.98f && tint.Y > 0.98f && tint.Z > 0.98f)
            tint = Renderer.RefTint(part.Material);
        return ForMaterial(part.Material, tint);
    }
}
