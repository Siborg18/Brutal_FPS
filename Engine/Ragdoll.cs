using Brutal.Numerics;
using BulletSharp;

namespace BrutalFps.Engine;

/// <summary>
/// How a skeleton breaks up into ragdoll bodies and the joints between them, worked out once from its rest pose. Each
/// body is a capsule or a box laid along a bone (from one node to another) and drives that node; every other node rides
/// rigidly on the nearest driven node above it, holding the local pose it had when the ragdoll took over. Joints are
/// cone-twists (hips, shoulders, waist, neck) or hinges (knees, elbows) whose frames come from the rest pose, so their
/// limits are anatomical whatever pose the body is in when it goes limp.
/// <para>
/// Model space as the characters: +Z forward, +Y up. A cone's centre can be tipped forward (a thigh swings much further
/// forward than back); a hinge bends one way only, towards <c>bendTowards</c>.
/// </para>
/// </summary>
public sealed class RagdollRig
{
    internal sealed class Body
    {
        public required int Node;
        public required CollisionShape Shape;
        public required float Mass;
        public required float Size;          // smallest dimension, for continuous collision
        public required float4x4 FromNode;   // body = FromNode * node (both model space)
        public required float4x4 ToNode;     // node = ToNode * body
        public required float4x4 Rest;       // the body's model-space frame in the rest pose
    }

    internal sealed class Joint
    {
        public required int A, B;            // parent body, child body
        public required bool Hinge;
        public required float4x4 FrameA, FrameB;
        public required float Swing, Twist, Low, High;
    }

    private static readonly float3 Forward = new(0, 0, 1);
    private readonly float4x4[] _rest;
    private readonly List<Body> _bodies = [];
    private readonly List<Joint> _joints = [];
    private int[] _driven = [];

    public Skeleton Skeleton { get; }

    internal List<Body> Bodies => _bodies;
    internal List<Joint> Joints => _joints;

    /// <summary>Total mass (kg).</summary>
    public float Mass => _bodies.Sum(b => b.Mass);

    public int BodyCount => _bodies.Count;

    public RagdollRig(Skeleton skeleton)
    {
        Skeleton = skeleton;
        _rest = new float4x4[skeleton.NodeCount];
        skeleton.ComputeWorld(skeleton.RestPose, _rest);
    }

    /// <summary>The rest-pose model-space position of a node (for laying out bodies).</summary>
    public float3 RestPosition(string node) => _rest[NodeOf(node)].Translation;

    /// <summary>A capsule from <paramref name="node"/> to <paramref name="end"/> (rest pose, model space) driving that node. Returns its index.</summary>
    public int AddCapsule(string node, float3 end, float radius, float mass)
    {
        var n = NodeOf(node);
        var start = _rest[n].Translation;
        var length = Length(end - start);
        return Add(n, start, end, new CapsuleShapeX(radius, float.Max(length - 2f * radius, 0.01f)), mass, radius * 2f);
    }

    public int AddCapsule(string node, string end, float radius, float mass) => AddCapsule(node, RestPosition(end), radius, mass);

    /// <summary>A box from <paramref name="node"/> to <paramref name="end"/> driving that node: <paramref name="depth"/> front to back, <paramref name="width"/> side to side (metres).</summary>
    public int AddBox(string node, string end, float depth, float width, float mass)
    {
        var n = NodeOf(node);
        var start = _rest[n].Translation;
        var to = RestPosition(end);
        var half = new float3(Length(to - start) * 0.5f, depth * 0.5f, width * 0.5f);
        return Add(n, start, to, new BoxShape(half.X, half.Y, half.Z) { Margin = 0.01 }, mass, float.Min(depth, width));
    }

    /// <summary>A ball-and-socket joint at the child's node: it swings up to <paramref name="swing"/> rad round a centre tipped <paramref name="tiltForward"/> rad forward of the rest pose, and twists up to <paramref name="twist"/>.</summary>
    public void AddCone(int parent, int child, float swing, float twist, float tiltForward = 0f)
    {
        var (pivot, along) = JointAxis(child);
        var tilted = along;
        if (tiltForward != 0f)
        {
            var perp = Forward - along * float3.Dot(Forward, along);
            var len = Length(perp);
            if (len > 1e-4f)
                tilted = float3.Normalize(along * float.Cos(tiltForward) + perp / len * float.Sin(tiltForward));
        }

        _joints.Add(new Joint
        {
            A = parent, B = child, Hinge = false,
            FrameA = Frame(pivot, tilted, Forward) * Inverse(_bodies[parent].Rest),
            FrameB = Frame(pivot, along, Forward) * Inverse(_bodies[child].Rest),
            Swing = swing, Twist = twist, Low = 0, High = 0,
        });
    }

    /// <summary>A hinge at the child's node, bending from straight (0) up to <paramref name="maxBend"/> rad with the far end moving towards <paramref name="bendTowards"/>.</summary>
    public void AddHinge(int parent, int child, float3 bendTowards, float maxBend)
    {
        var (pivot, along) = JointAxis(child);
        // Frame Z = along x bendTowards: a positive hinge angle then swings the child's far end towards bendTowards.
        var frame = Frame(pivot, along, bendTowards);
        _joints.Add(new Joint
        {
            A = parent, B = child, Hinge = true,
            FrameA = frame * Inverse(_bodies[parent].Rest),
            FrameB = frame * Inverse(_bodies[child].Rest),
            Swing = 0, Twist = 0, Low = -0.04f, High = maxBend,
        });
    }

    /// <summary>Call once all the bodies are added: works out which node each body drives.</summary>
    public RagdollRig Finish()
    {
        _driven = new int[Skeleton.NodeCount];
        Array.Fill(_driven, -1);
        for (var i = 0; i < _bodies.Count; i++)
            _driven[_bodies[i].Node] = i;
        return this;
    }

    /// <summary>The body a node is carried by: its own, or the nearest driven node's above it (-1 above them all).</summary>
    public int BodyOf(int node)
    {
        for (; node >= 0; node = Skeleton.Parent(node))
        {
            if (_driven[node] >= 0)
                return _driven[node];
        }

        return -1;
    }

    internal int Driven(int node) => _driven[node];

    private int Add(int node, float3 start, float3 end, CollisionShape shape, float mass, float size)
    {
        var rest = Frame((start + end) * 0.5f, end - start, Forward);
        var fromNode = rest * Inverse(_rest[node]);
        _bodies.Add(new Body { Node = node, Shape = shape, Mass = mass, Size = size, FromNode = fromNode, ToNode = Inverse(fromNode), Rest = rest });
        return _bodies.Count - 1;
    }

    /// <summary>A joint sits at the child body's node and its axis runs along the child body (rest pose).</summary>
    private (float3 Pivot, float3 Along) JointAxis(int child)
    {
        var b = _bodies[child];
        var pivot = _rest[b.Node].Translation;
        return (pivot, float3.Normalize(new float3(b.Rest.M11, b.Rest.M12, b.Rest.M13)));
    }

    private int NodeOf(string name)
    {
        var n = Skeleton.NodeIndex(name);
        if (n < 0)
            throw new ArgumentException($"Ragdoll: the skeleton has no node called '{name}'.");
        return n;
    }

    /// <summary>A frame at <paramref name="origin"/> with X along <paramref name="x"/>, Z = X cross <paramref name="hint"/>, Y = Z cross X (rows are the axes).</summary>
    internal static float4x4 Frame(float3 origin, float3 x, float3 hint)
    {
        x = float3.Normalize(x);
        var z = float3.Cross(x, hint);
        if (float3.Dot(z, z) < 1e-6f)
            z = float3.Cross(x, float3.UnitX);
        z = float3.Normalize(z);
        var y = float3.Cross(z, x);
        return new float4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            origin.X, origin.Y, origin.Z, 1);
    }

    internal static float4x4 Inverse(in float4x4 m)
    {
        float4x4.Invert(m, out var inverse);
        return inverse;
    }

    private static float Length(float3 v) => float.Sqrt(float3.Dot(v, v));
}

/// <summary>
/// One limp body in the physics world, made from a <see cref="RagdollRig"/> at the pose a character was in, moving the
/// way it was moving. <see cref="Pose"/> turns the bodies back into the skeleton's node matrices for skinning.
/// </summary>
public sealed class Ragdoll : IDisposable
{
    private const float MaxSpeed = 9f;          // m/s: a pose jump (a teleport, a clip switch) mustn't fling the body
    private const float MaxSpin = 14f;          // rad/s

    private readonly PhysicsWorld _world;
    private readonly RagdollRig _rig;
    private readonly RigidBody[] _bodies;
    private readonly TypedConstraint[] _joints;
    private readonly float4x4[] _bodyWorld;
    private bool _disposed;

    /// <param name="nodes">Model-space node matrices now, placed by <paramref name="transform"/>.</param>
    /// <param name="previousNodes">The same a frame ago (with <paramref name="previousTransform"/>), <paramref name="dt"/> earlier: sets each limb's velocity.</param>
    public Ragdoll(PhysicsWorld world, RagdollRig rig, ReadOnlySpan<float4x4> nodes, in float4x4 transform,
        ReadOnlySpan<float4x4> previousNodes, in float4x4 previousTransform, float dt)
    {
        _world = world;
        _rig = rig;
        var count = rig.Bodies.Count;
        _bodies = new RigidBody[count];
        _bodyWorld = new float4x4[count];
        for (var i = 0; i < count; i++)
        {
            var b = rig.Bodies[i];
            var now = b.FromNode * nodes[b.Node] * transform;
            var before = b.FromNode * previousNodes[b.Node] * previousTransform;
            var velocity = float3.Zero;
            var spin = float3.Zero;
            if (dt > 1e-4f)
            {
                velocity = Clamp((now.Translation - before.Translation) / dt, MaxSpeed);
                // The world-space turn from before to now (rows are axes: now = before * turn).
                var turn = floatQuat.Normalize(floatQuat.CreateFromRotationMatrix(RagdollRig.Inverse(Rotation(before)) * Rotation(now)));
                if (turn.W < 0)
                    turn = floatQuat.Negate(turn);
                var angle = 2f * float.Acos(float.Clamp(turn.W, -1f, 1f));
                var s = float.Sqrt(float.Max(1f - turn.W * turn.W, 0f));
                if (s > 1e-4f)
                    spin = Clamp(new float3(turn.X, turn.Y, turn.Z) / s * (angle / dt), MaxSpin);
            }

            _bodies[i] = world.AddRagdollBody(b.Shape, b.Mass, now, velocity, spin, b.Size);
        }

        // A body's own limbs don't collide (a hanging arm overlaps the hips): the joint limits keep its shape.
        for (var i = 0; i < count; i++)
        {
            for (var k = i + 1; k < count; k++)
            {
                _bodies[i].SetIgnoreCollisionCheck(_bodies[k], true);
                _bodies[k].SetIgnoreCollisionCheck(_bodies[i], true);
            }
        }

        _joints = new TypedConstraint[rig.Joints.Count];
        for (var i = 0; i < _joints.Length; i++)
        {
            var j = rig.Joints[i];
            var a = _bodies[j.A];
            var b = _bodies[j.B];
            TypedConstraint c;
            if (j.Hinge)
            {
                var hinge = new HingeConstraint(a, b, PhysicsWorld.ToBullet(j.FrameA), PhysicsWorld.ToBullet(j.FrameB), false);
                hinge.SetLimit(j.Low, j.High, 0.9, 0.35, 1.0);
                c = hinge;
            }
            else
            {
                var cone = new ConeTwistConstraint(a, b, PhysicsWorld.ToBullet(j.FrameA), PhysicsWorld.ToBullet(j.FrameB));
                cone.SetLimit(j.Swing, j.Swing, j.Twist, 0.9, 0.35, 1.0);
                cone.Damping = 0.05;
                c = cone;
            }

            c.OverrideNumSolverIterations = 20;   // a chain of joints holds together better than the world's 10
            world.AddConstraint(c);
            _joints[i] = c;
        }
    }

    /// <summary>Where the body carrying the skeleton's root (the pelvis) is now.</summary>
    public float3 Center => PhysicsWorld.TransformOf(_bodies[0]).Translation;

    /// <summary>All the bodies have come to rest (they cost next to nothing until something wakes them).</summary>
    public bool Asleep
    {
        get
        {
            foreach (var b in _bodies)
            {
                if (b.IsActive)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The skeleton's model-space node matrices for <paramref name="transform"/> (where the character is now drawn): driven
    /// nodes from their bodies, the rest from <paramref name="local"/> on top of their parents.
    /// </summary>
    public void Pose(ReadOnlySpan<NodePose> local, Span<float4x4> world, in float4x4 transform)
    {
        var toModel = RagdollRig.Inverse(transform);
        for (var i = 0; i < _bodies.Length; i++)
            _bodyWorld[i] = PhysicsWorld.TransformOf(_bodies[i]);
        var skeleton = _rig.Skeleton;
        foreach (var n in skeleton.Order)
        {
            var driven = _rig.Driven(n);
            if (driven >= 0)
            {
                world[n] = _rig.Bodies[driven].ToNode * _bodyWorld[driven] * toModel;
                continue;
            }

            var parent = skeleton.Parent(n);
            world[n] = parent >= 0 ? local[n].Matrix * world[parent] : local[n].Matrix;
        }
    }

    /// <summary>A push at <paramref name="point"/> (world) on whichever body carries <paramref name="node"/>.</summary>
    public void Push(int node, float3 impulse, float3 point)
    {
        var body = _rig.BodyOf(node);
        if (body >= 0)
            PhysicsWorld.Push(_bodies[body], impulse, point);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var c in _joints)
            _world.RemoveConstraint(c);
        foreach (var b in _bodies)
            _world.RemoveBody(b);
    }

    private static float4x4 Rotation(in float4x4 m) => new(
        m.M11, m.M12, m.M13, 0,
        m.M21, m.M22, m.M23, 0,
        m.M31, m.M32, m.M33, 0,
        0, 0, 0, 1);

    private static float3 Clamp(float3 v, float max)
    {
        var len = float.Sqrt(float3.Dot(v, v));
        return len > max ? v * (max / len) : v;
    }
}
