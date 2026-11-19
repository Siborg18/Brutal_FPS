using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Inside the jungle's storage shack: its plank door (E, in the east wall), the casing the olive portal stands in on the
/// wall outside, the PC wired to it through that wall on a plank desk (<see cref="JunglePcZone"/>, a <see cref="PcTerminal"/>),
/// and a whiteboard on the west wall: a kanban board in red tape with sticky notes (a texture, painted by
/// Tools/make_whiteboard.ps1), a tray of pads and a pen under it. Out here a 32-bit float only resolves about 2 mm, so
/// fine layers are kept centimetres apart (at a slant, less than that z-fights).
/// </summary>
public sealed partial class Level
{
    // (1 m wide, not the narrowest 0.9 m: with the leaf open beside it, that left people a single line of nav cells to come
    // and go by, and one of them standing in it to shoot kept the rest out.)
    private const float BodegaDoorZ0 = 1.7f, BodegaDoorZ1 = 2.7f, BodegaDoorHeight = 2.0f;

    private static readonly float3 OliveGlow = new(0.62f, 0.68f, 0.18f);

    private PrisonDoor? _bodegaDoor;

    /// <summary>"[E] Open door" / "[E] Close door" while the player is near the shack's door, otherwise null.</summary>
    public string? BodegaDoorPrompt { get; private set; }

    /// <summary>Debug: whether <paramref name="p"/> is inside the shack's walls.</summary>
    public static bool BodegaInside(float3 p) =>
        p.X > BodegaMin.X + BodegaWall && p.X < BodegaMax.X - BodegaWall && p.Z > BodegaMin.Z + BodegaWall && p.Z < BodegaMax.Z - BodegaWall;

    /// <summary>Where the player stands to use the PC behind the olive portal: in front of its desk.</summary>
    public static Aabb JunglePcZone => new(JungleOrigin + new float3(-1.2f, -0.5f, 1.0f), JungleOrigin + new float3(1.2f, 3f, 2.3f));

    /// <summary>The shack's bare bulb (it only lights the inside).</summary>
    private static LocalLight BodegaBulb => new(
        JungleOrigin + new float3(0, 2.0f, 1.9f), new float3(1.0f, 0.82f, 0.58f), 2.2f, 6f,
        new Aabb(BodegaMin + new float3(0.05f, -0.1f, 0.05f), BodegaMax - new float3(0.03f, 0.05f, 0.05f)));

    /// <summary>The dim inside of the shack, the sky's light fading away from its door. (Only just past the door's inside face: the walls' outsides stay in daylight.)</summary>
    private static ShadeVolume BodegaShade => new(
        new Aabb(BodegaMin + new float3(0.05f, -0.05f, 0.05f), BodegaMax - new float3(0.035f, 0.05f, 0.05f)),
        new float3(BodegaMax.X + 0.4f, 1.1f, JungleOrigin.Z + 2.2f), 1.6f, 0.25f);

    private void BuildBodegaInterior()
    {
        BuildBodegaDoor();
        BuildPortalSurround();
        BuildBodegaPc();
        BuildBodegaWhiteboard();

        var o = JungleOrigin;
        // Bare planks underfoot (flush enough to walk over).
        Add(o + new float3(0, 0.01f, 1.8f), new float3(5.2f - 2 * BodegaWall, 0.03f, 3.3f), PlankTint * 0.6f, Material.Wood, collides: false);
        // The bulb on its flex, and stores in the far corner.
        var flex = JB(Material.Plain, new float3(0.03f, 0.03f, 0.03f));
        OCyl(flex, float4x4.Identity, o + new float3(0, 2.1f, 1.9f), o + new float3(0, 2.48f, 1.9f), 0.006f, 0.006f, 6);
        OBox(JB(Material.Emissive, new float3(1.0f, 0.86f, 0.62f)), float4x4.Identity, o + new float3(0, 2.05f, 1.9f), new float3(0.07f, 0.09f, 0.07f));
        // (Against the south wall, well clear of the door: in the corner by it, they left a way in only 0.2 m wide.)
        AddSack(o + new float3(0.15f, 0, 3.1f), 0.5f);
        AddSack(o + new float3(-0.35f, 0, 3.15f), -0.3f);
        AddCollider(o + new float3(-0.1f, 0.35f, 3.12f), new float3(1.2f, 0.7f, 0.6f));

        FlushPoliceBatches();
    }

    // ------------------------------------------------------------------------------------------------ the door

    /// <summary>The plank door in the east wall: ledged and braced, hinged on its south edge, swinging out to rest by the rain barrel.</summary>
    private void BuildBodegaDoor()
    {
        var o = JungleOrigin;
        var max = BodegaMax;
        float z0 = o.Z + BodegaDoorZ0, z1 = o.Z + BodegaDoorZ1;
        var w = z1 - z0;
        var h = BodegaDoorHeight;
        CarveOpening(new Aabb(new float3(max.X - BodegaWall - 0.02f, 0, z0), new float3(max.X + 0.02f, h, z1)));

        // The frame round the opening, and last week's padlock left hanging on it.
        foreach (var z in (ReadOnlySpan<float>)[z0 - 0.04f, z1 + 0.04f])
            Add(new float3(max.X + 0.03f, h / 2, z), new float3(0.08f, h + 0.1f, 0.08f), PoleTint * 0.75f, Material.Wood, collides: false);
        Add(new float3(max.X + 0.03f, h + 0.04f, (z0 + z1) / 2), new float3(0.08f, 0.08f, w + 0.16f), PoleTint * 0.75f, Material.Wood, collides: false);
        Add(new float3(max.X + 0.08f, 1.05f, z0 - 0.04f), new float3(0.02f, 0.05f, 0.05f), new float3(0.25f, 0.24f, 0.22f), Material.Steel, collides: false);
        Add(new float3(max.X + 0.1f, 0.98f, z0 - 0.04f), new float3(0.03f, 0.07f, 0.06f), new float3(0.55f, 0.48f, 0.25f), Material.Steel, collides: false);

        // Built in the hinge's frame: the leaf runs north (-z) from the hinge, its outer face at x = 0.
        var id = float4x4.Identity;
        const float thick = 0.05f;
        var c = new float3(-thick / 2, h / 2, -w / 2);
        // (Open, square to the wall: at -85 degrees its far end leant back across the way through.)
        var leaf = HingedLeaf(new float3(max.X - 0.01f, 0, z1), float.DegreesToRadians(-90f), c, new float3(thick, h, w));
        var boards = leaf.B(Material.Wood, PlankTint * 0.75f);
        var ledges = leaf.B(Material.Wood, PlankTint * 0.62f);
        var iron = leaf.B(Material.Steel, new float3(0.25f, 0.24f, 0.22f));
        OBox(boards, id, c, new float3(thick, h - 0.01f, w - 0.01f));
        foreach (var x in (ReadOnlySpan<float>)[0.012f, -thick - 0.012f])
        {
            foreach (var y in (ReadOnlySpan<float>)[0.25f, 1.75f])
                OBox(ledges, id, new float3(x, y, -w / 2), new float3(0.024f, 0.12f, w - 0.04f));
            var run = w - 0.04f;
            const float rise = 1.38f;
            OBox(ledges, id, new float3(x, 1f, -w / 2), new float3(0.024f, 0.1f, float.Sqrt(run * run + rise * rise)),
                 float4x4.CreateRotationX(float.Atan2(rise, run)));
        }
        // Strap hinges, the hasp and a pull handle on each face.
        foreach (var y in (ReadOnlySpan<float>)[0.25f, 1.75f])
            OBox(iron, id, new float3(0.03f, y, -0.18f), new float3(0.012f, 0.05f, 0.36f));
        OBox(iron, id, new float3(0.012f, 1.05f, -w + 0.09f), new float3(0.024f, 0.08f, 0.16f));
        foreach (var x in (ReadOnlySpan<float>)[0.035f, -thick - 0.035f])
        {
            OBox(iron, id, new float3(x, 1.0f, -w + 0.09f), new float3(0.02f, 0.2f, 0.024f));
            foreach (var y in (ReadOnlySpan<float>)[0.91f, 1.09f])
                OBox(iron, id, new float3(x > 0 ? x / 2 : (x - thick) / 2, y, -w + 0.09f), new float3(0.03f, 0.024f, 0.024f));
        }
        _bodegaDoor = NewDoor("shack", 0.9f, leaf);
        MoveDoor(_bodegaDoor, false, instant: true);
    }

    /// <summary>Near the shack's door, E opens or shuts it. It won't shut on the player standing in the doorway.</summary>
    public void UpdateBodegaDoor(float dt, bool usePressed, float3 player)
    {
        if (_bodegaDoor is not { } door)
            return;
        var max = BodegaMax;
        float z0 = JungleOrigin.Z + BodegaDoorZ0, z1 = JungleOrigin.Z + BodegaDoorZ1;
        var near = player.X > max.X - 1.4f && player.X < max.X + 2.2f && player.Z > z0 - 1f && player.Z < z1 + 1f && player.Y > -1f && player.Y < 3f;
        var inDoorway = player.X > max.X - BodegaWall - 0.4f && player.X < max.X + 0.4f && player.Z > z0 - 0.35f && player.Z < z1 + 0.35f;
        var open = door.Target > 0.5f;
        BodegaDoorPrompt = near ? (open ? "[E] Close door" : "[E] Open door") : null;
        if (near && usePressed && !(inDoorway && open) && MoveDoor(door, !open))
            Log.Write(open ? "[DOOR] Shutting the shack's door" : "[DOOR] Opening the shack's door");
        StepDoor(door, dt);
    }

    // ------------------------------------------------------------------------------------------------ the portal's casing

    /// <summary>
    /// The plank casing the portal stands in, built out from the north wall (<see cref="JunglePortalDepth"/>): the wall
    /// behind it stays whole, with room behind the water for the player's body as they walk in.
    /// </summary>
    private void BuildPortalSurround()
    {
        var o = JungleOrigin;
        var r = PortalSystem.PortalRadius + 0.005f;   // (just clear of the cut the portal makes, which would eat them)
        const float side = 0.12f;
        var top = PortalSystem.CenterHeight + r;
        var z = BodegaMin.Z - JunglePortalDepth / 2;
        foreach (var sign in (ReadOnlySpan<float>)[-1f, 1f])
            Add(new float3(o.X + sign * (r + side / 2), (top + side) / 2, z), new float3(side, top + side, JunglePortalDepth), PlankTint * 0.85f, Material.Wood);
        Add(new float3(o.X, top + side / 2, z), new float3(2 * r, side, JunglePortalDepth), PlankTint * 0.85f, Material.Wood);
    }

    // ------------------------------------------------------------------------------------------------ the PC

    /// <summary>A plank desk against the north wall: a monitor, a keyboard, a tower under it, the wire through to the portal, a stool.</summary>
    private void BuildBodegaPc()
    {
        // x right, y up, z out from the wall into the room.
        var desk = WallFrame(new float3(JungleOrigin.X, 0, BodegaMin.Z + BodegaWall), new float3(0, 0, 1));
        var top = JB(Material.Wood, PlankTint * 0.85f);
        var legs = JB(Material.Wood, PoleTint * 0.8f);
        OBox(top, desk, new float3(0, 0.735f, 0.39f), new float3(1.8f, 0.04f, 0.76f));
        foreach (var x in (ReadOnlySpan<float>)[-0.84f, 0.84f])
            foreach (var z in (ReadOnlySpan<float>)[0.07f, 0.71f])
                OBox(legs, desk, new float3(x, 0.357f, z), new float3(0.07f, 0.715f, 0.07f));
        OBox(legs, desk, new float3(0, 0.66f, 0.07f), new float3(1.65f, 0.08f, 0.03f));
        FrameCollider(desk, new float3(0, 0.38f, 0.39f), new float3(1.8f, 0.76f, 0.76f));

        // The monitor, showing the portal: its ring, a link trace and a column of readouts.
        var plastic = JB(Material.Polymer, new float3(0.06f, 0.06f, 0.065f));
        var monitor = float4x4.CreateTranslation(0.1f, 0.755f, 0.26f) * desk;
        OBox(plastic, monitor, new float3(0, 0.012f, 0), new float3(0.28f, 0.024f, 0.2f));
        OBox(plastic, monitor, new float3(0, 0.15f, -0.065f), new float3(0.06f, 0.28f, 0.03f));
        // Everything on it stands well clear of what is behind it: at a slant, out here, a centimetre isn't enough to tell them apart.
        OBox(plastic, monitor, new float3(0, 0.36f, -0.03f), new float3(0.7f, 0.42f, 0.04f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            OBox(plastic, monitor, new float3(side * 0.3375f, 0.36f, 0.0125f), new float3(0.025f, 0.42f, 0.045f));
        OBox(plastic, monitor, new float3(0, 0.56f, 0.0125f), new float3(0.65f, 0.02f, 0.045f));
        OBox(plastic, monitor, new float3(0, 0.165f, 0.0125f), new float3(0.65f, 0.03f, 0.045f));
        OBox(JB(Material.Emissive, new float3(0.015f, 0.03f, 0.02f)), monitor, new float3(0, 0.365f, 0f), new float3(0.65f, 0.37f, 0.02f));
        var ring = JB(Material.Emissive, OliveGlow);
        var trace = JB(Material.Emissive, new float3(0.25f, 0.85f, 0.4f));
        for (var i = 0; i < 28; i++)
        {
            var a = float.Tau * i / 28;
            OBox(ring, monitor, float3.Zero, new float3(0.022f, 0.006f, 0.002f),
                 float4x4.CreateRotationZ(a + float.Pi / 2) * float4x4.CreateTranslation(-0.17f + float.Cos(a) * 0.1f, 0.38f + float.Sin(a) * 0.1f, 0.04f));
        }
        OBox(trace, monitor, new float3(-0.17f, 0.38f, 0.04f), new float3(0.03f, 0.03f, 0.002f));
        for (var i = 0; i < 9; i++)
            OBox(trace, monitor, new float3(0.14f, 0.5f - i * 0.028f, 0.04f), new float3(0.2f - (i * 37 % 5) * 0.03f, 0.01f, 0.002f));
        for (var i = 0; i < 16; i++)
        {
            var bar = 0.012f + 0.035f * float.Abs(float.Sin(i * 0.9f));
            OBox(ring, monitor, new float3(0.04f + i * 0.016f, 0.205f + bar / 2, 0.04f), new float3(0.01f, bar, 0.002f));
        }

        // Keyboard, mouse, a mug; the tower on the floor under the desk, its lights at the front.
        OBox(plastic, desk, new float3(0.1f, 0.767f, 0.58f), new float3(0.44f, 0.024f, 0.15f));
        OBox(plastic, desk, new float3(0.42f, 0.77f, 0.58f), new float3(0.06f, 0.03f, 0.1f));
        OCyl(JB(Material.Plain, new float3(0.62f, 0.6f, 0.52f)), desk, new float3(0.68f, 0.755f, 0.42f), new float3(0.68f, 0.85f, 0.42f), 0.04f, 0.04f, 12);
        var tower = new float3(0.58f, 0, 0.38f);
        OBox(plastic, desk, tower + new float3(0, 0.23f, 0), new float3(0.2f, 0.46f, 0.46f));
        OBox(JB(Material.Emissive, new float3(0.3f, 1.0f, 0.4f)), desk, tower + new float3(0.05f, 0.41f, 0.24f), new float3(0.014f, 0.014f, 0.01f));

        // The monitor's lead down behind the desk to the tower.
        var wire = JB(Material.Polymer, new float3(0.03f, 0.03f, 0.03f));
        OCyl(wire, desk, new float3(0.1f, 0.85f, 0.2f), new float3(0.45f, 0.77f, 0.006f), 0.006f, 0.006f, 6);
        OCyl(wire, desk, new float3(0.45f, 0.77f, 0.006f), tower + new float3(0, 0.3f, -0.23f), 0.006f, 0.006f, 6);

        // The wire to the portal: from the tower up the wall, along it and out through a hole, into the portal's casing.
        const float wireY = 1.1f, holeX = 1.55f, r = 0.011f;
        var outside = -BodegaWall - 0.06f;
        var casing = PortalSystem.PortalRadius + 0.125f;
        (float3 From, float3 To)[] runs =
        [
            (tower + new float3(0.05f, 0.25f, -0.23f), new float3(1.0f, 0.2f, 0.014f)),
            (new float3(1.0f, 0.2f, 0.014f), new float3(1.0f, wireY, 0.014f)),
            (new float3(1.0f, wireY, 0.014f), new float3(holeX, wireY, 0.014f)),
            (new float3(holeX, wireY, 0.014f), new float3(holeX, wireY, outside)),
            (new float3(holeX, wireY, outside), new float3(casing, wireY, outside)),
        ];
        foreach (var (from, to) in runs)
            OCyl(wire, desk, from, to, r, r, 8);
        var fitting = JB(Material.Steel, new float3(0.22f, 0.22f, 0.23f));
        // A grommet each side of the wall; clips holding it to the planks.
        OCyl(fitting, desk, new float3(holeX, wireY, 0f), new float3(holeX, wireY, 0.012f), 0.026f, 0.026f, 10);
        OCyl(fitting, desk, new float3(holeX, wireY, -BodegaWall), new float3(holeX, wireY, -BodegaWall - 0.012f), 0.026f, 0.026f, 10);
        foreach (var at in (ReadOnlySpan<float3>)[new(1.0f, 0.55f, 0.012f), new(1.0f, 0.9f, 0.012f), new(1.28f, wireY, 0.012f)])
            OBox(fitting, desk, at, new float3(0.035f, 0.035f, 0.03f));

        // A stool, pulled out to one side.
        var stool = float4x4.CreateTranslation(0.62f, 0, 1.05f) * desk;
        OCyl(top, stool, new float3(0, 0.44f, 0), new float3(0, 0.48f, 0), 0.17f, 0.17f, 14);
        for (var i = 0; i < 3; i++)
        {
            var a = float.Tau * i / 3 + 0.4f;
            OCyl(legs, stool, new float3(float.Cos(a) * 0.1f, 0.44f, float.Sin(a) * 0.1f), new float3(float.Cos(a) * 0.17f, 0, float.Sin(a) * 0.17f), 0.016f, 0.016f, 6);
        }
        FrameCollider(stool, new float3(0, 0.24f, 0), new float3(0.32f, 0.48f, 0.32f), blocksShots: false);
    }

    // ------------------------------------------------------------------------------------------------ the whiteboard

    /// <summary>The whiteboard on the west wall: its face one textured quad, an aluminium frame, and a tray of pads and a pen under it.</summary>
    private void BuildBodegaWhiteboard()
    {
        const float width = 1.6f, height = 1.2f, y = 1.55f, z = 1.8f;
        var wallX = BodegaMin.X + BodegaWall;
        // x right (north, as you face the wall), y up, z out of the wall.
        var wall = WallFrame(new float3(wallX, y, JungleOrigin.Z + z), new float3(1, 0, 0));

        var alu = JB(Material.Steel, new float3(0.38f, 0.36f, 0.32f));
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            OBox(alu, wall, new float3(side * (width / 2 + 0.012f), 0, 0.025f), new float3(0.024f, height + 0.048f, 0.05f));
            OBox(alu, wall, new float3(0, side * (height / 2 + 0.012f), 0.025f), new float3(width, 0.024f, 0.05f));
        }

        // The board's face, inside its frame with nothing behind it but the wall 4 cm back.
        const float face = 0.04f;
        float3 At(float u, float v) => float3.Transform(new float3((u - 0.5f) * width, (v - 0.5f) * height, face), wall);
        var tangent = float3.Normalize(At(1, 0) - At(0, 0));
        ReadOnlySpan<(float U, float V)> corners = [(0, 0), (1, 0), (1, 1), (0, 0), (1, 1), (0, 1)];
        var vertices = new List<float>(corners.Length * Mesh.FullStride);
        foreach (var (u, v) in corners)
        {
            var p = At(u, v);
            vertices.AddRange([p.X, p.Y, p.Z, 1, 0, 0, u, v, tangent.X, tangent.Y, tangent.Z, 1]);
        }
        var painted = new PartMaterial(Material.Custom, float3.One, false, 0.4f, 0f, TextureLibrary.Current?.GetCustom("whiteboard"));
        _props.Add(new Prop(new Mesh([.. vertices], Mesh.FullStride), float4x4.Identity, float3.One, Material.Custom, Part: painted));

        // The tray under it: six slots of sticky pads, a pen in the first, as on the wall it was copied from. The pads are
        // the size of the notes on the board (290 of the texture's 2048 pixels across).
        const float pad = 290f / 2048f * width, step = pad + 0.02f;
        var tray = float4x4.CreateTranslation(0, -height / 2 - 0.035f - step - 0.01f, 0) * wall;
        var black = JB(Material.Polymer, new float3(0.09f, 0.085f, 0.075f));
        OBox(black, tray, new float3(0, 0, 0.015f), new float3(3 * step + 0.02f, 2 * step + 0.02f, 0.03f));
        OBox(black, tray, new float3(0, -step - 0.005f, 0.04f), new float3(3 * step + 0.02f, 0.015f, 0.05f));
        (float3 Tint, float X, float Y)[] pads =
        [
            (new float3(0.72f, 0.46f, 0.5f), 0f, step / 2), (new float3(0.5f, 0.62f, 0.3f), step, step / 2),
            (new float3(0.74f, 0.64f, 0.28f), -step, -step / 2), (new float3(0.72f, 0.5f, 0.3f), 0f, -step / 2), (new float3(0.56f, 0.47f, 0.62f), step, -step / 2),
        ];
        foreach (var (tint, px, py) in pads)
            OBox(JB(Material.Plain, tint), tray, new float3(px, py, 0.045f), new float3(pad, pad, 0.03f));
        var pen = JB(Material.PaintedMetal, new float3(0.1f, 0.2f, 0.5f));
        OCyl(pen, tray, new float3(-step - 0.01f, 0.02f, 0.05f), new float3(-step + 0.005f, 0.19f, 0.05f), 0.007f, 0.007f, 8);
        OBox(JB(Material.Steel, new float3(0.6f, 0.6f, 0.62f)), tray, new float3(-step + 0.012f, 0.16f, 0.06f), new float3(0.006f, 0.05f, 0.012f));
    }
}
