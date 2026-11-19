using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Shouting, callouts and morale.
/// <list type="bullet">
/// <item>Going on the alert (<see cref="ShoutContact"/>): someone in a firefight shouts it now and then, and only those of
/// their side near enough to hear it (<see cref="ShoutRange"/> m in the open, half that through a wall) go on the alert.
/// They don't see any further for it, but they know the player for the enemy at a look, and they come and search for
/// them, starting where the shouter last had them (and the squad's search: <see cref="SquadIntel"/>). Those already
/// searching take it as a fresher lead. It doesn't pass on: someone who's only been shouted to doesn't shout on until
/// they've had contact themselves.</item>
/// <item>Callouts (<see cref="Say"/>, <see cref="NpcBarks"/>): "Contact!", "Reloading!", "Flanking left!", "Frag out!",
/// "Man down!"... logged, shown as subtitles near the player, and played if there's a recording.</item>
/// <item>Morale: losing people nearby (more if they see it), being hit, and staying under fire wear it down; it comes back
/// slowly when it's quiet. With the odds (how many of their side are in the fight nearby) it makes up their courage
/// (<see cref="Courage"/>), which the tactics weigh (Game/ArmedNpcTactics.cs): the brave push and flank, the shaken hold
/// back and, broken, fall back. Low morale also makes them easier to pin down.</item>
/// </list>
/// </summary>
public sealed partial class ArmedNpc
{
    // Shouting.
    private const float ShoutRange = 24f;      // m: a shout carries this far in the open
    private const float ShoutWalled = 0.5f;    // ...and this much of it through a wall
    private const float ShoutEvery = 2f;       // s between shouts from someone in a firefight
    private const float AlertedFor = 40f;      // s on the alert after the last shout they heard

    private float _shoutedAt = float.NegativeInfinity;
    private float _alertedUntil = float.NegativeInfinity;
    private bool _calledContact;               // has called out "Contact!" in this fight

    // Callouts.
    private float _saidAt = float.NegativeInfinity;

    // Morale.
    private const float MoraleRecover = 0.012f;   // per s, while it's quiet
    private float _morale = -1f, _moraleBase;

    /// <summary>On the alert: someone of their side in a firefight shouted to them in the last <see cref="AlertedFor"/> s.</summary>
    public bool Alerted(float now) => now < _alertedUntil;

    /// <summary>In a fight with the player in sight (for the player's reload: <see cref="Npcs.PlayerReloading"/>).</summary>
    public bool SeesPlayerNow => State == NpcState.Fighting && _targetVisible && _targetIsPlayer;

    /// <summary>0 (broken) .. 1: how much they've got left in them.</summary>
    public float Morale
    {
        get
        {
            if (_morale < 0f)
            {
                _moraleBase = Profile.IsCommando ? 0.95f : Aggression switch
                {
                    Aggression.Aggressive => 0.8f,
                    Aggression.Cautious => 0.65f,
                    _ => 0.6f,
                };
                _morale = _moraleBase;
            }

            return _morale;
        }
    }

    // ------------------------------------------------------------------------------------------------ callouts

    /// <summary>
    /// Calls something out (unless they did a moment ago, or someone of their side just said the same: no choruses).
    /// <paramref name="pick"/>: which line (-1: any).
    /// </summary>
    public void Say(Bark bark, NpcSenses s, int pick = -1)
    {
        if (!NpcBarks.Enabled || !Alive || Scripted || Hidden)
            return;
        var urgent = NpcBarks.Urgent(bark);
        if (s.Time - _saidAt < (urgent ? NpcBarks.UrgentCooldown : NpcBarks.PersonalCooldown))
            return;
        var intel = s.Squads.For(Faction, Zone);
        if (intel.LastSaid.TryGetValue(bark, out var last) && s.Time - last < (urgent ? 1f : NpcBarks.SideCooldown))
            return;
        _saidAt = s.Time;
        intel.LastSaid[bark] = s.Time;
        var (line, index) = NpcBarks.Pick(bark, pick >= 0 ? pick : _random.Next(16));
        Log.Write($"[BARK] {Name}: \"{line}\"");
        s.Spoke?.Invoke(this, bark, line, index);
    }

    // ------------------------------------------------------------------------------------------------ shouting

    /// <summary>
    /// In a firefight: now and then, a shout to those of their side within earshot (<see cref="ShoutRange"/>), who go on the
    /// alert and come to help (<see cref="HearShout"/>). Called each frame by <see cref="Npcs"/> for everyone in contact.
    /// </summary>
    public void ShoutContact(NpcSenses s)
    {
        if (s.Time - _shoutedAt < ShoutEvery || Blinded)
            return;
        _shoutedAt = s.Time;
        var from = Position + new float3(0, EyeHeight, 0);
        var where = _lastSeen;
        var told = new List<string>();
        foreach (var other in s.People)
        {
            if (other is not ArmedNpc { Alive: true, Scripted: false } ally || ReferenceEquals(ally, this) || ally.Zone != Zone || ally.Faction != Faction)
                continue;
            var ear = ally.Position + new float3(0, EyeHeight, 0);
            var dist = Distance(from, ear);
            if (dist > ShoutRange || (dist > ShoutRange * ShoutWalled && s.Clear(from, ear) < 0.99f))
                continue;
            if (ally.HearShout(this, where, dist, s))
                told.Add(ally.Name);
        }

        if (_targetVisible && (!_calledContact || told.Count > 0))
        {
            _calledContact = true;
            Say(Bark.Contact, s);
        }

        if (told.Count > 0)
            Log.Write($"[NPC] {Name} shouts contact: {string.Join(", ", told)} on the alert");
    }

    /// <summary>
    /// <paramref name="caller"/> shouted contact, <paramref name="dist"/> m off, with the enemy at <paramref name="where"/>:
    /// on the alert; not yet in the fight, they come and search there; searching already, a fresher lead. True: they
    /// weren't on the alert before.
    /// </summary>
    internal bool HearShout(ArmedNpc caller, float3 where, float dist, NpcSenses s)
    {
        if (Deafened || !Alive)
            return false;
        var fresh = !Alerted(s.Time) && State != NpcState.Fighting;
        _alertedUntil = s.Time + AlertedFor;
        if (!WantsToFight(s) || Blinded)
            return fresh;
        var delay = 0.3f + dist / 343f + 0.4f * (float)_random.NextDouble();
        if (State is NpcState.Calm or NpcState.Wary or NpcState.Returning)
        {
            if (fresh)
                Log.Write($"[NPC] {Name}: heard {caller.Name} shout contact {dist:0.0} m away: on the alert, coming to look");
            HearNoise(where, byPlayer: false, loud: true, delay);   // (Noticed: into the fight, searching from there; posted, they turn to look)
        }
        else if (State == NpcState.Fighting && !_targetVisible && caller._targetVisible && _hunt is Hunt.Search or Hunt.Hold)
        {
            Alarm(where, Stimulus.Noise);   // (UpdateHunt takes it for a new lead)
        }

        return fresh;
    }

    // ------------------------------------------------------------------------------------------------ morale

    /// <summary>
    /// <paramref name="victim"/> has just been killed: those of their side near enough take it in: more if they saw it, a
    /// little if they only heard it close by. The first to see it calls it out.
    /// </summary>
    public static void AllyDown(Npc victim, NpcSenses s)
    {
        var chest = victim.Position + new float3(0, 1f, 0);
        foreach (var other in s.People)
        {
            if (other is not ArmedNpc { Alive: true, Scripted: false } ally || ally.Faction != victim.Faction || ally.Zone != victim.Zone)
                continue;
            var eye = ally.Position + new float3(0, EyeHeight, 0);
            var dist = Distance(eye, chest);
            if (dist > 30f)
                continue;
            var saw = s.Visibility(eye, chest) > 0f;
            var shock = saw ? 0.25f : dist < 12f ? 0.12f : 0f;
            if (shock <= 0f)
                continue;
            ally.ShakeMorale(shock, $"{victim.Name} killed {dist:0} m away{(saw ? "" : ", out of sight")}");
            if (saw && ally.State == NpcState.Fighting)
                ally.Say(Bark.ManDown, s);
        }
    }

    /// <summary>Morale down by <paramref name="amount"/> (a commando takes it better).</summary>
    private void ShakeMorale(float amount, string why)
    {
        var before = Morale;
        _morale = float.Max(0f, before - amount * (Profile.IsCommando ? 0.6f : 1f));
        if ((before >= 0.4f && _morale < 0.4f) || (before >= 0.2f && _morale < 0.2f))
            Log.Write($"[NPC] {Name}: morale {before:0.00} -> {_morale:0.00} ({why}){(_morale < 0.2f ? ": breaking" : ": shaken")}");
    }

    /// <summary>Morale comes back slowly while nobody's shooting at them.</summary>
    private void RecoverMorale(float dt, float suppression)
    {
        if (suppression < ReleasedAt)
            _morale = float.Min(_moraleBase, _morale + MoraleRecover * dt);
    }

    /// <summary>
    /// Their courage now: morale, with the odds: each more of their side fighting nearby a little braver, each more enemy in
    /// sight a little less, and being badly hurt counts against it. 0..1.
    /// </summary>
    private float Courage(int allies, int enemies) =>
        float.Clamp(Morale + 0.08f * float.Min(allies, 4) - 0.1f * float.Max(0, enemies - 1) - (Health < 50f ? 0.15f : 0f), 0f, 1f);

    /// <summary>How many of their side are in the fight within 30 m (not counting them), and how many enemies they can see.</summary>
    private (int Allies, int Engaged, int Enemies) Odds(NpcSenses s)
    {
        int allies = 0, engaged = 0, enemies = _targetIsPlayer && _targetVisible ? 1 : 0;
        foreach (var other in s.People)
        {
            if (ReferenceEquals(other, this) || !other.Alive || other.Zone != Zone)
                continue;
            if (other is ArmedNpc ally && ally.Faction == Faction && ally.State == NpcState.Fighting)
            {
                if (Distance2(Position, ally.Position) > 30f * 30f)
                    continue;
                allies++;
                if (ally.InContact)
                    engaged++;
            }
            else if (_targetNpc is not null && ReferenceEquals(other, _targetNpc) && _targetVisible)
            {
                enemies++;
            }
        }

        return (allies, engaged, enemies);
    }

    // ------------------------------------------------------------------------------------------------ the player's reload

    private float _playerReloadUntil = float.NegativeInfinity;   // Senses.Time: the player's busy reloading till about then

    /// <summary>
    /// They heard (or saw) the player start to reload: a moment to make the most of. The bold rush them, those fighting from
    /// cover lean out at once, and they call it out (<see cref="Npcs.PlayerReloading"/>).
    /// </summary>
    public void PlayerReloading(NpcSenses s, string how)
    {
        if (!_targetIsPlayer || !_everSeen || Blinded || !EnemyOfPlayer(s))
            return;
        _playerReloadUntil = s.Time + 1.8f;
        Log.Write($"[NPC] {Name}: {how} the player reloading");
        Say(Bark.HeIsReloading, s);
        if (_cf == CoverFight.Hiding && _reloadLeft <= 0)
            _cfTimer = float.Min(_cfTimer, 0.15f);   // out now, while they can't shoot back
        _tacticThink = 0f;
    }
}
