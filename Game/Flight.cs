using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// How something small and thrown flies (a flashbang, a flare): not a Bullet body, but fixed steps of gravity, a ray through
/// the physics world for each step, and a bounce that loses most of the speed into the surface and some along it. The same
/// steps plot a throw ahead of time, so it lands where they said unless something moves into its way. Portal water carries it
/// through to the linked portal (an offline portal's water is just something to bounce off).
/// </summary>
public sealed class Flight(Level level, PortalSystem portals, PhysicsWorld? physics, SurfaceTable surfaces)
{
    public const float Step = 1f / 60f;         // s: one step of the flight
    public const float Radius = 0.03f;          // m: it stops this short of what it hits
    private const float Restitution = 0.32f;

    /// <summary>
    /// One step of flight: gravity, then the step's ray through the world. Returns true if it struck something (how
    /// hard: the speed into the surface, and what it was); <paramref name="jumped"/> if it went through a portal.
    /// </summary>
    public bool Advance(ref float3 position, ref float3 velocity, ref bool resting, out float impact, out SurfaceInfo surface, out bool jumped)
    {
        impact = 0;
        surface = default;
        jumped = false;
        if (resting)
            return false;
        velocity.Y -= 9.81f * Step;
        var move = velocity * Step;
        var length = float.Sqrt(float3.Dot(move, move));
        if (length < 1e-6f)
            return false;
        var direction = move / length;
        var reach = length + Radius;

        var hitWorld = false;
        var distance = reach;
        var normal = float3.UnitY;
        if (physics is not null)
        {
            if (physics.Raycast(position, position + direction * reach, out var hit, includeBodies: false))
            {
                hitWorld = true;
                distance = hit.Fraction * reach;
                normal = hit.Normal;
                surface = surfaces[hit.UserIndex];
            }
        }
        else if (level.RaycastSurface(position, direction, reach, out var d, out var n, out var s))
        {
            hitWorld = true;
            distance = d;
            normal = n;
            surface = s;
        }

        // Into portal water: out of the linked portal, carrying on as it went in (offline, it's water to bounce off).
        if (portals.Raycast(position, direction, distance, out var portalDistance, out var portal))
        {
            if (portal!.Online)
            {
                var toLinked = portal.ToLinked;
                var outDirection = float3.Normalize(float3.TransformNormal(direction, toLinked));
                position = float3.Transform(position + direction * portalDistance, toLinked) + outDirection * 0.02f;
                velocity = float3.TransformNormal(velocity, toLinked);
                jumped = true;
                return false;
            }

            hitWorld = true;
            distance = portalDistance;
            normal = portal.Normal;
            surface = new SurfaceInfo(SurfaceKind.Water, new float3(0.5f), Material.Plain);
        }

        if (!hitWorld)
        {
            position += move;
            return false;
        }

        position += direction * float.Max(0f, distance - Radius);
        var into = float3.Dot(velocity, normal);
        if (into >= 0f)
            return false;
        impact = -into;
        // A bounce: most of the speed into the surface is lost (a dead little hop off a hard hit, none off a light
        // one), and some along it, more the harder it hit (rolling along a floor, a little every step).
        var along = velocity - normal * into;
        var keep = 1f - float.Min(0.35f, 0.015f + 0.12f * impact);
        velocity = along * keep - normal * (into * (impact > 1f ? Restitution : 0.05f));
        position += normal * 0.002f;
        if (normal.Y > 0.7f && float3.Dot(velocity, velocity) < 0.2f * 0.2f)
        {
            resting = true;
            velocity = float3.Zero;
        }

        return impact > 0.6f;
    }
}
