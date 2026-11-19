using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The yard's card game: a white plastic garden table and four chairs (Tools/blender/build_garden_furniture.py) in the lane
/// between the lone green container and the east stacks, a little way past the pallets. Three of the yard crew sit at it
/// playing cards (<see cref="Npcs"/>: AddYardCrew, Game/NpcSeat.cs) until something alerts them; the fourth chair is
/// pushed back, empty. On the table: the deck, the cards played, their hands' discards, some cash and cans.
/// </summary>
public sealed partial class Level
{
    /// <summary>The middle of the card table, on the ground.</summary>
    public static float3 YardCardTable => YardOrigin + new float3(20.9f, 0, 4.4f);

    private const float CardTableTop = 0.72f;   // build_garden_furniture.py: TABLE_Z
    private const float CardTableRadius = 0.46f;
    private const float CardChairOut = 0.80f;   // m from the table's middle to a chair's (seatedlib.py: TABLE_AT)

    /// <summary>The chairs that are sat in: where each sitter's feet are (under the middle of the seat) and which way they face.</summary>
    public static IReadOnlyList<(float3 Seat, float Heading)> YardCardSeats => _cardSeats ??= MakeCardSeats();
    private static (float3, float)[]? _cardSeats;

    // Round the table, from the north: the one facing back down the lane towards the pallets, then east (back to the
    // stacks), then south (back to the pallets). The west one, by the container, is the empty one.
    private static readonly float[] CardChairAngles = [0.15f, float.Pi / 2 + 0.1f, float.Pi - 0.12f];
    private const float EmptyChairAngle = -float.Pi / 2 + 0.2f;

    private static (float3, float)[] MakeCardSeats() =>
        [.. CardChairAngles.Select(a => (YardCardTable + Out(a) * CardChairOut, FacingIn(a)))];

    /// <summary>From the table's middle out towards a chair at angle <paramref name="a"/> (0 = +Z, pi/2 = +X).</summary>
    private static float3 Out(float a) => new(float.Sin(a), 0, float.Cos(a));

    /// <summary>The heading (0 looks along +Z) that looks back in from there to the table.</summary>
    private static float FacingIn(float a) => a + float.Pi;

    private Mesh? _cardMesh, _canMesh;

    private void BuildYardCardTable()
    {
        var table = TryModel("garden_table");
        var chair = TryModel("garden_chair");
        var c = YardCardTable;
        var white = new float3(0.92f, 0.92f, 0.90f);

        // The table: solid to walk into, but only the top stops a shot (the legs are thin).
        AddCollider(c + new float3(0, CardTableTop / 2, 0), new float3(0.8f, CardTableTop, 0.8f), blocksShots: false);
        AddCollider(c + new float3(0, CardTableTop - 0.025f, 0), new float3(0.86f, 0.05f, 0.86f));
        if (table is not null)
            AddModel(table, float4x4.CreateTranslation(c), white);
        else
            Add(c + new float3(0, CardTableTop - 0.02f, 0), new float3(0.9f, 0.04f, 0.9f), white, Material.Plain);

        // The chairs. The ones sat in have no collision of their own (the sitter stands up out of it, and they'd only trap
        // them); the empty one is pushed back from the table and turned, and is solid.
        foreach (var (seat, heading) in YardCardSeats)
            AddChair(chair, seat, heading, white, solid: false);
        var empty = c + Out(EmptyChairAngle) * (CardChairOut + 0.3f);
        AddChair(chair, empty, FacingIn(EmptyChairAngle) + 0.5f, white, solid: true);

        BuildCardsOnTable(c);
    }

    private void AddChair(Engine.Model? chair, float3 at, float heading, float3 tint, bool solid)
    {
        if (solid)
            AddCollider(at + new float3(0, 0.22f, 0), new float3(0.5f, 0.44f, 0.46f), blocksShots: false);
        if (chair is not null)
        {
            AddModel(chair, float4x4.CreateRotationY(heading) * float4x4.CreateTranslation(at), tint);
            return;
        }

        var f = Out(heading);
        Add(at + new float3(0, 0.41f, 0), new float3(0.46f, 0.04f, 0.46f), tint, Material.Plain, collides: false);
        Add(at - f * 0.21f + new float3(0, 0.62f, 0), new float3(0.46f, 0.4f, 0.04f), tint, Material.Plain, collides: false);
    }

    /// <summary>A card (or a note, a can) lying on the table: centre <paramref name="at"/> on the top, turned by <paramref name="yaw"/>.</summary>
    private void AddFlat(float3 at, float3 size, float yaw, float3 tint, Material material = Material.Plain)
    {
        _cardMesh ??= new MeshBuilder().Box(float3.Zero, float3.One).Build();
        var model = float4x4.CreateScale(size) * float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(at + new float3(0, size.Y / 2, 0));
        _props.Add(new Prop(_cardMesh, model, tint, material));
    }

    private void BuildCardsOnTable(float3 c)
    {
        var top = c + new float3(0, CardTableTop + 0.001f, 0);
        var card = new float3(0.063f, 0.0012f, 0.088f);
        var face = new float3(0.93f, 0.92f, 0.88f);
        var back = new float3(0.62f, 0.10f, 0.10f);
        var random = new Random(52);
        float Jitter(float a) => (float)(random.NextDouble() * 2 - 1) * a;

        // The deck, face down, off to one side of the parasol hole; one card dealt crooked off it.
        AddFlat(top + new float3(0.13f, 0, -0.10f), new float3(0.063f, 0.022f, 0.088f), 0.3f, face);
        AddFlat(top + new float3(0.13f, 0.022f, -0.10f), card, 0.32f, back);
        AddFlat(top + new float3(0.20f, 0, -0.17f), card, 0.9f, back);

        // The cards played, in a loose heap in the middle, face up.
        for (var i = 0; i < 9; i++)
        {
            var at = new float3(-0.10f + Jitter(0.07f), i * 0.0013f, 0.07f + Jitter(0.07f));
            AddFlat(top + at, card, Jitter(float.Pi), i % 4 == 0 ? back : face);
        }

        // In front of each player: their tricks (a small face-down pile), a note or two, and a can.
        _canMesh ??= new MeshBuilder().Cylinder(0.033f, -0.06f, 0.06f, 16).Build();
        float3[] cans = [new(0.55f, 0.09f, 0.07f), new(0.12f, 0.25f, 0.55f), new(0.75f, 0.70f, 0.66f)];
        for (var p = 0; p < CardChairAngles.Length; p++)
        {
            var a = CardChairAngles[p];
            var outward = Out(a);
            var left = new float3(outward.Z, 0, -outward.X);
            var pile = top + outward * 0.27f + left * 0.08f;
            for (var k = 0; k < 4; k++)
                AddFlat(pile + new float3(0, k * 0.0013f, 0), card, a + Jitter(0.15f), back);
            AddFlat(top + outward * 0.30f - left * 0.12f, new float3(0.07f, 0.001f, 0.14f), a + 0.4f + Jitter(0.3f), new float3(0.45f, 0.55f, 0.42f));
            var can = top + outward * 0.36f - left * 0.26f;
            _props.Add(new Prop(_canMesh, float4x4.CreateRotationX(-float.Pi / 2) * float4x4.CreateTranslation(can + new float3(0, 0.06f, 0)),
                cans[p], Material.PaintedMetal));
        }
        // A can on its side, empty, by the empty chair.
        var spent = top + Out(EmptyChairAngle) * 0.33f + new float3(0, 0.033f, 0);
        _props.Add(new Prop(_canMesh, float4x4.CreateRotationY(1.1f) * float4x4.CreateTranslation(spent), cans[0], Material.PaintedMetal));
    }

    /// <summary>
    /// Where a card player's gun lies while they're sat down: on the ground by their right hand, on its side, pointing the
    /// way they face (the gun's model space: +X the muzzle, +Y its top, +Z its right; <paramref name="gripLocal"/> its grip).
    /// </summary>
    public static float4x4 GunBesideChair(float3 seat, float heading, float3 gripLocal)
    {
        var forward = new float3(float.Sin(heading), 0, float.Cos(heading));
        var left = new float3(forward.Z, 0, -forward.X);       // (the figure faces +Z with its left at +X)
        var basis = new float4x4(
            forward.X, forward.Y, forward.Z, 0,
            left.X, left.Y, left.Z, 0,                          // its top towards the chair...
            0, 1, 0, 0,                                         // ...lying on its left side
            0, 0, 0, 1);
        var grip = seat - left * 0.45f + forward * 0.05f + new float3(0, 0.035f, 0);
        return float4x4.CreateTranslation(-gripLocal) * basis * float4x4.CreateTranslation(grip);
    }
}
