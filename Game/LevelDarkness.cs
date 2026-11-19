using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// How much light falls on a point, for people's eyes (<see cref="NpcSenses.PlayerLight"/>): 1 = broad daylight, about 0.24
/// = open ground under the moon, 0.02 = a dark interior at night. It follows what the shader draws without reading it back
/// from the GPU: the time of day in that level, the same dim-interior volumes (<see cref="ShadeVolume"/>), the warehouse's
/// hanging lamps and the local lights. Rays test the sun or moon and anything overhead, so the shadow of a building counts.
/// </summary>
public sealed partial class Level
{
    private const float DaySky = 0.85f, DaySun = 0.15f;      // by day the shade of a wall still leaves you in plain view
    private const float NightSky = 0.16f, NightMoon = 0.08f;
    private const float Roofed = 0.6f;                       // under a roof that has no shade volume of its own
    private const float RayLength = 150f;
    private const float HallDayLight = 0.7f, HallNightLight = 0.08f, FactoryOfficeLight = 0.6f;

    /// <summary>
    /// The light at <paramref name="p"/>, 0..1. <paramref name="clear"/> is <see cref="NpcSenses.Clear"/>, the
    /// line-of-sight test: a handful of rays per call, so call it a few times a second, not per person per frame.
    /// </summary>
    public float LightAt(float3 p, Func<float3, float3, float> clear)
    {
        var zone = ZoneAt(p);
        var light = zone switch
        {
            Zones.Warehouse when InHall(p) => HallLight(p),
            Zones.Warehouse => Outdoors(p, DayNightLevel.Factory, FactorySunDirection, clear),
            Zones.Yard => Outdoors(p, DayNightLevel.Yard, YardSunDirection, clear),
            Zones.Outback => Outdoors(p, DayNightLevel.Outback, OutbackSunDirection, clear),
            Zones.Jungle => Outdoors(p, DayNightLevel.Jungle, JungleSunDirection, clear),
            Zones.Ship => Outdoors(p, DayNightLevel.Ship, float3.UnitY, clear),
            Zones.Arctic => TimeOfDay.IsNight(DayNightLevel.Arctic) ? 0.3f : 1f,   // a whiteout: the light comes from everywhere
            Zones.Bunker => 0.08f,
            Zones.Cellar => 0.04f,
            Zones.Police => 0.75f,   // lit from inside, day or night
            Zones.Prison => 0.8f,
            Zones.Flats => 0.55f,
            Zones.Rio => TimeOfDay.IsNight(DayNightLevel.Rio)
                ? Outdoors(p, DayNightLevel.Rio, RioSunDirection, clear)   // the moon, and the bulbs over the alleys (DarkLights)
                : InteriorAmbient(p),                                      // a bright day, but for the hiding places' dark rooms
            _ => 1f,
        };

        foreach (var lamp in DarkLights)
            light += LampLight(lamp, p, clear);
        if (zone == Zones.Yard && TimeOfDay.IsNight(DayNightLevel.Yard))
        {
            // (Not up on the stacks or out on the quay, where the floods hardly reach: YardFloodReach.)
            var reach = YardFloodReach(p);
            foreach (var flood in YardFloods)
            {
                var d = float3.Distance(flood.Position, p);
                if (d < 45f)
                    light += 0.5f * reach * float.Pow(1f - d / 45f, 2f);
            }
            // The pools of light under the quay's own masts.
            foreach (var lamp in QuayMastLamps)
            {
                var d = float3.Distance(lamp, p);
                if (d < QuayMastRange)
                    light += 0.35f * (p.Y - YardOrigin.Y > 4.8f ? 0.06f : 1f) * float.Pow(1f - d * d / (QuayMastRange * QuayMastRange), 2f);
            }
        }

        return float.Clamp(light, 0f, 1f);
    }

    /// <summary>Inside the factory's hall (past the inner face of its walls), and not up on its roof.</summary>
    public static bool InHall(float3 p) => float.Abs(p.X) < HalfWidth && float.Abs(p.Z) < HalfLength && p.Y < WallHeight + 0.6f;

    /// <summary>Whether someone at <paramref name="eye"/>, out in the factory's grounds, is looking at <paramref name="at"/> in the hall (through its door, once that's open: the walls stop the look).</summary>
    public static bool LooksIntoHall(float3 eye, float3 at) => InHall(at) && !InHall(eye) && ZoneAt(eye) == Zones.Warehouse;

    /// <summary>The warehouse: dim daylight through its grimy windows, or its hanging lamps (as the shader's indoorLight falls off).</summary>
    private float HallLight(float3 p)
    {
        if (FactoryOffice.Contains(p))
            return FactoryOfficeLight;
        var light = TimeOfDay.IsNight(DayNightLevel.Factory) ? HallNightLight : HallDayLight;
        foreach (var lamp in _lamps)
        {
            var to = new float3(lamp.X, lamp.Y, lamp.Z) - p;
            light += 0.7f * float.Min(1f, lamp.W / (1f + float3.Dot(to, to) * 0.07f));
        }

        return light;
    }

    /// <summary>Under the sky: its light, less inside a dim interior, plus the sun or moon if the way to it is clear.</summary>
    private float Outdoors(float3 p, DayNightLevel level, float3 sun, Func<float3, float3, float> clear)
    {
        var night = TimeOfDay.IsNight(level);
        var sky = (night ? NightSky : DaySky) * InteriorAmbient(p);
        if (sky > (night ? NightSky : DaySky) * 0.99f && clear(p, p + float3.UnitY * RayLength) < 0.999f)
            sky *= Roofed;
        var direct = clear(p, p + float3.Normalize(sun) * RayLength) >= 0.999f ? (night ? NightMoon : DaySun) : 0f;
        return sky + direct;
    }

    /// <summary>The shader's interiorAmbient: how much of the sky's light reaches a point in a dim interior.</summary>
    private float InteriorAmbient(float3 p)
    {
        _shadeVolumes ??= [.. ShadeVolumes, .. JungleShadeVolumes, .. PrisonShadeVolumes, .. FlatsShadeVolumes, .. RioShadeVolumes];
        var k = 1f;
        foreach (var v in _shadeVolumes)
        {
            if (!v.Bounds.Contains(p))
                continue;
            var d = float3.Distance(p, v.Opening) / v.Reach;
            k = float.Min(k, v.Floor + (1f - v.Floor) * float.Exp(-d * d));
        }

        return k;
    }

    // (Made on first use: the time of day is settled by then, and every level's static layout is ready.)
    private ShadeVolume[]? _shadeVolumes;
    private LocalLight[]? _darkLights;

    // The lamps people can be picked out by, if there's a clear line from them (only the ones in the part of the level they light).
    private LocalLight[] DarkLights => _darkLights ??= [CellarBulb, ShedSkylight, BodegaBulb, .. NightLights, .. RioNightLamps,
        new(VentCenter + new float3(0, -0.35f, 0), TimeOfDay.Daylight(DayNightLevel.Bunker, new float3(0.80f, 0.88f, 1.0f)), 12f, 20f, BunkerInterior)];

    private static float LampLight(in LocalLight lamp, float3 p, Func<float3, float3, float> clear)
    {
        if (!lamp.Bounds.Contains(p))
            return 0f;
        var d = float3.Distance(lamp.Position, p);
        if (d >= lamp.Range || clear(lamp.Position, p) < 0.98f)
            return 0f;
        var brightness = float.Max(lamp.Color.X, float.Max(lamp.Color.Y, lamp.Color.Z));
        var falloff = 1f - d / lamp.Range;
        return float.Min(1f, lamp.Intensity * brightness / 8f) * falloff * falloff;
    }
}
