using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Building blocks for the police station (see LevelPolice.cs): oriented boxes and cylinders merged into one mesh per
/// material and colour (so a room full of desks is a handful of draw calls), block lettering for its signs, sheriff's
/// stars, office chairs and long guns.
/// </summary>
public sealed partial class Level
{
    // One merged mesh per (material, colour, casts occlusion). Built up while the station is laid out, then turned into
    // props by FlushPoliceBatches.
    // (Pieces take what's added to them while they're built into their own: see Piece.)
    private readonly Dictionary<(Material Material, float3 Tint, bool Occludes), MeshBuilder> _policeBatches = [];
    private readonly Dictionary<(Material Material, float3 Tint, bool Occludes, int Piece), MeshBuilder> _pieceBatches = [];

    /// <summary>The station's furniture as seen from above, for its occlusion map (see <see cref="DrawPoliceOccluders"/>): indices into the props.</summary>
    private readonly List<int> _policeOccluders = [];

    /// <summary>BRUTALFPS_PROFILE=nopieces: everything merged by material as it used to be (to compare the draw calls).</summary>
    private static readonly bool NoPieces = Profile.Has("nopieces");

    // The piece being built (0: none), how many there have been, and what each is called.
    private int _piece, _pieceCount;
    private readonly Dictionary<int, string> _pieceNames = [];

    /// <summary>
    /// Starts an object of its own (a desk, a chair, a smoke alarm): until the scope is disposed, what goes into the
    /// merged meshes (<see cref="PB"/>), and any prop added, belongs to it. It's still merged by material, but only with
    /// itself, so the level editor can pick, move and delete it apart from the rest. Pieces can nest (a monitor on a
    /// desk is a piece of its own). Each costs a draw per material in it, so they're kept to things worth moving.
    /// </summary>
    private PieceScope Piece(string name)
    {
        if (_buildingContainer || NoPieces)
            return default;   // (the prison's container moves as a whole)
        // Where each merged mesh is up to: what's added to them from here to the end of the scope is the piece's.
        var marks = new Dictionary<(Material, float3, bool), int>(_policeBatches.Count);
        foreach (var (key, builder) in _policeBatches)
            marks[key] = builder.Mark;
        var piece = ++_pieceCount;
        var scope = new PieceScope(this, piece, _piece, _props.Count, marks);
        _piece = piece;
        _pieceNames[piece] = name;
        return scope;
    }

    private readonly struct PieceScope(Level level, int piece, int outer, int propsFrom, Dictionary<(Material, float3, bool), int> marks) : IDisposable
    {
        public void Dispose()
        {
            if (level is null || level._piece != piece)
                return;   // (not started, or already finished)
            // (A piece inside this one has already taken its own out, so what's left since the marks is all this one's.)
            foreach (var (key, builder) in level._policeBatches)
            {
                var pieceKey = (key.Material, key.Tint, key.Occludes, piece);
                if (!level._pieceBatches.TryGetValue(pieceKey, out var into))
                    into = new MeshBuilder();
                if (builder.MoveSince(marks.GetValueOrDefault(key), into))
                    level._pieceBatches[pieceKey] = into;
            }
            // Props added straight to the list while it was being built (a plant's leaves) are part of it too.
            for (var i = propsFrom; i < level._props.Count; i++)
                if (level._props[i].Piece == 0)
                    level._props[i] = level._props[i] with { Piece = piece };
            level._piece = outer;
        }
    }

    /// <summary>The merged mesh for <paramref name="material"/> in <paramref name="tint"/>.</summary>
    /// <param name="occludes">False for things that shouldn't shade what's under them (ceiling fittings, the wire cage, signs).</param>
    private MeshBuilder PB(Material material, float3 tint, bool occludes = true)
    {
        if (_buildingContainer)
        {
            // (The prison's container is drawn apart, so that it can move: see LevelPrisonContainer.cs.)
            if (!_containerBatches.TryGetValue((material, tint), out var moving))
                _containerBatches[(material, tint)] = moving = new MeshBuilder();
            return moving;
        }
        var key = (material, tint, occludes);
        if (!_policeBatches.TryGetValue(key, out var builder))
            _policeBatches[key] = builder = new MeshBuilder();
        return builder;
    }

    /// <summary>
    /// Every piece's mesh in one material and colour, merged into one draw (the <see cref="Prop.BatchMesh"/> prop at
    /// <see cref="Prop"/>); a piece moved in the editor is left out of it and drawn by itself (see <see cref="RebuildDrawBatches"/>).
    /// </summary>
    private sealed class DrawBatch(int prop, List<(int Prop, float[] Vertices)> pieces)
    {
        public int Prop { get; } = prop;
        public List<(int Prop, float[] Vertices)> Pieces { get; } = pieces;
        public bool Dirty { get; set; }
    }

    private readonly List<DrawBatch> _drawBatches = [];

    private void FlushPoliceBatches()
    {
        // What's in no piece: a prop per material, as it's always been (the editor can pick it, by its triangles).
        foreach (var ((material, tint, occludes), builder) in _policeBatches)
        {
            if (builder.IsEmpty)
                continue;   // (all of it went into pieces)
            _props.Add(new Prop(builder.Build(), float4x4.Identity, tint, material));
            if (occludes)
                _policeOccluders.Add(_props.Count - 1);
        }
        // The pieces: a prop each per material (the editor's, and the physics' and occlusion map's once moved), drawn
        // merged with the rest of that material's pieces until the editor moves them.
        foreach (var group in _pieceBatches.GroupBy(kv => (kv.Key.Material, kv.Key.Tint, kv.Key.Occludes)))
        {
            var (material, tint, occludes) = group.Key;
            var batch = _drawBatches.Count + 1;
            var pieces = new List<(int, float[])>();
            foreach (var (key, builder) in group)
            {
                var vertices = builder.ToArray();
                _props.Add(new Prop(new Mesh(vertices, upload: false), float4x4.Identity, tint, material, Piece: key.Piece, InBatch: batch));
                pieces.Add((_props.Count - 1, vertices));
                if (occludes)
                    _policeOccluders.Add(_props.Count - 1);
            }
            _props.Add(new Prop(new Mesh([.. pieces.SelectMany(p => p.Item2)]), float4x4.Identity, tint, material, BatchMesh: true));
            _drawBatches.Add(new DrawBatch(_props.Count - 1, pieces));
        }
        _policeBatches.Clear();
        _pieceBatches.Clear();
    }

    /// <summary>Makes the merged meshes again without the pieces the editor has moved or deleted since (they're drawn by themselves).</summary>
    private void RebuildDrawBatches()
    {
        foreach (var batch in _drawBatches)
        {
            if (!batch.Dirty)
                continue;
            batch.Dirty = false;
            var vertices = batch.Pieces.Where(p => _props[p.Prop].InBatch != 0).SelectMany(p => p.Vertices).ToArray();
            var merged = _props[batch.Prop];
            _props[batch.Prop] = vertices.Length == 0 ? merged with { Removed = true } : merged with { Mesh = new Mesh(vertices), Removed = false };
        }
    }

    /// <summary>The piece's prop <paramref name="now"/> leaves its merged batch (moved) or goes back into it (put back as built).</summary>
    private void PieceMoved(in Prop built, in Prop before, in Prop now)
    {
        if (built.InBatch != 0 && before.InBatch != now.InBatch)
            _drawBatches[built.InBatch - 1].Dirty = true;
    }

    /// <summary>A box of <paramref name="size"/> centred on <paramref name="c"/> in the frame <paramref name="xf"/>.</summary>
    private static void OBox(MeshBuilder b, in float4x4 xf, float3 c, float3 size)
    {
        var h = size * 0.5f;
        ReadOnlySpan<(float3 N, float3 U, float3 V)> faces =
        [
            (new float3(1, 0, 0), new float3(0, 1, 0), new float3(0, 0, 1)),
            (new float3(-1, 0, 0), new float3(0, 0, 1), new float3(0, 1, 0)),
            (new float3(0, 1, 0), new float3(0, 0, 1), new float3(1, 0, 0)),
            (new float3(0, -1, 0), new float3(1, 0, 0), new float3(0, 0, 1)),
            (new float3(0, 0, 1), new float3(1, 0, 0), new float3(0, 1, 0)),
            (new float3(0, 0, -1), new float3(0, 1, 0), new float3(1, 0, 0)),
        ];
        var origin = float3.Transform(c, xf);
        foreach (var (n, u, v) in faces)
        {
            var fc = c + n * h;
            var uh = u * h;
            var vh = v * h;
            var normal = float3.Normalize(float3.Transform(c + n, xf) - origin);
            b.Quad(float3.Transform(fc - uh - vh, xf), float3.Transform(fc + uh - vh, xf),
                   float3.Transform(fc + uh + vh, xf), float3.Transform(fc - uh + vh, xf), normal);
        }
    }

    /// <summary>A box turned by <paramref name="rotation"/> about its own centre, then placed in <paramref name="xf"/>.</summary>
    private static void OBox(MeshBuilder b, in float4x4 xf, float3 c, float3 size, in float4x4 rotation) =>
        OBox(b, rotation * float4x4.CreateTranslation(c) * xf, float3.Zero, size);

    /// <summary>A closed cylinder (or tapered prism) from <paramref name="a"/> to <paramref name="z"/> in the frame <paramref name="xf"/>.</summary>
    private static void OCyl(MeshBuilder b, in float4x4 xf, float3 a, float3 z, float r0, float r1 = -1f, int sides = 12)
    {
        if (r1 < 0)
            r1 = r0;
        var p0 = float3.Transform(a, xf);
        var p1 = float3.Transform(z, xf);
        // The frame may be scaled: measure the radius in it.
        var scale = float3.Distance(float3.Transform(a + new float3(1, 0, 0), xf), p0);
        r0 *= scale;
        r1 *= scale;
        AddTrunk(b, p0, p1, r0, r1, sides);
        var axis = float3.Normalize(p1 - p0);
        var side = float3.Normalize(float3.Cross(axis, float.Abs(axis.Y) > 0.9f ? float3.UnitX : float3.UnitY));
        var other = float3.Cross(side, axis);
        for (var i = 0; i < sides; i++)
        {
            var a0 = float.Tau * i / sides;
            var a1 = float.Tau * (i + 1) / sides;
            var d0 = side * float.Cos(a0) + other * float.Sin(a0);
            var d1 = side * float.Cos(a1) + other * float.Sin(a1);
            b.Triangle(p0, p0 + d0 * r0, p0 + d1 * r0, -axis);
            b.Triangle(p1, p1 + d0 * r1, p1 + d1 * r1, axis);
        }
    }

    /// <summary>A flat disc (a dial, a badge's centre) of <paramref name="radius"/> in the frame's XY plane, facing +Z, <paramref name="depth"/> thick.</summary>
    private static void ODisc(MeshBuilder b, in float4x4 xf, float3 c, float radius, float depth, int sides = 20) =>
        OCyl(b, xf, c, c + new float3(0, 0, depth), radius, radius, sides);

    /// <summary>A low half-dome (a CCTV camera, a smoke alarm) hanging from the frame's origin, opening up.</summary>
    private static void OHemisphere(MeshBuilder b, in float4x4 xf, float radius, int around = 14, int rings = 5)
    {
        float3 P(int ring, int k)
        {
            var phi = float.Pi / 2 * ring / rings;               // 0 at the rim, pi/2 at the bottom
            var theta = float.Tau * k / around;
            return new float3(float.Cos(theta) * float.Cos(phi), -float.Sin(phi), float.Sin(theta) * float.Cos(phi)) * radius;
        }
        for (var r = 0; r < rings; r++)
            for (var k = 0; k < around; k++)
            {
                var a = P(r, k);
                var c = P(r + 1, k + 1);
                var n = float3.Normalize(float3.Transform(a + c, xf) - float3.Transform(float3.Zero, xf));
                b.Quad(float3.Transform(a, xf), float3.Transform(P(r, k + 1), xf), float3.Transform(c, xf), float3.Transform(P(r + 1, k), xf), n);
            }
    }

    /// <summary>A frame with its origin at <paramref name="origin"/>, turned by <paramref name="yaw"/> (yaw 0: local -z points north, -z world).</summary>
    private static float4x4 Frame(float3 origin, float yaw) => float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(origin);

    /// <summary>A frame on a wall: x along <paramref name="right"/> (the reader's right), y up, z out of the wall along <paramref name="normal"/>.</summary>
    private static float4x4 WallFrame(float3 origin, float3 normal)
    {
        normal = float3.Normalize(normal);
        var right = float3.Normalize(float3.Cross(-normal, float3.UnitY));
        return new float4x4(
            right.X, right.Y, right.Z, 0,
            0, 1, 0, 0,
            normal.X, normal.Y, normal.Z, 0,
            origin.X, origin.Y, origin.Z, 1);
    }

    /// <summary>Invisible collision for a box given in a frame (its world bounding box).</summary>
    private void FrameCollider(in float4x4 xf, float3 c, float3 size, bool blocksShots = true)
    {
        var h = size * 0.5f;
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = c + new float3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
            var w = float3.Transform(corner, xf);
            min = float3.Min(min, w);
            max = float3.Max(max, w);
        }
        AddStatic(new Aabb(min, max), default, visible: false, blocksShots: blocksShots);
    }

    // ------------------------------------------------------------------------------------------------
    // Lettering: a 5 x 7 block font, each lit run of a row one thin box.
    // ------------------------------------------------------------------------------------------------


    /// <summary>
    /// Block capitals, <paramref name="height"/> tall and <paramref name="depth"/> proud, centred on <paramref name="center"/>
    /// on a surface facing <paramref name="normal"/>.
    /// </summary>
    private void AddLettering(string text, float3 center, float3 normal, float height, Material material, float3 tint, float depth = 0.004f)
    {
        var px = height / 7f;
        var width = (text.Length * 6 - 1) * px;
        var xf = WallFrame(center, normal);
        var b = PB(material, tint, occludes: false);
        for (var i = 0; i < text.Length; i++)
        {
            if (!BlockFont.TryGet(char.ToUpperInvariant(text[i]), out var glyph))
                continue;
            var left = -width / 2 + i * 6 * px;
            for (var row = 0; row < 7; row++)
            {
                var y = height / 2 - (row + 0.5f) * px;
                var line = glyph[row];
                var col = 0;
                while (col < 5)
                {
                    if (line[col] != '#')
                    {
                        col++;
                        continue;
                    }
                    var start = col;
                    while (col < 5 && line[col] == '#')
                        col++;
                    var run = col - start;
                    OBox(b, xf, new float3(left + (start + run / 2f) * px, y, depth / 2), new float3(run * px + 0.0004f, px * 1.02f, depth));
                }
            }
        }
    }

    /// <summary>A seven-pointed sheriff's star with ball tips and a round centre, on a surface facing <paramref name="normal"/>.</summary>
    private void AddSheriffStar(float3 center, float3 normal, float radius, float3 tint, float3 centreTint)
    {
        var xf = WallFrame(center, normal);
        var b = PB(Material.Plain, tint, occludes: false);
        const int points = 7;
        const float depth = 0.012f;
        var inner = radius * 0.42f;
        var outline = new float3[points * 2];
        for (var i = 0; i < points * 2; i++)
        {
            var a = float.Pi / 2 + float.Pi * i / points;
            var r = i % 2 == 0 ? radius : inner;
            outline[i] = new float3(float.Cos(a) * r, float.Sin(a) * r, 0);
        }
        var front = new float3(0, 0, depth);
        var originW = float3.Transform(front, xf);
        var nFront = float3.Normalize(float3.Transform(new float3(0, 0, 1), xf) - float3.Transform(float3.Zero, xf));
        for (var i = 0; i < outline.Length; i++)
        {
            var p0 = outline[i];
            var p1 = outline[(i + 1) % outline.Length];
            b.Triangle(originW, float3.Transform(p0 + front, xf), float3.Transform(p1 + front, xf), nFront);
            var edge = float3.Normalize(float3.Cross(p1 - p0, new float3(0, 0, 1)));
            var nEdge = float3.Normalize(float3.Transform(edge, xf) - float3.Transform(float3.Zero, xf));
            b.Quad(float3.Transform(p0, xf), float3.Transform(p1, xf), float3.Transform(p1 + front, xf), float3.Transform(p0 + front, xf), nEdge);
        }
        // Balls on the tips, and the raised centre with the department's seal in it.
        for (var i = 0; i < points * 2; i += 2)
            ODisc(b, xf, outline[i] * 1.06f, radius * 0.11f, depth * 1.3f, 10);
        ODisc(PB(Material.Plain, centreTint, occludes: false), xf, new float3(0, 0, depth), radius * 0.3f, 0.004f, 24);
        ODisc(b, xf, new float3(0, 0, depth + 0.004f), radius * 0.2f, 0.003f, 20);
        ODisc(PB(Material.Plain, centreTint, occludes: false), xf, new float3(0, 0, depth + 0.007f), radius * 0.07f, 0.002f, 12);
    }

    // ------------------------------------------------------------------------------------------------
    // Furniture.
    // ------------------------------------------------------------------------------------------------

    private static readonly float3 ChairFabric = new(0.05f, 0.05f, 0.055f);
    private static readonly float3 OfficeBlack = new(0.045f, 0.045f, 0.05f);
    private static readonly float3 Aluminium = new(0.62f, 0.63f, 0.64f);

    /// <summary>
    /// An office chair: five-star base on castors, gas lift, padded seat and mesh back, armrests. <paramref name="f"/> is
    /// on the floor under its centre; whoever sits in it faces the frame's -z.
    /// </summary>
    private void AddOfficeChair(in float4x4 f, float recline = 0.12f)
    {
        using var piece = Piece("office chair");
        var plastic = PB(Material.Polymer, OfficeBlack);
        var fabric = PB(Material.Cloth, ChairFabric);
        var chrome = PB(Material.Steel, new float3(0.32f, 0.32f, 0.33f));
        for (var i = 0; i < 5; i++)
        {
            var a = float.Tau * i / 5 + 0.3f;
            var leg = float4x4.CreateRotationY(a);
            OBox(plastic, leg * f, new float3(0.16f, 0.075f, 0), new float3(0.32f, 0.035f, 0.045f));
            OCyl(plastic, leg * f, new float3(0.31f, 0.0f, 0), new float3(0.31f, 0.055f, 0), 0.026f, 0.026f, 8);
        }
        OCyl(plastic, f, new float3(0, 0.06f, 0), new float3(0, 0.26f, 0), 0.045f, 0.04f, 12);
        OCyl(chrome, f, new float3(0, 0.26f, 0), new float3(0, 0.42f, 0), 0.022f, 0.022f, 10);
        OBox(plastic, f, new float3(0, 0.425f, 0.02f), new float3(0.22f, 0.04f, 0.24f));
        OBox(fabric, f, new float3(0, 0.475f, 0), new float3(0.48f, 0.07f, 0.47f));
        OBox(fabric, f, new float3(0, 0.505f, -0.2f), new float3(0.48f, 0.04f, 0.06f), float4x4.CreateRotationX(-0.25f)); // rolled front edge
        // The back, on a spine from under the seat; reclined a little.
        var back = float4x4.CreateRotationX(recline) * float4x4.CreateTranslation(0, 0.5f, 0.24f) * f;
        OBox(plastic, f, new float3(0, 0.47f, 0.27f), new float3(0.07f, 0.05f, 0.1f));
        OBox(plastic, back, new float3(0, 0.12f, 0.03f), new float3(0.07f, 0.26f, 0.03f));
        OBox(fabric, back, new float3(0, 0.36f, 0), new float3(0.46f, 0.5f, 0.055f));
        OBox(plastic, back, new float3(0, 0.36f, 0.03f), new float3(0.48f, 0.52f, 0.015f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            OBox(plastic, f, new float3(side * 0.27f, 0.6f, 0.02f), new float3(0.03f, 0.2f, 0.05f));
            OBox(plastic, f, new float3(side * 0.27f, 0.7f, -0.01f), new float3(0.065f, 0.028f, 0.25f));
            OBox(plastic, f, new float3(side * 0.2f, 0.5f, 0.02f), new float3(0.14f, 0.025f, 0.05f));
        }
    }

    /// <summary>The kinds of long gun in the armoury.</summary>
    private enum LongGun { Carbine, Shotgun, LessLethal }

    /// <summary>
    /// A long gun in the frame <paramref name="f"/>: the butt at the origin, the muzzle along +x, the top of the gun +y.
    /// </summary>
    private void AddLongGun(in float4x4 f, LongGun kind)
    {
        using var piece = Piece("long gun");
        var black = PB(Material.Polymer, new float3(0.06f, 0.06f, 0.065f));
        var steel = PB(Material.Gunmetal, new float3(0.14f, 0.14f, 0.15f));
        if (kind == LongGun.Carbine)
        {
            // An AR-pattern carbine with a collapsing stock, a red-dot sight and a 30-round magazine.
            OBox(black, f, new float3(0.09f, -0.005f, 0), new float3(0.18f, 0.1f, 0.038f));
            OBox(black, f, new float3(0.01f, -0.005f, 0), new float3(0.02f, 0.12f, 0.042f));
            OCyl(steel, f, new float3(0.16f, 0.03f, 0), new float3(0.31f, 0.03f, 0), 0.016f, 0.016f, 10);
            OBox(steel, f, new float3(0.42f, 0.018f, 0), new float3(0.23f, 0.06f, 0.032f));
            OBox(steel, f, new float3(0.43f, 0.064f, 0), new float3(0.25f, 0.034f, 0.03f));
            OBox(steel, f, new float3(0.43f, 0.087f, 0), new float3(0.22f, 0.012f, 0.022f));
            OCyl(black, f, new float3(0.37f, 0.12f, 0), new float3(0.47f, 0.12f, 0), 0.02f, 0.02f, 10);
            OBox(black, f, new float3(0.42f, 0.098f, 0), new float3(0.05f, 0.012f, 0.02f));
            OBox(black, f, new float3(0.345f, -0.055f, 0), new float3(0.034f, 0.1f, 0.03f), float4x4.CreateRotationZ(0.35f));
            OBox(steel, f, new float3(0.47f, -0.085f, 0), new float3(0.066f, 0.16f, 0.024f), float4x4.CreateRotationZ(-0.12f));
            OBox(steel, f, new float3(0.39f, -0.03f, 0), new float3(0.07f, 0.008f, 0.01f));
            OBox(black, f, new float3(0.69f, 0.045f, 0), new float3(0.3f, 0.056f, 0.05f));
            OBox(black, f, new float3(0.69f, 0.078f, 0), new float3(0.3f, 0.01f, 0.022f));
            OCyl(steel, f, new float3(0.84f, 0.045f, 0), new float3(0.99f, 0.045f, 0), 0.0085f, 0.0085f, 8);
            OBox(steel, f, new float3(0.86f, 0.075f, 0), new float3(0.02f, 0.06f, 0.018f));
            OCyl(steel, f, new float3(0.97f, 0.045f, 0), new float3(1.03f, 0.045f, 0), 0.012f, 0.012f, 8);
            return;
        }

        // A pump shotgun: wooden (or orange, for the less-lethal beanbag gun) stock and fore-end, a long tube under the barrel.
        var furniture = kind == LongGun.LessLethal ? PB(Material.Polymer, new float3(0.85f, 0.38f, 0.06f)) : PB(Material.Veneer, new float3(0.42f, 0.25f, 0.12f));
        OBox(furniture, f, new float3(0.14f, -0.02f, 0), new float3(0.28f, 0.11f, 0.04f), float4x4.CreateRotationZ(0.08f));
        OBox(black, f, new float3(0.005f, -0.03f, 0), new float3(0.015f, 0.13f, 0.045f));
        OBox(furniture, f, new float3(0.31f, -0.035f, 0), new float3(0.1f, 0.06f, 0.035f), float4x4.CreateRotationZ(0.4f));
        OBox(steel, f, new float3(0.43f, 0.02f, 0), new float3(0.24f, 0.07f, 0.036f));
        OBox(steel, f, new float3(0.4f, -0.03f, 0), new float3(0.08f, 0.008f, 0.012f));
        OCyl(steel, f, new float3(0.55f, 0.035f, 0), new float3(1.06f, 0.035f, 0), 0.011f, 0.011f, 8);
        OCyl(steel, f, new float3(0.55f, 0.0f, 0), new float3(0.98f, 0.0f, 0), 0.012f, 0.012f, 8);
        OBox(furniture, f, new float3(0.73f, 0.005f, 0), new float3(0.2f, 0.05f, 0.05f));
        OBox(steel, f, new float3(1.04f, 0.052f, 0), new float3(0.008f, 0.012f, 0.006f));
    }

    /// <summary>A pistol lying on its side (the slide along +x, muzzle +x), in the frame <paramref name="f"/>.</summary>
    private void AddPistolProp(in float4x4 f)
    {
        using var piece = Piece("pistol");
        var black = PB(Material.Polymer, new float3(0.06f, 0.06f, 0.065f));
        var steel = PB(Material.Gunmetal, new float3(0.16f, 0.16f, 0.17f));
        OBox(steel, f, new float3(0.1f, 0.055f, 0), new float3(0.19f, 0.032f, 0.026f));
        OBox(black, f, new float3(0.11f, 0.03f, 0), new float3(0.17f, 0.022f, 0.024f));
        OBox(black, f, new float3(0.035f, -0.025f, 0), new float3(0.035f, 0.11f, 0.028f), float4x4.CreateRotationZ(0.3f));
        OBox(black, f, new float3(0.075f, 0.005f, 0), new float3(0.045f, 0.008f, 0.012f));
    }
}
