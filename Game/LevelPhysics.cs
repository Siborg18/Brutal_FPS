using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

public sealed partial class Level
{
    private PhysicsWorld? _physics;   // once BuildPhysics has run: the doors keep their parts in it up to date


    /// <summary>
    /// Adds everything that is drawn to the physics world as static geometry, so bullets and debris meet exactly what
    /// you see: every visible box, and every mesh (Blender models part by part, code-built meshes, ground slabs).
    /// Invisible collision boxes (the player's stand-ins for models, walls round the walkable gaps) stay out of it;
    /// leaf cards and scrub stay out too, so shots go through foliage. Each object is tagged with an index into
    /// <paramref name="surfaces"/>, which says what it is made of. The moving doors go in as parts that follow them.
    /// </summary>
    public void BuildPhysics(PhysicsWorld physics, SurfaceTable surfaces)
    {
        _physics = physics;
        physics.PrepareMeshes(_props.SelectMany(p => p.Asset is { } model
            ? model.Parts.Where(q => SurfaceInfo.ForPart(q.Material, p.Tint) is not null).Select(q => q.Mesh)
            : p.Mesh is { } mesh && p.Part is { } part && SurfaceInfo.ForPart(part, p.Tint) is not null ? [mesh] : []));

        BulletSharp.CollisionObject? AddDoorPart(Mesh mesh, Material material, float3 tint, in float4x4 pose)
        {
            var surface = DoorSurface(material, tint);
            var (restitution, friction) = surface.Contact;
            return physics.AddDoorPart(mesh, pose, surfaces.Index(surface), restitution, friction);
        }

        foreach (var (mesh, tint, material) in _doorParts)
        {
            if (AddDoorPart(mesh, material, tint, FactoryDoorPose(_doorOpen)) is { } part)
                _factoryDoorPhysics.Add(part);
        }

        foreach (var door in LeafDoors())
        {
            foreach (var leaf in door.Leaves)
            {
                foreach (var (mesh, tint, material) in leaf.Meshes)
                {
                    if (AddDoorPart(mesh, material, tint, leaf.Pose(door.Open)) is { } part)
                        leaf.PhysicsParts.Add(part);
                }
            }
        }

        foreach (var box in _boxes)
        {
            if (!box.Visible || box.Material is Material.Sky or Material.Bush)
                continue;
            if (box.Material == Material.Window && box.Color == DoorwayTint)
                continue;   // the warehouse's bright open doorway: shots fly out through it
            var surface = SurfaceInfo.ForMaterial(box.Material, box.Color);
            var (restitution, friction) = surface.Contact;
            physics.AddStaticBox(box.Bounds, surfaces.Index(surface), restitution, friction);
        }

        foreach (var prop in _props)
        {
            if (prop.Removed || prop.InBatch != 0)
                continue;   // (deleted in the level editor; solid as part of its piece's batch)
            if (prop.Asset is { } model)
            {
                foreach (var part in model.Parts)
                {
                    if (SurfaceInfo.ForPart(part.Material, prop.Tint) is not { } surface)
                        continue;
                    var (restitution, friction) = surface.Contact;
                    if (prop.Arrives)
                    {
                        // (The raid's delivery van can drive off: a part that can be moved out of the way, as the doors' are.)
                        if (physics.AddDoorPart(part.Mesh, prop.Model, surfaces.Index(surface), restitution, friction) is { } moving)
                            _arrivingVanPhysics.Add(moving);
                        continue;
                    }
                    physics.AddStaticMesh(part.Mesh, prop.Model, surfaces.Index(surface), restitution, friction);
                }
            }
            else if (prop.Mesh is { } mesh)
            {
                SurfaceInfo? surface = prop.Part is { } part
                    ? SurfaceInfo.ForPart(part, prop.Tint)
                    : prop.Material is Material.Bush || (prop.Decal && prop.Material == Material.Plain)
                        ? null   // scrub, and the shade painted on the ground under trees
                        : SurfaceInfo.ForMaterial(prop.Material, prop.Tint);
                if (surface is not { } s || s.Material == Material.Bush)
                    continue;
                var (restitution, friction) = s.Contact;
                physics.AddStaticMesh(mesh, prop.Model, surfaces.Index(s), restitution, friction);
            }
        }

        if (!_arrivingVanThere)
            SetArrivingVanThere(false);

        // The sea round the ship: shots splash into it, debris sinks.
        var sea = SurfaceInfo.ForMaterial(Material.Ocean, new float3(0.1f, 0.2f, 0.3f));
        physics.AddStaticBox(OceanBounds, surfaces.Index(sea), 0f, 1f);

        // The observation rooms' one-way glass (drawn by OneWayMirrors, so there's no box for it): it stops shots.
        var mirrorGlass = SurfaceInfo.ForMaterial(Material.Glass, new float3(0.08f, 0.09f, 0.1f));
        foreach (var pane in VaultMirrors)
        {
            var half = new float3(pane.Size.X / 2, pane.Size.Y / 2, 0.015f);
            var (restitution, friction) = mirrorGlass.Contact;
            physics.AddStaticBox(new Aabb(pane.Center - pane.Normal * 0.015f - half, pane.Center - pane.Normal * 0.015f + half), surfaces.Index(mirrorGlass), restitution, friction);
        }

        // The factory office's clear panes (drawn by DrawGlass, so there's no box for them either).
        var windowGlass = SurfaceInfo.ForMaterial(Material.Glass, OfficeGlassTint);
        foreach (var pane in _officeGlassPanes)
        {
            var (restitution, friction) = windowGlass.Contact;
            physics.AddStaticBox(pane, surfaces.Index(windowGlass), restitution, friction);
        }
    }

    /// <summary>
    /// Shots without the physics world: the nearest box that stops shots, and what it's made of (invisible boxes
    /// count as concrete).
    /// </summary>
    public bool RaycastSurface(float3 origin, float3 direction, float maxDistance, out float distance, out float3 normal, out SurfaceInfo surface)
    {
        distance = maxDistance;
        normal = default;
        surface = default;
        var hit = false;
        foreach (var box in _boxes)
        {
            if (!box.BlocksShots)
                continue;
            if (box.Bounds.Raycast(origin, direction, distance, out var t, out var n) && t <= distance)
            {
                distance = t;
                normal = n;
                surface = SurfaceInfo.ForMaterial(box.Visible ? box.Material : Material.Concrete, box.Color);
                hit = true;
            }
        }

        if (RaycastDoors(origin, direction, ref distance, out var doorNormal, out var doorSurface))
        {
            normal = doorNormal;
            surface = doorSurface;
            hit = true;
        }

        return hit;
    }
}

/// <summary>The distinct surfaces in the physics world; static objects carry an index into it.</summary>
public sealed class SurfaceTable
{
    private readonly List<SurfaceInfo> _list = [];
    private readonly Dictionary<SurfaceInfo, int> _index = [];

    public int Index(SurfaceInfo surface)
    {
        if (!_index.TryGetValue(surface, out var i))
        {
            i = _list.Count;
            _list.Add(surface);
            _index[surface] = i;
        }

        return i;
    }

    /// <summary>The surface for a physics object's tag (concrete if it has none).</summary>
    public SurfaceInfo this[int index] => index >= 0 && index < _list.Count ? _list[index] : new SurfaceInfo(SurfaceKind.Concrete, new float3(0.5f), Material.Concrete);

    public int Count => _list.Count;
}
