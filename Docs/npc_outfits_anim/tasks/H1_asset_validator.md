# H1: Asset validator script

**Model:** Haiku · **Wave:** 1 · **Branch:** `h1-validator`

## Goal
Write `Tools/validate_skinned.py`: one plain-Python script (standard library + numpy only, **no bpy**) that checks every
NPC asset against the contract and prints one line per check. Opus runs it after every merge.

## Read first
`Docs/npc_outfits_anim/CONTRACTS.md` §1, §2, §3, §7. Nothing else is needed.

## Owns
`Tools/validate_skinned.py`, `Tools/tests/test_validate_skinned.sh`.

## Must not touch
Anything else.

## Usage
```
python3 Tools/validate_skinned.py [--root <project root, default: the parent of Tools/>] [--strict]
```
Output lines are exactly `PASS <check>: <detail>`, `FAIL <check>: <detail>`, `WARN <check>: <detail>` or
`SKIP <check>: <reason>`. The last line is `ALL PASS` or `<n> FAILURES`. Exit code 0 if there are no FAILs, otherwise 1. Without `--strict`, a missing garment
that wardrobe.json references is a WARN. With `--strict` it's a FAIL.

## Code structure (follow it)
* `read_glb(path) -> (json_dict, bin_bytes)`: a 12-byte header, then chunks; the JSON chunk is type `0x4E4F534A`, BIN is `0x004E4942`.
* `read_accessor(gltf, bin, index) -> numpy array of shape (count, components)`: handle `bufferView.byteOffset`,
  `accessor.byteOffset`, `byteStride` (default = tightly packed), componentType 5120/5121/5122/5123/5125/5126, and `normalized`
  (unsigned byte /255, unsigned short /65535, signed byte /127, signed short /32767, clamped at −1).
* `node_names(gltf)`: list of names; `skin_joint_names(gltf)`: names of `skins[0].joints`.
* One function per check group below; `main()` calls them in order and counts FAILs.

## Checks (implement every one; the names in quotes are the `<check>` text)
**human.glb** (`Assets/Models/human.glb`; a FAIL if missing):
1. "human skin": `skins` present; joint count == 59.
2. "human regions": every primitive of every mesh used by a skinned node has a `_REGION` attribute; all values are integers
   (|v − round(v)| < 1e-3) in 0–14; all 15 values occur. The detail lists the count per region name (the names from CONTRACTS §1).
3. "human weights": for every primitive, each vertex's WEIGHTS_0 sums to 1 ± 0.01, and has ≤ 4 nonzero weights.
4. "human clips": animation names include `Walk` and `Idle`.

**Each `Assets/Models/garments/*.glb`** (`<check>` = "garment <id> ..."; SKIP the group if there are none):
5. "... skin": it has a skin, and every joint name is in human.glb's joint names.
6. "... bind": for every joint, the inverse bind matrix equals human.glb's matrix for the same joint name, max abs diff ≤ 1e-4.
   The detail gives the worst joint and diff.
7. "... weights": as check 3.
8. "... sidecar": `<id>.json` exists; its keys are exactly `id, slot, layer, offset_mm, hides, wound, materials, triangles`; `id` == the file
   stem; `slot` ∈ {hair, head, torso_base, torso_outer, legs, feet}; every `hides` entry is a region name; `wound` ∈ {skin,
   fabric, hard}; `layer` and `offset_mm` match the CONTRACTS §2 layer table for that slot (for offset_mm accept the table's
   range for hair, and exactly the table's value for the others, except `hair_buzz`, which is 4); `materials` as a set equals the glb's material names
   used by primitives; `triangles` == the sum over primitives of (index count / 3).
9. "... catalogue": `id` is in the CONTRACTS §2 catalogue, and slot, hides and materials equal the catalogue row (write the catalogue as a
   dict constant in the script, copied from the table).

**`Assets/Models/anims/human_anims.glb`** (SKIP if missing):
10. "anims names": the animation names are exactly {Run, IdleLook, IdleShift, ShuffleTurn, Cower, DeathFwd, DeathBack, Talk}
    (no Walk or Idle).
11. "anims targets": every channel's target node name exists in human.glb's node names.
12. "anims interpolation": every sampler is LINEAR or STEP.
13. "anims durations": each animation's duration (max of its input accessors' max) equals the CONTRACTS §3 Duration ± 1/30 s.

**`Assets/Models/anims/clips.json`** (SKIP if missing):
14. "clips schema": `fps` == 30; every clip has `loop` (bool), `stride` (number ≥ 0), `tags` (list of str), `events` (list of
    {name: str, time: number}).
15. "clips coverage": the set of clip names in clips.json == the animations in human.glb ∪ human_anims.glb (only those files that
    exist); each event time ≤ the clip's duration.
16. "clips values": loop, stride, tags and events equal the CONTRACTS §3 table (put the table in the script as a dict constant).

**`Assets/Models/garments/wardrobe.json`** (SKIP if missing):
17. "wardrobe schema": the top-level keys are `palettes`, `archetypes`, `npcs`; every colour is 3 numbers in 0–1; every archetype has
    `weight` > 0 and `slots`, the slot keys are valid slot names, and each choice has `id` (str or null), `weight` > 0, and `palette` (str,
    required when `id` isn't null).
18. "wardrobe refs": every referenced palette exists; every non-null id has a sidecar (WARN without --strict); the garment's
    sidecar slot equals the slot it's listed under; every archetype has a non-null option under `legs` and under `feet`; every npc's
    archetype exists; npc names ⊆ {test subject, patrol, dock, road, pumps, verandah, car, motel, billboard}.

## Self-test (`Tools/tests/test_validate_skinned.sh`)
1. Run on the real repo, and save the output.
2. Make a temp root (`mktemp -d`) with: a copy of `Assets/Models/human.glb`; `garments/fake.glb` = another copy of human.glb plus
   a deliberately wrong `fake.json` (slot "hat"); and a `clips.json` missing `Walk`. Run with `--root`. The expected FAILs are
   "human regions" (if the real file has no `_REGION` yet), "garment fake sidecar", "garment fake catalogue", and "clips coverage". Assert those FAIL lines
   appear, and that "garment fake bind" PASSes (identical file).

## Acceptance checks
1. Paste the output of the run on the real repo.
2. Paste the self-test result (which expected FAILs appeared).
3. `python3 -m py_compile Tools/validate_skinned.py` succeeds.

## Report
Format in README §4. Commit on branch `h1-validator`.
