using Brutal.Numerics;
using BulletSharp;
using BVector3 = BulletSharp.Math.Vector3;
using BMatrix = BulletSharp.Math.Matrix;

namespace BrutalFps.Engine;

/// <summary>Where a ray met the physics world.</summary>
/// <param name="Normal">Unit normal of the surface, facing back along the ray.</param>
/// <param name="Fraction">0 at the ray's start, 1 at its end.</param>
/// <param name="UserIndex">The tag the hit object was added with (for static geometry: its surface).</param>
/// <param name="Body">The moving body that was hit, or null for static geometry.</param>
/// <param name="Object">What was hit (a body, or a piece of static geometry).</param>
public readonly record struct PhysicsHit(float3 Point, float3 Normal, float Fraction, int UserIndex, RigidBody? Body, CollisionObject Object);

/// <summary>
/// The game's Bullet physics world (bullet3, through the BulletSharp wrapper). The level is added as static
/// geometry - its boxes as box shapes, and every drawn mesh (Blender models, code-built meshes, portal rings) as a
/// BVH triangle mesh, so bullets and debris collide with exactly what you see. The player is still moved by
/// <c>PlayerController</c>'s own box collision; this world only holds bullets' targets and small moving debris
/// (casings, magazines, chips, splinters, squashed bullets).
/// Semi-realistic rather than simulation-grade: a fixed 120 Hz step, small margins, continuous collision for the
/// small fast pieces, and everything goes to sleep quickly once it has settled.
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    public const int StaticGroup = (int)CollisionFilterGroups.StaticFilter;
    public const int DebrisGroup = (int)CollisionFilterGroups.DebrisFilter;

    /// <summary>Ragdoll limbs: they lie on the level, debris bounces off them, and other bodies fall over them; shots test the skin instead.</summary>
    public const int RagdollGroup = (int)CollisionFilterGroups.CharacterFilter;

    public const float FixedStep = 1f / 120f;

    private readonly DefaultCollisionConfiguration _config;
    private readonly CollisionDispatcher _dispatcher;
    private readonly DbvtBroadphase _broadphase;
    private readonly SequentialImpulseConstraintSolver _solver;
    private readonly DiscreteDynamicsWorld _world;
    private ClosestRayResultCallback? _rayCallback;

    // Everything handed to Bullet stays referenced here: the wrappers free their native objects when collected.
    private readonly List<object> _keepAlive = [];
    private readonly Dictionary<Mesh, BvhTriangleMeshShape?> _meshShapes = [];
    private readonly Dictionary<float3, BoxShape> _boxShapes = [];
    private readonly HashSet<CollisionObject> _doorParts = [];

    public int StaticObjects { get; private set; }
    public long StaticTriangles { get; private set; }
    public int Bodies { get; private set; }

    /// <summary>Goes up each time a door's part moves.</summary>
    public int DoorMoves { get; private set; }

    private PhysicsWorld()
    {
        _config = new DefaultCollisionConfiguration();
        _dispatcher = new CollisionDispatcher(_config);
        _broadphase = new DbvtBroadphase(null);
        _solver = new SequentialImpulseConstraintSolver();
        _world = new DiscreteDynamicsWorld(_dispatcher, _broadphase, _solver, _config)
        {
            Gravity = new BVector3(0, -9.81, 0),
        };
        _world.SolverInfo.NumIterations = 10;
        // Only what moves needs its bounds worked out again each step (doors say when they move: MoveDoorPart). By default
        // Bullet does it for all the level's thousands of static objects as well, on every step.
        _world.ForceUpdateAllAabbs = false;
    }

    /// <summary>The physics world, or null if Bullet's native library can't be loaded on this machine.</summary>
    public static PhysicsWorld? TryCreate()
    {
        try
        {
            var world = new PhysicsWorld();
            Log.Write("Physics: Bullet (bullet3 2.89 via BulletSharp, double precision).");
            return world;
        }
        catch (Exception e) when (e is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException)
        {
            Log.Write($"[WARN] Bullet physics isn't available ({e.GetType().Name}: {e.Message}); shots use the level's boxes and debris is off.");
            return null;
        }
    }

    // ------------------------------------------------------------------------------------------------ static world

    /// <summary>A static box (a level box). Returns false if it was degenerate.</summary>
    public void AddStaticBox(in Aabb box, int userIndex, float restitution, float friction)
    {
        var half = box.Size * 0.5f;
        if (half.X <= 0 || half.Y <= 0 || half.Z <= 0)
            return;
        var key = new float3(MathF.Round(half.X, 4), MathF.Round(half.Y, 4), MathF.Round(half.Z, 4));
        if (!_boxShapes.TryGetValue(key, out var shape))
        {
            shape = new BoxShape(key.X, key.Y, key.Z);
            shape.Margin = float.Min(0.02f, float.Min(key.X, float.Min(key.Y, key.Z)) * 0.5f);
            _boxShapes[key] = shape;
            _keepAlive.Add(shape);
        }

        AddStatic(shape, float4x4.CreateTranslation(box.Center), userIndex, restitution, friction);
        StaticTriangles += 12;
    }

    /// <summary>
    /// A static triangle mesh drawn with <paramref name="transform"/>. Meshes drawn many times (a model's parts)
    /// share one BVH; scaled copies use a scaled view of it, and anything sheared or mirrored gets its own.
    /// </summary>
    public void AddStaticMesh(Mesh mesh, in float4x4 transform, int userIndex, float restitution, float friction)
    {
        if (mesh.Positions.Length < 9)
            return;

        // Split the transform into scale, rotation and translation (rows: v * S * R + T).
        var rows = new[] { new float3(transform.X.X, transform.X.Y, transform.X.Z), new float3(transform.Y.X, transform.Y.Y, transform.Y.Z), new float3(transform.Z.X, transform.Z.Y, transform.Z.Z) };
        var scale = new float3(Length(rows[0]), Length(rows[1]), Length(rows[2]));
        if (scale.X < 1e-6f || scale.Y < 1e-6f || scale.Z < 1e-6f)
            return;
        var r0 = rows[0] / scale.X;
        var r1 = rows[1] / scale.Y;
        var r2 = rows[2] / scale.Z;
        var orthogonal = float.Abs(float3.Dot(r0, r1)) < 1e-3f && float.Abs(float3.Dot(r1, r2)) < 1e-3f && float.Abs(float3.Dot(r0, r2)) < 1e-3f;
        var rightHanded = float3.Dot(float3.Cross(r0, r1), r2) > 0;

        CollisionShape shape;
        float4x4 placement;
        if (orthogonal && rightHanded)
        {
            var bvh = SharedShape(mesh);
            if (bvh is null)
                return;
            var unit = float.Abs(scale.X - 1) < 1e-4f && float.Abs(scale.Y - 1) < 1e-4f && float.Abs(scale.Z - 1) < 1e-4f;
            if (unit)
            {
                shape = bvh;
            }
            else
            {
                var scaled = new ScaledBvhTriangleMeshShape(bvh, new BVector3(scale.X, scale.Y, scale.Z));
                _keepAlive.Add(scaled);
                shape = scaled;
            }

            placement = new float4x4(
                r0.X, r0.Y, r0.Z, 0,
                r1.X, r1.Y, r1.Z, 0,
                r2.X, r2.Y, r2.Z, 0,
                transform.W.X, transform.W.Y, transform.W.Z, 1);
        }
        else
        {
            // Bake the whole transform into the triangles.
            var baked = BuildBvh(mesh.Positions, transform);
            if (baked is null)
                return;
            shape = baked;
            placement = float4x4.Identity;
        }

        AddStatic(shape, placement, userIndex, restitution, friction);
        StaticTriangles += mesh.Positions.Length / 9;
    }

    /// <summary>
    /// Builds the trees of these meshes on all cores, ready for <see cref="AddStaticMesh"/>. (A tree is by far the slowest
    /// part of adding a mesh, and it's only the mesh's own triangles: they can all be built at once.)
    /// </summary>
    public void PrepareMeshes(IEnumerable<Mesh> meshes)
    {
        var distinct = meshes.Distinct().Where(m => !_meshShapes.ContainsKey(m) && m.VertexCount >= 3).ToArray();
        var shapes = new BvhTriangleMeshShape?[distinct.Length];
        Parallel.For(0, distinct.Length, i => shapes[i] = BuildBvh(distinct[i].Positions, float4x4.Identity));
        for (var i = 0; i < distinct.Length; i++)
            _meshShapes[distinct[i]] = shapes[i];
    }

    private BvhTriangleMeshShape? SharedShape(Mesh mesh)
    {
        if (!_meshShapes.TryGetValue(mesh, out var shape))
        {
            shape = BuildBvh(mesh.Positions, float4x4.Identity);
            _meshShapes[mesh] = shape;
        }

        return shape;
    }

    private BvhTriangleMeshShape? BuildBvh(float[] positions, in float4x4 transform)
    {
        var vertexCount = positions.Length / 3;
        var triangles = vertexCount / 3;
        if (triangles == 0)
            return null;
        var vertices = new double[triangles * 9];
        var indices = new int[triangles * 3];
        var identity = transform == float4x4.Identity;
        for (var i = 0; i < triangles * 3; i++)
        {
            var p = new float3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            if (!identity)
                p = float3.Transform(p, transform);
            vertices[i * 3] = p.X;
            vertices[i * 3 + 1] = p.Y;
            vertices[i * 3 + 2] = p.Z;
            indices[i] = i;
        }

        var array = new TriangleIndexVertexArray(indices, vertices);
        var shape = new BvhTriangleMeshShape(array, true, true);
        lock (_keepAlive)
        {
            _keepAlive.Add(array);
            _keepAlive.Add(shape);
        }

        return shape;
    }

    /// <summary>
    /// A rigid piece of the level that moves when told to (<see cref="MoveDoorPart"/>): one mesh of a door's leaf,
    /// <paramref name="transform"/> being how it's drawn (rotation and translation only). Null if the mesh is empty.
    /// </summary>
    public CollisionObject? AddDoorPart(Mesh mesh, in float4x4 transform, int userIndex, float restitution, float friction)
    {
        if (mesh.Positions.Length < 9 || SharedShape(mesh) is not { } shape)
            return null;
        var part = AddStatic(shape, transform, userIndex, restitution, friction);
        _doorParts.Add(part);
        StaticTriangles += mesh.Positions.Length / 9;
        return part;
    }

    /// <summary>Moves a door's part; shots meet it there straight away. (Debris asleep in its way stays asleep, and the door passes it.)</summary>
    public void MoveDoorPart(CollisionObject part, in float4x4 transform)
    {
        part.WorldTransform = ToBullet(transform);
        _world.UpdateSingleAabb(part);
        DoorMoves++;
    }

    /// <summary>Takes a part added by <see cref="AddDoorPart"/> out of the world again (a vehicle that's gone).</summary>
    public void RemoveDoorPart(CollisionObject part)
    {
        if (!_doorParts.Remove(part))
            return;
        _world.RemoveCollisionObject(part);   // (its shape is shared: it stays, for the next one)
    }

    /// <summary>The door's part at a point on a surface, if that surface is one (so that what's on it can move with it).</summary>
    public CollisionObject? DoorPartAt(float3 point, float3 normal)
    {
        if (_doorParts.Count == 0 || !Raycast(point + normal * 0.01f, point - normal * 0.01f, out var hit, includeBodies: false))
            return null;
        return _doorParts.Contains(hit.Object) ? hit.Object : null;
    }

    private CollisionObject AddStatic(CollisionShape shape, in float4x4 placement, int userIndex, float restitution, float friction)
    {
        var obj = new CollisionObject
        {
            CollisionShape = shape,
            WorldTransform = ToBullet(placement),
            UserIndex = userIndex,
            Restitution = restitution,
            Friction = friction,
            CollisionFlags = CollisionFlags.StaticObject,
        };
        _world.AddCollisionObject(obj, StaticGroup, (int)CollisionFilterGroups.AllFilter ^ StaticGroup);
        // Asleep, as Bullet makes its own static rigid bodies. A plain collision object starts out "active", and with
        // ForceUpdateAllAabbs off Bullet still works out every active object's bounds again on every step: over 1 ms a
        // step for the level's thousands of objects, with nothing moving. (Shots and moving bodies meet it just the same;
        // a door's part has its bounds updated by MoveDoorPart.)
        obj.ActivationState = ActivationState.IslandSleeping;
        _keepAlive.Add(obj);
        StaticObjects++;
        return obj;
    }

    // ------------------------------------------------------------------------------------------------ moving bodies

    /// <summary>
    /// A small moving body. <paramref name="size"/> (its smallest dimension, metres) sets up continuous collision so it
    /// can't tunnel through thin walls at speed.
    /// </summary>
    public RigidBody AddBody(CollisionShape shape, float mass, in float4x4 transform, float3 velocity, float3 angularVelocity,
        float restitution, float friction, float size, int userIndex = -1)
    {
        var inertia = shape.CalculateLocalInertia(mass);
        if (_pool.TryPop(out var pooled))
        {
            // A body given back earlier (ReleaseBody): set it up afresh rather than make a new one. Making and freeing
            // Bullet bodies (each a motion state, a construction info and the native body) is what a firefight's chips
            // and spent rounds churn through, dozens a second.
            var start = ToBullet(transform);
            pooled.CollisionShape = shape;
            pooled.SetMassProps(mass, inertia);
            pooled.UpdateInertiaTensor();
            pooled.Restitution = restitution;
            pooled.Friction = friction;
            pooled.WorldTransform = start;
            pooled.InterpolationWorldTransform = start;
            pooled.MotionState!.SetWorldTransform(ref start);
            pooled.LinearVelocity = ToBullet(velocity);
            pooled.AngularVelocity = ToBullet(angularVelocity);
            pooled.InterpolationLinearVelocity = pooled.LinearVelocity;
            pooled.InterpolationAngularVelocity = pooled.AngularVelocity;
            pooled.ClearForces();
            pooled.HitFraction = 1;
            pooled.DeactivationTime = 0;
            pooled.ForceActivationState(ActivationState.ActiveTag);
            pooled.CcdMotionThreshold = size * 0.5f;
            pooled.CcdSweptSphereRadius = size * 0.35f;
            pooled.UserIndex = userIndex;
            _world.AddRigidBody(pooled, DebrisGroup, StaticGroup | DebrisGroup | RagdollGroup | (int)CollisionFilterGroups.DefaultFilter);
            Bodies++;
            Reused++;
            return pooled;
        }

        var motion = new DefaultMotionState(ToBullet(transform));
        using var info = new RigidBodyConstructionInfo(mass, motion, shape, inertia)
        {
            Restitution = restitution,
            Friction = friction,
            RollingFriction = 0.002,
            LinearDamping = 0.05,
            AngularDamping = 0.25,
            LinearSleepingThreshold = 0.06,
            AngularSleepingThreshold = 0.6,
        };
        var body = new RigidBody(info)
        {
            LinearVelocity = ToBullet(velocity),
            AngularVelocity = ToBullet(angularVelocity),
            CcdMotionThreshold = size * 0.5f,
            CcdSweptSphereRadius = size * 0.35f,
            UserIndex = userIndex,
            SpinningFriction = 0.002,
        };
        _world.AddRigidBody(body, DebrisGroup, StaticGroup | DebrisGroup | RagdollGroup | (int)CollisionFilterGroups.DefaultFilter);
        Bodies++;
        return body;
    }

    /// <summary>
    /// One limb of a ragdoll (<see cref="Ragdoll"/>): heavier damping than debris so a body settles rather than rolls, and
    /// continuous collision sized to the limb so a fast fall doesn't leave a foot inside the floor.
    /// </summary>
    public RigidBody AddRagdollBody(CollisionShape shape, float mass, in float4x4 transform, float3 velocity, float3 angularVelocity, float size)
    {
        var inertia = shape.CalculateLocalInertia(mass);
        var motion = new DefaultMotionState(ToBullet(transform));
        using var info = new RigidBodyConstructionInfo(mass, motion, shape, inertia)
        {
            Restitution = 0.05,
            Friction = 0.85,
            RollingFriction = 0.02,
            LinearDamping = 0.08,
            AngularDamping = 0.6,
            LinearSleepingThreshold = 0.12,
            AngularSleepingThreshold = 0.8,
        };
        var body = new RigidBody(info)
        {
            LinearVelocity = ToBullet(velocity),
            AngularVelocity = ToBullet(angularVelocity),
            CcdMotionThreshold = size * 0.5f,
            CcdSweptSphereRadius = size * 0.4f,
            SpinningFriction = 0.02,
        };
        _world.AddRigidBody(body, RagdollGroup, StaticGroup | DebrisGroup | RagdollGroup);
        Bodies++;
        return body;
    }

    /// <summary>A joint between two bodies, which then don't collide with each other.</summary>
    public void AddConstraint(TypedConstraint constraint) => _world.AddConstraint(constraint, true);

    public void RemoveConstraint(TypedConstraint constraint)
    {
        _world.RemoveConstraint(constraint);
        constraint.Dispose();
    }

    public void RemoveBody(RigidBody body)
    {
        _world.RemoveRigidBody(body);
        body.MotionState?.Dispose();
        body.Dispose();
        Bodies--;
    }

    /// <summary>
    /// Takes a body made by <see cref="AddBody"/> out of the world and keeps it for the next <see cref="AddBody"/> to
    /// reuse (object pooling), rather than freeing it. Past <see cref="PoolSize"/> spare bodies, it's freed after all.
    /// </summary>
    public void ReleaseBody(RigidBody body)
    {
        if (_pool.Count >= PoolSize)
        {
            RemoveBody(body);
            return;
        }

        _world.RemoveRigidBody(body);
        body.LinearVelocity = default;
        body.AngularVelocity = default;
        _pool.Push(body);
        Bodies--;
    }

    /// <summary>The most spare debris bodies kept for reuse (about what the debris caps add up to).</summary>
    public const int PoolSize = 320;

    private readonly Stack<RigidBody> _pool = new(PoolSize);

    /// <summary>How many times <see cref="AddBody"/> handed back a pooled body rather than making one (for the log).</summary>
    public int Reused { get; private set; }

    /// <summary>Spare bodies waiting in the pool.</summary>
    public int Pooled => _pool.Count;

    /// <summary>A body's interpolated transform, for drawing.</summary>
    public static float4x4 TransformOf(RigidBody body)
    {
        BMatrix m;
        if (body.MotionState is { } state)
            state.GetWorldTransform(out m);
        else
            m = body.WorldTransform;
        return FromBullet(m);
    }

    public static float3 VelocityOf(RigidBody body) => FromBullet(body.LinearVelocity);

    /// <summary>Pushes a body at a point on it (world space), waking it up.</summary>
    public static void Push(RigidBody body, float3 impulse, float3 worldPoint)
    {
        body.Activate(true);
        var com = FromBullet(body.CenterOfMassPosition);
        body.ApplyImpulse(ToBullet(impulse), ToBullet(worldPoint - com));
    }

    /// <summary>
    /// One fixed step while the game's still loading. Bullet's first step sorts every body in the world into its broadphase
    /// and sets up its solver (30 to 70 ms with all the levels in): this has it done before the first frame, rather than as a
    /// hitch in it. It doesn't count towards <see cref="SubSteps"/> or what a step costs.
    /// </summary>
    public void WarmUp() => _world.StepSimulation(FixedStep, 1, FixedStep);

    public void Step(float dt)
    {
        if (dt <= 0)
            return;
        using var timer = Perf.PhysicsStep.Time();
        // No more fixed steps in a frame than fit in StepBudget, going by what one cost lately. Without this a slow frame
        // asks for more steps, which make the next frame slower still (up to 6 a frame): the debris just goes into slow
        // motion for a moment instead.
        var most = int.Clamp((int)(StepBudgetMs / float.Max(_msPerStep, 1e-3f)), 1, 6);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var steps = _world.StepSimulation(dt, most, FixedStep);
        if (steps > 0)
        {
            var ms = (float)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / steps;
            _msPerStep += (ms - _msPerStep) * 0.2f;
        }

        SubSteps += steps;
    }

    /// <summary>The time (ms) a frame may give the fixed steps; see <see cref="Step"/>.</summary>
    public const float StepBudgetMs = 6f;

    private float _msPerStep = 0.1f;   // what one fixed step has been costing (eased)

    /// <summary>Fixed steps taken so far (for the stress test's log).</summary>
    public int SubSteps;

    /// <summary>How many moving bodies are awake (not asleep or switched off): what each step really costs.</summary>
    public int AwakeBodies()
    {
        var awake = 0;
        foreach (var obj in _world.CollisionObjectArray)
        {
            if (!obj.IsStaticOrKinematicObject && obj.IsActive)
                awake++;
        }

        return awake;
    }

    // ------------------------------------------------------------------------------------------------ queries

    /// <summary>The first thing the segment from <paramref name="from"/> to <paramref name="to"/> touches.</summary>
    public bool Raycast(float3 from, float3 to, out PhysicsHit hit, bool includeBodies = true)
    {
        using var timer = Perf.PhysicsRay.Time();
        var a = ToBullet(from);
        var b = ToBullet(to);
        // One callback, reset for each ray: a new one is about 240 bytes of garbage, and a firefight casts about 200 rays a frame.
        // (Only the game's thread casts rays, and nothing casts one from inside another.)
        var callback = _rayCallback ??= new ClosestRayResultCallback(ref a, ref b) { CollisionFilterGroup = (int)CollisionFilterGroups.DefaultFilter };
        callback.RayFromWorld = a;
        callback.RayToWorld = b;
        callback.ClosestHitFraction = 1;
        callback.CollisionObject = null;
        callback.CollisionFilterMask = includeBodies ? StaticGroup | DebrisGroup : StaticGroup;
        _world.RayTestRef(ref a, ref b, callback);
        if (!callback.HasHit || callback.CollisionObject is not { } obj)
        {
            hit = default;
            return false;
        }

        var direction = to - from;
        var normal = FromBullet(callback.HitNormalWorld);
        var length = Length(normal);
        normal = length > 1e-6f ? normal / length : -float3.Normalize(direction);
        if (float3.Dot(normal, direction) > 0)
            normal = -normal; // the back of a triangle
        var body = obj.IsStaticOrKinematicObject ? null : RigidBody.Upcast(obj);
        hit = new PhysicsHit(FromBullet(callback.HitPointWorld), normal, (float)callback.ClosestHitFraction, obj.UserIndex, body, obj);
        return true;
    }

    // ------------------------------------------------------------------------------------------------ conversions

    public static BVector3 ToBullet(float3 v) => new(v.X, v.Y, v.Z);

    public static float3 FromBullet(BVector3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static BMatrix ToBullet(in float4x4 m) => new()
    {
        M11 = m.X.X, M12 = m.X.Y, M13 = m.X.Z, M14 = 0,
        M21 = m.Y.X, M22 = m.Y.Y, M23 = m.Y.Z, M24 = 0,
        M31 = m.Z.X, M32 = m.Z.Y, M33 = m.Z.Z, M34 = 0,
        M41 = m.W.X, M42 = m.W.Y, M43 = m.W.Z, M44 = 1,
    };

    public static float4x4 FromBullet(in BMatrix m) => new(
        (float)m.M11, (float)m.M12, (float)m.M13, 0,
        (float)m.M21, (float)m.M22, (float)m.M23, 0,
        (float)m.M31, (float)m.M32, (float)m.M33, 0,
        (float)m.M41, (float)m.M42, (float)m.M43, 1);

    private static float Length(float3 v) => float.Sqrt(float3.Dot(v, v));

    public void Dispose()
    {
        while (_pool.TryPop(out var spare))
        {
            spare.MotionState?.Dispose();
            spare.Dispose();
        }

        _rayCallback?.Dispose();
        _world.Dispose();
        _solver.Dispose();
        _broadphase.Dispose();
        _dispatcher.Dispose();
        _config.Dispose();
    }
}
