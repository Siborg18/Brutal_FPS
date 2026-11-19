using System.Globalization;
using System.Text.Json;
using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>Something the level editor can move: one of the level's boxes or props, by its place in the level's list.</summary>
public readonly record struct EditRef(bool IsProp, int Index);

/// <summary>
/// Where the level editor has put an item, relative to where the level built it: turned by <paramref name="Yaw"/> about
/// the world's vertical axis, then moved by <paramref name="Offset"/>; or deleted.
/// </summary>
public readonly record struct EditPose(float Yaw, float3 Offset, bool Deleted = false)
{
    public bool IsNone => Yaw == 0f && Offset.X == 0f && Offset.Y == 0f && Offset.Z == 0f && !Deleted;

    public float4x4 Matrix => float4x4.CreateRotationY(Yaw) * float4x4.CreateTranslation(Offset);

    /// <summary>This pose, then turned by <paramref name="turn"/> about the vertical line through <paramref name="pivot"/>, then moved by <paramref name="move"/>.</summary>
    public EditPose Then(float3 pivot, float turn, float3 move)
    {
        var offset = Offset;
        if (turn != 0f)
            offset = float3.Transform(offset - pivot, float4x4.CreateRotationY(turn)) + pivot;
        var yaw = Yaw + turn;
        yaw -= float.Round(yaw / float.Tau) * float.Tau;   // (kept within half a turn either way)
        if (float.Abs(yaw) < 1e-5f)
            yaw = 0f;
        return new EditPose(yaw, offset + move, Deleted);
    }
}

/// <summary>
/// The level editor's side of the level (Game/LevelEditor.cs): every box and prop the level builds can be moved, turned
/// or deleted, and new ones added (copies of what's there, or the Blender models in Assets/Models). What's been changed
/// is kept in Assets/Levels/level_edits.json (in the project folder when running from a build in it), keyed by where
/// each item was built, and put back every time the level loads, before the shadows and physics are made from it.
/// BRUTALFPS_NOEDITS=1 loads the level as the code builds it, without the saved changes.
/// </summary>
public sealed partial class Level
{
    private string _editWorld = "full";
    private bool _editingPrepared;
    private int _nextBoxId, _nextPropId;

    // Each editable item as the level built it (or as it was added), by its index; what's been done to it; its name in the file.
    private readonly Dictionary<int, LevelBox> _boxBase = [];
    private readonly Dictionary<int, Prop> _propBase = [];
    private readonly Dictionary<EditRef, EditPose> _poses = [];
    private readonly Dictionary<EditRef, string> _signatures = [];
    private readonly Dictionary<int, List<int>> _boxesById = [];
    // Items added in the editor (in the order they were added), and for copies of procedural meshes, what they copy.
    private readonly List<EditRef> _added = [];
    private readonly HashSet<EditRef> _addedSet = [];
    private readonly Dictionary<EditRef, string> _addedCopyOf = [];

    /// <summary>Changes whenever an item is moved, turned, deleted or added in the editor.</summary>
    public int EditVersion { get; private set; }

    /// <summary>Numbers every box and (separately drawn) prop the level built, so pieces cut from one box are known as one.</summary>
    private void AssignBuildIds(string world)
    {
        _editWorld = world;
        for (var i = 0; i < _boxes.Count; i++)
            _boxes[i] = _boxes[i] with { Id = i + 1 };
        for (var i = 0; i < _props.Count; i++)
        {
            // (Merged instances, a whole forest in one mesh, and the van that drives in on a raid stay as they are.)
            if (_props[i] is { Part: null, Arrives: false, BatchMesh: false } prop)
                _props[i] = prop with { Id = i + 1 };
        }
        _nextBoxId = _boxes.Count + 1;
        _nextPropId = _props.Count + 1;
    }

    /// <summary>
    /// Call once the level is finished (after the portals have cut their doorways, before the shadows and physics are
    /// made): remembers every item as it was built, and puts back the changes saved from the editor.
    /// </summary>
    public void PrepareEditing()
    {
        if (_editingPrepared)
            return;
        _editingPrepared = true;
        var seen = new Dictionary<string, int>();
        string Unique(string signature)
        {
            seen.TryGetValue(signature, out var n);
            seen[signature] = n + 1;
            return n == 0 ? signature : $"{signature}#{n}";
        }

        for (var i = 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            if (box.Id == 0)
                continue;
            _boxBase[i] = box;
            if (!_boxesById.TryGetValue(box.Id, out var pieces))
                _boxesById[box.Id] = pieces = [];
            pieces.Add(i);
            _signatures[new EditRef(false, i)] = Unique(BoxSignature(box));
        }
        for (var i = 0; i < _props.Count; i++)
        {
            var prop = _props[i];
            if (prop.Id == 0)
                continue;
            var bounds = PropBounds(prop);
            if (!(bounds.Min.X <= bounds.Max.X && bounds.Min.Y <= bounds.Max.Y && bounds.Min.Z <= bounds.Max.Z))
                continue;   // (an empty mesh: nothing to see or pick)
            _propBase[i] = prop;
            _signatures[new EditRef(true, i)] = Unique(PropSignature(prop));
        }

        if (Environment.GetEnvironmentVariable("BRUTALFPS_NOEDITS") == "1")
        {
            Log.Write("[EDITOR] BRUTALFPS_NOEDITS: the level as built, without the saved changes");
            return;
        }
        LoadEdits();
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
    private static string V(float3 v) => $"{F(v.X)},{F(v.Y)},{F(v.Z)}";
    private static string BoxSignature(in LevelBox box) => $"B|{V(box.Bounds.Center)}|{V(box.Bounds.Size)}|{(int)box.Material}";
    private static string PropSignature(in Prop prop)
    {
        var bounds = PropBounds(prop);
        return $"P|{(prop.Asset is { Name.Length: > 0 } a ? a.Name : prop.Decal ? "decal" : "mesh")}|{V(bounds.Center)}|{V(bounds.Size)}";
    }

    private static Aabb PropBounds(in Prop prop) => (prop.Asset?.Bounds ?? prop.Mesh!.Bounds).Transformed(prop.Model);

    // ------------------------------------------------------------------------------------------------
    // What the editor asks about items
    // ------------------------------------------------------------------------------------------------

    public bool IsEditable(EditRef item) => item.IsProp ? _propBase.ContainsKey(item.Index) : _boxBase.ContainsKey(item.Index);

    // Names given in the editor.
    private readonly Dictionary<EditRef, string> _names = [];

    /// <summary>The name given to the item in the editor, or null.</summary>
    public string? NameOf(EditRef item) => _names.GetValueOrDefault(item);

    /// <summary>Names the item (null or blank: back to what it's called by what it is).</summary>
    public void SetName(EditRef item, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            _names.Remove(item);
        else
            _names[item] = name.Trim();
        EditVersion++;
    }

    /// <summary>
    /// Which object the item is part of, for counting and outlining: a piece (a desk drawn as a prop per material) is
    /// one object; anything else is its own. Invisible collision boxes are -1 (they go with what they're in).
    /// </summary>
    public long ObjectKey(EditRef item)
    {
        if (!item.IsProp)
            return IsVisibleBox(item) ? (1L << 40) + item.Index : -1;
        var piece = _props[item.Index].Piece;
        return piece != 0 ? piece : (2L << 40) + item.Index;
    }

    /// <summary>The box round the whole object the item is part of (all of a piece's props).</summary>
    public Aabb OutlineOf(EditRef item)
    {
        var bounds = BoundsOf(item);
        if (!item.IsProp || _props[item.Index].Piece is not (> 0 and var piece))
            return bounds;
        foreach (var (index, _) in _propBase)
        {
            var prop = _props[index];
            if (prop.Piece == piece && !prop.Removed)
                bounds = Union(bounds, PropBounds(prop));
        }
        return bounds;
    }

    /// <summary>
    /// BRUTALFPS_EDITOR=audit: logs every item the editor can pick whose box is over <paramref name="metres"/> across
    /// (by area, then size), so things merged into one mesh across a room, whose box is far bigger than any one of them, show up.
    /// </summary>
    public void AuditBounds(float metres = 3f)
    {
        // Items whose box is far bigger than any one thing in them: their triangles fall into clusters with open gaps
        // between them (along some axis), so the box spans empty space: several things merged into one mesh.
        var rows = new List<(Zones Zone, string What, Aabb Box, int Triangles, int Clusters, float Gap)>();
        foreach (var (index, _) in _propBase)
        {
            var prop = _props[index];
            var box = OutlineOf(new EditRef(true, index));
            if (prop.Removed || float.Max(box.Size.X, float.Max(box.Size.Y, box.Size.Z)) <= metres)
                continue;
            var (clusters, gap) = Clusters(prop, 1f);
            if (clusters < 2)
                continue;   // (one thing, just a big one)
            var triangles = prop.Asset is { } m ? m.Parts.Sum(p => p.Mesh.VertexCount) / 3 : prop.Mesh!.VertexCount / 3;
            rows.Add((ZoneAt(box.Center), $"{Describe(new EditRef(true, index))} ({prop.Material}{(prop.Piece != 0 ? $", piece {prop.Piece}" : "")})", box, triangles, clusters, gap));
        }
        foreach (var row in rows.OrderBy(r => r.Zone).ThenByDescending(r => r.Box.Size.X * r.Box.Size.Z))
            Log.Write(string.Create(CultureInfo.InvariantCulture,
                $"[EDITOR] AUDIT {row.Zone}: {row.What} {row.Box.Size.X:0.0} x {row.Box.Size.Y:0.0} x {row.Box.Size.Z:0.0} m at {V(row.Box.Center)}, {row.Triangles} triangles, {row.Clusters} clusters (gaps up to {row.Gap:0.0} m)"));
        Log.Write($"[EDITOR] AUDIT {rows.Count} items over {metres} m spread over separate clusters");
        AuditDoubleWalls();
        AuditLadders();
    }

    /// <summary>
    /// Every ladder: can it be climbed where it is, and does its climbing space follow it when the editor moves and
    /// turns it (and come back when it's put back)? ([EDITOR] LADDER)
    /// </summary>
    private void AuditLadders()
    {
        var (bad, unfollowed, flagged) = (0, 0, 0);
        for (int i = 0, count = _ladders.Count; i < count; i++)
        {
            var ladder = _ladders[i];
            if (ladder.Prop < 0 || !_propBase.ContainsKey(ladder.Prop))
                continue;
            var item = new EditRef(true, ladder.Prop);
            var where = V(PropBounds(_props[ladder.Prop]).Center);
            if (LadderTrouble(i) is { } trouble)
            {
                bad++;
                Log.Write($"[EDITOR] LADDER {i} at {where}: {trouble}");
            }
            var pose = PoseOf(item);
            var shift = new float3(1.5f, 0.5f, -2f);
            SetPose(item, pose.Then(float3.Zero, 0, shift));
            var moved = _ladders[i];
            if (LadderTrouble(i) is not null)
                flagged++;   // (half a metre up, its top's off the floor: the check should say so)
            var turnedPose = pose.Then(PropBounds(_props[ladder.Prop]).Center, float.Pi / 2, float3.Zero);
            SetPose(item, turnedPose);
            var turned = _ladders[i];
            SetPose(item, pose);
            var follows = float3.Distance(moved.Volume.Center, ladder.Volume.Center + shift) < 1e-3f && float.Abs(moved.Top - ladder.Top - shift.Y) < 1e-3f
                          && float.Abs(float3.Dot(turned.Facing, ladder.Facing)) < 1e-3f && _ladders[i] == ladder;
            if (!follows)
            {
                unfollowed++;
                Log.Write($"[EDITOR] LADDER {i} at {where}: its climbing space DOESN'T FOLLOW it (moved {moved.Volume.Center}, turned facing {turned.Facing})");
            }
        }
        Log.Write($"[EDITOR] LADDERS {_ladders.Count}: {bad} can't be climbed where they are, {unfollowed} don't follow their prop, {flagged} found unclimbable when moved off their floor");
    }

    /// <summary>
    /// How many separate clusters the prop's triangles fall into along the axis that splits them most (a gap of at
    /// least <paramref name="minGap"/> metres between them), and the widest gap.
    /// </summary>
    private static (int Clusters, float Gap) Clusters(in Prop prop, float minGap)
    {
        var best = (Clusters: 1, Gap: 0f);
        var spans = new List<(float Min, float Max)>[3] { [], [], [] };
        void Add(float[] p, in float4x4 model)
        {
            for (var i = 0; i + 8 < p.Length; i += 9)
            {
                var a = float3.Transform(new float3(p[i], p[i + 1], p[i + 2]), model);
                var b = float3.Transform(new float3(p[i + 3], p[i + 4], p[i + 5]), model);
                var c = float3.Transform(new float3(p[i + 6], p[i + 7], p[i + 8]), model);
                var lo = float3.Min(a, float3.Min(b, c));
                var hi = float3.Max(a, float3.Max(b, c));
                spans[0].Add((lo.X, hi.X));
                spans[1].Add((lo.Y, hi.Y));
                spans[2].Add((lo.Z, hi.Z));
            }
        }
        if (prop.Asset is { } model)
            foreach (var part in model.Parts)
                Add(part.Mesh.Positions, prop.Model);
        else if (prop.Mesh is { } mesh)
            Add(mesh.Positions, prop.Model);
        foreach (var axis in spans)
        {
            axis.Sort((x, y) => x.Min.CompareTo(y.Min));
            var (clusters, widest) = (1, 0f);
            var reach = float.MinValue;
            foreach (var (lo, hi) in axis)
            {
                if (reach != float.MinValue && lo - reach >= minGap)
                {
                    clusters++;
                    widest = float.Max(widest, lo - reach);
                }
                reach = float.Max(reach, hi);
            }
            if (clusters > best.Clusters)
                best = (clusters, widest);
        }
        return best;
    }

    /// <summary>
    /// Logs walls built as two boxes where one would do, painted differently on its faces: a thin coat of paint laid
    /// over another box's face ([EDITOR] COAT), and a wall made of two half-thickness boxes side by side ([EDITOR] HALVES).
    /// </summary>
    private void AuditDoubleWalls()
    {
        static float Axis(float3 v, int a) => a == 0 ? v.X : a == 1 ? v.Y : v.Z;
        const float Touch = 0.003f;
        var boxes = _boxes.Select((b, i) => (Box: b, Index: i)).Where(x => x.Box.Visible).ToList();
        var (coats, halves) = (0, 0);
        foreach (var (thin, ti) in boxes)
        {
            for (var a = 0; a < 3; a++)
            {
                var t = Axis(thin.Bounds.Size, a);
                if (t > 0.35f)
                    continue;
                foreach (var (host, hi) in boxes)
                {
                    if (hi == ti)
                        continue;
                    // Face to face along axis a, and the thin box's face inside the other's (on the two other axes).
                    var touchesMax = float.Abs(Axis(thin.Bounds.Min, a) - Axis(host.Bounds.Max, a)) < Touch;
                    var touchesMin = float.Abs(Axis(thin.Bounds.Max, a) - Axis(host.Bounds.Min, a)) < Touch;
                    if (!touchesMax && !touchesMin)
                        continue;
                    var inside = true;
                    for (var o = 0; o < 3 && inside; o++)
                        if (o != a)
                            inside = Axis(thin.Bounds.Min, o) >= Axis(host.Bounds.Min, o) - Touch && Axis(thin.Bounds.Max, o) <= Axis(host.Bounds.Max, o) + Touch;
                    if (!inside)
                        continue;
                    var h = Axis(host.Bounds.Size, a);
                    var kind = t <= 0.03f && h > t * 2 ? "COAT" : t > 0.03f && float.Abs(h - t) < 0.01f && thin.Material == host.Material && ti < hi ? "HALVES" : null;
                    if (kind is null)
                        continue;
                    if (kind == "COAT") coats++; else halves++;
                    Log.Write(string.Create(CultureInfo.InvariantCulture,
                        $"[EDITOR] {kind} {ZoneAt(thin.Bounds.Center)}: box {ti} ({thin.Material}, {V(thin.Color)}, {V(thin.Bounds.Size)} at {V(thin.Bounds.Center)}) on box {hi} ({host.Material}, {V(host.Color)}, {V(host.Bounds.Size)})"));
                }
            }
        }
        Log.Write($"[EDITOR] DOUBLE WALLS: {coats} coats of paint on another box, {halves} walls of two halves");
    }

    private static Aabb Union(in Aabb a, in Aabb b) => new(float3.Min(a.Min, b.Min), float3.Max(a.Max, b.Max));

    public EditPose PoseOf(EditRef item) => _poses.TryGetValue(item, out var pose) ? pose : default;

    /// <summary>The box round the item where it is now.</summary>
    public Aabb BoundsOf(EditRef item) => item.IsProp ? PropBounds(_props[item.Index]) : _boxes[item.Index].Bounds;

    /// <summary>A box that's drawn (not one of the invisible ones that give a model its collision).</summary>
    public bool IsVisibleBox(EditRef item) => !item.IsProp && _boxBase.TryGetValue(item.Index, out var box) && box.Visible;

    /// <summary>A few words on what the item is, for the editor's status line.</summary>
    public string Describe(EditRef item)
    {
        if (NameOf(item) is { } name)
            return $"\"{name}\"";
        if (item.IsProp)
        {
            var prop = _props[item.Index];
            if (prop.Piece != 0 && _pieceNames.TryGetValue(prop.Piece, out var piece))
                return piece;
            return prop.Asset is { Name.Length: > 0 } a ? a.Name.Replace('_', ' ') : prop.Decal ? "ground decal" : $"{prop.Material} mesh".ToLowerInvariant();
        }
        var box = _boxBase[item.Index];
        var size = _boxes[item.Index].Bounds.Size;
        var what = box.Visible ? box.Material.ToString().ToLowerInvariant() + " box" : "collision box";
        return string.Create(CultureInfo.InvariantCulture, $"{what} {size.X:0.##} x {size.Y:0.##} x {size.Z:0.##} m");
    }

    /// <summary>Moves, turns or deletes an item (or puts it back as built, with a default pose).</summary>
    public void SetPose(EditRef item, EditPose pose)
    {
        if (pose.IsNone)
            _poses.Remove(item);
        else
            _poses[item] = pose;
        Refresh(item);
        if (item.IsProp)
            MoveLadders(item.Index, pose);
    }

    /// <summary>Puts the item where its pose says, in the texture sets painted on it.</summary>
    private void Refresh(EditRef item)
    {
        var pose = PoseOf(item);
        var paint = _paints.GetValueOrDefault(item);
        if (item.IsProp)
        {
            var built = _propBase[item.Index];
            var before = _props[item.Index];
            // (A piece's prop drawn merged with the rest leaves the batch once it's moved or painted, and goes back when it's put back.)
            var inBatch = pose.IsNone && paint is null ? built.InBatch : 0;
            var now = pose.Deleted ? built with { Removed = true, InBatch = inBatch } : built with { Model = built.Model * pose.Matrix, InBatch = inBatch };
            if (paint is not null)
            {
                if (now.Asset is not null)
                    now = now with { Painted = paint };
                else
                    now = now with { Material = paint.TextureAt(0) ?? built.Material, Tint = paint.ColorAt(0) ?? built.Tint };
            }
            _props[item.Index] = now;
            PieceMoved(built, before, now);
        }
        else
        {
            var built = _boxBase[item.Index];
            var box = pose.Deleted
                ? built with { Visible = false, BlocksShots = false, Collides = false }
                : built with { Bounds = built.Bounds.Transformed(pose.Matrix), PaintFaces = LevelBox.TurnFaces(built.PaintFaces, pose.Yaw) };
            if (paint is not null)
            {
                // (Painted by the faces as built: a box turned a quarter turn takes its paint round with it.)
                var textures = new Material[6];
                var colors = new float3[6];
                for (var face = 0; face < 6; face++)
                {
                    var turned = System.Numerics.BitOperations.Log2((uint)LevelBox.TurnFaces(1 << face, pose.Yaw));
                    textures[turned] = paint.TextureAt(face) ?? built.TextureOf(face);
                    colors[turned] = paint.ColorAt(face) ?? built.ColorOf(face);
                }
                box = box with { FaceTextures = textures, FaceColors = colors };
            }
            _boxes[item.Index] = box;
        }
        _version++;
        EditVersion++;
    }

    // ------------------------------------------------------------------------------------------------
    // Painting: a texture set and a colour for each face of a box, each part of a Blender model, or the whole of a
    // code-built mesh (one item's own, by its "slots"); or for each part of every copy of a model (Model.SetRepaint).
    // ------------------------------------------------------------------------------------------------

    // The paint on each item painted in the editor.
    private readonly Dictionary<EditRef, SlotPaint> _paints = [];

    /// <summary>The texture sets the paint tool offers: all but the ones that only work on what they were made for.</summary>
    public static readonly Material[] PaintTextures =
        [.. Enum.GetValues<Material>().Where(m => m is not (Material.Sky or Material.Ocean or Material.Custom or Material.Leaves))];

    /// <summary>How many things on the item can be painted on their own: a box's six faces, a model's parts, or a mesh as a whole (0: none).</summary>
    public int SlotCount(EditRef item)
    {
        if (!IsEditable(item))
            return 0;
        if (!item.IsProp)
            return _boxBase[item.Index].Visible ? 6 : 0;
        var prop = _propBase[item.Index];
        return prop.Asset is { } asset ? asset.Parts.Count : prop.Mesh is not null ? 1 : 0;
    }

    /// <summary>The paint on the item's slots, or null.</summary>
    public SlotPaint? PaintOf(EditRef item) => _paints.GetValueOrDefault(item);

    /// <summary>Paints the item's slots; null (or nothing painted) puts it back as built.</summary>
    public void SetPaint(EditRef item, SlotPaint? paint)
    {
        if (paint is null || paint.IsEmpty || paint.Count != SlotCount(item))
            _paints.Remove(item);
        else
            _paints[item] = paint.Copy();
        Refresh(item);
    }

    private static readonly string[] FaceNames = ["+X side", "-X side", "top", "bottom", "+Z side", "-Z side"];

    /// <summary>What the slot is (a box's face, a model's part) and what it's painted with now.</summary>
    public string SlotName(EditRef item, int slot)
    {
        if (!item.IsProp)
            return FaceNames[slot];
        var prop = _propBase[item.Index];
        return prop.Asset is { } asset ? PartName(asset, slot) : "all of it";
    }

    /// <summary>A model's part, by its Blender object (numbered when the object has several materials).</summary>
    public static string PartName(Engine.Model model, int slot)
    {
        var name = model.Parts[slot].Name;
        var same = 0;
        var nth = 0;
        for (var i = 0; i < model.Parts.Count; i++)
        {
            if (model.Parts[i].Name != name)
                continue;
            same++;
            if (i < slot)
                nth++;
        }
        name = name.Replace('_', ' ');
        return same > 1 ? $"{name} ({nth + 1} of {same})" : name;
    }

    /// <summary>The texture set the slot is drawn in now.</summary>
    public Material TextureOf(EditRef item, int slot)
    {
        if (PaintOf(item)?.TextureAt(slot) is { } painted)
            return painted;
        if (!item.IsProp)
            return _boxBase[item.Index].TextureOf(slot);
        var prop = _propBase[item.Index];
        return prop.Asset is { } asset ? asset.Parts[slot].Material.Material : prop.Material;
    }

    /// <summary>The colour the slot is drawn in now.</summary>
    public float3 ColorOf(EditRef item, int slot)
    {
        if (PaintOf(item)?.ColorAt(slot) is { } painted)
            return painted;
        if (!item.IsProp)
            return _boxBase[item.Index].ColorOf(slot);
        var prop = _propBase[item.Index];
        return prop.Asset is { } asset ? PartColor(asset.Parts[slot].Material, prop.Tint) : prop.Tint;
    }

    /// <summary>The colour a model's part is drawn in: the model's (a paint part) or its own.</summary>
    public static float3 PartColor(in PartMaterial material, float3 tint) => material.TakesInstanceTint ? tint : material.Color;

    /// <summary>The slot of one of <paramref name="items"/> the ray meets first (a box's face, a model's part, a mesh).</summary>
    public bool PickSlot(IEnumerable<EditRef> items, float3 origin, float3 direction, out EditRef hit, out int slot, out float distance)
    {
        distance = 1e5f;
        hit = default;
        slot = -1;
        foreach (var item in items)
        {
            if (SlotCount(item) == 0 || PoseOf(item).Deleted)
                continue;
            if (!item.IsProp)
            {
                var box = _boxes[item.Index];
                if (box.Bounds.Contains(origin) || !box.Bounds.Raycast(origin, direction, distance, out var t, out var normal) || t > distance)
                    continue;
                var world = LevelBox.FaceOf(normal);
                distance = t;
                hit = item;
                slot = System.Numerics.BitOperations.Log2((uint)LevelBox.TurnFaces(world, -PoseOf(item).Yaw));
                continue;
            }
            var prop = _props[item.Index];
            if (prop.Asset is { } asset)
            {
                if (RaycastModelPart(asset, prop.Model, origin, direction, ref distance) is >= 0 and var part)
                {
                    hit = item;
                    slot = part;
                }
            }
            else if (prop.Mesh is not null && RaycastProp(prop, origin, direction, distance, out var t))
            {
                distance = t;
                hit = item;
                slot = 0;
            }
        }
        return slot >= 0;
    }

    /// <summary>The part of the model (placed by <paramref name="world"/>) the ray meets first, nearer than <paramref name="distance"/>; else -1.</summary>
    public static int RaycastModelPart(Engine.Model model, in float4x4 world, float3 origin, float3 direction, ref float distance)
    {
        if (!float4x4.Invert(world, out var toLocal))
            return -1;
        var o = float3.Transform(origin, toLocal);
        var d = float3.Transform(origin + direction, toLocal) - o;
        var found = -1;
        for (var i = 0; i < model.Parts.Count; i++)
        {
            if (RaycastTriangles(model.Parts[i].Mesh.Positions, o, d, ref distance))
                found = i;
        }
        return found;
    }

    /// <summary>The box round the slot where it is now (a face, flat; a part; the whole mesh).</summary>
    public Aabb SlotBounds(EditRef item, int slot)
    {
        if (!item.IsProp)
        {
            var b = _boxes[item.Index].Bounds;
            var world = System.Numerics.BitOperations.Log2((uint)LevelBox.TurnFaces(1 << slot, PoseOf(item).Yaw));
            var (min, max) = (b.Min, b.Max);
            switch (world)
            {
                case 0: min.X = max.X; break;
                case 1: max.X = min.X; break;
                case 2: min.Y = max.Y; break;
                case 3: max.Y = min.Y; break;
                case 4: min.Z = max.Z; break;
                default: max.Z = min.Z; break;
            }
            return new Aabb(min, max);
        }
        var prop = _props[item.Index];
        return prop.Asset is { } asset ? asset.Parts[slot].Mesh.Bounds.Transformed(prop.Model) : PropBounds(prop);
    }

    /// <summary>Draws just these items (the paint tool's view of what it's painting).</summary>
    public void DrawItems(Renderer renderer, IEnumerable<EditRef> items)
    {
        foreach (var item in items)
        {
            if (!IsEditable(item) || PoseOf(item).Deleted)
                continue;
            if (item.IsProp)
                DrawProp(renderer, _props[item.Index]);
            else if (_boxes[item.Index].Visible)
                _boxes[item.Index].Draw(renderer);
        }
    }

    /// <summary>Where a prop is placed now, and the colour it's drawn in.</summary>
    public (float4x4 Model, float3 Tint) PlacementOf(EditRef item) => (_props[item.Index].Model, _props[item.Index].Tint);

    /// <summary>Every item in the level that's a copy of the Blender model <paramref name="name"/>.</summary>
    public IEnumerable<EditRef> CopiesOf(string name)
    {
        foreach (var (index, _) in _propBase)
        {
            if (_props[index] is { Removed: false, Asset.Name: var asset } && asset == name)
                yield return new EditRef(true, index);
        }
    }

    /// <summary>
    /// The texture sets given to every copy of a model in the editor, read from the saved changes: call before the level
    /// is built (so its merged copies, a forest in one mesh, are made in them too).
    /// </summary>
    public static void LoadModelPaints()
    {
        if (Environment.GetEnvironmentVariable("BRUTALFPS_NOEDITS") == "1" || ReadEditsFile()?.Models is not { Count: > 0 } models)
            return;
        foreach (var (name, saved) in models)
            Engine.Model.SetRepaint(name, ReadPaint(saved.Textures, saved.Colors, saved.Textures?.Length ?? saved.Colors?.Length ?? 0));
        Log.Write($"[EDITOR] Painted every copy of {models.Count} models ({string.Join(", ", models.Keys)})");
    }

    /// <summary>Items too big to grab with a plain click (the ground, long walls): the editor wants Alt for those.</summary>
    public static bool IsBig(in Aabb bounds) => bounds.Size.X > 40f || bounds.Size.Z > 40f;

    /// <summary>The nearest item the ray hits (boxes that are drawn, and props by the box round them).</summary>
    public bool PickEditable(float3 origin, float3 direction, float maxDistance, bool includeBig, out EditRef hit, out float distance)
    {
        distance = maxDistance;
        hit = default;
        var found = false;
        foreach (var (index, _) in _boxBase)
        {
            var box = _boxes[index];
            if (!box.Visible || box.Bounds.Contains(origin) || (!includeBig && IsBig(box.Bounds)))
                continue;
            if (box.Bounds.Raycast(origin, direction, distance, out var t, out _) && t <= distance)
            {
                distance = t;
                hit = new EditRef(false, index);
                found = true;
            }
        }
        foreach (var (index, _) in _propBase)
        {
            var prop = _props[index];
            if (prop.Removed)
                continue;
            var bounds = PropBounds(prop);
            if (!includeBig && IsBig(bounds))
                continue;
            // (Inside its box is fine: what's picked is its triangles, not the box round it.)
            if ((bounds.Contains(origin) || (bounds.Raycast(origin, direction, distance, out var tb, out _) && tb <= distance))
                && RaycastProp(prop, origin, direction, distance, out var t))
            {
                distance = t;
                hit = new EditRef(true, index);
                found = true;
            }
        }
        return found;
    }

    /// <summary>Where the ray first meets the prop's triangles (not the box round it: a few things spread across a room are one mesh).</summary>
    private static bool RaycastProp(in Prop prop, float3 origin, float3 direction, float maxDistance, out float distance)
    {
        distance = maxDistance;
        // Into the mesh's own space (the distance along the ray stays the same: the direction isn't normalised there).
        if (!float4x4.Invert(prop.Model, out var toLocal))
            return false;
        var o = float3.Transform(origin, toLocal);
        var d = float3.Transform(origin + direction, toLocal) - o;
        var found = false;
        if (prop.Asset is { } model)
        {
            foreach (var part in model.Parts)
                found |= RaycastTriangles(part.Mesh.Positions, o, d, ref distance);
        }
        else if (prop.Mesh is { } mesh)
        {
            found = RaycastTriangles(mesh.Positions, o, d, ref distance);
        }
        return found;
    }

    private static bool RaycastTriangles(float[] p, float3 o, float3 d, ref float distance)
    {
        var found = false;
        for (var i = 0; i + 8 < p.Length; i += 9)
        {
            // Möller-Trumbore, both faces.
            var a = new float3(p[i], p[i + 1], p[i + 2]);
            var e1 = new float3(p[i + 3], p[i + 4], p[i + 5]) - a;
            var e2 = new float3(p[i + 6], p[i + 7], p[i + 8]) - a;
            var h = float3.Cross(d, e2);
            var det = float3.Dot(e1, h);
            if (float.Abs(det) < 1e-12f)
                continue;
            var inv = 1f / det;
            var s = o - a;
            var u = float3.Dot(s, h) * inv;
            if (u < 0 || u > 1)
                continue;
            var q = float3.Cross(s, e1);
            var v = float3.Dot(d, q) * inv;
            if (v < 0 || u + v > 1)
                continue;
            var t = float3.Dot(e2, q) * inv;
            if (t > 1e-4f && t < distance)
            {
                distance = t;
                found = true;
            }
        }
        return found;
    }

    /// <summary>Where a ray first meets something solid or drawn (to set things down on), passing through <paramref name="ignore"/>.</summary>
    public bool RaycastForPlacing(float3 origin, float3 direction, float maxDistance, IReadOnlySet<EditRef> ignore, out float distance, out string what)
    {
        distance = maxDistance;
        what = "nothing";
        var found = false;
        for (var i = 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            if ((!box.Visible && !box.Collides) || box.Bounds.Contains(origin) || (ignore.Count > 0 && ignore.Contains(new EditRef(false, i))))
                continue;
            if (box.Bounds.Raycast(origin, direction, distance, out var t, out _) && t <= distance)
            {
                distance = t;
                what = $"box {i} ({(box.Visible ? box.Material.ToString() : "invisible")}, {box.Bounds})";
                found = true;
            }
        }
        foreach (var (index, _) in _propBase)
        {
            var prop = _props[index];
            if (prop.Removed || (ignore.Count > 0 && ignore.Contains(new EditRef(true, index))))
                continue;
            var bounds = PropBounds(prop);
            if (IsBig(bounds))
                continue;
            if ((bounds.Contains(origin) || (bounds.Raycast(origin, direction, distance, out var tb, out _) && tb <= distance))
                && RaycastProp(prop, origin, direction, distance, out var t))
            {
                distance = t;
                what = $"prop {index} ({PropSignature(prop)})";
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// What goes with an item when it's picked: a box with the other pieces cut from it (round a doorway); a prop with
    /// the invisible boxes inside it that give it its collision.
    /// </summary>
    public void CollectGroup(EditRef item, HashSet<EditRef> into)
    {
        if (!IsEditable(item) || PoseOf(item).Deleted)
            return;
        if (!item.IsProp)
        {
            foreach (var cut in _boxesById[_boxes[item.Index].Id])
                if (!PoseOf(new EditRef(false, cut)).Deleted)
                    into.Add(new EditRef(false, cut));
            return;
        }

        // A piece (a desk, a chair) is a prop for each material in it: all of them.
        var members = new List<EditRef> { item };
        if (_props[item.Index].Piece is > 0 and var piece)
        {
            foreach (var (index, _) in _propBase)
                if (index != item.Index && _props[index].Piece == piece && !PoseOf(new EditRef(true, index)).Deleted)
                    members.Add(new EditRef(true, index));
        }
        foreach (var member in members)
        {
            into.Add(member);
            var around = PropBounds(_props[member.Index]).Expand(0.3f);
            foreach (var (index, built) in _boxBase)
            {
                if (built.Visible)
                    continue;
                var box = _boxes[index].Bounds;
                if (PoseOf(new EditRef(false, index)).Deleted || IsBig(box))
                    continue;
                if (box.Min.X >= around.Min.X && box.Min.Y >= around.Min.Y && box.Min.Z >= around.Min.Z
                    && box.Max.X <= around.Max.X && box.Max.Y <= around.Max.Y && box.Max.Z <= around.Max.Z)
                    into.Add(new EditRef(false, index));
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Adding
    // ------------------------------------------------------------------------------------------------

    /// <summary>Adds a box (in the world, as given).</summary>
    public EditRef AddEditorBox(LevelBox box)
    {
        var id = _nextBoxId++;
        box = box with { Id = id };
        _boxes.Add(box);
        var index = _boxes.Count - 1;
        _boxBase[index] = box;
        _boxesById[id] = [index];
        return Added(new EditRef(false, index));
    }

    /// <summary>Adds the Blender model Assets/Models/<paramref name="name"/>.glb, placed by <paramref name="model"/>; null if it won't load.</summary>
    public EditRef? AddEditorModel(string name, in float4x4 model, float3 tint)
    {
        if (TryModel(name) is not { } asset)
            return null;
        var prop = new Prop(null, model, tint, Material.Plain, Asset: asset, Id: _nextPropId++);
        return AddProp(prop with { Zone = ZoneAt(PropBounds(prop).Center) });
    }

    /// <summary>
    /// The number plates a Transit van can wear (Assets/Models/transit_plate_*.glb, build_plates.py: the Factory's
    /// Albanian, German, Dutch and Romanian ones), by the code after "transit_plate_".
    /// </summary>
    public static IReadOnlyList<string> TransitPlates => _transitPlates ??= FindTransitPlates();
    private static string[]? _transitPlates;

    private static string[] FindTransitPlates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Models");
        try
        {
            return [.. Directory.EnumerateFiles(dir, "transit_plate_*.glb").Select(f => Path.GetFileNameWithoutExtension(f)["transit_plate_".Length..]).Order()];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>Makes the props one piece (picked, moved and deleted together), called <paramref name="name"/>.</summary>
    public void JoinPiece(IEnumerable<EditRef> props, string name) => SetPiece(props, ++_pieceCount, name);

    private void SetPiece(IEnumerable<EditRef> props, int piece, string name)
    {
        _pieceNames[piece] = name;
        foreach (var item in props.Where(i => i.IsProp))
        {
            _props[item.Index] = _props[item.Index] with { Piece = piece };
            if (_propBase.ContainsKey(item.Index))
                _propBase[item.Index] = _propBase[item.Index] with { Piece = piece };
        }
    }

    private EditRef AddProp(Prop prop)
    {
        _props.Add(prop);
        var index = _props.Count - 1;
        _propBase[index] = prop;
        return Added(new EditRef(true, index));
    }

    private EditRef Added(EditRef item)
    {
        _added.Add(item);
        _addedSet.Add(item);
        _version++;
        EditVersion++;
        return item;
    }

    /// <summary>
    /// A copy of the item, where it is now; null for one that couldn't be saved (a procedural mesh with no name).
    /// <paramref name="pieces"/> keeps the copies of one piece's props together, as a new piece (pass the same one for a whole copy).
    /// </summary>
    public EditRef? Duplicate(EditRef item, Dictionary<int, int> pieces)
    {
        if (!IsEditable(item) || PoseOf(item).Deleted)
            return null;
        if (!item.IsProp)
        {
            var boxCopy = AddEditorBox(_boxBase[item.Index] with { Bounds = _boxes[item.Index].Bounds, PaintFaces = _boxes[item.Index].PaintFaces });
            if (NameOf(item) is { } boxName)
                _names[boxCopy] = boxName;
            if (PaintOf(item) is { } boxPaint)
            {
                // (The copy is built as the original stands now, turned or not: its paint goes by those faces.)
                var yaw = PoseOf(item).Yaw;
                var turned = new int[6];
                for (var face = 0; face < 6; face++)
                    turned[face] = System.Numerics.BitOperations.Log2((uint)LevelBox.TurnFaces(1 << face, -yaw));
                SetPaint(boxCopy, new SlotPaint([.. turned.Select(f => boxPaint.Textures[f])], [.. turned.Select(f => boxPaint.Colors[f])]));
            }
            return boxCopy;
        }

        var prop = _props[item.Index];
        string? copyOf = null;
        if (prop.Asset is not { Name.Length: > 0 })
        {
            copyOf = _addedCopyOf.TryGetValue(item, out var source) ? source : _signatures.GetValueOrDefault(item);
            if (copyOf is null)
                return null;
        }
        var copy = AddProp(prop with { Id = _nextPropId++, Removed = false, Piece = CopyPiece(prop.Piece, pieces), InBatch = 0,
                                       Material = _propBase[item.Index].Material, Tint = _propBase[item.Index].Tint, Painted = null });   // (its paint goes on below, as the original's)
        CopyLadders(item.Index, copy.Index, float4x4.Identity, asBuilt: false);   // (a copied ladder climbs too)
        if (copyOf is not null)
            _addedCopyOf[copy] = copyOf;
        if (NameOf(item) is { } name)
            _names[copy] = name;
        if (PaintOf(item) is { } paint)
            SetPaint(copy, paint);
        return copy;
    }

    /// <summary>The new piece the copies of <paramref name="piece"/>'s props go into (made the first time it's asked for).</summary>
    private int CopyPiece(int piece, Dictionary<int, int> pieces)
    {
        if (piece == 0)
            return 0;
        if (!pieces.TryGetValue(piece, out var copy))
        {
            pieces[piece] = copy = ++_pieceCount;
            _pieceNames[copy] = _pieceNames.GetValueOrDefault(piece, "piece");
        }
        return copy;
    }

    // ------------------------------------------------------------------------------------------------
    // The file
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Where the changes are kept: Assets/Levels/level_edits.json in the project folder (found by walking up from the
    /// exe to BrutalFps.csproj), else next to the exe; or BRUTALFPS_EDITS.
    /// </summary>
    public static string EditsPath { get; } = FindEditsPath();

    private static string FindEditsPath()
    {
        if (Environment.GetEnvironmentVariable("BRUTALFPS_EDITS") is { Length: > 0 } path)
            return Path.GetFullPath(path);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var up = 0; dir is not null && up < 8; up++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BrutalFps.csproj")))
                return Path.Combine(dir.FullName, "Assets", "Levels", "level_edits.json");
        }
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Levels", "level_edits.json");
    }

    private sealed class EditsFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, WorldEdits> Worlds { get; set; } = [];
        /// <summary>Paint given to every copy of a model (by its name), per part: see Model.SetRepaint.</summary>
        public Dictionary<string, SavedPaint>? Models { get; set; }
    }

    /// <summary>Paint given to every copy of a model: a texture set (-1: its own) and a colour (null: its own) per part.</summary>
    private sealed class SavedPaint
    {
        public int[]? Textures { get; set; }
        public float[]?[]? Colors { get; set; }
    }

    /// <summary>The changes to one world: the whole game ("full"), a factory raid's ("raid") or the firing range's ("range").</summary>
    private sealed class WorldEdits
    {
        public List<MovedItem> Moved { get; set; } = [];
        public List<AddedItem> Added { get; set; } = [];
    }

    /// <summary>An item the level built, moved, deleted or named (or any of them) in the editor.</summary>
    private sealed class MovedItem
    {
        public string Id { get; set; } = "";
        public float Yaw { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public bool Deleted { get; set; }
        public string? Name { get; set; }
        /// <summary>Texture sets painted on it, a Material per slot (-1: as built): see Level.SlotCount.</summary>
        public int[]? Textures { get; set; }
        /// <summary>Colours painted on it, red, green and blue per slot (null: as built).</summary>
        public float[]?[]? Colors { get; set; }
    }

    private sealed class AddedItem
    {
        /// <summary>"box", "model" (Assets/Models/<see cref="Model"/>.glb) or "copy" (of the mesh of the built prop <see cref="CopyOf"/>).</summary>
        public string Kind { get; set; } = "";
        public string? Model { get; set; }
        public string? CopyOf { get; set; }
        public float[]? Min { get; set; }
        public float[]? Max { get; set; }
        public float[]? Color { get; set; }
        public int Material { get; set; }
        public bool Visible { get; set; } = true;
        public bool BlocksShots { get; set; } = true;
        public bool Collides { get; set; } = true;
        /// <summary>A model's placement: the 16 numbers of its matrix, row by row.</summary>
        public float[]? Matrix { get; set; }
        public string? Name { get; set; }
        /// <summary>A box's second colour, and the faces painted it (see <see cref="LevelBox.PaintFaces"/>).</summary>
        public float[]? Paint { get; set; }
        public int PaintFaces { get; set; }
        public int? PaintMaterial { get; set; }
        /// <summary>For a copy of part of a piece: the copies with the same number are one piece.</summary>
        public int Piece { get; set; }
        /// <summary>Texture sets painted on it, a Material per slot (-1: as built): see Level.SlotCount.</summary>
        public int[]? Textures { get; set; }
        /// <summary>Colours painted on it, red, green and blue per slot (null: as built).</summary>
        public float[]?[]? Colors { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static EditsFile? ReadEditsFile()
    {
        if (!File.Exists(EditsPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<EditsFile>(File.ReadAllText(EditsPath), JsonOptions);
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] [EDITOR] Couldn't read {EditsPath} ({e.GetType().Name}: {e.Message}); the level is as built.");
            return null;
        }
    }

    private static float3 Vec(float[]? v) => v is [var x, var y, var z, ..] ? new float3(x, y, z) : float3.Zero;
    private static float[] Arr(float3 v) => [v.X, v.Y, v.Z];

    private static float[] MatrixToArray(in float4x4 m) =>
        [m.X.X, m.X.Y, m.X.Z, m.X.W, m.Y.X, m.Y.Y, m.Y.Z, m.Y.W, m.Z.X, m.Z.Y, m.Z.Z, m.Z.W, m.W.X, m.W.Y, m.W.Z, m.W.W];

    private static float4x4 ArrayToMatrix(float[] a)
    {
        var m = float4x4.Identity;
        m.X = new float4(a[0], a[1], a[2], a[3]);
        m.Y = new float4(a[4], a[5], a[6], a[7]);
        m.Z = new float4(a[8], a[9], a[10], a[11]);
        m.W = new float4(a[12], a[13], a[14], a[15]);
        return m;
    }

    private void LoadEdits()
    {
        if (ReadEditsFile() is not { } file || !file.Worlds.TryGetValue(_editWorld, out var world))
            return;
        var bySignature = new Dictionary<string, EditRef>();
        foreach (var (item, signature) in _signatures)
            bySignature[signature] = item;

        var (moved, added, lost) = (0, 0, 0);
        foreach (var m in world.Moved)
        {
            if (bySignature.TryGetValue(m.Id, out var item))
            {
                if (ReadPaint(m.Textures, m.Colors, SlotCount(item)) is { } paint)
                    _paints[item] = paint;
                SetPose(item, new EditPose(m.Yaw, new float3(m.X, m.Y, m.Z), m.Deleted));
                if (m.Name is { Length: > 0 } given)
                    _names[item] = given;
                moved++;
            }
            else
            {
                lost++;
            }
        }
        var copiedPieces = new Dictionary<int, int>();
        foreach (var a in world.Added)
        {
            EditRef? made = null;
            switch (a.Kind)
            {
                case "box":
                    made = AddEditorBox(new LevelBox(new Aabb(Vec(a.Min), Vec(a.Max)), Vec(a.Color), (Material)a.Material, a.Visible, a.BlocksShots, a.Collides,
                                                     Paint: Vec(a.Paint), PaintFaces: a.PaintFaces, PaintMaterial: (Material?)a.PaintMaterial));
                    break;
                case "model" when a.Model is { } name && a.Matrix is { Length: 16 } matrix:
                    made = AddEditorModel(name, ArrayToMatrix(matrix), Vec(a.Color));
                    // (A model added with others as one piece, a van and its number plates: together again, by the number saved with them.)
                    if (made is { } model && a.Piece != 0)
                    {
                        if (!copiedPieces.TryGetValue(-a.Piece, out var joined))
                            copiedPieces[-a.Piece] = joined = ++_pieceCount;
                        SetPiece([model], joined, _pieceNames.GetValueOrDefault(joined, name.Replace('_', ' ')));
                    }
                    break;
                case "copy" when a.CopyOf is { } source && a.Matrix is { Length: 16 } matrix
                                 && bySignature.TryGetValue(source, out var original) && original.IsProp:
                    var built = _propBase[original.Index];
                    // (A copy of a piece: a new piece, shared with the rest of its copy, by the number saved with them.)
                    var piece = a.Piece != 0 && built.Piece != 0 ? CopyPiece(-a.Piece, copiedPieces) : 0;
                    if (piece != 0)
                        _pieceNames[piece] = _pieceNames.GetValueOrDefault(built.Piece, "piece");
                    made = AddProp(built with { Model = ArrayToMatrix(matrix), Id = _nextPropId++, Removed = false, Piece = piece, InBatch = 0 });
                    _addedCopyOf[made.Value] = source;
                    if (float4x4.Invert(built.Model, out var fromBuilt))
                        CopyLadders(original.Index, made.Value.Index, fromBuilt * ArrayToMatrix(matrix), asBuilt: true);
                    break;
            }
            if (made is not { } done)
            {
                lost++;
                continue;
            }
            if (a.Name is { Length: > 0 } given)
                _names[done] = given;
            if (ReadPaint(a.Textures, a.Colors, SlotCount(done)) is { } paint)
                SetPaint(done, paint);
            added++;
        }
        Log.Write($"[EDITOR] Loaded {EditsPath} ({_editWorld}): {moved} items moved or deleted, {added} added" +
                  (lost > 0 ? $", {lost} no longer found (the level's code has changed where they were)" : ""));
        // Ladders put somewhere they can't be climbed (into a wall, or with nothing to step off onto at the top).
        foreach (var index in _ladderBase.Keys)
        {
            if (_ladders[index].Prop is >= 0 and var prop && LadderTrouble(index) is { } trouble)
                Log.Write($"[WARN] [EDITOR] The ladder at {V(PropBounds(_props[prop]).Center)}: {trouble}");
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Ladders: the space you climb in goes where the editor puts the ladder's prop
    // ------------------------------------------------------------------------------------------------

    // Each ladder the editor has moved (or copied), as it was before the editor's pose: by its index in _ladders.
    private readonly Dictionary<int, Ladder> _ladderBase = [];

    /// <summary>The ladder, moved by <paramref name="m"/> (a turn about the vertical and a move).</summary>
    private static Ladder Transformed(in Ladder ladder, float4x4 m)
    {
        var origin = float3.Transform(float3.Zero, m);
        float3 Turn(float3 d) => float3.Normalize(float3.Transform(d, m) - origin);
        return ladder with { Volume = ladder.Volume.Transformed(m), Facing = Turn(ladder.Facing), Exit = Turn(ladder.Exit), Top = ladder.Top + origin.Y };
    }

    /// <summary>Puts the ladders drawn by prop <paramref name="prop"/> where its new pose puts them (well out of reach when it's deleted).</summary>
    private void MoveLadders(int prop, in EditPose pose)
    {
        for (var i = 0; i < _ladders.Count; i++)
        {
            if (_ladders[i].Prop != prop)
                continue;
            if (!_ladderBase.TryGetValue(i, out var built))
                _ladderBase[i] = built = _ladders[i];
            _ladders[i] = pose.Deleted
                ? built with { Volume = new Aabb(new float3(0, -1e6f, 0), new float3(0, -1e6f, 0)) }
                : pose.IsNone ? built : Transformed(built, pose.Matrix);
        }
    }

    /// <summary>
    /// Gives the copy <paramref name="copy"/> of prop <paramref name="source"/> the source's ladders, moved by <paramref name="m"/>:
    /// from where the level built them (<paramref name="asBuilt"/>), or from where they are now.
    /// </summary>
    private void CopyLadders(int source, int copy, in float4x4 m, bool asBuilt)
    {
        for (int i = 0, count = _ladders.Count; i < count; i++)
        {
            if (_ladders[i].Prop != source)
                continue;
            var from = asBuilt && _ladderBase.TryGetValue(i, out var built) ? built : _ladders[i];
            var ladder = Transformed(from, m) with { Prop = copy };
            _ladders.Add(ladder);
            _ladderBase[_ladders.Count - 1] = ladder;
        }
    }

    /// <summary>
    /// Why the editor's ladders drawn by the item can't be climbed where it's put them (into something solid, or with
    /// nothing to step off onto at the top), or null when they can (or it isn't a ladder).
    /// </summary>
    public string? LadderTrouble(EditRef item)
    {
        if (!item.IsProp || PoseOf(item).Deleted)
            return null;
        for (var i = 0; i < _ladders.Count; i++)
        {
            if (_ladders[i].Prop == item.Index && LadderTrouble(i) is { } trouble)
                return trouble;
        }
        return null;
    }

    private string? LadderTrouble(int index)
    {
        var ladder = _ladders[index];
        if (ladder.Prop < 0 || _props[ladder.Prop].Removed)
            return null;
        // (The ladder's own boxes, the rungs that stop you, are inside the box round its prop: they don't count.)
        var own = PropBounds(_props[ladder.Prop]).Expand(0.1f);
        bool Own(in Aabb box) => box.Min.X >= own.Min.X && box.Max.X <= own.Max.X && box.Min.Z >= own.Min.Z && box.Max.Z <= own.Max.Z;

        var (volume, facing) = (ladder.Volume, ladder.Facing);
        if (ladder.Top - 0.1f > volume.Min.Y + 0.1f)
        {
            var climb = new Aabb(volume.Min + new float3(0.05f, 0.1f, 0.05f), new float3(volume.Max.X - 0.05f, ladder.Top - 0.1f, volume.Max.Z - 0.05f));
            foreach (var box in _boxes)
            {
                if (box.Collides && !Own(box.Bounds) && box.Bounds.Overlaps(climb))
                    return "something solid is in the way of climbing it";
            }
        }

        // Somewhere to step off at the top: a floor at about the ladder's top, just past the rungs (the way off it, or any way).
        var c = volume.Center;
        var rungs = new float3(c.X, ladder.Top, c.Z) + facing * (float.Abs(float3.Dot(volume.Size, facing)) / 2);
        foreach (var dir in (ReadOnlySpan<float3>)[ladder.Exit, -facing, facing, new float3(facing.Z, 0, -facing.X), new float3(-facing.Z, 0, facing.X)])
        {
            for (var d = 0.1f; d < 1.3f; d += 0.1f)
            {
                var p = rungs + dir * d;
                foreach (var box in _boxes)
                {
                    var b = box.Bounds;
                    if (box.Collides && !Own(b) && p.X >= b.Min.X && p.X <= b.Max.X && p.Z >= b.Min.Z && p.Z <= b.Max.Z
                        && float.Abs(b.Max.Y - ladder.Top) < 0.35f)
                        return null;
                }
            }
        }
        return string.Create(CultureInfo.InvariantCulture, $"there's no floor to step off onto at its top ({ladder.Top:0.##} m up)");
    }

    /// <summary>
    /// An added box's paint by its faces as it stands now (it's saved as it stands, unturned): the faces it was
    /// painted by are the ones it was added with, before any turn since.
    /// </summary>
    private SlotPaint? PaintAsPlaced(EditRef item)
    {
        if (PaintOf(item) is not { } paint)
            return null;
        var yaw = PoseOf(item).Yaw;
        var from = new int[6];
        for (var face = 0; face < 6; face++)
            from[face] = System.Numerics.BitOperations.Log2((uint)LevelBox.TurnFaces(1 << face, -yaw));
        return new SlotPaint([.. from.Select(f => paint.Textures[f])], [.. from.Select(f => paint.Colors[f])]);
    }

    /// <summary>The paint's colours as saved (red, green, blue per slot, null as built), or null when none are painted.</summary>
    private static float[]?[]? SavedColors(SlotPaint? paint) =>
        paint is null || Array.TrueForAll(paint.Colors, c => c is null) ? null : [.. paint.Colors.Select(c => c is { } v ? Arr(v) : null)];

    /// <summary>Paint as saved, for an item with <paramref name="slots"/> slots; null when there's none, or it doesn't fit (the item's changed).</summary>
    private static SlotPaint? ReadPaint(int[]? textures, float[]?[]? colors, int slots)
    {
        if (slots == 0 || (textures is null && colors is null) || (textures is not null && textures.Length != slots) || (colors is not null && colors.Length != slots))
            return null;
        var paint = new SlotPaint(textures is null ? [.. Enumerable.Repeat(-1, slots)] : [.. textures],
                                  colors is null ? new float3?[slots] : [.. colors.Select(c => c is { Length: >= 3 } ? Vec(c) : (float3?)null)]);
        return paint.IsEmpty ? null : paint;
    }

    /// <summary>Writes every change made to this world (in this run and before) to <see cref="EditsPath"/>.</summary>
    public void SaveEdits()
    {
        var file = ReadEditsFile() ?? new EditsFile();
        var world = new WorldEdits();
        foreach (var item in _poses.Keys.Union(_names.Keys).Union(_paints.Keys).OrderBy(i => i.IsProp).ThenBy(i => i.Index))
        {
            if (_addedSet.Contains(item) || !_signatures.TryGetValue(item, out var signature))
                continue;
            var pose = PoseOf(item);
            world.Moved.Add(new MovedItem
            {
                Id = signature, Yaw = pose.Yaw, X = pose.Offset.X, Y = pose.Offset.Y, Z = pose.Offset.Z, Deleted = pose.Deleted, Name = NameOf(item),
                Textures = PaintOf(item)?.Textures, Colors = SavedColors(PaintOf(item)),
            });
        }
        foreach (var item in _added)
        {
            if (PoseOf(item).Deleted)
                continue;
            if (!item.IsProp)
            {
                var box = _boxes[item.Index];
                world.Added.Add(new AddedItem
                {
                    Kind = "box", Min = Arr(box.Bounds.Min), Max = Arr(box.Bounds.Max), Color = Arr(box.Color), Material = (int)box.Material,
                    Visible = box.Visible, BlocksShots = box.BlocksShots, Collides = box.Collides, Name = NameOf(item),
                    Paint = box.PaintFaces != 0 ? Arr(box.Paint) : null, PaintFaces = box.PaintFaces, PaintMaterial = (int?)box.PaintMaterial,
                    Textures = PaintAsPlaced(item)?.Textures, Colors = SavedColors(PaintAsPlaced(item)),
                });
                continue;
            }
            var prop = _props[item.Index];
            world.Added.Add(_addedCopyOf.TryGetValue(item, out var source)
                ? new AddedItem { Kind = "copy", CopyOf = source, Matrix = MatrixToArray(prop.Model), Name = NameOf(item), Piece = prop.Piece, Textures = PaintOf(item)?.Textures, Colors = SavedColors(PaintOf(item)) }
                : new AddedItem { Kind = "model", Model = prop.Asset!.Name, Matrix = MatrixToArray(prop.Model), Color = Arr(prop.Tint), Name = NameOf(item), Piece = prop.Piece, Textures = PaintOf(item)?.Textures, Colors = SavedColors(PaintOf(item)) });
        }

        if (world.Moved.Count == 0 && world.Added.Count == 0)
            file.Worlds.Remove(_editWorld);
        else
            file.Worlds[_editWorld] = world;
        file.Models = Engine.Model.AllRepaints.Count > 0
            ? Engine.Model.AllRepaints.ToDictionary(kv => kv.Key, kv => new SavedPaint { Textures = kv.Value.Textures, Colors = SavedColors(kv.Value) })
            : null;
        Directory.CreateDirectory(Path.GetDirectoryName(EditsPath)!);
        File.WriteAllText(EditsPath, JsonSerializer.Serialize(file, JsonOptions));
        Log.Write($"[EDITOR] Saved {EditsPath} ({_editWorld}): {world.Moved.Count} items moved or deleted, {world.Added.Count} added");
    }
}
