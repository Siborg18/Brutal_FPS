using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// The police station's first aid kit: a white wall cabinet with a green cross on the tech room's east wall, just south
/// of the doorway into the bullpen (a few steps from the spawn point in front of the portal). Standing at it, E heals
/// the player back to full.
/// </summary>
public sealed partial class Level
{
    /// <summary>The cabinet's centre, on the face of the tech room's east wall (facing west, back into the room).</summary>
    private static float3 PoliceMedkitPoint => PAt(-BullpenHalfX - PoliceWall, 1.35f, DoorZ + 1.6f);
    private static readonly float3 PoliceMedkitNormal = new(-1, 0, 0);

    /// <summary>The hint shown at the kit (null when not near it).</summary>
    public string? PoliceMedkitPrompt { get; private set; }

    private void BuildPoliceMedkit()
    {
        var at = PoliceMedkitPoint;
        var wall = WallFrame(at, PoliceMedkitNormal);
        const float w = 0.44f, h = 0.38f, d = 0.13f;
        var white = PB(Material.PaintedMetal, new float3(0.93f, 0.93f, 0.9f), occludes: false);
        var green = PB(Material.Plain, new float3(0.05f, 0.55f, 0.25f), occludes: false);
        var cross = PB(Material.Plain, new float3(0.97f, 0.97f, 0.97f), occludes: false);
        var steel = PB(Material.Steel, new float3(0.55f, 0.55f, 0.56f), occludes: false);

        // The cabinet body and its door (a hair proud, a seam round it), a handle on the right.
        OBox(white, wall, new float3(0, 0, d / 2), new float3(w, h, d));
        OBox(PB(Material.Plain, new float3(0.6f, 0.6f, 0.58f), occludes: false), wall, new float3(0, 0, d + 0.0005f), new float3(w - 0.02f, h - 0.02f, 0.001f));
        OBox(white, wall, new float3(0, 0, d + 0.002f), new float3(w - 0.026f, h - 0.026f, 0.003f));
        OBox(steel, wall, new float3(w / 2 - 0.04f, 0, d + 0.012f), new float3(0.015f, 0.09f, 0.015f));
        // The green square with a white cross on the door.
        OBox(green, wall, new float3(-0.02f, 0.03f, d + 0.0045f), new float3(0.2f, 0.2f, 0.002f));
        OBox(cross, wall, new float3(-0.02f, 0.03f, d + 0.006f), new float3(0.14f, 0.045f, 0.002f));
        OBox(cross, wall, new float3(-0.02f, 0.03f, d + 0.006f), new float3(0.045f, 0.14f, 0.002f));
        AddLettering("FIRST AID", at + PoliceMedkitNormal * (d + 0.004f) + new float3(0, -0.12f, -0.02f), PoliceMedkitNormal, 0.035f, Material.Plain, new float3(0.05f, 0.45f, 0.2f), 0.002f);
    }

    /// <summary>
    /// Per frame: near the kit, the prompt shows, and E (<paramref name="usePressed"/>) heals the player to full.
    /// </summary>
    public void UpdatePoliceMedkit(bool usePressed, PlayerController player)
    {
        var at = PoliceMedkitPoint;
        var p = player.Position;
        var front = at + PoliceMedkitNormal * 0.6f;
        var near = player.Alive
            && float.Abs(p.X - front.X) < 0.9f && float.Abs(p.Z - front.Z) < 0.9f
            && p.Y > at.Y - 2f && p.Y < at.Y + 0.5f;
        if (!near)
        {
            PoliceMedkitPrompt = null;
            return;
        }

        var hurt = player.Health < PlayerController.MaxHealth;
        PoliceMedkitPrompt = hurt ? "[E] Use first aid kit" : "First aid kit (health full)";
        if (hurt && usePressed)
        {
            player.Heal();
            Log.Write("[MEDKIT] Healed to full at the police station's first aid kit");
        }
    }
}
