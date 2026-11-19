using Brutal.Numerics;

namespace BrutalFps.Game;

/// <summary>
/// The armoured car (Assets/Models/armoured_car.glb and armoured_car_turret.glb: Tools/blender/build_armoured_car.py): a
/// desert-sand 4x4 with an M249 in an armoured turret on its roof, parked on the outback plain just south of the fenced yard
/// behind the shack, its nose towards the road. Its gunner (Npcs.Populate: "turret gunner") stands in the hatch; the turret
/// and its gun are <see cref="GunNest.ArmouredCar"/>'s, turning as they aim (Game/NpcGunner.cs).
/// </summary>
public sealed partial class Level
{
    /// <summary>The car is in this world (its model loaded, and the outback built).</summary>
    public bool HasArmouredCar { get; private set; }

    /// <summary>The car's hull and its turret, for the ones that drive in with the army on a factory raid (Game/FactoryArmy.cs).</summary>
    public static (Engine.Model? Hull, Engine.Model? Turret) ArmyCarModels => (TryModel("armoured_car"), TryModel("armoured_car_turret"));

    /// <summary>The middle of the car on the ground under it.</summary>
    public static float3 ArmouredCarAt => OutbackOrigin + new float3(20.5f, 0, 13.5f);

    /// <summary>Its nose points west (-X), towards the road.</summary>
    private const float ArmouredCarNoseYaw = float.Pi / 2;   // (the model's front, its -Z, turned onto -X)

    // Tools/blender/build_armoured_car.py: Z_ROOF, TURRET_AT, CRADLE_AHEAD, CRADLE_UP.
    private const float ArmouredCarRoof = 2.27f;
    public const float ArmouredCarRoofHeight = ArmouredCarRoof;
    private const float ArmouredCarTurretBack = 0.30f;   // the ring's middle, this far behind the middle of the car

    /// <summary>The middle of the turret's ring, on the roof.</summary>
    public static float3 ArmouredCarTurret => ArmouredCarAt + new float3(ArmouredCarTurretBack, ArmouredCarRoof, 0);

    /// <summary>Where the turret is laid at rest: towards the fenced yard and its portal, to the north-west.</summary>
    public static float ArmouredCarFacing => float.Atan2(-0.62f, -1f);

    public const float ArmouredCarCradleAhead = 0.80f, ArmouredCarCradleUp = 0.36f;

    /// <summary>Where the gunner stands in the hatch, the turret at rest: the floor under them is the top of the hull's lower box.</summary>
    public static float3 ArmouredCarGunnerAt
    {
        get
        {
            var (sin, cos) = float.SinCos(ArmouredCarFacing);
            var left = new float3(cos, 0, -sin);
            return (ArmouredCarTurret + left * 0.13f) with { Y = ArmouredCarTurret.Y + ArmouredCarCradleUp - GunNest.PintleHeight };
        }
    }

    /// <summary>
    /// The hull and its collision: a box for the lower hull up to the gunner's floor, and the cabin above it in pieces round
    /// the hatch, so the gunner stands in a hole in it (a bullet only finds their legs from above, and they fall into it).
    /// The turret has none: it turns.
    /// </summary>
    private void BuildArmouredCar()
    {
        if (TryModel("armoured_car") is not { } car)
            return;
        HasArmouredCar = true;
        var o = ArmouredCarAt;
        AddModel(car, float4x4.CreateRotationY(ArmouredCarNoseYaw) * float4x4.CreateTranslation(o), float3.One);

        // (Along X: the nose at -X, the back at +X; across Z.)
        void Box(float x0, float x1, float y0, float y1, float z0, float z1) =>
            AddCollider(o + new float3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new float3(x1 - x0, y1 - y0, z1 - z0));

        var floor = ArmouredCarGunnerAt.Y - o.Y;
        const float half = 1.15f, hole = 0.45f, roof = ArmouredCarRoof;
        var hx = ArmouredCarTurretBack;
        Box(-2.95f, 2.95f, 0.30f, floor, -half - 0.08f, half + 0.08f);        // lower hull, bumpers, flares
        Box(-1.30f, -0.80f, floor, 1.95f, -half, half);                       // up the windscreen
        Box(-0.80f, hx - hole, floor, roof, -half, half);                     // the cab, ahead of the hatch
        Box(hx + hole, 2.80f, floor, roof, -half, half);                      // the crew's space, behind it
        Box(hx - hole, hx + hole, floor, roof, -half, -hole);                 // either side of it
        Box(hx - hole, hx + hole, floor, roof, hole, half);
    }
}
