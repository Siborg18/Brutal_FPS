using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A dome security camera on a ceiling (its housing is a level prop; the models are <c>cctv_dome</c> / <c>cctv_eye</c>
/// from <c>Tools/blender/build_props.py</c>). Its black ball turns to follow the player while they're in the room it
/// watches and sweeps the room slowly when they aren't. What it sees is rendered into <see cref="Feed"/> a few times a
/// second, with a caption, and shown on a CCTV monitor in the police station's tech room. There are two: the bunker's
/// (<see cref="Bunker"/>, on <see cref="Level.PoliceCctvScreen"/>) and the police vault's (<see cref="Vault"/>, on
/// <see cref="Level.PoliceVaultCctvScreen"/>, the monitor beside it).
/// </summary>
public sealed class SecurityCamera : IDisposable
{
    /// <summary>The ball's centre below the ceiling (<c>CCTV_PIVOT</c> in build_props.py).</summary>
    private const float PivotDrop = 0.062f;

    /// <summary>How far in front of the ball's centre the view is taken (just clear of the lens and the housing).</summary>
    private const float LensReach = 0.09f;

    private const float TurnSpeed = 1.4f;          // rad/s at most, so it visibly swings round after a fast mover
    private const float MaxPitch = -0.12f;          // the housing's rim stops it looking any flatter than this
    private const float MinPitch = -1.45f;          // (straight down the view's up vector degenerates)
    private const float FeedRate = 15f;             // frames a second: CCTV, not a portal
    private static readonly int2 FeedSize = new(640, 360);

    private readonly Model? _eye;
    private readonly string _caption;
    private readonly Zones _zone;                   // the area it's in (and draws)
    private readonly Func<float3, bool> _watches;   // where it follows the player
    private readonly float4x4 _screen;              // its monitor's picture in the tech room
    private readonly float _gain;
    private readonly float _homePitch;
    private readonly float3 _pivot;
    private readonly float _homeYaw;                // looking from the corner across the room's middle
    private float _yaw, _pitch;
    private float _sweep;
    private float _sinceFeed = float.MaxValue;

    /// <summary>The camera's latest picture (tone-mapped, like a portal's view).</summary>
    public RenderTarget Feed { get; } = new();

    /// <summary>Whether the camera is following the player (the caption says so).</summary>
    public bool Tracking { get; private set; }

    /// <summary>The bunker's camera, in its north-east corner.</summary>
    public static SecurityCamera Bunker() =>
        new("CAM 01  BUNKER", Level.BunkerCameraMount, Level.BunkerOrigin, Zones.Bunker, p => Level.ZoneAt(p) == Zones.Bunker,
            Level.PoliceCctvScreen, gain: 1.6f, homePitch: -0.42f);   // (the bunker is dim: the camera's gain is up)

    /// <summary>The police vault's camera, in its south-east corner over the inner door, looking at the portal.</summary>
    public static SecurityCamera Vault() =>
        new("CAM 02  VAULT", Level.VaultCameraMount, Level.PoliceVaultPortalFloorPoint + new float3(2f, 0, 4f), Zones.Police,
            p => Inside(Level.PoliceVault, p), Level.PoliceVaultCctvScreen, gain: 1.15f, homePitch: -0.32f);

    private static bool Inside(Aabb box, float3 p) =>
        p.X >= box.Min.X && p.X <= box.Max.X && p.Y >= box.Min.Y && p.Y <= box.Max.Y && p.Z >= box.Min.Z && p.Z <= box.Max.Z;

    private SecurityCamera(string caption, float3 mount, float3 lookAt, Zones zone, Func<float3, bool> watches, float4x4 screen, float gain, float homePitch)
    {
        try
        {
            _eye = Model.Get("cctv_eye");
        }
        catch (Exception e)
        {
            Log.Write($"[WARN] Model 'cctv_eye' couldn't be loaded ({e.GetType().Name}: {e.Message}); the camera has no ball.");
        }

        _caption = caption;
        _zone = zone;
        _watches = watches;
        _screen = screen;
        _gain = gain;
        _homePitch = homePitch;
        _pivot = mount - new float3(0, PivotDrop, 0);
        var across = lookAt - _pivot;
        _homeYaw = float.Atan2(across.X, -across.Z);
        _yaw = _homeYaw;
        _pitch = -0.45f;
    }
    /// <summary>The camera looking out of its lens.</summary>
    private Camera View
    {
        get
        {
            var forward = Camera.DirectionFromAngles(_yaw, _pitch);
            return Camera.Default with
            {
                Position = _pivot + forward * LensReach,
                Yaw = _yaw,
                Pitch = _pitch,
                FieldOfViewDegrees = 72f,
                NearPlane = 0.03f,
                FarPlane = 60f,
            };
        }
    }

    /// <summary>Turns the ball towards <paramref name="playerBody"/> (the middle of the player) when they're in its room.</summary>
    public void Update(float dt, float3 playerBody, bool playerAlive)
    {
        if (dt <= 0)
            return;
        _sinceFeed += dt;

        float wantYaw, wantPitch;
        Tracking = playerAlive && _watches(playerBody);
        if (Tracking)
        {
            var to = playerBody - _pivot;
            var flat = float.Sqrt(to.X * to.X + to.Z * to.Z);
            wantYaw = float.Atan2(to.X, -to.Z);
            wantPitch = float.Atan2(to.Y, flat);
            _sweep = 0;
        }
        else
        {
            // Nobody about: a slow pan to and fro across the room, pausing a little at each end.
            _sweep += dt;
            wantYaw = _homeYaw + 0.75f * float.Clamp(1.25f * float.Sin(_sweep * 0.35f), -1f, 1f);
            wantPitch = _homePitch;
        }

        var step = TurnSpeed * dt;
        var dy = float.Ieee754Remainder(wantYaw - _yaw, float.Tau);
        _yaw += float.Clamp(dy, -step, step);
        _pitch += float.Clamp(float.Clamp(wantPitch, MinPitch, MaxPitch) - _pitch, -step, step);
    }

    /// <summary>
    /// Renders the camera's picture (when it's due) before the frame's other views: its area, seen through
    /// <paramref name="drawScene"/>, and a caption over it.
    /// </summary>
    public void RenderFeed(Renderer renderer, Action<Zones> drawScene)
    {
        if (_sinceFeed < 1f / FeedRate)
            return;
        _sinceFeed = 0;

        var view = View;
        Feed.EnsureSize(FeedSize);
        var aspect = (float)FeedSize.X / FeedSize.Y;
        renderer.BeginView(Feed, FeedSize, view.View * view.Projection(aspect), view.Position);
        drawScene(_zone);

        // The caption: the camera's name, the date and time, and a blinking REC.
        renderer.BeginOverlay();
        var pixel = 2f / FeedSize.Y * 2f;   // two feed pixels per font pixel
        var white = new float3(0.92f, 0.92f, 0.9f);
        var left = -aspect + 0.05f;
        PcTerminal.Text(renderer, aspect, _caption, new float2(left, 0.94f), pixel, white);
        PcTerminal.Text(renderer, aspect, DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss"), new float2(left, -0.86f), pixel, white);
        if (DateTime.Now.Millisecond < 600)
        {
            PcTerminal.Rect(renderer, aspect, new float2(aspect - 0.3f, 0.94f - 3.5f * pixel), new float2(7 * pixel, 7 * pixel), new float3(0.95f, 0.1f, 0.08f));
            PcTerminal.Text(renderer, aspect, "REC", new float2(aspect - 0.25f, 0.94f), pixel, white);
        }
        if (Tracking)
            PcTerminal.Text(renderer, aspect, "TRACKING", new float2(aspect - 0.05f - (8 * BlockFont.Advance - 1) * pixel, -0.86f), pixel, new float3(1.0f, 0.75f, 0.2f));
    }

    /// <summary>Draws the ball (in its area) and the monitor's picture (in the police station).</summary>
    public void Draw(Renderer renderer, Zones zones)
    {
        if ((zones & _zone) != 0 && _eye is not null)
        {
            // The ball's lens is the model's -z; turned as the view is, about its centre.
            var world = (View with { Position = _pivot }).World;
            renderer.DrawModel(_eye, world, float3.One);
        }

        if ((zones & Zones.Police) != 0 && Feed.Size.X > 0)
            renderer.DrawScreen(_screen, Feed, _gain);
    }

    public void Dispose() => Feed.Dispose();
}
