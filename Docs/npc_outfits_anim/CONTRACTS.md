# CONTRACTS: the fixed interfaces every agent codes against

If your brief and this file disagree, this file wins. Don't change anything here. If it's impossible, report BLOCKED with the reason.

Conventions you must keep (from the existing code):
* C#: .NET 10, file-scoped namespaces (`BrutalFps.Engine` / `BrutalFps.Game`), `Brutal.Numerics` types (`float3`, `float4`,
  `float4x4`, `floatQuat`), **row-vector matrices** (`v * M`, so world = local × parent), `Log.Write(string)` for logging,
  `[WARN]` prefix for recoverable problems. The game must never crash on a missing or bad asset: log a warning and fall back.
* Blender: Z up, the figure faces −Y, its left side is +X, and metres. glTF export with `export_yup=True`, so game +Y = Blender +Z and
  game −Z = Blender +Y.
* Material names: `<set>_tint` takes the instance colour; `flat_*` is untextured; `tex_<set>` is a model's own set.

---

## 1. Body regions

A per-vertex float attribute **`_REGION`** on the body mesh holds an integer 0–14 (exported with
`export_attributes=True`; glTF attribute name `_REGION`). The bit for region *i* is `1u << i`.

| i | Name | Body part (by the dominant deform bone after weighting, and the position along it, t = 0 at the head and 1 at the tail) |
| --- | --- | --- |
| 0 | `head` | `head`, `eye.*` vertices not in `scalp` (face, ears, eyes, jaw) |
| 1 | `scalp` | `head` vertices above the hairline: in front of the ears (y < ear centre y), Blender z > brow z + 0.04 m; level with and behind the ears, z > ear-top z + 0.01 m. Ears and face are never scalp. |
| 2 | `neck` | `neck_01` |
| 3 | `chest` | `spine_03`, `clavicle.*` |
| 4 | `belly` | `spine_02`, `spine_01` |
| 5 | `hips` | `pelvis`, `root` |
| 6 | `upperarm_hi` | `upperarm.*`, `upperarm_twist.*`, t < 0.5 along `upperarm` |
| 7 | `upperarm_lo` | the same, t ≥ 0.5 |
| 8 | `forearm` | `forearm.*`, `forearm_twist.*` |
| 9 | `hand` | `hand.*` and all fingers |
| 10 | `thigh_hi` | `thigh.*`, t < 0.5 |
| 11 | `thigh_lo` | `thigh.*`, t ≥ 0.5 |
| 12 | `calf_hi` | `calf.*`, t < 0.5 |
| 13 | `calf_lo` | `calf.*`, t ≥ 0.5 |
| 14 | `foot` | `foot.*`, `ball.*` |
| 15 | reserved | not used |

**Coverage rule:** a garment that lists region R in `hides` must cover every vertex of R, and must extend **at least 25 mm
past every boundary between R and a region it does not hide** (so the culling seam is never visible), measured along the body
surface. Exception: `hair`/`head` slot garments need only **10 mm** past the scalp boundary, and the ear geometry is never
covered (ears stick out through hair and under caps).

**Culling rule (engine):** when drawing the body with `hideRegions`, a triangle is skipped only if **all three** of its vertices'
regions are hidden.

## 2. Garment assets

For each garment `<id>` (lower_snake_case):

* `Assets/Models/garments/<id>.glb`: one skinned mesh object (one or more materials), exported **together with `Human_Rig`
  (deform bones only)** from the same rest pose as `human.glb`, so its inverse bind matrices equal the body's to within 1e-4.
  At most 4 influences per vertex, normalised. **Weights are copied exactly from the source body vertex (no re-smoothing)**;
  added geometry (hems, soles, peaks) takes its nearest carved vertex's weights. Every garment vertex also carries
  **`_REGION`**, copied from its source body vertex (added geometry: nearest carved vertex). No animations. Same export flags as `build_human.export_glb` plus
  `export_animations=False`.
* `Assets/Models/garments/<id>.json`, the sidecar (written by the build script, never by hand):

```json
{
  "id": "tshirt",
  "slot": "torso_base",
  "layer": 3,
  "offset_mm": 9,
  "hides": ["chest", "belly", "upperarm_hi"],
  "wound": "fabric",
  "materials": ["cloth_tint"],
  "triangles": 5210
}
```

* `slot` ∈ `hair`, `head`, `torso_base`, `torso_outer`, `legs`, `feet`. An outfit has at most one garment per slot.
* `wound` ∈ `skin`, `fabric`, `hard`.
* **Layers and offsets** (the offset from the skin surface along the body normal, after smoothing):

| slot | layer | offset_mm | notes |
| --- | --- | --- | --- |
| hair | 1 | 4–9 | never thicker than 9 mm where a cap sits (scalp) |
| legs | 2 | 6 | flares to 14 mm over `calf_lo` so the trousers sit over the boot shafts |
| feet | 1 | 6 (shaft/upper) | sole: 25 mm slab under the foot |
| torso_base | 3 | 9 | goes over the trouser waistband (its hem reaches 80 mm into `hips`) |
| head | 3 | 14 | cap or beanie, over hair |
| torso_outer | 4 | 18 | |

Where two garments overlap, the one with the higher layer must be ≥ 4 mm further out over the overlap.

**Layered drawing (engine):** each garment is drawn with `hideRegions` = OR of the `hides` of every outfit item with a
**strictly higher layer** (so a shirt's chest is culled under a jacket); the body is drawn with the OR of all items' hides.

**Poke-through is measured only on what is drawn** (rest pose + every 4th frame of each clip checked):
* P1 body: body vertices of triangles still drawn under the garment's hide mask, lying under the garment (nearest garment
  point ≤ 30 mm away): ≤ 0.5 % outside the garment on the worst frame, 0 in the rest pose.
* P2 holes: for every culled body triangle, a ray from its centroid along its normal must hit the garment within 40 mm
  (60 mm for the `feet` slot, because of the sole) **unless it first hits the body's own skin** (an occluded crease such
  as the armpit or crotch, where no hole can be seen): ≤ 0.1 % misses on the worst frame.
* P3 layers: for a pair (inner, outer), inner vertices still drawn under the outer's hide mask that lie under the outer
  (≤ 30 mm): ≤ 0.5 % outside the outer on the worst frame.
* P1 is measured on drawn body triangles that touch the garment's hidden regions, excluding hem triangles and points past
  the garment's open edge. With fewer than 400 such vertices, the limit is **≤ 3 vertices** instead of 0.5 %.

### Garment catalogue (ids, slots, hides, materials). Build exactly these.

| id | slot | hides | material(s) | built by |
| --- | --- | --- | --- | --- |
| `tshirt` | torso_base | chest, belly, upperarm_hi | `cloth_tint` | S2 |
| `trousers_work` | legs | hips, thigh_hi, thigh_lo, calf_hi, calf_lo | `canvas_tint` | S2 |
| `boots_work` | feet | foot | `leather_tint` (upper), `flat_sole` (sole, 0.05, 0.05, 0.05; rough 0.9) | S2 |
| `longsleeve` | torso_base | chest, belly, upperarm_hi, upperarm_lo, forearm | `cloth_tint` | S3 |
| `hoodie` | torso_outer | chest, belly, upperarm_hi, upperarm_lo, forearm | `knit_tint` (+ hood as a rolled collar, no full hood) | S3 |
| `work_jacket` | torso_outer | chest, belly, upperarm_hi, upperarm_lo, forearm | `canvas_tint` | S3 |
| `hivis_vest` | torso_outer | chest, belly | `cloth_tint` (body), `flat_reflective` (2 bands, 0.75 grey, rough 0.25) | S3 |
| `jeans` | legs | hips, thigh_hi, thigh_lo, calf_hi, calf_lo | `denim_tint` | S3 |
| `shorts` | legs | hips, thigh_hi | `canvas_tint` | S3 |
| `sneakers` | feet | foot | `cloth_tint` (upper), `flat_sole_white` (0.85 grey, rough 0.7) | S3 |
| `cap` | head | scalp | `cloth_tint` (crown + peak) | S3 |
| `beanie` | head | scalp | `knit_tint` | S3 |
| `hair_short` | hair | scalp | `hair_tint` | S3 |
| `hair_buzz` | hair | scalp | `hair_tint` (offset 4 mm) | S3 |

Note: shorts hide only `thigh_hi`, so their hem must reach ≥ 25 mm into `thigh_lo` (the coverage rule).

## 3. Clips

* `Assets/Models/anims/human_anims.glb`: `Human_Rig` (deform bones; a mesh is not required) with only the **new** actions
  below, baked, linear interpolation (`export_force_sampling=True`), 30 fps, `export_anim_slide_to_zero=True`.
  Walk and Idle stay in `human.glb` and must **not** be re-exported here.
* `Assets/Models/anims/clips.json`: the metadata for **every** clip (including Walk and Idle):

```json
{
  "fps": 30,
  "clips": {
    "Walk": { "loop": true, "stride": 1.24, "tags": ["locomotion"], "events": [ { "name": "footL", "time": 0.0 }, { "name": "footR", "time": 0.5333 } ] }
  }
}
```

`stride` is metres covered per cycle (0 = stationary), `time` is in seconds from the clip start. The speed at 1× playback is
`stride / duration`.

| Clip | Frames (30 fps) | Duration s | loop | stride m | tags | events (s) | Content |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Idle | 96 | 3.2 | yes | 0 | idle | — | existing |
| Walk | 32 | 1.0667 | yes | 1.24 | locomotion | footL 0.0, footR 0.5333 | existing |
| Run | 22 | 0.7333 | yes | 2.70 | locomotion | footL 0.0, footR 0.3667 | in place; left heel strike at phase 0, right at 0.5; flight phase; trunk leans 8° forward; elbows ~80° |
| IdleLook | 150 | 5.0 | no | 0 | idle, variant | — | looks left (head 50°, chest 15°), holds, right, back. Starts and ends on Idle frame 1. |
| IdleShift | 120 | 4.0 | no | 0 | idle, variant | — | weight onto the right leg (pelvis 4 cm, hip drop), holds, back. Starts and ends on Idle frame 1. |
| ShuffleTurn | 24 | 0.8 | no | 0 | turn | footL 0.1, footR 0.4667 | two small in-place steps, **no net rotation** (the game turns the heading while it plays). Starts and ends on Idle frame 1. |
| Cower | 60 | 2.0 | yes | 0 | fear | — | crouched (pelvis down 0.45 m), arms over the head, slight tremble |
| DeathFwd | 54 | 1.8 | no | 0 | death | — | knees buckle, falls forward, ends prone on the ground (z ≥ 0 everywhere) |
| DeathBack | 54 | 1.8 | no | 0 | death | — | staggers back a step, falls backward, ends supine |
| Talk | 120 | 4.0 | yes | 0 | overlay, talk | — | upper-body gestures only: bones below `spine_02` keep the Idle pose |

Rules: looping clips have first frame = last frame. Root motion: none, except Death clips may move `pelvis` (the game
leaves the person where they lie).

## 4. C# API: Engine (S1 implements; S5 and S6 call)

```csharp
namespace BrutalFps.Engine;

public static class BodyRegions
{
    public const int Count = 15;
    public static readonly string[] Names = ["head","scalp","neck","chest","belly","hips","upperarm_hi","upperarm_lo",
                                             "forearm","hand","thigh_hi","thigh_lo","calf_hi","calf_lo","foot"];
    /// <summary>Bitmask of the named regions; unknown names are logged ([WARN]) and ignored.</summary>
    public static uint Mask(IEnumerable<string> names);
}

public sealed class SkinnedPart
{
    // existing members stay. New:
    public bool HasRegions { get; }
    /// <summary>Draws the level of detail, skipping triangles whose three vertices are all in hidden regions.
    /// hideRegions == 0 (or no regions) is the existing fast path. Index lists per (lod, mask) are built lazily and cached.</summary>
    public void Draw(int lod, uint hideRegions);
    public void Draw(int lod);  // existing; same as Draw(lod, 0)
}

public sealed class SkinnedModel
{
    // existing members stay. New / changed:
    /// <summary>name may contain a subfolder ("garments/tshirt"). bindTo != null: an attachment. Its vertices' joint
    /// indices are remapped by joint node name to bindTo's joint indices (throws FormatException naming the missing joint),
    /// and inverse bind matrices are compared (a [WARN] if any differs by more than 1e-3). Cached per (name, bindTo).</summary>
    public static SkinnedModel Get(string name, Skeleton? bindTo = null);
    public bool IsAttachment { get; }
}

/// <summary>Per-clip metadata from clips.json.</summary>
public sealed record ClipInfo(string Name, bool Loop, float Stride, string[] Tags, (string Name, float Time)[] Events)
{
    public static ClipInfo Default(string name);  // loop true, stride 0, no tags or events
}

public sealed class AnimationClip
{
    // existing members stay. New:
    public ClipInfo Info { get; internal set; }   // ClipInfo.Default(Name) until a library assigns it
    public float Speed => Duration > 0 ? Info.Stride / Duration : 0;   // m/s at 1x
}

/// <summary>Clips from several glTF files, retargeted by node name onto one skeleton.</summary>
public sealed class ClipLibrary
{
    /// <summary>Loads Assets/Models/{file}.glb for each file in order (later files override earlier names, with a [WARN]),
    /// retargeting each channel's node by name to target (channels on unknown nodes are dropped, counted in the log),
    /// then reads Assets/Models/anims/clips.json if present. Missing files are a [WARN], not an error.</summary>
    public static ClipLibrary Load(Skeleton target, params string[] files);
    /// <summary>The model's own clips (no retarget needed) plus the files.</summary>
    public static ClipLibrary Load(SkinnedModel model, params string[] files);
    public AnimationClip? Get(string name);           // case-insensitive
    public IEnumerable<AnimationClip> WithTag(string tag);
    public IReadOnlyCollection<AnimationClip> All { get; }
}
```

The game calls it like this: `ClipLibrary.Load(SkinnedModel.Get("human"), "anims/human_anims")`.

## 5. C# API: Renderer (S6 implements; S5 calls)

```csharp
public enum WoundStyle { Skin = 0, Fabric = 1, Hard = 2 }

// Renderer: the existing signature extended (the old call sites still compile):
public void DrawSkinned(SkinnedModel model, int lod, in float4x4 world, ReadOnlySpan<float4x4> bones, float3 tint,
    ReadOnlySpan<float4> wounds = default, ReadOnlySpan<float4> woundInfo = default,
    uint hideRegions = 0, WoundStyle woundStyle = WoundStyle.Skin);
```

New `Material` enum values (Renderer.cs): `Denim = 28, Canvas = 29, Leather = 30, Knit = 31, Hair = 32`, all
"textured and tinted like cloth" (the same shading path as `Fleece`).
Texture set names and tile sizes (metres per repeat): `denim` 0.05, `canvas` 0.06, `leather` 0.08, `knit` 0.04, `hair` 0.03.

Fabric wound look (shader, `uWoundStyle == 1`): the distance is measured in the bind-space tangent plane of the cloth vertex
(`d − n·(d·n)` with n = bind normal), and a cloth fragment ignores a wound when `|d·n| > 0.04` m.
* hole: a dark, frayed-edged hole of 1.3 × the wound radius (no flesh);
* soak: a blood-darkened stain (wet colour 0.30, 0.018, 0.018 → dry 0.11, 0.018, 0.014, multiplied in at 0.88–0.96 strength,
  centre pooled ×0.8 while wet, roughness −0.3 while wet), radius
  `0.006 + 0.045 · (1 − exp(−age / 12))` m (× 1.5 for exits), elongated 1.6× downward (bind −Y, game down), drying darker
  and less glossy from 40 to 90 s.

`Hard` draws only the hole. `Skin` is the existing `applyWounds`, unchanged.

## 6. C# API: Game (S5 implements)

```csharp
namespace BrutalFps.Engine;
public sealed class BoneMask
{
    public static BoneMask Subtree(Skeleton s, params string[] roots);   // weight 1 on the roots and everything below
    public float this[int node] { get; }
}

public sealed class Animator
{
    public Animator(Skeleton skeleton, ClipLibrary clips);
    /// <summary>Base layer locomotion: groundSpeed m/s → Idle (0), Walk (Walk.Speed), Run (Run.Speed), blended. Between
    /// Walk and Run both are sampled at the same normalised phase, and the playback rate matches the ground speed (no foot
    /// sliding). Missing Run: Walk is sped up, capped at 1.6×.</summary>
    public void SetLocomotion(float groundSpeed);
    /// <summary>A full-body clip over the locomotion (fadeIn/fadeOut seconds). Non-looping clips end by themselves; looping ones play until Stop.</summary>
    public void PlayFullBody(string clip, float fadeIn = 0.2f, float fadeOut = 0.25f, float speed = 1f);
    /// <summary>The overlay layer: a clip applied only where mask &gt; 0 (e.g. Talk on spine_02 and up).</summary>
    public void PlayOverlay(string clip, BoneMask mask, float fadeIn = 0.3f, float fadeOut = 0.3f);
    public void Stop(string clip, float fadeOut = 0.25f);
    public bool IsPlaying(string clip);
    /// <summary>Remaining seconds of the current full-body one-shot, or 0.</summary>
    public float FullBodyRemaining { get; }
    /// <summary>Advances time and writes the final local pose (starting from the rest pose) into pose.</summary>
    public void Update(float dt, Span<NodePose> pose);
    /// <summary>Clip events crossed this frame (footL, footR...), cleared at the start of each Update.</summary>
    public IReadOnlyList<string> Events { get; }
}
```

```csharp
namespace BrutalFps.Game;
public readonly record struct OutfitItem(SkinnedModel Model, float3 Tint, WoundStyle Wound, string Id);
public sealed class Outfit
{
    public IReadOnlyList<OutfitItem> Items { get; }
    public uint HideRegions { get; }          // OR of every item's hides
}
public sealed class Wardrobe
{
    /// <summary>Reads Assets/Models/garments/wardrobe.json and each referenced garment's sidecar; loads garment models
    /// bound to body. Missing garments are a [WARN] and dropped from the choices.</summary>
    public static Wardrobe Load(SkinnedModel body);
    /// <summary>The outfit for this person: wardrobe.json "npcs" entry if present, else a random archetype
    /// (weighted), deterministic for (name, seed).</summary>
    public Outfit For(string npcName, int seed);
    /// <summary>For the BRUTALFPS_OUTFIT override.</summary>
    public Outfit FromIds(IEnumerable<string> ids, int seed);
}
```

**Environment variables** (for testing):
* `BRUTALFPS_OUTFIT="test subject:tshirt,trousers_work,boots_work;patrol:hivis_vest,..."` puts an outfit on the named people.
* `BRUTALFPS_NPCANIM="test subject:Run;dock:Talk"` makes the named person play that clip, looped, in place (locomotion clips at
  their own speed, overlays over Idle). Their route is frozen.
* `BRUTALFPS_NOCLOTHES=1` turns clothes off.

## 7. wardrobe.json (H2 writes; S5 reads)

```json
{
  "palettes": { "<name>": [[r, g, b], ...] },
  "archetypes": {
    "<name>": { "weight": 1.0, "slots": { "<slot>": [ { "id": "<garment id or null>", "weight": 1.0, "palette": "<palette name>" } ] } }
  },
  "npcs": { "<npc name>": { "archetype": "<name>", "seed": 1 } }
}
```
* Colours are linear RGB in 0–1, used directly as the shader tint (like `Npcs.SkinTones` and the car colours).
* `"id": null` means the slot is left empty. Every archetype must give `legs` and `feet` a non-null choice (nobody walks around in underwear or barefoot).
