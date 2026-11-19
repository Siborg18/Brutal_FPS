using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What moves on the far bank of the dock across from the Antwerp yard (<see cref="Level.BuildFarBank"/>): each
/// ship-to-shore crane works its ship, its trolley running out along the boom, the spreader going down on its ropes for a
/// box, lifting it and carrying it back to set it down on the quay, over and over; a tug goes up and down the dock; the
/// cranes' red beacons blink, and at night the cabs and the tug's lamps are lit. Drawn whenever the yard is in view.
/// </summary>
public sealed class Harbour
{
    // One crane's cycle (s): out to the ship, down, latch on, up, back over the quay, down, let go, up.
    private static readonly float[] Phases = [14f, 9f, 3f, 9f, 14f, 9f, 3f, 8f];
    private static readonly float Cycle = Phases.Sum();

    private const float HighY = 38f;                       // the spreader's bottom when it's carried along
    private const float TrolleyY = Level.StsBoomY + 1.25f;  // between the girders
    private static readonly float PickY = Level.FarShipDeckY + 1.5f + 3 * 2.59f;   // on top of the stacks being cleared
    private const float BoxHeight = 2.59f;

    private static readonly float3 SpreaderYellow = new(0.85f, 0.66f, 0.08f);
    private static readonly float3 Rope = new(0.05f, 0.05f, 0.05f);
    private static readonly float3 CabWhite = new(0.8f, 0.8f, 0.77f);
    private static readonly float3 BeaconRed = new(1.0f, 0.08f, 0.04f), BeaconOff = new(0.25f, 0.03f, 0.02f);

    private readonly Model? _box;
    private readonly bool _night;
    private float _time;

    public Harbour()
    {
        try
        {
            _box = Model.Get("container_lod");
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] Harbour: no container model ({e.Message}); the cranes carry nothing.");
        }
        _night = TimeOfDay.IsNight(DayNightLevel.Yard);
    }

    public void Update(float dt) => _time += dt;

    public void Draw(Renderer renderer, Zones zones)
    {
        if ((zones & Zones.Yard) == 0)
            return;
        var o = Level.YardOrigin;
        for (var i = 0; i < Level.StsCraneZ.Length; i++)
            DrawCrane(renderer, o, i);
        DrawTug(renderer, o);
    }

    /// <summary>
    /// Where crane <paramref name="i"/> is in its cycle: trolley x, the spreader's bottom, whether it holds a box (the
    /// cycle's), and which cycle's box is waiting on the quay (the last one set down, until the next is lifted), if any.
    /// </summary>
    private (float X, float Y, bool Carrying, int Cycle, int? OnQuay) CraneState(int i)
    {
        var t = _time + i * 11.7f;
        var n = (int)(t / Cycle);
        t -= n * Cycle;
        // A different row of the ship each time round.
        var row = (int)((uint)(n * 7919 + i * 104729) % 9) - 4;
        var shipX = Level.FarShipX + row * 2.5f;
        var quayX = Level.StsQuayLaneX;
        var phase = 0;
        while (phase < Phases.Length - 1 && t >= Phases[phase])
            t -= Phases[phase++];
        var s = Smooth(t / Phases[phase]);
        int? before = n > 0 ? n - 1 : null;
        return phase switch
        {
            0 => (Lerp(quayX, shipX, s), HighY, false, n, before),
            1 => (shipX, Lerp(HighY, PickY, s), false, n, before),
            2 => (shipX, PickY, s > 0.5f, n, before),
            3 => (shipX, Lerp(PickY, HighY, s), true, n, null),
            4 => (Lerp(shipX, quayX, s), HighY, true, n, null),
            5 => (quayX, Lerp(HighY, BoxHeight, s), true, n, null),
            6 => (quayX, BoxHeight, s < 0.5f, n, s < 0.5f ? null : n),
            _ => (quayX, Lerp(BoxHeight, HighY, s), false, n, n),
        };
    }

    private void DrawCrane(Renderer renderer, float3 o, int i)
    {
        var cz = Level.StsCraneZ[i];
        var (x, y, carrying, cycle, onQuay) = CraneState(i);
        float3 P(float px, float py, float pz) => o + new float3(px, py, cz + pz);

        // Trolley between the girders, the cab hung under one side of it, the ropes down to the spreader.
        Cube(renderer, P(x, TrolleyY, 0), new float3(6f, 2.2f, 5.6f), Level.StsPaint * 0.85f, Material.PaintedMetal);
        Cube(renderer, P(x - 1.5f, Level.StsBoomY - 1.6f, 4.6f), new float3(3f, 2.8f, 2.6f), CabWhite, Material.PaintedMetal);
        Cube(renderer, P(x - 0.2f, Level.StsBoomY - 1.4f, 4.6f), new float3(0.05f, 1.2f, 2.2f),
            _night ? new float3(0.9f, 0.8f, 0.55f) : new float3(0.08f, 0.09f, 0.1f), _night ? Material.Emissive : Material.Glass);
        var ropeTop = TrolleyY - 1.1f;   // (the trolley's underside)
        var ropeBottom = y + 0.6f;
        foreach (var dx in (ReadOnlySpan<float>)[-0.9f, 0.9f])
            foreach (var dz in (ReadOnlySpan<float>)[-2.2f, 2.2f])
                Cube(renderer, P(x + dx, (ropeTop + ropeBottom) / 2, dz), new float3(0.07f, ropeTop - ropeBottom, 0.07f), Rope, Material.Steel);
        Cube(renderer, P(x, y + 0.3f, 0), new float3(2.6f, 0.6f, 12.3f), SpreaderYellow, Material.PaintedMetal);

        // The box it's carrying, and the last one it set down on the quay (taken away while the next comes off the ship).
        if (_box is not null)
        {
            if (carrying)
                renderer.DrawModel(_box, float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(P(x, y - BoxHeight, 0)), BoxColor(i, cycle));
            if (onQuay is { } dropped)
                renderer.DrawModel(_box, float4x4.CreateRotationY(float.Pi / 2) * float4x4.CreateTranslation(P(Level.StsQuayLaneX, 0, 0)), BoxColor(i, dropped));
        }

        // The aircraft-warning beacon on top of the A-frame.
        var on = ((_time + i * 0.37f) % 1.6f) < 0.5f;
        Cube(renderer, P(Level.StsApex.X, Level.StsApex.Y + 1.2f, 0), new float3(0.7f, 0.7f, 0.7f), on ? BeaconRed : BeaconOff, Material.Emissive);
    }

    private static float3 BoxColor(int crane, int cycle) =>
        Level.FarContainerColors[(int)((uint)(cycle * 31 + crane * 17) % (uint)Level.FarContainerColors.Length)];

    /// <summary>A tug going round the dock: north on the far side, south on the near side, turning at either end out of sight.</summary>
    private void DrawTug(Renderer renderer, float3 o)
    {
        const float period = 600f;
        var a = float.Tau * _time / period;
        var pos = o + new float3(-160f + 22f * float.Cos(a), Level.YardSeaLevel, 420f * float.Sin(a));
        var velocity = new float3(-22f * float.Sin(a), 0, 420f * float.Cos(a));
        var yaw = float.Atan2(velocity.X, velocity.Z);
        var frame = float4x4.CreateRotationY(yaw) * float4x4.CreateTranslation(pos);
        void Part(float3 c, float3 size, float3 color, Material material) =>
            renderer.DrawCube(float4x4.CreateScale(size) * float4x4.CreateTranslation(c) * frame, color, ShadeMode.Lit, material);

        Part(new float3(0, 0.2f, 0), new float3(8f, 3f, 26f), new float3(0.08f, 0.08f, 0.09f), Material.MarinePaint);   // hull (local +z forward)
        Part(new float3(0, 1.85f, 0), new float3(8.1f, 0.3f, 26.1f), new float3(0.55f, 0.12f, 0.08f), Material.MarinePaint);   // sheer strake
        Part(new float3(0, 3.3f, 2f), new float3(5.5f, 2.6f, 9f), new float3(0.86f, 0.86f, 0.84f), Material.MarinePaint);   // deckhouse
        Part(new float3(0, 5.6f, 3.5f), new float3(4.6f, 2f, 4.4f), new float3(0.86f, 0.86f, 0.84f), Material.MarinePaint);   // wheelhouse
        Part(new float3(0, 5.8f, 5.75f), new float3(4f, 0.9f, 0.05f), _night ? new float3(0.9f, 0.8f, 0.55f) : new float3(0.08f, 0.09f, 0.1f),
            _night ? Material.Emissive : Material.Glass);
        Part(new float3(0, 8.6f, 2.5f), new float3(0.25f, 4f, 0.25f), new float3(0.2f, 0.2f, 0.2f), Material.Steel);   // mast
        Part(new float3(0, 3.5f, -4.5f), new float3(1.2f, 3.5f, 1.4f), new float3(0.55f, 0.12f, 0.08f), Material.MarinePaint);   // funnel
        if (_night)
        {
            Part(new float3(-2.4f, 6.8f, 3.5f), new float3(0.3f, 0.3f, 0.3f), new float3(1f, 0.05f, 0.03f), Material.Emissive);   // port
            Part(new float3(2.4f, 6.8f, 3.5f), new float3(0.3f, 0.3f, 0.3f), new float3(0.05f, 1f, 0.2f), Material.Emissive);   // starboard
            Part(new float3(0, 10.8f, 2.5f), new float3(0.35f, 0.35f, 0.35f), new float3(1f, 0.95f, 0.85f), Material.Emissive);   // masthead
        }
    }

    private static void Cube(Renderer renderer, float3 center, float3 size, float3 color, Material material) =>
        renderer.DrawCube(float4x4.CreateScale(size) * float4x4.CreateTranslation(center), color, ShadeMode.Lit, material);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Smooth(float t) => t * t * (3 - 2 * t);
}
