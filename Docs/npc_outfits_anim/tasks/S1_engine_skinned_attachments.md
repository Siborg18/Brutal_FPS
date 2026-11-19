# S1: Engine: skinned attachments, body regions, clip library

**Model:** Sonnet · **Wave:** 1 · **Branch:** `s1-engine`

## Goal
Extend `Engine/SkinnedModel.cs` so that (a) garments load as attachments sharing the body's skeleton, (b) the body can be
drawn with regions hidden, and (c) clips from several glTF files are retargeted onto one skeleton with metadata from
clips.json. The API is fixed: implement CONTRACTS.md §4 **exactly** (names, signatures, semantics).

## Read first
1. `Docs/npc_outfits_anim/CONTRACTS.md`: §1 (regions, culling rule), §3 (clips.json schema), §4 (your API).
2. `Engine/SkinnedModel.cs`: the whole file; you're extending it.
3. `Game/Npcs.cs` lines 282–320, to see how the model and clips are used today (don't edit it).
Don't read `Engine/Renderer.cs` (124 KB); you don't need it.

## Owns (may edit)
`Engine/SkinnedModel.cs` only. You may add new types to it (`BodyRegions`, `ClipInfo`, `ClipLibrary`).

## Must not touch
Every other file. In particular `Npcs.cs` and `Renderer.cs` (other agents own them and call your API through the contract).

## Steps
1. **Regions.** In `ReadPrimitive`, read the optional `_REGION` attribute (`a.TryGetValue("_REGION", ...)`; use
   `GltfData.Read`; round to int, clamp 0–15). Keep the 20-float vertex layout **unchanged** (the shader depends on it); hold
   the regions in a separate `byte[]` passed into `SkinnedPart`.
2. **`SkinnedPart.Draw(int lod, uint hideRegions)`.** With mask 0 or no regions, use the existing path. Otherwise get (or lazily build
   and cache in a `Dictionary<(int lod, uint mask), ...>`) a filtered index list: drop a triangle only if all 3 vertices'
   region bits are in the mask. Upload filtered lists to **their own element buffer bound in their own vertex array object**
   that reuses the same vertex buffer and attribute setup. Factor the attribute setup into a helper so both VAOs are
   identical. The same `GlVertexArray`/`BufferData`/`VertexAttribute`/`DrawIndexed` calls as the existing code are the only GL API you
   may use.
3. **Attachments.** `Get(string name, Skeleton? bindTo = null)`: the cache key includes `bindTo` (reference identity). Path:
   `Assets/Models/{name}.glb` (`name` may contain `/`; use `Path.Combine` with the parts split on `/`). When `bindTo` is set,
   after reading the vertices, remap joints `jointIndex → bindTo joint index with the same node name`. You'll need a
   `Skeleton.JointIndex(string nodeName)` helper (returns −1 if none): add it. Throw `FormatException` naming the missing
   joint. Compare inverse bind matrices per remapped joint (max abs element diff > 1e-3 → one `[WARN]` line naming the worst
   joint). Remap the collision builder's joints as well (it reads the vertex array; remap before `collision.Add`).
4. **Clips.**
   * `ClipInfo` record + `Default`.
   * `AnimationClip`: add `Info` (default `ClipInfo.Default(Name)`), `Speed`, and an internal constructor overload taking
     `int[] nodeRemap` (source node → target node, −1 = drop the channel). Count dropped channels.
   * `ClipLibrary.Load(Skeleton target, params string[] files)`: for each file, load the glTF (as `SkinnedModel.Load` does),
     build the remap from the file's node names to `target.NodeIndex(name)`, make the clips, and add them (a later duplicate name replaces the earlier one with a
     `[WARN]`). Then parse `Assets/Models/anims/clips.json` with `System.Text.Json` (`JsonDocument`; tolerate a missing file or
     missing fields) and assign `Info` to the clips by name (case-insensitive). Log one summary line in the style of `SkinnedModel.Load`'s log line.
   * `ClipLibrary.Load(SkinnedModel model, params string[] files)`: start from `model.Clips` (no remap), then as above.
   * Missing glb: a `[WARN]` and continue. Never throw from `ClipLibrary.Load` except for a null target.
5. Keep every existing public member and behaviour (Npcs.cs must still compile unchanged against your file).

## Acceptance checks (you can't run dotnet; do these instead)
1. **API diff:** list every public/internal member you added. It must match CONTRACTS §4 one for one (plus
   `Skeleton.JointIndex`). Paste the list in your report.
2. **Compile-risk review:** grep your new code for every Brutal/GL API you call and show that each call already appears
   elsewhere in the repo with the same shape (`grep -n` lines). Any API not used elsewhere must be justified in DEVIATIONS.
3. **Logic test in Python:** port the triangle-filter rule and the joint-remap rule to a ~40-line Python script and run it on
   `Assets/Models/human.glb` (parse with `json` + `struct` + `numpy`; it has no `_REGION` yet, so fake regions by
   dominant joint name using the CONTRACTS §1 table, ignoring t). Report: triangle counts at mask 0, at `chest|belly|upperarm_hi`
   and at the trousers mask. They must decrease, and no triangle with any visible vertex may be dropped.
4. `git diff --stat` shows only `Engine/SkinnedModel.cs` (plus the Python test under `Tools/tests/`, which you may add).

## Report
Use the report format in `README.md` §4. Commit on branch `s1-engine`.
