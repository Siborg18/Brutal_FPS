using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Someone with a seat (<see cref="SeatAt"/>): the yard crew's card players (<see cref="Level.YardCardSeats"/>). While
/// they're calm (or only wary: they look round, but stay put) and at their seat, they sit in it playing cards (the
/// SitCards clip, Tools/blender/humankit/seatedlib.py), their gun on the ground beside them. Anything that really alerts
/// them (a startle, a fight, a flight, being shot) and they get up: for <see cref="StandUpTime"/> they do nothing else,
/// then the gun is in their hands and they carry on as anyone would. Calm again, they walk back to their seat
/// (their route's only stop) and sit back down.
/// </summary>
public partial class Npc
{
    private const string SitClip = "SitCards";
    private const float StandUpTime = 0.6f;   // s getting up out of the chair: as long as the clip's fade
    private const float SitDownFrom = 1.2f;   // m: back this near their seat, calm and stopped, they sit down in it

    private float3? _seat;
    private float _seatHeading;
    private bool _sitting;
    private float _risingLeft;

    /// <summary>Sat in their seat now.</summary>
    public bool Sitting => _sitting;

    /// <summary>Has a seat (<see cref="SeatAt"/>).</summary>
    public bool HasSeat => _seat is not null;

    /// <summary>Where their seat is (their feet, under the middle of it) and which way it faces. Only with <see cref="HasSeat"/>.</summary>
    private protected (float3 At, float Heading) SeatPlace => (_seat ?? Position, _seatHeading);

    /// <summary>They sit at their route's first stop, facing <paramref name="heading"/>, from the start.</summary>
    public void SeatAt(float heading)
    {
        _seat = _route[0].Position;
        _seatHeading = heading;
        Position = _seat.Value;
        Heading = heading;
        SitDown(0f);
    }

    /// <summary>Their gun's down: the seat's in charge this frame (sitting or getting up out of it, they don't move).</summary>
    private protected bool SeatHolds => _sitting || _risingLeft > 0f;

    /// <summary>Called once a frame, after the behaviour: sits them down or gets them up, as their state says.</summary>
    private void UpdateSeat(float dt, bool moving)
    {
        if (_seat is not { } seat)
            return;
        if (_risingLeft > 0f)
        {
            _risingLeft -= dt;
            if (_risingLeft <= 0f && Alive)
                StoodUp();
            return;
        }

        var calm = Alive && !Scripted && _link is null && _away.Count == 0 && _staggerLeft <= 0f && _forcedClip is null
                   && State is NpcState.Calm or NpcState.Wary;
        if (_sitting)
        {
            if (!calm)
                GetUp();
            return;
        }

        if (calm && State == NpcState.Calm && !moving && _animSpeed < 0.05f)
        {
            var off = seat - Position;
            off.Y = 0;
            if (float3.Dot(off, off) < SitDownFrom * SitDownFrom)
            {
                SitDown(0.6f);
                Log.Write($"[NPC] {Name}: back in their seat at the card table");
            }
        }
    }

    private void SitDown(float fade)
    {
        Position = _seat!.Value;
        Heading = _seatHeading;
        _sitting = true;
        StopIdleClips();
        // Each at their own place in the game, at their own pace.
        var clip = _clips.Get(SitClip);
        var start = clip is null ? 0f : (float)_random.NextDouble() * clip.Duration;
        _animator.PlayFullBody(SitClip, fade, StandUpTime, 0.92f + 0.16f * (float)_random.NextDouble(), start);
        Seated(true);
    }

    private void GetUp()
    {
        _sitting = false;
        _animator.Stop(SitClip, StandUpTime);
        if (!Alive)
            return;
        _risingLeft = StandUpTime;
        Log.Write($"[NPC] {Name}: up from the card table ({State.ToString().ToLowerInvariant()})");
    }

    /// <summary>Sat down (true) or getting up (false): for the gun to go down beside them, or for them to reach for it.</summary>
    private protected virtual void Seated(bool sitting) { }

    /// <summary>Up out of the chair, <see cref="StandUpTime"/> after they started getting up.</summary>
    private protected virtual void StoodUp() { }
}

/// <summary>A card player's gun: on the ground beside their chair while they're sat down (<see cref="Level.GunBesideChair"/>).</summary>
public sealed partial class ArmedNpc
{
    private bool _gunDown;

    private protected override void Seated(bool sitting)
    {
        if (sitting)
            _gunDown = true;
    }

    private protected override void StoodUp() => _gunDown = false;

    /// <summary>The gun on the ground by the chair, this frame (or null: it's on them).</summary>
    private float4x4? GunOnGround => _gunDown && Alive ? Level.GunBesideChair(SeatPlace.At, SeatPlace.Heading, Gun.GripLocal) : null;
}

/// <summary>A card player's hand of cards, drawn fanned out of their left hand.</summary>
public sealed partial class Npcs
{
    private static readonly float3 CardSize = new(0.063f, 0.088f, 0.0007f);
    private static readonly float3 CardFace = new(0.93f, 0.92f, 0.88f);
    private static readonly float3 CardBack = new(0.62f, 0.10f, 0.10f);

    /// <summary>
    /// Five cards fanned out of the left hand (the hand bone's frame: Y from the wrist to the knuckles, Z out of the back of
    /// the hand): standing up out of the fist on the palm side, their faces to the holder, their red backs out.
    /// </summary>
    private void DrawCardsInHand(Renderer renderer, in float4x4 hand)
    {
        const int Cards = 5;
        var scale = float4x4.CreateScale(CardSize);
        for (var i = 0; i < Cards; i++)
        {
            var fan = float4x4.CreateRotationZ((i - (Cards - 1) / 2f) * 0.24f);
            var pivot = float4x4.CreateTranslation(0, 0.07f, -0.018f - 0.0016f * i);
            var card = float4x4.CreateTranslation(0, 0.03f, 0) * fan * pivot * hand;
            renderer.DrawCube(scale * float4x4.CreateTranslation(0, 0, -0.00035f) * card, CardFace);
            renderer.DrawCube(scale * float4x4.CreateTranslation(0, 0, 0.00035f) * card, CardBack);
        }
    }
}
