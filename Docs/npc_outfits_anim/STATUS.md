# NPC outfits and animation: status

Facts from the agents' reports and the merged build. Plan and contracts are in `Docs/npc_outfits_anim/README.md` and `Docs/npc_outfits_anim/CONTRACTS.md`.

## Built

| Area | Files | Result |
| --- | --- | --- |
| Engine: skinned attachments, body regions, clip library (S1) | `Engine/SkinnedModel.cs` | Filtered index lists per (lod, mask), cached lazily. Logic test: body 31066 triangles at mask 0, 27418 with chest, belly and upperarm_hi hidden, 26972 with the trousers mask. |
| Body regions (S2) | `Tools/blender/humankit/regions.py`, `Tools/blender/build_human.py`, `Assets/Models/human.glb` | 59 joints, 17016 vertices, 31066 triangles, all 15 regions present. `build_human.py -- regions` re-exports `human.glb` without a rebuild. |
| Clothkit (S2) | `Tools/blender/clothkit/*` (carve, shell, weights, uvs, extras, export, check, preview, catalog, build), `Tools/blender/build_clothes.py` | Weights are an exact copy of the body's. IBM difference 0.0. |
| Garments, wave 1 (S2) | `Assets/Models/garments/tshirt.*`, `trousers_work.*`, `boots_work.*` | tshirt 5524 triangles, trousers_work 6014, boots_work 2898. Coverage minimum 30.1 / 31.4 / 109.1 mm (in S2's report order: tshirt / trousers_work / boots_work). |
| Garments, wave 2 (S3) | `Tools/blender/clothkit/garments_more.py`, `Tools/blender/clothkit/detail.py`, `Tools/blender/build_clothes_more.py`, `Assets/Models/garments/*` | 11 garments: longsleeve 8712 triangles, hoodie 12232, work_jacket 11064, hivis_vest 7476, jeans 8256, shorts 3022, sneakers 2638, cap 6342, beanie 6606, hair_short 4530, hair_buzz 4530. Jeans foot offset 24 mm (boot clearance). |
| Animation clips (S4) | `Tools/blender/humankit/animlib.py`, `Tools/blender/build_anims.py`, `Assets/human_anims.blend`, `Assets/Models/anims/human_anims.glb` | 8 new clips: Run, IdleLook, IdleShift, ShuffleTurn, Cower, DeathFwd, DeathBack, Talk. Run planted-foot speed 3.676 m/s against a 3.682 target, pelvis bob about 6 cm, stance 5 of 22 frames (23%). Loop closure 0.0016° or less. Talk channels below `spine_02` are not exported. Death clips switch the legs to FK. |
| Animator, wardrobe, NPC integration (S5) | `Engine/Animator.cs`, `Game/Wardrobe.cs`, `Game/Npcs.cs` | Locomotion with phase-synced Walk and Run (simulation error 4.4e-16 m/s), full-body one-shots, masked overlay, footstep events. Idle variants after 4 s stopped, ShuffleTurn when turning more than 0.6 rad while stopped, layered culling of garments. Idle to Walk blends linearly (smoothstep made the feet slide). |
| Renderer: hide mask, fabric wounds, materials (S6) | `Engine/Renderer.cs` | `WoundStyle` (Skin, Fabric, Hard). Materials Denim 28, Canvas 29, Leather 30, Knit 31, Hair 32. `vBindNormal`, `applyClothWounds`. `Tools/check_shaders.py` ALL PASS. Stain sizes 1.2, 4.3, 9.2, 11.2 cm at ages 0, 5, 20, 90 s. |
| Cloth texture sets (S7) | `Tools/blender/bake_textures.py`, `Tools/blender/modelkit.py`, `Engine/TextureLibrary.cs`, `Assets/Textures/{denim,canvas,leather,knit,hair}_*.png` | 1024 x 1024 sets, baked with the real nodekit. `bake_textures.py` accepts names with or without `--`. |
| Metadata (H2) | `Assets/Models/anims/clips.json`, `Assets/Models/garments/wardrobe.json` | Clip metadata for all 10 clips. Wardrobe palettes, archetypes and NPC entries. |
| Asset validator (H1) | `Tools/validate_skinned.py` | Final run on the merged repo: ALL PASS, 105 PASS lines. The WARN lines are the heuristic weights-match check only. |
| Project file (Opus) | `BrutalFps.csproj` | Copies `Assets/Models/**/*.glb` and `*.json` (garments and anims subfolders) to the output. |
| Test helpers | `Tools/tests/glbread.py`, `Tools/tests/glbread_cloth.py` (S2's copy, renamed) | Two agents had created `glbread.py`; S2's copy was renamed. |

Integration: the first compile of all the C# had 0 errors. In-game on an RTX 3050 at 1600 x 900: 9 people, 14 garments and 10 clips loaded with no warnings. A scripted shooting test gave 4 hits, 2 of them through and through, with fabric holes and blood soak visible on the vest.

## How to rebuild

Run from the project root. Each command rebuilds one part; the body and the garments/clips are independent.

```
blender -b --factory-startup -P Tools/blender/build_human.py -- regions
blender -b --factory-startup -P Tools/blender/build_clothes.py tshirt trousers_work boots_work
blender -b --factory-startup -P Tools/blender/build_clothes_more.py
blender -b --factory-startup -P Tools/blender/build_anims.py
blender -b --factory-startup -P Tools/blender/bake_textures.py -- denim canvas leather knit hair
blender -b --factory-startup -P Tools/blender/bake_textures.py -- sheet
```

Headless in the cloud workspace, the same scripts run as `python3.13 <script> <args>` with the `bpy` module (for example `python3.13 Tools/blender/build_anims.py`).

Build the game again after any of these, because the game loads its assets from the copy next to the exe.

## How to test

Environment variables (set them as for `BRUTALFPS_START` in the README: Visual Studio, Debug properties, environment variables):

* `BRUTALFPS_OUTFIT="test subject:tshirt,trousers_work,boots_work"` puts that outfit on the person named `test subject`. Several people are separated by `;`.
* `BRUTALFPS_NPCANIM="test subject:Run"` makes that person play `Run` looped, in place. Their route is frozen. Example with two people: `BRUTALFPS_NPCANIM="test subject:Run;dock:Talk"`.
* `BRUTALFPS_NOCLOTHES=1` turns clothes off.

Screenshot start used for checks: `BRUTALFPS_START=6.0,0,3.3,0`.

Asset checks, from the repo root:

```
python3 Tools/validate_skinned.py     # must end with ALL PASS
python3 Tools/check_shaders.py        # must end with ALL PASS
```

## Known issues

Each item names the owner of the work it concerns.

* (S2) tshirt: P1 has 2 vertices in the armpit; P2 misses about 1.2% in the armpit crease (occluded, so no hole is visible). Owner: S2.
* (S2) trousers_work: P1 has 10 vertices at the ankle at toe-off. Not drawn in a full outfit. Owner: S2.
* (S2) boots_work under trousers: P3 is about 9% at the ankle cuff at toe-off. Owner: S2.
* (S2) trousers under tshirt: P3 has 4 vertices outside. Owner: S2.
* (S3) hivis_vest: P1 is up to 18.8% measured on bare skin. A torso_base garment is always worn under it in every archetype, so this does not show in game. The vest reads as wide-shouldered (coverage rule). Owner: S3.
* (S3) jeans: P1 is 4.1% at the ankle at toe-off. Owner: S3.
* (S3) sneakers: P1 is 7.0% at the ankle at toe-off. Owner: S3.
* (S3) P2 is 0.5–1.5% at Walk and Idle for hoodie, work_jacket, jeans, shorts and longsleeve (creases). Owner: S3.
* (S2/S3) Run and Cower: P3 for the legs under torso garments is 32–41% in the checks (extreme bends, linear blend skinning). It looks clean in game at play distance. It is the first thing to fix if these clips get used by behaviour. Owner: S2/S3.
* (H1) The "weights match" check is a heuristic. Smoothing and crease-bridging move cloth vertices sideways, so it WARNs on several garments (hivis_vest, hoodie, jeans, longsleeve, tshirt, work_jacket), although the weights are copied exactly. Owner: H1.
* (Opus) The contract was revised twice during the work: exact weights, `_REGION` on garments, layered culling, drawn-surface P1/P2/P3 checks, and layers renumbered to hair 1, feet 1, legs 2, torso_base 3, head 3, torso_outer 4, plus a darker stain. Owner: Opus.

## Numbers

* Frame time before clothes: 13.8–15 ms.
* Frame time with 9 people dressed: 13.4–15.2 ms. The budget was +1.5 ms.
* Load time: body `human.glb` 73–111 ms (unchanged); each garment 10–19 ms as an attachment (tshirt, trousers_work measured); clip library loads in the same log line as the body.
* Loaded in game: 9 people, 14 garments, 10 clips, no warnings.
