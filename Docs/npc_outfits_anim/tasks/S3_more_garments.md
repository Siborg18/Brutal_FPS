# S3: The rest of the garment catalogue

**Model:** Sonnet · **Wave:** 2 (after S2 and S7 are merged) · **Branch:** `s3-garments`

## Goal
Build the 11 garments that CONTRACTS §2 assigns to S3, with S2's `clothkit`, to the same quality bar and checks.

## Read first
1. `Docs/npc_outfits_anim/CONTRACTS.md` §2.
2. `Tools/blender/clothkit/` (all; it's S2's package) and `clothkit/catalog.py`, to see how the three existing recipes are written.
3. S2's report (Opus pastes it below this line when launching you).

## Owns
`Tools/blender/clothkit/garments_more.py` (your recipes, registered in the catalog the way S2 documented),
`Assets/Models/garments/<your ids>.{glb,json}`, `Tools/previews/models/garment_<your ids>*.png`. Small, backward-compatible
additions to clothkit modules (a new hem style, a peak, a collar roll) are allowed; list each in DEVIATIONS.

## Must not touch
S2's three garments' outputs, `human.glb`, C#.

## Specifics
* `longsleeve`: cuffs end over the wrist (coverage rule: ≥ 25 mm onto `hand`); a ribbed cuff band 30 mm.
* `hoodie`: thicker look (offset 18 mm), a rolled collar ring 35 mm high instead of a full hood, a kangaroo-pocket outline as an
  inset seam (geometry, not a texture), and a ribbed waistband.
* `work_jacket`: front zip line (a seam), collar flaps 50 mm, hem 100 mm into `hips`, chest pockets as inset panels.
* `hivis_vest`: sleeveless, but by the coverage rule it must reach ≥ 25 mm past the chest boundary onto the shoulder cap
  (`upperarm_hi`) and up the neck, so cut wide shoulders, not narrow straps. Two 50 mm reflective bands round the torso
  as their own material.
* `jeans`: like trousers_work but slimmer (6 mm, flaring to 12 mm over calf_lo), with a belt-loop waistband.
* `shorts`: hide `hips` and `thigh_hi`; hem at ≥ 25 mm into `thigh_lo`.
* `sneakers`: low upper (top at the ankle bone), 20 mm sole.
* `cap`: a crown over the scalp (14 mm) plus a peak 70 mm forward, rigid on `head` (weights 1.0 on head).
* `beanie`: knit crown, a rolled brim 40 mm, sits 20 mm lower than the cap on the forehead.
* `hair_short`: 9 mm over the scalp, thinning to 4 mm at the hairline edge. It reaches 10 mm past the scalp boundary (the hair/head
  exception in CONTRACTS §1), so the edge sits at about the ear tops; ear faces are excluded from the carve.
* `hair_buzz`: 4 mm, the same outline.

## Acceptance checks
All six of S2's checks, for every one of your garments. In addition, run the poke-through check for these pairs: hair under cap, hair under beanie,
tshirt/longsleeve under hoodie, work_jacket and hivis_vest, jeans/shorts under each torso garment, jeans over boots_work and sneakers.
If `Assets/Models/anims/human_anims.glb` exists, also run the per-frame check on every 4th frame of Run and Cower.

## Report
Format in README §4: a table of id, triangles, worst poke-through %, and min coverage mm.
