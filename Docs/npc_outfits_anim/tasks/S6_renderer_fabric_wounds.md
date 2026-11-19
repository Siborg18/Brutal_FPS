# S6: Renderer: hidden regions, fabric wounds, new cloth materials

**Model:** Sonnet · **Wave:** 1 · **Branch:** `s6-renderer`

## Goal
Implement CONTRACTS §5 in `Engine/Renderer.cs`: the extended `DrawSkinned`, `WoundStyle`, the fabric and hard wound looks in the
shader, and the five new `Material` values shaded like `Fleece`.

## Read first (Renderer.cs is 124 KB: navigate with grep, don't read it whole)
1. `Docs/npc_outfits_anim/CONTRACTS.md` §1 (culling rule, for context) and §5 (your spec).
2. In `Engine/Renderer.cs`:
   * `grep -n "Fleece\|Material.Cloth" Engine/Renderer.cs`: every place the new materials must be added alongside Fleece;
   * `grep -n "vBindPos\|uSkin\|applyWounds\|uWound" Engine/Renderer.cs`: the skinning and wound code. Read
     `applyWounds` in full (about 60 lines from its definition) and the vertex shader's `uSkin == 2` branch;
   * `DrawSkinned` (around line 2240) and the uniform-location setup (around line 1770).

## Owns
`Engine/Renderer.cs` only (plus `Tools/tests/` for test files).

## Must not touch
Everything else. Note: `SkinnedPart.Draw(int lod, uint hideRegions)` is being added by S1 at the same time. Call it
exactly with that signature (`part.Draw(lod, hideRegions)`); it won't exist in your worktree, which is expected.

## Steps
1. `public enum WoundStyle { Skin = 0, Fabric = 1, Hard = 2 }` next to `Material` (namespace `BrutalFps.Engine`).
2. `Material` enum: `Denim = 28, Canvas = 29, Leather = 30, Knit = 31, Hair = 32`, each with a short comment. Add them everywhere
   `Fleece` is handled (ref tints, roughness defaults, texture-set lookups, shader branches that test the material number).
3. Vertex shader: output `vBindNormal` (the bind-space normal, `aNormal`, only meaningful when `uSkin == 2`), next to
   `vBindPos`. Fragment shader: `uniform int uWoundStyle;`.
4. `applyWounds`: keep the skin path **byte-identical** in behaviour for `uWoundStyle == 0`. Add the fabric path and the hard path
   per CONTRACTS §5 (tangent-plane distance, the `|d·n| > 0.04` rejection, frayed hole, growing downward-elongated soak, wet then
   drying). Use the same noise/hash helpers the skin path uses.
5. `DrawSkinned(..., uint hideRegions = 0, WoundStyle woundStyle = WoundStyle.Skin)`: set `uWoundStyle`, call
   `part.Draw(lod, hideRegions)`, and reset `uWoundStyle` to 0 afterwards, as `uSkin` is reset.

## Acceptance checks
1. `python3 Tools/check_shaders.py` → `ALL PASS` (it extracts both shaders from Renderer.cs and runs glslangValidator).
   Run it before your change too, to confirm the baseline.
2. **Skin path unchanged:** `git diff` of the `applyWounds` skin branch shows only structural changes (a style branch wrapped round it),
   no maths edits. Paste the diff hunk summary.
3. **Visual unit test of the fabric look:** port the fabric wound function to Python/numpy (`Tools/tests/fabric_wound.py`) and render a
   512×512 PNG of a flat cloth patch (albedo 0.5 grey) with one entry wound at ages 0, 5, 20 and 90 s, and one exit wound at 20 s (5 tiles in a row).
   Read the PNG yourself and describe it. The hole must be dark with a ragged edge, the stain must grow over time and run downward, and the
   90 s tile must be darker and drier than the 20 s one.
4. Every `Fleece` occurrence has a matching treatment for the five new materials (list the line numbers).
5. `git diff --stat` shows only `Engine/Renderer.cs` and `Tools/tests/`.

## Report
Format in README §4. Commit on branch `s6-renderer`.
