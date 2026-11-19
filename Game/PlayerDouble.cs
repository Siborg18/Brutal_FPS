using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// A body for the player, seen only by the bunker's security camera (the player's own view has just the hands and gun):
/// the NPCs' human in a navy hoodie (the view model's sleeves), jeans and work boots, walking and running with the
/// player's ground speed, facing where they look, the pistol held up in the right hand. Made by <see cref="Npcs.CreatePlayerDouble"/>.
/// </summary>
public sealed class PlayerDouble
{
    private static readonly float3 SkinTone = new(0.88f, 0.68f, 0.56f);   // the view model's hands
    private static readonly float3 Hoodie = new(0.16f, 0.24f, 0.46f);

    private readonly SkinnedModel _model;
    private readonly Outfit _outfit;
    private readonly Model? _gun;
    private readonly Animator _animator;
    private readonly BoneMask _upperBody;
    private readonly NodePose[] _pose;
    private readonly float4x4[] _world;
    private readonly float4x4[] _skin;
    private readonly int _handR;
    private float3 _position;
    private float _heading;
    private float _speed;
    private bool _alive;

    internal PlayerDouble(SkinnedModel model, ClipLibrary clips, Outfit outfit, Model? gun)
    {
        _model = model;
        _gun = gun;
        var skeleton = model.Skeleton;
        _animator = new Animator(skeleton, clips);
        _upperBody = BoneMask.Subtree(skeleton, "spine_02");
        _pose = new NodePose[skeleton.NodeCount];
        _world = new float4x4[skeleton.NodeCount];
        _skin = new float4x4[skeleton.JointCount];
        _handR = skeleton.NodeIndex("hand.R");
        _outfit = new Outfit([.. outfit.Items.Select(i => i.Id == "hoodie" ? i with { Tint = Hoodie } : i)], outfit.HideRegions);
    }

    private float4x4 Transform => float4x4.CreateRotationY(_heading) * float4x4.CreateTranslation(_position);

    /// <summary>Poses the body where the player is (feet at <see cref="PlayerController.Position"/>).</summary>
    public void Update(float dt, PlayerController player)
    {
        _alive = player.Alive;
        _position = player.Position;
        _heading = float.Pi - player.Yaw;   // the body faces its +z; the camera's yaw 0 looks down -z
        if (dt <= 0)
            return;

        // Ground speed, eased so a stop or a sprint doesn't snap the legs from one cycle to the other.
        var flat = player.Grounded ? float.Sqrt(player.Velocity.X * player.Velocity.X + player.Velocity.Z * player.Velocity.Z) : 0f;
        _speed += float.Clamp(flat - _speed, -12f * dt, 12f * dt);
        _animator.SetLocomotion(_speed);
        if (!_animator.IsPlaying("AimPistol"))
            _animator.PlayOverlay("AimPistol", _upperBody, 0.2f, 0f);   // (no fade out: it holds the aim)
        _animator.Update(dt, _pose);
        _model.Skeleton.ComputeWorld(_pose, _world);
        _model.Skeleton.ComputeSkin(_world, _skin);
    }

    public void Draw(Renderer renderer)
    {
        if (!_alive)
            return;
        var world = Transform;
        renderer.DrawSkinned(_model, 0, world, _skin, SkinTone, hideRegions: _outfit.HideRegions);
        foreach (var item in _outfit.Items)
            renderer.DrawSkinned(item.Model, 0, world, _skin, item.Tint, hideRegions: item.HideBelow, woundStyle: item.Wound);

        if (_gun is null || _handR < 0)
            return;
        // The pistol in the right hand, as the armed NPCs hold it (ArmedNpc.GunToWorld).
        var hand = _world[_handR] * world;
        var along = float3.Normalize(new float3(hand.M21, hand.M22, hand.M23));
        var across = float3.Normalize(new float3(hand.M11, hand.M12, hand.M13));
        var up = float3.Normalize(-across - along * float3.Dot(-across, along));
        var side = float3.Cross(along, up);
        var basis = new float4x4(
            along.X, along.Y, along.Z, 0,
            up.X, up.Y, up.Z, 0,
            side.X, side.Y, side.Z, 0,
            0, 0, 0, 1);
        var grip = hand.Translation + along * 0.075f - up * 0.01f;
        var gunToWorld = float4x4.CreateTranslation(-NpcGun.Pistol.GripLocal) * basis * float4x4.CreateTranslation(grip);
        foreach (var part in _gun.Parts)
        {
            if (!Npcs.NotOnNpcGun.Contains(part.Name))
                renderer.DrawPart(part, gunToWorld, float3.One);
        }
    }
}
