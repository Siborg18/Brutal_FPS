using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// On a factory raid: the women in the hall's office (<see cref="Npcs.OfficeWomen"/>), their guards (<see cref="Npcs.OfficeGuards"/>)
/// and the portal on the loading dock. The guards stay in the office with them until the raid's under way: in the van, the
/// moment the first shot of the fight is fired; over the fence, the moment the player opens any door into the hall (the big
/// sliding door, the rear door at the top of the outside steps or the side door by the outbuilding); either way, at the latest when the player sets foot in the
/// hall. Then the women are walked out in a line, across the hall, up the dock's steps and through the blue portal, each with a
/// guard walking behind her with his gun on her. Each one who goes through is gone. The guards stop on the dock and hold it.
///
/// The player never finds out where it goes: the portal only ever shows blackness and can't be entered (<see cref="Portal.Dark"/>).
/// </summary>
public sealed class FactoryEvacuation(Npcs npcs, Level level, FactoryEntry entry)
{
    private const float RaiseTheAlarm = 0.8f;      // s: from the trigger to the first woman setting off (the guards get their guns on them)
    private const float Hurried = 1.5f;            // × the walk clip's own speed: a hurried walk (the locomotion blend leans towards the run)
    private const float WomanSpacing = 1.0f;       // s between one setting off and the next (hurrying, about 2 m apart)
    private const float EscortLag = 0.65f;         // s a guard sets off after the woman he's walking
    private const float CoverRange = 45f, CoverEvery = 0.9f, CoverHold = 1.1f, CoverScatter = 0.9f;

    // The way onto the dock: its steps taken as a ramp, and the water.
    private static readonly float3 StepsFoot = new(-14f, 0, -0.6f);
    private static readonly float3 DockEdge = new(-14f, 2f, -4.4f);
    private static readonly float3 AtTheWater = new(-14f, 2f, -8.6f);

    // Where the escort stops on the dock, either side of the top of the steps, to hold off whoever's coming.
    private static readonly float3[] EscortPosts = [new(-12.2f, 2f, -5.2f), new(-15.8f, 2f, -5.2f), new(-14f, 2f, -6.2f)];

    private enum Stage { Waiting, Moving, Over }

    private sealed class Walker(Npc npc, List<float3> way, float speed, float delay = 0f)
    {
        public readonly Npc Npc = npc;
        public readonly float Speed = speed;
        public readonly List<float3> Way = way;
        public int Next;
        public float Delay = delay;
        public Walker? Covers;       // a guard: the woman he's walking, his gun on her
        public float Shooting;       // a guard: s left turned on the player instead
        public bool Finished => Next >= Way.Count && Npc.ScriptArrived;
    }

    private readonly Random _random = new(4417);
    private readonly List<Walker> _women = [];
    private readonly List<Walker> _escort = [];
    private readonly List<float3> _path = [];
    private Stage _stage = Stage.Waiting;
    private float _coverIn;
    private int _through;
    private int _respawns = int.MinValue;
    private int _shotsBefore;
    private bool _frontWasOpen, _rearWasOpen, _sideWasOpen;

    private float WalkSpeed => (npcs.WalkSpeed > 0.3f ? npcs.WalkSpeed : 1.35f) * Hurried;

    /// <summary>Call once a frame after the people have moved, and before anything's drawn.</summary>
    public void Update(float dt, PlayerController player)
    {
        if (_respawns != player.Respawns)
        {
            if (_respawns != int.MinValue)
                Reset();
            _respawns = player.Respawns;
            foreach (var guard in npcs.OfficeGuards)
                guard.ScriptTake(guard.Position, guard.Heading);   // (they stay where they are until they're needed)
            _shotsBefore = npcs.FactoryGunshots;
            _frontWasOpen = level.FactoryDoorOpening;
            _rearWasOpen = level.FactoryRearDoorOpening;
            _sideWasOpen = level.FactorySideDoorOpening;
        }

        switch (_stage)
        {
            case Stage.Waiting when Triggered(player) is { } why:
                Log.Write($"[EVAC] {why}: getting the women out");
                Begin();
                break;
            case Stage.Moving:
                UpdateMoving(dt);
                break;
        }
    }

    /// <summary>What sets them off, if anything has this frame: the van's fight starting, either door into the hall opened, or the player inside.</summary>
    private string? Triggered(PlayerController player)
    {
        var front = level.FactoryDoorOpening;
        var rear = level.FactoryRearDoorOpening;
        var side = level.FactorySideDoorOpening;
        var why = (entry == FactoryEntry.Van && npcs.FactoryGunshots > _shotsBefore) ? "The shooting's started"
            : front && !_frontWasOpen ? "The player's opened the big door"
            : rear && !_rearWasOpen ? "The player's opened the rear door"
            : side && !_sideWasOpen ? "The player's opened the side door"
            : Level.InHall(player.EyePosition) ? $"The player's in the hall at ({player.EyePosition.X:0.0}, {player.EyePosition.Z:0.0})"
            : null;
        _frontWasOpen = front;
        _rearWasOpen = rear;
        _sideWasOpen = side;
        return why;
    }

    /// <summary>A respawn: everyone's back as they started (<see cref="Npcs.ApplyRespawn"/>).</summary>
    private void Reset()
    {
        _women.Clear();
        _escort.Clear();
        _stage = Stage.Waiting;
        _through = 0;
    }

    private bool Clear(float3 from, float3 to) => npcs.Senses.Clear(from, to) >= 0.995f;

    private void Begin()
    {
        var speed = WalkSpeed;

        // The women, nearest the way out first, each up off the floor if she was cowering, in a line at a walk.
        var women = npcs.OfficeWomen.Where(w => w.Alive).OrderBy(w => float3.Distance(w.Position, StepsFoot)).ToList();
        foreach (var (woman, i) in women.Select((w, i) => (w, i)))
        {
            woman.ScriptSettle();
            woman.ScriptTake(woman.Position, woman.Heading);
            _women.Add(new Walker(woman, OnToTheDock(woman.Position, AtTheWater), speed, RaiseTheAlarm + i * WomanSpacing));
        }

        // Each guard takes the nearest woman nobody has yet (or the nearest, if there are more of them than her), goes to
        // where she stood and follows her way, his gun on her, to a post on the dock.
        var taken = new HashSet<Walker>();
        var guards = npcs.OfficeGuards.Where(g => g.Alive).ToList();
        foreach (var (guard, i) in guards.Select((g, i) => (g, i)))
        {
            guard.ScriptSeize(90);
            var her = _women.Where(w => !taken.Contains(w)).DefaultIfEmpty().MinBy(w => w is null ? float.MaxValue : float3.Distance(w.Npc.Position, guard.Position))
                      ?? _women.MinBy(w => float3.Distance(w.Npc.Position, guard.Position));
            List<float3> way;
            float delay;
            if (her is not null)
            {
                taken.Add(her);
                way = WayTo(guard.Position, her.Npc.Position);
                way.AddRange(her.Way.Take(her.Way.Count - 1));
                delay = her.Delay + EscortLag;
                guard.ScriptFace(Chest(her.Npc));
            }
            else
            {
                way = [.. WayTo(guard.Position, StepsFoot), DockEdge];
                delay = RaiseTheAlarm;
            }
            way.Add(EscortPosts[i % EscortPosts.Length]);
            _escort.Add(new Walker(guard, way, speed, delay) { Covers = her });
            Log.Write($"[EVAC] {guard.Name} walks {her?.Npc.Name ?? "nobody"} out at gunpoint");
        }

        _stage = Stage.Moving;
        Log.Write($"[EVAC] On the move: {_women.Count} women at {speed:0.00} m/s, {_escort.Count} guards walking them");
    }

    private void UpdateMoving(float dt)
    {
        if (AbandonedForWantOfPeople())
            return;

        for (var i = _women.Count - 1; i >= 0; i--)
        {
            var w = _women[i];
            if (!w.Npc.Alive)
            {
                _women.RemoveAt(i);
                continue;
            }

            Step(w, dt, face: true);
            if (w.Finished)
            {
                _women.RemoveAt(i);
                npcs.GoneForGood(w.Npc);
                _through++;
                Log.Write($"[EVAC] {w.Npc.Name} is through the portal ({_through} so far)");
            }
        }

        Cover(dt);
        foreach (var e in _escort)
        {
            Step(e, dt, face: false);
            Aim(e, dt);
        }

        if (_women.Count == 0)
        {
            Log.Write($"[EVAC] It's over: {_through} got out");
            ReleaseEscort();
            _stage = Stage.Over;
        }
    }

    /// <summary>Where a guard points his gun: at his woman while she's still to be taken, else the way he's going, or back down the steps once he's at his post.</summary>
    private void Aim(Walker e, float dt)
    {
        if (!e.Npc.Alive)
            return;
        if (e.Shooting > 0f)
        {
            e.Shooting -= dt;
            return;
        }

        if (e.Covers is { } her && her.Npc.Alive && _women.Contains(her))
            e.Npc.ScriptFace(Chest(her.Npc));
        else if (e.Next < e.Way.Count || !e.Npc.ScriptArrived)
            e.Npc.ScriptFace(e.Way[int.Min(int.Max(e.Next - 1, 0), e.Way.Count - 1)] + new float3(0, Npc.EyeHeight, 0));
        else
            e.Npc.ScriptFace(StepsFoot + new float3(0, 1.3f, 0));
    }

    private static float3 Chest(Npc npc) => npc.Position + new float3(0, 1.2f, 0);

    /// <summary>Everyone meant to go is dead, or there's nobody left to take them.</summary>
    private bool AbandonedForWantOfPeople()
    {
        if (_women.Any(w => w.Npc.Alive) && _escort.Any(e => e.Npc.Alive))
            return false;
        Log.Write("[EVAC] Nobody left to take, or to take them: it's off");
        Abandon();
        return true;
    }

    /// <summary>There's nobody to go through it, or to take them: everyone looks after themselves.</summary>
    private void Abandon()
    {
        foreach (var w in _women)
        {
            if (w.Npc.Alive)
                w.Npc.ScriptRelease();
        }

        _women.Clear();
        ReleaseEscort();
        _stage = Stage.Over;
    }

    private void ReleaseEscort()
    {
        foreach (var e in _escort)
        {
            if (e.Npc is ArmedNpc { Alive: true } guard)
                guard.ScriptEngage(npcs.Senses.PlayerEye, 0.4f);
        }

        _escort.Clear();
    }

    private static void Step(Walker walker, float dt, bool face)
    {
        if (walker.Delay > 0f)
        {
            walker.Delay -= dt;
            return;
        }

        if (!walker.Npc.Alive || !walker.Npc.ScriptArrived || walker.Next >= walker.Way.Count)
            return;
        var to = walker.Way[walker.Next++];
        if (face)
            walker.Npc.ScriptFace(to);
        walker.Npc.ScriptWalk(to, walker.Speed);
    }

    /// <summary>
    /// Now and then the guard nearest the player, if he has them in the open, turns his gun from the woman to the player for a
    /// moment and fires on the move; then it's back on her.
    /// </summary>
    private void Cover(float dt)
    {
        _coverIn -= dt;
        var s = npcs.Senses;
        if (_coverIn > 0f || !s.PlayerAlive || s.PlayerConcealed)
            return;
        _coverIn = CoverEvery;
        var eye = s.PlayerEye;
        var aim = eye - new float3(0, 0.35f, 0);
        foreach (var e in _escort.OrderBy(e => float3.Distance(e.Npc.Position, eye)))
        {
            if (e.Npc is not ArmedNpc { Alive: true } guard)
                continue;
            var from = guard.Position + new float3(0, Npc.EyeHeight, 0);
            if (float3.Distance(from, eye) > CoverRange || !Clear(from, aim))
                continue;
            e.Shooting = CoverHold;
            guard.ScriptFace(aim);
            guard.ScriptShoot(s, aim + new float3(Scatter(), Scatter() * 0.6f, Scatter()));
            return;
        }
    }

    private float Scatter() => ((float)_random.NextDouble() - 0.5f) * 2f * CoverScatter;

    private List<float3> OnToTheDock(float3 from, float3 end)
    {
        var way = WayTo(from, StepsFoot);
        way.Add(DockEdge);
        way.Add(end);
        return way;
    }

    private List<float3> WayTo(float3 from, float3 to)
    {
        if (npcs.Senses.Nav is { } nav && nav.FindPath(from, to, _path, out _) && _path.Count > 0)
            return [.. _path];
        Log.Write($"[WARN] No path from ({from.X:0.0}, {from.Z:0.0}) to ({to.X:0.0}, {to.Z:0.0}): straight there");
        return [to];
    }
}
