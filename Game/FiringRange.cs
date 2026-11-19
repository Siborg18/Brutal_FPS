using Brutal.Numerics;
using BrutalFps.Engine;

namespace BrutalFps.Game;

/// <summary>
/// What moves on the firing range (<see cref="Level.RangeFiringPoint"/>): its targets, one for each of
/// <see cref="Level.RangeLanes"/>, and the red button on the post beside the bench. Stand by it and press E: every board
/// is as new, its bullet holes and its score gone.
/// </summary>
public sealed class FiringRange
{
    private const float ButtonReach = 3f;   // (from anywhere round the bench)
    private const float PressTime = 0.3f;
    private const float CapSize = 0.13f, CapHeight = 0.05f, CapTravel = 0.03f;
    private static readonly float3 CapColor = new(0.75f, 0.06f, 0.04f);
    private static readonly float3 CapLitColor = new(2.4f, 0.35f, 0.2f);

    private static readonly float3 ReadoutBackdrop = new(0.02f, 0.02f, 0.025f);
    private static readonly float3 ReadoutInk = new(0.9f, 0.9f, 0.86f);
    private static readonly float3 ReadoutTitleInk = new(1.0f, 0.8f, 0.25f);
    private static readonly float3 HitMarkColor = new(0.02f, 0.02f, 0.02f);
    private const float HitMarkSize = 0.014f;

    private readonly List<Target> _targets;
    private float _pressed;
    private TargetHit? _lastHit;
    private bool _onRange;

    /// <summary>"[E] Reset targets" while the player is at the button, otherwise null.</summary>
    public string? Prompt { get; private set; }

    /// <summary>Puts the targets up (their frames join <paramref name="level"/>) and adds them to <paramref name="targets"/>.</summary>
    public FiringRange(Level level, List<Target> targets)
    {
        _targets = targets;
        foreach (var lane in Level.RangeLanes)
        {
            var foot = Level.RangeFiringPoint + new float3(lane.Side, 0, -lane.Distance);
            targets.Add(new Target($"{lane.Distance} m", foot, Level.RangeBoardHeight, level, swings: false));
        }
    }

    public void Update(float dt, bool usePressed, float3 player, BulletHoles holes, AudioEngine? audio)
    {
        _pressed = float.Max(0f, _pressed - dt);
        _onRange = Level.OnFiringRange(player);
        var offset = Level.RangeButtonPost - player;
        var near = offset.X * offset.X + offset.Z * offset.Z < ButtonReach * ButtonReach && float.Abs(player.Y - Level.RangeFiringPoint.Y) < 2f;
        Prompt = near ? "[E] Reset targets" : null;
        if (near && usePressed)
            Reset(holes, audio);
    }

    private void Reset(BulletHoles holes, AudioEngine? audio)
    {
        _pressed = PressTime;
        _lastHit = null;
        var hits = _targets.Sum(t => t.Hits);
        foreach (var target in _targets)
            target.Reset();
        var removed = holes.RemoveWhere(point => _targets.Any(t => t.Covers(point)));
        audio?.Play(audio.Get("Weapons/pistol_mag_in"), 0.7f, 0.95f);   // the button clicking in
        Log.Write($"[RANGE] Targets reset: {hits} hits and {removed} bullet holes cleared");
    }

    /// <summary>A bullet hit a target: its numbers go up on the readout.</summary>
    public void OnTargetHit(TargetHit hit)
    {
        _lastHit = hit;
        Log.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[RANGE] {hit.Target.Name} board: {hit.Zone}, {hit.Offset.X:0.00} m right, {hit.Offset.Y:0.00} m up"));
    }

    /// <summary>
    /// At the top of the screen while on the range: the last hit, with which board and ring it was and the board's running
    /// total, where on the board it landed, and how far, how fast and how hard the bullet got there.
    /// </summary>
    public void DrawReadout(Renderer renderer, int2 framebufferSize)
    {
        if (_lastHit is not { } hit || !_onRange)
            return;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var title = string.Create(c, $"{hit.Target.Name} board: {hit.Zone}, {hit.Points} points   {hit.Target.Hits} {(hit.Target.Hits == 1 ? "hit" : "hits")}, score {hit.Target.Score}   {hit.Round?.Name ?? "9 mm"}");
        var height = string.Create(c, $"{float.Abs(hit.Offset.Y):0.00} m {(hit.Offset.Y < 0f ? "low" : "high")}");
        var side = string.Create(c, $"{float.Abs(hit.Offset.X):0.00} m {(hit.Offset.X < 0f ? "left" : "right")}");
        var numbers = string.Create(c, $"{hit.Range:0.0} m out   {height}   {side}   {hit.Speed:0} m/s   {hit.Energy:0} J   {hit.Flight:0.00} s");

        renderer.BeginOverlay();
        var aspect = framebufferSize.Y > 0 ? (float)framebufferSize.X / framebufferSize.Y : 1f;
        var size = 3f * 2f / float.Max(framebufferSize.Y, 1);
        var width = (int.Max(title.Length, numbers.Length) * BlockFont.Advance - 1) * size;
        var lineHeight = (BlockFont.Height + 4) * size;
        var top = 0.95f;
        PcTerminal.Rect(renderer, aspect, new float2(0, top - lineHeight + 1.5f * size), new float2(width + 10 * size, lineHeight * 2 + 5 * size), ReadoutBackdrop);
        PcTerminal.Text(renderer, aspect, title, new float2(-width / 2, top), size, ReadoutTitleInk);
        PcTerminal.Text(renderer, aspect, numbers, new float2(-width / 2, top - lineHeight), size, ReadoutInk);
        DrawLastBoard(renderer, aspect, hit.Target, size);
    }

    /// <summary>
    /// On the right of the screen, the board that was last hit, seen square on, with every hit on it (the newest stands
    /// out), and under it how far down the range that board is.
    /// </summary>
    private static void DrawLastBoard(Renderer renderer, float aspect, Target board, float textSize)
    {
        const float panel = 0.5f, margin = 0.06f, border = 0.008f;
        var center = new float2(aspect - margin - panel / 2, 0f);
        var perMetre = panel / Target.BoardSize;
        PcTerminal.Rect(renderer, aspect, center, new float2(panel + border * 2, panel + border * 2), ReadoutBackdrop);
        foreach (var (extent, color, _, _) in Target.Rings)
            PcTerminal.Rect(renderer, aspect, center, new float2(panel * extent, panel * extent), color);

        var marks = board.HitMarks;
        for (var i = 0; i < marks.Count; i++)
        {
            var at = center + float2.Clamp(marks[i], new float2(-Target.BoardSize / 2), new float2(Target.BoardSize / 2)) * perMetre;
            if (i == marks.Count - 1)
                Dot(renderer, aspect, at, HitMarkSize * 1.9f, ReadoutTitleInk);
            Dot(renderer, aspect, at, HitMarkSize, HitMarkColor);
        }

        var stripHeight = (BlockFont.Height + 6) * textSize;
        var stripTop = center.Y - panel / 2 - border;
        PcTerminal.Rect(renderer, aspect, new float2(center.X, stripTop - stripHeight / 2), new float2(panel + border * 2, stripHeight), ReadoutBackdrop);
        var labelWidth = (board.Name.Length * BlockFont.Advance - 1) * textSize;
        PcTerminal.Text(renderer, aspect, board.Name, new float2(center.X - labelWidth / 2, stripTop - 3 * textSize), textSize, ReadoutInk);
    }

    /// <summary>A round dot (near enough: two squares, one turned 45 degrees).</summary>
    private static void Dot(Renderer renderer, float aspect, float2 at, float size, float3 color)
    {
        PcTerminal.Rect(renderer, aspect, at, new float2(size, size), color);
        PcTerminal.Rect(renderer, aspect, at, new float2(size, size), color, float.Pi / 4);
    }

    /// <summary>The button: it sinks and lights up as it's pressed.</summary>
    public void Draw(Renderer renderer, Zones zones)
    {
        if ((zones & Zones.Outback) == 0)
            return;
        var t = _pressed / PressTime;
        var center = Level.RangeButtonPost + new float3(0, 0.03f + CapHeight / 2 - CapTravel * t, 0);
        renderer.DrawCube(float4x4.CreateScale(CapSize, CapHeight, CapSize) * float4x4.CreateTranslation(center),
            CapColor + (CapLitColor - CapColor) * t, t > 0 ? ShadeMode.Unlit : ShadeMode.Lit);
    }
}
