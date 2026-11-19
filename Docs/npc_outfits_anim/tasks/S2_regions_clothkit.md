# S2: Body regions, clothkit, and the first three garments

**Model:** Sonnet · **Wave:** 1 · **Branch:** `s2-clothkit`

## Goal
1. Give the body mesh the `_REGION` attribute (CONTRACTS §1) and re-export `Assets/Models/human.glb` with it, **without**
   rebuilding the human (that takes 3 minutes and changes nothing else).
2. Write `clothkit`, a small reusable package that carves garments out of the body's game mesh, and use it to build
   `tshirt`, `trousers_work` and `boots_work` (CONTRACTS §2) as `.glb` + sidecar `.json`, with previews and a
   poke-through check. S3 will use your package next to build 11 more garments, so it must be easy to reuse.

## Environment
Headless Blender: `python3.13 script.py` with `import bpy` (bpy 5.2.3). No GPU, so render previews with **Cycles CPU** (32
samples, denoise off is fine). The scripts must also run on the PC with `blender -b --factory-startup -P <script> -- <args>`:
parse arguments after `--` the same way `build_human.py` does, and if `--` is absent, treat everything after the script path as
arguments.

## Read first
1. `Docs/npc_outfits_anim/CONTRACTS.md` §1, §2 (all of it: layers, offsets, catalogue, coverage rule).
2. `Tools/blender/build_human.py` (its export flags and main), `Tools/blender/humankit/skin.py` (`smooth_and_limit`: reuse it),
   `Tools/blender/humankit/uvs.py` (how the body is seamed), and `humankit/anatomy.py` only for the joint names you need (brow, ear).
3. `Tools/blender/modelkit.py` lines 1–50 (the TILE table and UV convention: 1 UV unit = one texture tile).

## Owns (may edit/create)
`Tools/blender/humankit/regions.py`, `Tools/blender/clothkit/` (new package), `Tools/blender/build_clothes.py`,
`Tools/blender/build_human.py` (only: call `regions.assign` before export, add `export_attributes=True`, add a `regions`
mode), `Assets/Models/human.glb`, `Assets/Models/garments/{tshirt,trousers_work,boots_work}.{glb,json}`,
`Tools/previews/models/garment_*.png`, `Tools/tests/`.

## Must not touch
`Assets/human.blend` (open it, never save over it), C# files, the other humankit modules, other garment ids.

## Steps
1. **`humankit/regions.py`:** `assign(body, rig)` writes the `_REGION` float point attribute from the final weights (the dominant deform
   bone, plus t along the bone from `bone.head_local`/`tail_local`). For `scalp`, derive the brow and ear heights from
   `anatomy` joints if they can be had cheaply, otherwise from the eye bones' positions with documented constants. Print a
   count per region.
2. **`build_human.py -- regions`:** open `Assets/human.blend`, `regions.assign`, export `human.glb` with the same flags as
   `export_glb` plus `export_attributes=True`. Don't save the .blend. The full build path also calls `assign` and exports with
   attributes.
3. **`clothkit/`** (suggested modules; keep each under ~300 lines):
   * `recipe.py`: a `Garment` dataclass with id, slot, layer, offset_mm, hides, materials, carve spec (regions plus extra coverage as
     cut planes relative to bones, e.g. "upperarm.L t ≤ 0.55"), per-region offset overrides (the trousers' calf_lo flare),
     hem style, and extras (a sole slab, etc.).
   * `carve.py`: copy the body (evaluated, rest pose) and keep the faces whose vertices are all inside the carve spec. Apply the coverage
     rule (+25 mm into neighbouring visible regions, measured along the surface; geodesic over mesh edges is fine).
   * `shell.py`: offset along the vertex normals, Laplacian-smooth the positions (hides the muscle definition; keep 6–12
     iterations), then push any vertex back out to ≥ offset − 1 mm from the body surface (BVH `find_nearest`). Add a hem: the
     boundary loop extruded 3 mm inward toward the skin and rolled, so the cloth reads as having thickness. No inner
     faces beyond the hem.
   * `weights.py`: copy the source body vertex's groups to each garment vertex (index map kept from carve), then
     `smooth_and_limit`. Extra geometry (sole, hem) takes the weights of its nearest garment vertex.
   * `uvs.py`: keep the body's UVs per vertex, rescale the islands uniformly so 1 UV unit = `TILE[set]` metres (measure 3D area
     against UV area per island).
   * `export.py`: parent to `Human_Rig`, add an armature modifier, and export the garment and rig (deform bones) with CONTRACTS §2 flags;
     write the sidecar JSON (triangles = LOD0 count).
   * `check.py`: the poke-through check (below).
   * `preview.py`: Cycles renders of the garment on the body, at front, back and three-quarter in the rest pose, plus Walk frames 1, 9 and 17.
     Save `Tools/previews/models/garment_<id>_<view>.png` (600×900) and one contact sheet `garment_<id>.png`.
4. **`build_clothes.py [ids...]`:** open `human.blend`, import `clothkit`, build the listed ids (default: all registered recipes),
   run `check`, write previews. Recipes live in `clothkit/catalog.py` as a registry so S3 adds entries in a new file.
5. Build `tshirt`, `trousers_work` and `boots_work`. Triangle budgets (LOD0): tshirt ≤ 6000, trousers ≤ 7000, boots ≤ 3000. The
   boots: the foot shell (6 mm) plus a 25 mm sole slab with a heel step, and shafts reaching 120 mm above the ankle.

## Acceptance checks (run them all; put the numbers in your report)
1. `human.glb` (new): parse it and confirm the `_REGION` attribute is present, every value in 0–14, all 15 regions non-empty; joint count 59; clips
   Walk and Idle present; vertex count unchanged from the old file (`git show HEAD:Assets/Models/human.glb`).
2. **Poke-through** (`check.py`): for the rest pose and every 4th frame of Walk and Idle, pose the rig and evaluate
   body + garment. For each body vertex in a region the garment hides, take the nearest point on the garment and its
   normal: the vertex pokes through if it's outside. **Pass:** 0 in the rest pose and ≤ 0.2% of those vertices on every frame.
   Run the same test for the pairs trousers-under-tshirt (over the hips band the shirt covers) and boots-under-trousers (over calf_lo).
3. Coverage: the minimum geodesic distance from any hidden-region boundary to the garment edge is ≥ 25 mm.
4. Inverse bind matrices of each garment glb equal `human.glb`'s per joint name, to within 1e-4 (parse both).
5. The sidecar JSON keys and values match CONTRACTS §2 exactly.
6. Look at your own contact sheets (Read the PNGs). Hems, collar and sole look right, no holes and no spikes. Say what you saw.

## Report
Format in README §4. Include the per-region vertex counts, poke-through worst frame %, and the triangle counts. Commit on
branch `s2-clothkit` (the .glb and .png files too).
