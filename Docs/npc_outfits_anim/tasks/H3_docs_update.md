# H3: Documentation update

**Model:** Haiku · **Wave:** 3 (after Opus has integrated and verified) · **Branch:** `h3-docs`

## Goal
Record what was built, from the agents' reports and the merged code. Write facts only: no plans or praise.

## Inputs (Opus pastes them below this line when launching you)
* Every agent's final report (S1–S7, H1, H2).
* Opus's integration notes: compile fixes, frame times, screenshot verdicts.

## Owns
`Docs/npc_outfits_anim/STATUS.md` (new), `README.md` (only the NPC section: find the heading that mentions people/NPCs
with `grep -n "^#" README.md`; if there's none, add `## People: clothes and animation` at the end).

## STATUS.md structure (use these headings exactly)
1. `## Built`: a table: area · files · result (one line each, with the key numbers: triangles, poke-through %, clip count).
2. `## How to rebuild`: the exact commands for regions, clothes, anims and textures (from the reports), as a code block.
3. `## How to test`: the env vars (CONTRACTS §6) with one example each, and `python3 Tools/validate_skinned.py`.
4. `## Known issues`: every FAIL, PARTIAL or DEVIATION from the reports and integration notes, one bullet each, naming the owner.
5. `## Numbers`: the frame times before and after, the load time.

## README.md NPC section
At most 25 lines: what an outfit is, where garments and clips live, the three env vars, and the rebuild commands. Link to
`Docs/npc_outfits_anim/STATUS.md`.

## Acceptance checks
1. Every file path you mention exists (`test -e` each one; paste the result).
2. Every number you write appears in one of the inputs (no rounding beyond 2 significant figures).
3. `git diff --stat`: only the two owned files.

## Report
Format in README §4. Commit on branch `h3-docs`.
