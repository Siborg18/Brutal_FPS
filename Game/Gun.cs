using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What the game needs from one of the player's guns: Program switches between them (1 pistol, 2 AK-47, ...), the HUD
/// reads their ammunition and spread, the view takes their recoil.
/// </summary>
public interface IGun
{
    string Name { get; }

    /// <summary>Rounds a full magazine holds (not counting the chamber).</summary>
    int Capacity { get; }

    int AmmoInMag { get; }
    bool Chambered { get; }
    bool Reloading { get; }
    float ReloadProgress { get; }
    int ShotsFired { get; }

    /// <summary>How far its shot carries to people's ears (1: as most guns; a suppressor brings it down).</summary>
    float Loudness => 1f;

    /// <summary>The selector setting ("SEMI", "AUTO"); B switches it on a gun that has more than one.</summary>
    string FireMode { get; }

    /// <summary>0 at the hip, 1 aiming down the sights.</summary>
    float AimBlend { get; }

    /// <summary>Aimed through a scope: the view is zoomed in by <see cref="ScopeZoom"/>, the gun isn't drawn and the
    /// HUD draws the scope's picture (its tube and reticle) instead. How it's seen depends on <see cref="ScopeKind"/>.</summary>
    bool Scoped => false;

    /// <summary>The scope's magnification (1: open sights).</summary>
    float ScopeZoom => 1f;

    /// <summary>Looking through a scope that has more than one power, turns it that many steps (positive: more), stopping at
    /// each end. True whenever a scope is in use, even one with a single power: the wheel does nothing else then. False
    /// otherwise: the mouse wheel changes guns instead.</summary>
    bool TurnScope(int steps) => false;

    /// <summary>What the scope shows the world as, when looking through it (it stays set between looks).</summary>
    ScopeVision Vision => ScopeVision.Off;

    /// <summary>Looking through a scope, switches it on to the next: off, night vision, thermal, off.</summary>
    void TurnVision() { }

    /// <summary>The kind of scope: a long tube seen black all round, or a short prism sight (an ACOG) whose magnified
    /// circle shows in the middle of the view with the world unmagnified round its eyepiece.</summary>
    ScopeKind ScopeKind => ScopeKind.Tube;

    /// <summary>Looking through a reflex sight at 1x (the gun in view): which reticle the HUD lights (a red dot or a holographic ring); null otherwise.</summary>
    ScopeKind? Reticle => null;

    /// <summary>Which way the sight's line points (camera space, unit): where a reflex sight's reticle is seen. Null without a reticle.</summary>
    float3? SightDirection => null;

    /// <summary>How far below the point of aim (radians) a round lands at this range: where a bullet-drop ladder puts its marks.</summary>
    float HoldUnder(float range) => 0f;

    /// <summary>The view's field of view (degrees) with this gun: aiming narrows it a little, a tube scope zooms it.</summary>
    float FieldOfView(float baseDegrees) => Scoped && ScopeKind == ScopeKind.Tube
        ? Zoomed(baseDegrees, ScopeZoom)
        : Zoomed(baseDegrees - 12f * AimBlend, 1f + (AimZoom - 1f) * AimBlend);

    /// <summary>How much a sight looked through at 1x closes the view in, aimed, beyond what aiming always does (a reflex sight's a little: 1 for open sights).</summary>
    float AimZoom => 1f;

    /// <summary>The field of view (degrees) of the magnified picture inside a prism sight's circle: the aimed view zoomed.</summary>
    float ScopeFieldOfView(float baseDegrees) => Zoomed(baseDegrees - 12f * AimBlend, ScopeZoom);

    private static float Zoomed(float degrees, float zoom) =>
        2f * float.RadiansToDegrees(float.Atan(float.Tan(float.DegreesToRadians(degrees) / 2f) / zoom));

    /// <summary>How fast the player moves with this gun in hand, of their usual speed (a heavy gun slows them down).</summary>
    float MoveScale => 1f;

    /// <summary>What recoil does to the view (pitch, yaw radians).</summary>
    float2 ViewKick { get; }

    /// <summary>Radius (radians) of the cone the next shot can land anywhere in: grows as shots follow each other.</summary>
    float Spread { get; }

    /// <summary>Put away (after <see cref="Holster"/>): the other gun can come up.</summary>
    bool Holstered { get; }

    AudioEngine? Audio { get; set; }

    void Update(float dt, Input input, in Camera camera, PlayerController player, bool allowFire);
    void Draw(Renderer renderer, in Camera camera);
    float4 FlashLight(in Camera camera);

    /// <summary>Starts with the gun already raised (screenshot mode).</summary>
    void SkipDraw();

    /// <summary>Lowers the gun out of view (a reload in progress is abandoned).</summary>
    void Holster();

    /// <summary>Already lowered out of view, with no animation (a gun picked while a cutscene has the player's hands down).</summary>
    void HolsterNow();

    /// <summary>Brings it (back) up.</summary>
    void Raise();
}

/// <summary>
/// The spread of a string of shots. Every shot leaves within a cone of its own (the gun's and the shooter's
/// precision: tight aimed, loose from the hip), and shots in quick succession open that cone up: each shot heats it,
/// and once you stop firing (after <c>coolDelay</c>) it cools at <c>coolRate</c> per second. Slow, careful shots
/// group tightly; holding the trigger down sprays.
/// </summary>
public sealed class ShotSpread(float hip, float aimed, float hipBloom, float aimedBloom, float perShot, float coolDelay, float coolRate)
{
    private float _heat;
    private float _sinceShot = 10f;

    /// <summary>0 (cold) .. 1 (fully opened up).</summary>
    public float Heat => _heat;

    public void Update(float dt)
    {
        _sinceShot += dt;
        if (_sinceShot > coolDelay)
            _heat = float.Max(0f, _heat - coolRate * dt);
    }

    /// <summary>The cone's radius (radians) for the next shot.</summary>
    public float Radius(float aim) => float.Lerp(hip, aimed, aim) + float.Lerp(hipBloom, aimedBloom, aim) * _heat * float.Sqrt(_heat);

    /// <summary>A shot went off: the cone opens up for the next one.</summary>
    public void Shot()
    {
        _heat = float.Min(1f, _heat + perShot);
        _sinceShot = 0f;
    }

    public void Reset()
    {
        _heat = 0f;
        _sinceShot = 10f;
    }
}

/// <summary>How a gun's scope is seen through (see <see cref="IGun.ScopeKind"/>).</summary>
public enum ScopeKind
{
    /// <summary>A long tube scope: the whole view zoomed, black all round its circle, a duplex reticle.</summary>
    Tube,

    /// <summary>A short prism sight (an ACOG): its magnified picture in a circle in the middle of the view, a thick black
    /// eyepiece ring round it, and the world outside seen as it is; a red chevron over a bullet-drop ladder.</summary>
    Acog,

    /// <summary>A red dot: 1x, the gun stays in view and a lit dot floats on the line of sight.</summary>
    RedDot,

    /// <summary>A holographic sight: 1x like a red dot (a ring round a dot); its magnifier flipped in, it's seen as an <see cref="Acog"/>.</summary>
    Holo,
}
