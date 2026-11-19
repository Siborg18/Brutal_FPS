using System.Text.Json;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>One garment worn by a person: the model (bound to the body's skeleton), its colour and how bullets mark it.</summary>
public readonly record struct OutfitItem(SkinnedModel Model, float3 Tint, WoundStyle Wound, string Id, int Layer, uint Hides, uint HideBelow = 0);

/// <summary>What a person wears: at most one garment per slot, and the body regions those garments cover.</summary>
public sealed class Outfit
{
    public static readonly Outfit Empty = new([], 0);

    public IReadOnlyList<OutfitItem> Items { get; }

    /// <summary>OR of every item's hidden body regions (the body isn't drawn where cloth covers it).</summary>
    public uint HideRegions { get; }

    internal Outfit(List<OutfitItem> items, uint hideRegions)
    {
        // Each garment hides what the garments in strictly higher layers cover (HideBelow), worked out once here.
        for (var i = 0; i < items.Count; i++)
        {
            uint above = 0;
            for (var j = 0; j < items.Count; j++)
            {
                if (items[j].Layer > items[i].Layer)
                    above |= items[j].Hides;
            }

            items[i] = items[i] with { HideBelow = above };
        }

        Items = items;
        HideRegions = hideRegions;
    }
}

/// <summary>
/// The clothes people can wear: Assets/Models/garments/wardrobe.json (palettes, archetypes with weighted choices per
/// slot, and fixed outfits for named people) and each garment's sidecar json. Choosing is deterministic.
/// </summary>
public sealed class Wardrobe
{
    private sealed record Garment(string Id, string Slot, int Layer, uint Hides, WoundStyle Wound, SkinnedModel Model);

    private sealed record Choice(string? Id, float Weight, string Palette);

    private sealed record Archetype(string Name, float Weight, Dictionary<string, List<Choice>> Slots);

    private static readonly string[] SlotOrder = ["hair", "head", "torso_base", "torso_outer", "legs", "feet"];

    private readonly SkinnedModel _body;
    private readonly Dictionary<string, Garment?> _garments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float3[]> _palettes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Archetype> _archetypes = [];
    private readonly Dictionary<string, (string Archetype, int Seed)> _npcs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Garments that loaded.</summary>
    public int GarmentCount => _garments.Values.Count(g => g is not null);

    /// <summary>True when there is nothing to wear (no wardrobe.json, or no garment loaded).</summary>
    public bool IsEmpty => _archetypes.Count == 0 && _npcs.Count == 0;

    private Wardrobe(SkinnedModel body) => _body = body;

    private static string GarmentDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "garments");

    /// <summary>Reads wardrobe.json and the sidecar of every garment it names; never throws (problems are a [WARN]).</summary>
    public static Wardrobe Load(SkinnedModel body)
    {
        var wardrobe = new Wardrobe(body);
        var path = Path.Combine(GarmentDir, "wardrobe.json");
        try
        {
            if (!File.Exists(path))
            {
                Log.Write($"[WARN] No wardrobe ({path} not found): people are drawn unclothed.");
                return wardrobe;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            wardrobe.Read(doc.RootElement);
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] Couldn't read the wardrobe ({e.GetType().Name}: {e.Message}): people are drawn unclothed.");
            wardrobe._archetypes.Clear();
            wardrobe._npcs.Clear();
        }

        return wardrobe;
    }

    private void Read(JsonElement root)
    {
        if (root.TryGetProperty("palettes"u8, out var palettes))
        {
            foreach (var p in palettes.EnumerateObject())
            {
                var colours = new List<float3>();
                foreach (var c in p.Value.EnumerateArray())
                    colours.Add(new float3(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle()));
                if (colours.Count > 0)
                    _palettes[p.Name] = [.. colours];
            }
        }

        if (root.TryGetProperty("archetypes"u8, out var archetypes))
        {
            foreach (var a in archetypes.EnumerateObject())
            {
                var slots = new Dictionary<string, List<Choice>>(StringComparer.OrdinalIgnoreCase);
                if (a.Value.TryGetProperty("slots"u8, out var slotObject))
                {
                    foreach (var s in slotObject.EnumerateObject())
                    {
                        var choices = new List<Choice>();
                        foreach (var c in s.Value.EnumerateArray())
                        {
                            string? id = c.TryGetProperty("id"u8, out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
                            var weight = c.TryGetProperty("weight"u8, out var w) ? w.GetSingle() : 1f;
                            var palette = c.TryGetProperty("palette"u8, out var pal) && pal.ValueKind == JsonValueKind.String ? pal.GetString() ?? "" : "";
                            if (id is not null && GetGarment(id) is null)
                                continue;   // missing garment: dropped from the choices (already warned)
                            if (id is not null && palette.Length > 0 && !_palettes.ContainsKey(palette))
                                Log.Write($"[WARN] Wardrobe: garment '{id}' names an unknown palette '{palette}'; it will be undyed.");
                            choices.Add(new Choice(id, float.Max(weight, 0f), palette));
                        }

                        slots[s.Name] = choices;
                    }
                }

                var archetypeWeight = a.Value.TryGetProperty("weight"u8, out var aw) ? aw.GetSingle() : 1f;
                _archetypes.Add(new Archetype(a.Name, float.Max(archetypeWeight, 0f), slots));
            }
        }

        if (root.TryGetProperty("npcs"u8, out var npcs))
        {
            foreach (var n in npcs.EnumerateObject())
            {
                var archetype = n.Value.TryGetProperty("archetype"u8, out var an) ? an.GetString() ?? "" : "";
                var seed = n.Value.TryGetProperty("seed"u8, out var sd) ? sd.GetInt32() : 0;
                if (_archetypes.Any(x => string.Equals(x.Name, archetype, StringComparison.OrdinalIgnoreCase)))
                    _npcs[n.Name] = (archetype, seed);
                else
                    Log.Write($"[WARN] Wardrobe: person '{n.Name}' names an unknown archetype '{archetype}'.");
            }
        }
    }

    /// <summary>A garment by id (loaded from its sidecar and glb the first time), or null with a [WARN] if it can't be.</summary>
    private Garment? GetGarment(string id)
    {
        if (_garments.TryGetValue(id, out var cached))
            return cached;
        Garment? garment = null;
        try
        {
            var sidecar = Path.Combine(GarmentDir, id + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(sidecar));
            var root = doc.RootElement;
            var slot = root.GetProperty("slot"u8).GetString() ?? "";
            if (Array.IndexOf(SlotOrder, slot) < 0)
                throw new FormatException($"unknown slot '{slot}'");
            var hides = root.TryGetProperty("hides"u8, out var h)
                ? BodyRegions.Mask(h.EnumerateArray().Select(e => e.GetString() ?? ""))
                : 0u;
            var wound = root.TryGetProperty("wound"u8, out var wd) ? wd.GetString() : "skin";
            var style = wound switch { "fabric" => WoundStyle.Fabric, "hard" => WoundStyle.Hard, _ => WoundStyle.Skin };
            var model = SkinnedModel.Get("garments/" + id, _body.Skeleton);
            var layer = root.TryGetProperty("layer"u8, out var l) ? l.GetInt32() : 1;
            garment = new Garment(id, slot, layer, hides, style, model);
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] Wardrobe: couldn't load garment '{id}' ({e.GetType().Name}: {e.Message}).");
        }

        _garments[id] = garment;
        return garment;
    }

    /// <summary>
    /// The outfit for this person: the wardrobe.json "npcs" entry if there is one, else a random archetype (weighted),
    /// the same every time for the same (name, seed).
    /// </summary>
    public Outfit For(string npcName, int seed)
    {
        if (_archetypes.Count == 0)
            return Outfit.Empty;
        Archetype? archetype = null;
        if (_npcs.TryGetValue(npcName, out var fixedOutfit))
        {
            archetype = _archetypes.First(a => string.Equals(a.Name, fixedOutfit.Archetype, StringComparison.OrdinalIgnoreCase));
            seed = fixedOutfit.Seed;
        }

        var random = new Random(StableHash(npcName) ^ (seed * 7919));
        archetype ??= Pick(_archetypes, a => a.Weight, random);
        if (archetype is null)
            return Outfit.Empty;

        var items = new List<OutfitItem>();
        uint hide = 0;
        foreach (var slot in SlotOrder)
        {
            if (!archetype.Slots.TryGetValue(slot, out var choices) || choices.Count == 0)
                continue;
            var choice = Pick(choices, c => c.Weight, random);
            if (choice is null || choice.Id is null || GetGarment(choice.Id) is not { } garment)
                continue;
            items.Add(new OutfitItem(garment.Model, Colour(choice.Palette, random), garment.Wound, garment.Id, garment.Layer, garment.Hides));
            hide |= garment.Hides;
        }

        return new Outfit(items, hide);
    }

    /// <summary>For the BRUTALFPS_OUTFIT override: those garments (the last of any slot wins), coloured from the palettes the archetypes use for them.</summary>
    public Outfit FromIds(IEnumerable<string> ids, int seed)
    {
        var random = new Random(seed);
        var bySlot = new Dictionary<string, OutfitItem>();
        var hides = new Dictionary<string, uint>();
        foreach (var raw in ids)
        {
            var id = raw.Trim();
            if (id.Length == 0)
                continue;
            if (GetGarment(id) is not { } garment)
                continue;
            var palette = _archetypes.SelectMany(a => a.Slots.Values).SelectMany(c => c)
                .FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))?.Palette ?? "";
            if (bySlot.ContainsKey(garment.Slot))
                Log.Write($"[WARN] Outfit: two garments for slot '{garment.Slot}'; '{garment.Id}' replaces '{bySlot[garment.Slot].Id}'.");
            bySlot[garment.Slot] = new OutfitItem(garment.Model, Colour(palette, random), garment.Wound, garment.Id, garment.Layer, garment.Hides);
            hides[garment.Slot] = garment.Hides;
        }

        uint hide = 0;
        var items = new List<OutfitItem>();
        foreach (var slot in SlotOrder)
        {
            if (bySlot.TryGetValue(slot, out var item))
            {
                items.Add(item);
                hide |= hides[slot];
            }
        }

        return new Outfit(items, hide);
    }

    /// <summary>Those garments in exactly those colours (a uniform), whatever the palettes say.</summary>
    public Outfit Dyed(params (string Id, float3 Tint)[] garments)
    {
        var outfit = FromIds(garments.Select(g => g.Id), OutfitDyeSeed);
        var items = outfit.Items.Select(item =>
            item with { Tint = garments.First(g => string.Equals(g.Id, item.Id, StringComparison.OrdinalIgnoreCase)).Tint }).ToList();
        return new Outfit(items, outfit.HideRegions);
    }

    private const int OutfitDyeSeed = 1;

    private float3 Colour(string palette, Random random)
    {
        if (palette.Length > 0 && _palettes.TryGetValue(palette, out var colours))
            return colours[random.Next(colours.Length)];
        if (palette.Length == 0 && _palettes.Count > 0)
            random.Next();   // keep the draws the same whether or not a palette is named
        return new float3(1, 1, 1);
    }

    private static T? Pick<T>(List<T> items, Func<T, float> weight, Random random) where T : class
    {
        var total = 0f;
        foreach (var item in items)
            total += weight(item);
        if (total <= 0)
            return items.Count > 0 ? items[random.Next(items.Count)] : null;
        var x = (float)random.NextDouble() * total;
        foreach (var item in items)
        {
            x -= weight(item);
            if (x < 0)
                return item;
        }

        return items[^1];
    }

    /// <summary>FNV-1a, so the same name always gives the same number (string.GetHashCode differs per run).</summary>
    private static int StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s)
            h = (h ^ c) * 16777619u;
        return (int)h;
    }
}
