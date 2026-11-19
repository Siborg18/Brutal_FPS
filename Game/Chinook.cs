using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The CH-47 Chinook (Assets/Models/chinook.glb, Tools/blender/build_chinook.py) that lowers the container into the
/// prison yard: draws the airframe, turns the two rotor heads, and at speed swaps the blades for their motion blur.
///
/// A real blade passes too fast for a frame to catch: at 225 rpm it moves 22 degrees in a 60 fps frame, so a solid blade
/// would strobe about. What a camera sees is what's built here each frame (<see cref="BuildBlur"/>): the wedge each blade
/// swept since the last frame, dark near the hub, where it covered the same air for longer, and fainter towards the tip,
/// where it went past quickly. Its opacity at radius r is the exact fraction of the frame the blade's chord covered that
/// point, so it comes out of the blade's size and the frame time rather than from a painted disc. The wedges lie on the
/// blades' coned surface, in the rotor's own tilted plane. A faint disc and tip ring stay on, for what the eye holds.
///
/// The rotors are geared together and overlap by a third of their width: keep them <see cref="PhaseDeg"/> apart and
/// their blades pass 2.3 m clear of each other (build_chinook.py checks that when it builds the model).
/// </summary>
public sealed class Chinook
{
    // Points on the model (chinook_points.json, in model space: origin on the ground under the middle, nose at -Z).
    private static readonly float3 HubForward = new(0, 4.78f, -5.97f);
    private static readonly float3 HubAft = new(0, 5.56f, 5.97f);
    private static readonly float TiltForward = float.DegreesToRadians(6f);
    private static readonly float TiltAft = float.DegreesToRadians(4f);

    /// <summary>The container's origin (bottom centre) as it hangs from the sling, lengthways under the helicopter.</summary>
    public static readonly float3 ContainerOrigin = new(0, -11.87f, 0);

    private const float PhaseDeg = 60f;
    private const float FullRpm = 225f;

    // The blades (the same numbers as build_chinook.py).
    private const float BladeRadius = 9.145f, BladeRoot = 1.55f, BladeChord = 0.81f, BladeBend = 0.26f;
    private static readonly float ConeSlope = float.Tan(float.DegreesToRadians(2.6f));

    /// <summary>Above this share of full speed the blades are drawn as their blur.</summary>
    private const float BlurFromSpeed = 0.25f;

    /// <summary>How much of a frame the "shutter" is open for, and the longest wedge it will draw (low frame rates).</summary>
    private const float Shutter = 0.85f;
    private static readonly float MaxSweep = float.DegreesToRadians(40f);

    private const int BladeSpans = 11, BladeArcs = 8, DiscSegments = 48;
    private static readonly float[] DiscRings = [BladeRoot, 3f, 5f, 7f, 8.3f, 8.85f, BladeRadius];
    private const float DiscAlpha = 0.03f, TipRingAlpha = 0.05f;

    /// <summary>Display-space colour of the blur; the erosion caps at the tips show lighter (the shader's mode 11).</summary>
    private static readonly float3 BlurColor = new(0.085f, 0.09f, 0.085f);

    /// <summary>The olive drab its paint is drawn with.</summary>
    public float3 Tint { get; set; } = new(0.30f, 0.31f, 0.19f);

    public float3 Position { get; set; }

    /// <summary>Turned about the vertical, anticlockwise from above (radians). Zero: the nose points along -Z.</summary>
    public float Yaw { get; set; }

    /// <summary>Nose up (radians).</summary>
    public float Pitch { get; set; }

    /// <summary>Radians about its length.</summary>
    public float Roll { get; set; }

    /// <summary>The rotors' speed, as a share of full speed (225 rpm).</summary>
    public float RotorSpeed { get; set; } = 1f;

    /// <summary>Whether the sling is still on the hooks: clear it when the load has been let go.</summary>
    public bool SlingAttached { get; set; } = true;

    public float4x4 World =>
        float4x4.CreateRotationZ(Roll) * float4x4.CreateRotationX(Pitch) * float4x4.CreateRotationY(Yaw) * float4x4.CreateTranslation(Position);

    private readonly List<ModelPart> _airframe = [], _forwardHub = [], _forwardBlades = [], _aftHub = [], _aftBlades = [], _sling = [];
    private readonly DynamicMesh _blur = new();
    private float _angle;

    public Chinook()
    {
        foreach (var part in Model.Get("chinook").Parts)
        {
            (part.Name switch
            {
                "rotor_fwd_hub" => _forwardHub,
                "rotor_fwd_blades" => _forwardBlades,
                "rotor_aft_hub" => _aftHub,
                "rotor_aft_blades" => _aftBlades,
                "sling" => _sling,
                _ => _airframe,
            }).Add(part);
        }
    }

    public void Update(float dt)
    {
        var step = float.Tau * FullRpm / 60f * RotorSpeed * dt;
        _angle = (_angle + step) % float.Tau;
        if (RotorSpeed >= BlurFromSpeed)
            BuildBlur(float.Clamp(step * Shutter, float.DegreesToRadians(1f), MaxSweep));
        else
            _blur.Clear();
    }

    /// <summary>The solid parts: draw it with the other opaque things.</summary>
    public void Draw(Renderer renderer, Zones zones)
    {
        if ((zones & Level.ZoneAt(Position)) == 0)
            return;
        var world = World;
        foreach (var part in _airframe)
            renderer.DrawPart(part, world, Tint);
        if (SlingAttached)
            foreach (var part in _sling)
                renderer.DrawPart(part, world, Tint);

        var forward = HubSpin(HubForward, TiltForward, ForwardAzimuth) * world;
        var aft = HubSpin(HubAft, TiltAft, AftAzimuth) * world;
        foreach (var part in _forwardHub)
            renderer.DrawPart(part, forward, Tint);
        foreach (var part in _aftHub)
            renderer.DrawPart(part, aft, Tint);
        if (RotorSpeed >= BlurFromSpeed)
            return;
        foreach (var part in _forwardBlades)
            renderer.DrawPart(part, forward, Tint);
        foreach (var part in _aftBlades)
            renderer.DrawPart(part, aft, Tint);
    }

    /// <summary>The sling on its own, once it has come off the hooks.</summary>
    public void DrawSling(Renderer renderer, in float4x4 world)
    {
        foreach (var part in _sling)
            renderer.DrawPart(part, world, Tint);
    }

    /// <summary>The blades' blur: draw it with the other see-through things, after everything solid.</summary>
    public void DrawBlur(Renderer renderer, Zones zones)
    {
        if ((zones & Level.ZoneAt(Position)) != 0)
            renderer.DrawRotorBlur(_blur, BlurColor);
    }

    private float ForwardAzimuth => _angle;                                                // anticlockwise from above
    private float AftAzimuth => float.DegreesToRadians(PhaseDeg) - _angle;                 // clockwise, geared to it

    private static float3 AxisOf(float tilt) => new(0, float.Cos(tilt), -float.Sin(tilt));   // leaning forward (-Z)

    private static float4x4 HubSpin(float3 hub, float tilt, float azimuth) =>
        float4x4.CreateTranslation(-hub) * float4x4.CreateFromAxisAngle(AxisOf(tilt), azimuth) * float4x4.CreateTranslation(hub);

    private static float BladeHeight(float r)
    {
        var s = r / BladeRadius;
        return r * ConeSlope + BladeBend * s * s;
    }

    /// <summary>The blade's chord at radius r: the last metre tapers, like the model's.</summary>
    private static float ChordAt(float r)
    {
        if (r < 8.2f)
            return BladeChord;
        var t = (r - 8.2f) / (BladeRadius - 8.2f);
        return BladeChord * (1f - 0.6f * t * t);
    }

    private void BuildBlur(float sweep)
    {
        _blur.Clear();
        var world = World;
        AddRotor(HubForward, TiltForward, ForwardAzimuth, 1f, sweep, world);
        AddRotor(HubAft, TiltAft, AftAzimuth, -1f, sweep, world);
    }

    private void AddRotor(float3 hub, float tilt, float azimuth, float turn, float sweep, in float4x4 world)
    {
        // The rotor's plane: x is the blade at azimuth zero, y a quarter turn anticlockwise from it, z the axis.
        var ex = new float3(1, 0, 0);
        var ey = new float3(0, -float.Sin(tilt), -float.Cos(tilt));
        var ez = AxisOf(tilt);

        float3 At(float r, float a, in float4x4 w) =>
            float3.Transform(hub + ex * (r * float.Cos(a)) + ey * (r * float.Sin(a)) + ez * BladeHeight(r), w);

        // The three wedges.
        for (var blade = 0; blade < 3; blade++)
        {
            var lead = azimuth + blade * float.Tau / 3f;
            for (var i = 0; i < BladeSpans; i++)
            {
                var r0 = BladeRoot + (BladeRadius - BladeRoot) * i / BladeSpans;
                var r1 = BladeRoot + (BladeRadius - BladeRoot) * (i + 1) / BladeSpans;
                var tip0 = r0 >= 8.8f ? 1f : 0f;
                var tip1 = r1 >= 8.8f ? 1f : 0f;
                for (var j = 0; j < BladeArcs; j++)
                {
                    var u0 = (float)j / BladeArcs;
                    var u1 = (float)(j + 1) / BladeArcs;
                    var a = Wedge(r0, u0, lead, turn, sweep, out var alphaA);
                    var b = Wedge(r1, u0, lead, turn, sweep, out var alphaB);
                    var c = Wedge(r1, u1, lead, turn, sweep, out var alphaC);
                    var d = Wedge(r0, u1, lead, turn, sweep, out var alphaD);
                    Quad(At(r0, a, world), alphaA, tip0, At(r1, b, world), alphaB, tip1,
                        At(r1, c, world), alphaC, tip1, At(r0, d, world), alphaD, tip0);
                }
            }
        }

        // The faint disc, with the tips' path a little stronger.
        for (var i = 0; i < DiscRings.Length - 1; i++)
        {
            var r0 = DiscRings[i];
            var r1 = DiscRings[i + 1];
            var alpha0 = i == 0 ? 0f : DiscAlphaAt(i);
            var alpha1 = i == DiscRings.Length - 2 ? 0f : DiscAlphaAt(i + 1);   // soft at both edges: no hard hoop
            for (var s = 0; s < DiscSegments; s++)
            {
                var a0 = float.Tau * s / DiscSegments;
                var a1 = float.Tau * (s + 1) / DiscSegments;
                Quad(At(r0, a0, world), alpha0, 0f, At(r1, a0, world), alpha1, 0f,
                    At(r1, a1, world), alpha1, 0f, At(r0, a1, world), alpha0, 0f);
            }
        }
    }

    private static float DiscAlphaAt(int ring) => ring == DiscRings.Length - 2 ? TipRingAlpha : DiscAlpha;

    /// <summary>
    /// A point of one blade's wedge, u across it from its trailing end to its leading end: its azimuth, and how much of
    /// the frame the blade's chord covers that point (a ramp up over one chord's width, flat, a ramp down again).
    /// </summary>
    private static float Wedge(float r, float u, float lead, float turn, float sweep, out float alpha)
    {
        var chord = ChordAt(r);
        var chordAngle = chord / r;
        var span = sweep + chordAngle;
        var along = u * span;
        var edge = float.Min(along, span - along) / chordAngle;
        alpha = float.Min(edge, 1f) * chord / (chord + r * sweep);
        return lead + turn * (chordAngle * 0.5f - (span - along));
    }

    private void Quad(float3 a, float alphaA, float tipA, float3 b, float alphaB, float tipB, float3 c, float alphaC, float tipC, float3 d, float alphaD, float tipD)
    {
        V(a, alphaA, tipA); V(b, alphaB, tipB); V(c, alphaC, tipC);
        V(a, alphaA, tipA); V(c, alphaC, tipC); V(d, alphaD, tipD);

        void V(float3 p, float alpha, float tip) =>
            _blur.Vertex(p, new float3(0, 1, 0), new float2(tip, 0), new float4(1, 0, 0, alpha));
    }
}
