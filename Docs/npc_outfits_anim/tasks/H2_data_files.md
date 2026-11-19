# H2: Data files: wardrobe.json and clips.json

**Model:** Haiku · **Wave:** 1 · **Branch:** `h2-data`

## Goal
Write two JSON files, transcribed **exactly** from the tables below and in CONTRACTS. Don't invent, rename or
"improve" anything.

## Read first
`Docs/npc_outfits_anim/CONTRACTS.md` §3 (the clips.json schema and clip table) and §7 (the wardrobe.json schema).

## Owns
`Assets/Models/anims/clips.json`, `Assets/Models/garments/wardrobe.json`.

## 1. `Assets/Models/anims/clips.json`
One entry per row of the CONTRACTS §3 clip table (all 10 clips, including Idle and Walk), with `loop`, `stride`, `tags`, and `events`
(an empty list where the table says —). Top-level `"fps": 30`. Write numbers as in the table (e.g. 0.5333). Indent with 2 spaces.

## 2. `Assets/Models/garments/wardrobe.json`
Use the schema in CONTRACTS §7. Indent with 2 spaces.

### palettes (linear RGB)
| palette | colours |
| --- | --- |
| shirts | [0.75,0.75,0.72], [0.30,0.31,0.32], [0.04,0.06,0.15], [0.25,0.03,0.04], [0.13,0.15,0.06], [0.03,0.03,0.03], [0.30,0.45,0.65] |
| workwear | [0.38,0.30,0.17], [0.05,0.07,0.14], [0.09,0.09,0.10], [0.75,0.20,0.02] |
| security | [0.03,0.04,0.09], [0.02,0.02,0.02] |
| denim | [0.06,0.10,0.22], [0.12,0.18,0.32], [0.03,0.04,0.07] |
| hivis | [0.85,0.80,0.02], [0.95,0.35,0.02] |
| leather | [0.18,0.10,0.05], [0.05,0.04,0.03], [0.35,0.22,0.10] |
| sneaker | [0.80,0.80,0.80], [0.05,0.05,0.05], [0.20,0.25,0.40] |
| knit | [0.05,0.05,0.06], [0.25,0.03,0.04], [0.05,0.10,0.05], [0.30,0.30,0.30] |
| hair | [0.015,0.012,0.010], [0.05,0.03,0.02], [0.12,0.07,0.035], [0.40,0.28,0.13], [0.30,0.29,0.28], [0.30,0.10,0.03] |

### archetypes
Each cell is `id weight palette`; `null weight` means an empty slot (no palette key).

| archetype (weight) | hair | head | torso_base | torso_outer | legs | feet |
| --- | --- | --- | --- | --- | --- | --- |
| labourer (3) | hair_short 0.5 hair; hair_buzz 0.4 hair; null 0.1 | null 0.6; cap 0.3 shirts; beanie 0.1 knit | tshirt 0.6 shirts; longsleeve 0.4 shirts | null 0.5; hivis_vest 0.35 hivis; work_jacket 0.15 workwear | trousers_work 0.7 workwear; jeans 0.3 denim | boots_work 1 leather |
| security (1) | hair_buzz 0.7 hair; hair_short 0.3 hair | null 0.5; cap 0.5 security | longsleeve 1 security | null 0.5; work_jacket 0.5 security | trousers_work 1 security | boots_work 1 leather |
| local (2) | hair_short 0.6 hair; hair_buzz 0.2 hair; null 0.2 | null 0.7; cap 0.3 shirts | tshirt 0.7 shirts; longsleeve 0.3 shirts | null 0.6; hoodie 0.4 knit | jeans 0.5 denim; shorts 0.5 workwear | sneakers 0.7 sneaker; boots_work 0.3 leather |
| trucker (1) | hair_short 0.5 hair; hair_buzz 0.3 hair; null 0.2 | cap 0.7 shirts; null 0.3 | tshirt 0.5 shirts; longsleeve 0.5 shirts | null 0.6; work_jacket 0.4 workwear | jeans 0.6 denim; trousers_work 0.4 workwear | boots_work 1 leather |

### npcs
| npc | archetype | seed |
| --- | --- | --- |
| test subject | labourer | 1 |
| patrol | security | 2 |
| dock | labourer | 3 |
| road | local | 4 |
| pumps | trucker | 5 |
| verandah | local | 6 |
| car | local | 7 |
| motel | trucker | 8 |
| billboard | labourer | 9 |

## Acceptance checks
1. `python3 -c "import json; json.load(open('Assets/Models/anims/clips.json')); json.load(open('Assets/Models/garments/wardrobe.json'))"` succeeds.
2. Write a throwaway Python snippet (don't commit it) that re-reads both files and prints, for every row in the tables above,
   the value from the file next to the table's value. Every row must match; paste the count of matched rows (clips: 10, palettes:
   9, archetypes: 4 × 6 slots, npcs: 9).
3. If `Tools/validate_skinned.py` exists in your worktree, run it and paste the "clips" and "wardrobe" lines (missing garments are WARN; that's expected).

## Report
Format in README §4. Commit on branch `h2-data`.
