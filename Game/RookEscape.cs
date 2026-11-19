using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// Rook's way out, once he's through the portal and out of the cobalt one in the undercroft (<see cref="PrisonSquad"/>):
/// he runs across to the stairwell's door and stops there with his hand on it, looking back, until the player has come
/// through after him (so you see him go, however long the raiders took you). Then he's through it, it shuts behind him unless
/// you're in the way, and he runs up the dog-leg four floors, in through the flat's door on the top landing, and that shuts too.
/// It's the script's: he doesn't notice you, and nothing here waits for you once he's started up the stairs.
/// </summary>
public sealed class RookEscape(Level level, NpcSenses senses, Npc rook)
{
    private const float RunSpeed = 3.7f;        // m/s (the locomotion blend picks the run from how fast he's moved)
    private const float StairSpeed = 2.9f;      // m/s, along the ground
    private const float DoorTakes = 0.55f;      // s from the door starting to open to his going through it
    private const float ShutsAfter = 1.2f;      // s after he goes through
    private const int FloorsBeforeLog = 8;

    private enum Stage { ToDoor, WaitingAtDoor, OpeningDoor, Upstairs, TopDoor, Gone }

    private Stage _stage = Stage.ToDoor;
    private readonly float3[] _stairs = Level.RookUpTheStairs;
    private int _next;
    private float _clock;
    private bool _doorShut;

    /// <summary>He's started: on his way to the door.</summary>
    public void Start()
    {
        rook.ScriptFace(Level.RookAtDoor);
        rook.ScriptWalk(Level.RookAtDoor, RunSpeed);
        Log.Write($"[ROOK] Out of the portal in the undercroft: running for the door at {Level.RookAtDoor}");
    }

    public void Update(float dt)
    {
        if (!rook.Alive || _stage == Stage.Gone)
            return;
        _clock += dt;
        ShutStairDoorBehindHim();
        switch (_stage)
        {
            case Stage.ToDoor when rook.ScriptArrived:
                _stage = Stage.WaitingAtDoor;
                rook.ScriptFace(Level.FlatsPortalFloorPoint + new float3(0, 1.6f, 0));   // (looks back the way he came)
                Log.Write("[ROOK] At the door, waiting for the player to come through");
                break;
            case Stage.WaitingAtDoor when Level.ZoneAt(senses.PlayerEye) == Zones.Flats:
                _stage = Stage.OpeningDoor;
                _clock = 0;
                rook.ScriptFace(Level.RookAtDoor + new float3(1f, 0, 0));
                if (level.FlatsStairDoorShut)
                    level.SetFlatsDoor(top: false, open: true);
                Log.Write("[ROOK] The player's through: he opens the door");
                break;
            case Stage.OpeningDoor when _clock >= DoorTakes:
                _stage = Stage.Upstairs;
                _clock = 0;
                _next = 0;
                Next(RunSpeed);
                Log.Write($"[ROOK] Through the door and up the stairs ({_stairs.Length} points)");
                break;
            case Stage.Upstairs when rook.ScriptArrived:
                if (_next < _stairs.Length)
                {
                    Next(StairSpeed);
                    if (_next % FloorsBeforeLog == 0)
                        Log.Write($"[ROOK] On the stairs at {rook.Position - Level.FlatsOrigin}");
                }
                else
                {
                    _stage = Stage.TopDoor;
                    _clock = 0;
                    rook.ScriptFace(Level.RookGone);
                    level.SetFlatsDoor(top: true, open: true);
                    Log.Write($"[ROOK] On the top landing at {rook.Position - Level.FlatsOrigin}: he unlocks the door");
                }
                break;
            case Stage.TopDoor when _clock >= 0.6f && rook.ScriptArrived && float3.Distance(rook.Position, Level.RookGone) > 0.1f:
                rook.ScriptWalk(Level.RookGone, RunSpeed);
                break;
            case Stage.TopDoor when rook.ScriptArrived && float3.Distance(rook.Position, Level.RookGone) <= 0.1f:
                _stage = Stage.Gone;
                level.SetFlatsDoor(top: true, open: false);
                Log.Write("[ROOK] Gone: the door on the top landing shuts behind him");
                break;
        }
    }

    private void Next(float speed)
    {
        var to = _stairs[_next++];
        rook.ScriptFace(to);
        rook.ScriptWalk(to, speed);
    }

    /// <summary>He pulls it to behind him, a moment after he's through (not on the player, who'll have to open it again).</summary>
    private void ShutStairDoorBehindHim()
    {
        if (_doorShut || _stage < Stage.Upstairs || (_stage == Stage.Upstairs && _clock < ShutsAfter))
            return;
        _doorShut = true;
        if (!Level.InFlatsDoorway(senses.PlayerEye))
            level.SetFlatsDoor(top: false, open: false);
    }
}
