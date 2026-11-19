# S7: Cloth texture sets

**Model:** Sonnet · **Wave:** 1 · **Branch:** `s7-textures`

## Goal
Add five tileable PBR texture sets: `denim` (0.05 m per tile), `canvas` (0.06), `leather` (0.08), `knit` (0.04) and `hair` (0.03).
Bake them, and wire them up so `<set>_tint` materials on garments use them.

## Environment
Headless Blender (`python3.13`, `import bpy`, Cycles CPU). `bake_textures.py` takes set names after `--`. When you run it
headless with `python3.13`, pass the names as arguments (adapt the argument parsing to accept both forms, the same rule as in the other
briefs). 1024² is enough for every set (the same as `cloth`/`fleece`).

## Read first
1. `Docs/npc_outfits_anim/CONTRACTS.md` §5 (material numbers and tile sizes).
2. `Tools/blender/bake_textures.py`: the helper `g` functions used by `cloth` and `fleece` (lines ~299–345), the `RECIPES` table
   (~line 486), `main`, and `contact_sheet`.
3. `Tools/blender/modelkit.py` lines 26–48 (`TILE`, `set_of`).
4. `Engine/TextureLibrary.cs` (the `Table`), and in `Engine/Model.cs` grep how a material name maps to a `Material` value (`fleece`).

## Owns
`Tools/blender/bake_textures.py`, `Tools/blender/modelkit.py` (the TILE entries only), `Engine/TextureLibrary.cs` (Table rows),
`Engine/Model.cs` (the name → material mapping only), `Assets/Textures/{denim,canvas,leather,knit,hair}_*.png`, and
`Assets/Textures/` contact sheet.

## Must not touch
`Engine/Renderer.cs` (S6 adds the enum values `Denim = 28 ... Hair = 32`; reference them by name in your C#), other sets' PNGs.

## Look of each set (all are tinted by the instance colour, so bake a near-neutral albedo with a paint mask, as `fleece` does)
* **denim:** 2/1 right-hand twill diagonals, slubby warp threads, faint white weft showing through, subtle fading. Roughness 0.75–0.85.
* **canvas:** a tight plain weave (heavier than `cloth`), slight stiffness wrinkles in the height, a few scuffs. Roughness 0.8.
* **leather:** fine pebble grain, creases, slightly glossier worn patches. Roughness 0.45–0.65.
* **knit:** rows of stockinette V-stitches (a visible repeating V pattern in normal and height), fuzz. Roughness 0.9.
* **hair:** fine strands running along V (vertical in UV space), clumping, darker roots. Roughness 0.5. The albedo is neutral grey
  so the tint gives the hair colour.

## Steps
1. Add five recipe functions and the `RECIPES` rows, following the `fleece` pattern exactly (same dict keys returned).
2. Add `TILE` entries (modelkit), `TextureLibrary.Table` rows `(Material.Denim, "denim", 0.05f)` etc., and the `Model.cs` name mapping
   so `denim_tint`, `canvas_tint`, `leather_tint`, `knit_tint` and `hair_tint` resolve to the new materials.
3. Bake only your five sets, then **rebuild the contact sheet from all PNGs** with `-- sheet` (a partial bake once overwrote the sheet
   with just the new sets).

## Acceptance checks
1. For each set, all the PNG files the existing sets have exist (compare suffixes with `fleece_*`), at 1024².
2. **Tiling:** for each albedo and normal map, the mean absolute difference between the left and right edge columns (and top vs
   bottom rows) is ≤ 2× the mean difference between adjacent interior columns. Report the numbers (script in `Tools/tests/`).
3. Read the contact sheet PNG and describe each new set in one line: does it read as the material?
4. `git diff --stat`: only the owned files.

## Report
Format in README §4. Commit on branch `s7-textures` (the PNGs too).
