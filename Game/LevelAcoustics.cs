using Brutal.Numerics;
using Brutal.OpenAlApi;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// How each part of the world sounds: a reverb room for every zone (by the same x ranges as <see cref="ZoneAt"/>),
/// and smaller rooms inside them (the container, the shacks, the cellar, the hut). A sound belongs to the smallest
/// room that contains it. The presets are OpenAL Soft's (EFX "reverb presets"); Wet turns a room's reverb down.
/// Doorways cross-fade two rooms across an opening, so stepping (or shooting) out of a door doesn't flip the reverb.
/// </summary>
public sealed partial class Level
{
    // Zone rooms reach far up, down and sideways: each zone is a strip of x.
    private const float ZoneReach = 5000f;

    // How far a doorway's blend reaches either side of the opening (m), and past its edges.
    private const float DoorBlendDepth = 1.25f;
    private const float DoorBlendMargin = 0.3f;

    private static Aabb ZoneStrip(float minX, float maxX) =>
        new(new float3(minX, -ZoneReach, -ZoneReach), new float3(maxX, ZoneReach, ZoneReach));

    /// <summary>
    /// A container you can walk into: <paramref name="Interior"/> is the space inside, <paramref name="Opening"/> the
    /// open part of its door end (a flat box in the door plane), facing along <paramref name="Axis"/> (0 = x, 2 = z),
    /// toward + when <paramref name="DoorsAtMax"/>. <paramref name="Outside"/> is the room you step out into.
    /// Each one gets its own reverb room and a doorway blend, from <see cref="OpenContainers"/>.
    /// </summary>
    public readonly record struct OpenContainer(string Name, Aabb Interior, Aabb Opening, int Axis, bool DoorsAtMax, string Outside);

    /// <summary>
    /// Every container that can be entered. Only the one the orange portal opens into is hollow so far (the other
    /// containers in the yard and on the ship are solid); add any new open container here and it gets its acoustics.
    /// </summary>
    public static IReadOnlyList<OpenContainer> OpenContainers =>
    [
        // The doors are at the east end: the right-hand one (+z half) is shut, the left-hand one (-z half) open.
        new("Container", ContainerInterior,
            new Aabb(new float3(ContainerEastX, 0, YardOrigin.Z - InnerHalfWidth), new float3(ContainerEastX, InnerHeight, YardOrigin.Z)),
            Axis: 0, DoorsAtMax: true, Outside: "Yard"),
        // The grey one beside it with the orange portal's PC in it: built the same, its -z door open.
        new("PC container", YardPcInterior,
            new Aabb(new float3(ContainerEastX, 0, YardPcZ - InnerHalfWidth), new float3(ContainerEastX, InnerHeight, YardPcZ)),
            Axis: 0, DoorsAtMax: true, Outside: "Yard"),
        // The container in the prison yard: both its doors open at its east end.
        new("Prison container", PrisonContainerInterior,
            new Aabb(PrisonAt(PcEastX, 0, -PcInnerHalf), PrisonAt(PcEastX, PcInnerHeight, PcInnerHalf)),
            Axis: 0, DoorsAtMax: true, Outside: "Prison yard"),
        // The Arctic's second container: its doors at the south end, the east one (+x half) open.
        new("Arctic container", ArcticContainerInterior,
            new Aabb(new float3(ArcticOrigin.X + ArcticContainerX, 0, ArcticOrigin.Z + ArcticContainerZ1), new float3(ArcticOrigin.X + ArcticContainerX + InnerHalfWidth, InnerHeight, ArcticOrigin.Z + ArcticContainerZ1)),
            Axis: 2, DoorsAtMax: true, Outside: "Arctic"),
    ];

    public static IReadOnlyList<ReverbRoom> ReverbRooms()
    {
        List<ReverbRoom> rooms =
        [
            // The factory grounds outside, open but walled in by buildings; inside, the warehouse: a big concrete hall
            // (40 x 60 x 10 m) with a steel roof.
            new("Factory grounds", ZoneStrip(-ZoneReach, 100), ReverbPreset.Plain, 0.5f),
            new("Warehouse", FactoryHall, ReverbPreset.FactoryLargeroom, 0.9f),
            new("Factory office", FactoryOffice, ReverbPreset.Room, 0.85f),
            // The container yard: open sky, but walled in by stacks of steel boxes (slap-back off the stacks).
            new("Yard", ZoneStrip(100, 1500), ReverbPreset.Alley, 0.7f),
            // The outback: flat open plain; the tin shack, the roadhouse store and the shed over the cellar.
            new("Outback", ZoneStrip(1500, 4500), ReverbPreset.Plain, 0.45f),
            new("Shack", new Aabb(ShackMin, ShackMin + ShackSize), ReverbPreset.PrefabCaravan, 0.9f),
            new("Store", new Aabb(OutbackOrigin + new float3(12, 0, -96), OutbackOrigin + new float3(16, 2.6f, -88)), ReverbPreset.WoodenSmallroom, 0.8f),
            new("Shed", new Aabb(ShedMin, ShedMax), ReverbPreset.PrefabOuthouse, 0.9f),
            // The cellar and its shaft (the shed above is its own, smaller room).
            new("Cellar", CellarBounds, ReverbPreset.CastleSmallroom, 1f),
            // The bunker: a bare 12 x 12 m concrete room deep underground.
            new("Bunker", ZoneStrip(4500, 5500), ReverbPreset.FactoryMediumroom, 1f),
            // The Arctic: snow soaks up almost everything; the timber hut is small and boxy.
            new("Arctic", ZoneStrip(5500, 9000), ReverbPreset.Plain, 0.2f),
            new("Hut", new Aabb(HutMin, HutMax), ReverbPreset.WoodenSmallroom, 0.8f),
            // The ship: on deck at sea, between stacks of containers and the superstructure.
            new("Ship", ZoneStrip(9000, 16000), ReverbPreset.Alley, 0.5f),
            // The jungle: thick, damp forest soaks up the sound; the plank shack is a small wooden room.
            new("Jungle", ZoneStrip(16000, ZoneReach * 6), ReverbPreset.Forest, 0.35f),
            new("Bodega", new Aabb(BodegaMin, BodegaMax), ReverbPreset.WoodenSmallroom, 0.8f),
            // The police station: a carpeted open-plan office under a soft tiled ceiling; the tech room's bare walls;
            // the armoury's concrete floor and steel.
            new("Police", PoliceBuilding, ReverbPreset.Livingroom, 0.8f),   // (inside the warehouse's strip, but smaller)
            new("Tech room", PoliceTechRoom, ReverbPreset.Room, 0.85f),
            new("Armoury", PoliceArmoury, ReverbPreset.FactorySmallroom, 0.85f),
            // Behind the airlock: a long bare block corridor and a big empty concrete room.
            new("Vault corridor", PoliceVaultCorridor, ReverbPreset.Hallway, 0.9f),
            new("Vault", PoliceVault, ReverbPreset.FactoryMediumroom, 1f),
            new("Observation 1", PoliceObservationWest, ReverbPreset.Room, 0.9f),
            new("Observation 2", PoliceObservationEast, ReverbPreset.Room, 0.9f),
            // The prison: its yard walled in by the wings (slap-back off the blockwork), the white gatehouse, the
            // locker room's hard surfaces, the long block-walled cell corridor, a small bare cell.
            new("Prison yard", PrisonGrounds, ReverbPreset.CastleCourtyard, 0.6f),
            new("Gatehouse", PrisonGatehouse, ReverbPreset.Room, 0.8f),
            new("Locker room", PrisonLockerRoom, ReverbPreset.Bathroom, 0.55f),
            new("Cell corridor", PrisonCorridor, ReverbPreset.Hallway, 0.9f),
            new("Cell", PrisonCell, ReverbPreset.Room, 0.9f),
            // The flats: a street between brick blocks, the concrete undercroft, the cramped stairwell.
            new("Flats street", FlatsGrounds, ReverbPreset.CastleCourtyard, 0.5f),
            new("Undercroft", FlatsUndercroft, ReverbPreset.FactorySmallroom, 0.9f),
            new("Stairwell", FlatsStairwell, ReverbPreset.Hallway, 1f),
            // Rio: narrow alleys and stairways between rendered block walls, open to the sky.
            new("Rio", RioGrounds, ReverbPreset.Alley, 0.6f),
        ];

        // Inside a container: a narrow steel tube, bright and ringing.
        foreach (var c in OpenContainers)
            rooms.Add(new(c.Name, c.Interior, ReverbPreset.SpacestationShortpassage, 0.9f));
        return rooms;
    }

    /// <summary>The warehouse's inside (its own reverb room inside the factory grounds').</summary>
    private static Aabb FactoryHall => new(new float3(-HalfWidth, 0, -HalfLength), new float3(HalfWidth, WallHeight, HalfLength));

    /// <summary>The openings where two rooms blend: every open container's doors, and the warehouse's sliding door.</summary>
    public static IReadOnlyList<ReverbDoorway> ReverbDoorways() =>
        [.. ContainerDoorways(), new ReverbDoorway("Factory door",
            new Aabb(new float3(-FactoryDoorHalfWidth - DoorBlendMargin, 0, -HalfLength - DoorBlendDepth), new float3(FactoryDoorHalfWidth + DoorBlendMargin, FactoryDoorHeight + DoorBlendMargin, -HalfLength + DoorBlendDepth)),
            2, MinSide: "Factory grounds", MaxSide: "Warehouse"),
            new ReverbDoorway("Factory rear door",
            new Aabb(new float3(RearDoorX0 - DoorBlendMargin, RearDeckY, HalfLength - DoorBlendDepth), new float3(RearDoorX1 + DoorBlendMargin, RearDeckY + RearDoorHeight + DoorBlendMargin, HalfLength + WallThickness + DoorBlendDepth)),
            2, MinSide: "Warehouse", MaxSide: "Factory grounds"),
            new ReverbDoorway("Bodega door",
            new Aabb(new float3(BodegaMax.X - DoorBlendDepth, 0, JungleOrigin.Z + BodegaDoorZ0 - DoorBlendMargin), new float3(BodegaMax.X + DoorBlendDepth, BodegaDoorHeight + DoorBlendMargin, JungleOrigin.Z + BodegaDoorZ1 + DoorBlendMargin)),
            0, MinSide: "Bodega", MaxSide: "Jungle")];

    private static IEnumerable<ReverbDoorway> ContainerDoorways() =>
        OpenContainers.Select(c =>
        {
            var reach = new float3(DoorBlendMargin, 0, DoorBlendMargin);
            reach[c.Axis] = DoorBlendDepth;
            var bounds = new Aabb(c.Opening.Min - reach, c.Opening.Max + reach + new float3(0, DoorBlendMargin, 0));
            return c.DoorsAtMax
                ? new ReverbDoorway($"{c.Name} doors", bounds, c.Axis, MinSide: c.Name, MaxSide: c.Outside)
                : new ReverbDoorway($"{c.Name} doors", bounds, c.Axis, MinSide: c.Outside, MaxSide: c.Name);
        });
}
