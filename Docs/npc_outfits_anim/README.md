# NPC outfits and animation: architecture and delegation plan

Status: design, 22 Oct 2026. Builds on `claude/human-npc.md` (base human, 59-joint rig, `SkinnedModel`, `Npcs.cs`).

This folder is the single source of truth for the work. Every agent reads **this file**, **CONTRACTS.md** and **its own
brief** in `tasks/`, and nothing else unless its brief names it.

---

## 1. Goals

1. People wear clothes: shirts, jackets, trousers, boots, caps, hair. Clothes come in a few colours each and are picked per person
   from an archetype (labourer, security, local, trucker...).
2. Clothes move exactly with the body (all walk/idle/run clips), never let skin poke through, and show bullet holes and
   blood soaking into the fabric.
3. More animation: run, idle variants, a turn shuffle, cower, two deaths, a talk overlay. They're driven by a small animator
   (crossfades, phase-synced walk/run blend, one-shots, masked overlay layer) instead of the hand-written crossfade in `Npc.Update`.
4. Everything is data-driven, so new garments and clips are added without touching C#.

Out of scope for this round: ragdoll, health/death logic, AI behaviour (flee/cover), cloth simulation, skirts or long coats
(anything that spans the legs), facial animation.

## 2. Architecture

### 2.1 Clothing: garments are skinned meshes on the body's skeleton

```
 Blender (offline)                                   Game (runtime)
 ─────────────────                                   ──────────────
 human.blend ── body mesh ──► _REGION attribute ──►  human.glb ─────────► SkinnedModel "human"  (body)
      │                       (15 body regions)                              │  Skeleton (59 joints)
      │                                                                      │
      └─ clothkit: carve the garment from the body mesh                       │  SkinnedModel.Get("garments/tshirt", bindTo: body.Skeleton)
         (region mask + cut planes) → offset → smooth → hem/thickness          │     joints remapped BY NAME to the body's joints
         → copy body weights/UVs  ──► garments/<id>.glb + <id>.json ──►       ▼
                                                                         Outfit = [(garment, tint, woundStyle)] + HideRegions
                                                                              │
                                                     Npcs.Draw: body (hide covered regions) + each garment,
                                                     all with the SAME bone matrices and wound list
```

Key decisions (and why):

| Decision | Why |
| --- | --- |
| A garment is **derived from the body's own game mesh** (selected faces → offset along normals → smoothed), not re-modelled. | The weights are copied vertex for vertex, so the cloth deforms exactly like the skin under it. That is the cheapest way to get zero poke-through on every clip, present and future. |
| **Same skeleton, joints remapped by name** at load. | One bone-matrix array per person drives the body and all its garments. No extra CPU skinning, and the 60-bone uniform limit still holds. |
| **Body regions** (`_REGION` per vertex, 15 regions, a bitmask) and garments declare what they `hides`. | Hidden skin isn't drawn: no poke-through where the cloth sits close, and less overdraw. The bitmask is OR'd over the outfit. |
| **Layers with fixed offsets** (legs 6 mm < torso base 9 mm < outer 18 mm). | Any combination of garments nests without testing every pair. |
| **Bullets still hit the body only.** Wounds stay in bind space; garments draw the same wound list. | The cloth is within 2 cm of the skin, so a body hit is a cloth hit. Wounds already live in the shared bind space, so no new collision code is needed and through-and-through still works. |
| Fabric wounds look different (`WoundStyle.Fabric`): a frayed hole plus a blood soak that spreads and darkens. | The look a bullet through a shirt should have. Hard items (boot sole, cap peak) get just a hole. |
| One tint per garment (the instance colour, `_tint` materials), and new tileable texture sets (denim, canvas, leather, knit, hair). | Follows the engine's existing material conventions; one model serves every colour. |

### 2.2 Animation: clip libraries plus an animator

```
 human.glb (Walk, Idle)           ─┐
 anims/human_anims.glb (Run, ...)  ─┼─► ClipLibrary (retargets node indices BY NAME to the body skeleton)
 anims/clips.json (loop, stride,  ─┘        + ClipInfo (loop, stride, speed, tags, events)
    tags, foot events)                          │
                                                ▼
                         Animator (per person)
                           base layer:  locomotion  Idle ◄─► Walk ◄─► Run   (speed-driven, phase-synced, feet don't slide)
                                        or a full-body one-shot (ShuffleTurn, IdleLook, Cower, Death...) crossfaded over it
                           overlay:     masked one-shot/loop (Talk on spine_02 and up)
                           events:      footL / footR (for footsteps later)
                                                │ local pose (NodePose[])
                                                ▼
                         Npc: ComputeWorld → procedural flinch (unchanged) → ComputeSkin → Skin[]  → body + garments
```

* Clips are authored in Blender with the existing `humankit.anim.Poser` on `Human_Rig`, saved **separately** from the human
  (`human_anims.glb`). Clips can be added without the 3-minute human rebuild, and the clip work runs in parallel with the
  clothing work.
* The retarget is by **node name**, so the clip file only needs the same bone names as the body.

## 3. Files (who owns what)

| Area | Files | Owner |
| --- | --- | --- |
| Contract (read-only for agents) | `Docs/npc_outfits_anim/CONTRACTS.md` | Opus |
| Engine: skinned attachments, regions, clip library | `Engine/SkinnedModel.cs` | S1 (Sonnet) |
| Body regions + clothkit + first garments | `Tools/blender/humankit/regions.py`, `Tools/blender/clothkit/*`, `Tools/blender/build_clothes.py`, small hook in `build_human.py`, `Assets/Models/human.glb`, `Assets/Models/garments/{tshirt,trousers_work,boots_work}.*` | S2 (Sonnet) |
| More garments | `Tools/blender/clothkit/garments_more.py`, `Assets/Models/garments/*` (the rest) | S3 (Sonnet), wave 2 |
| Animation clips | `Tools/blender/humankit/animlib.py`, `Tools/blender/build_anims.py`, `Assets/Models/anims/human_anims.glb` | S4 (Sonnet) |
| Animator + wardrobe + NPC integration | `Engine/Animator.cs`, `Game/Wardrobe.cs`, `Game/Npcs.cs` | S5 (Sonnet) |
| Renderer: hide mask, fabric wounds, new materials | `Engine/Renderer.cs` | S6 (Sonnet) |
| Cloth texture sets | `Tools/blender/bake_textures.py`, `Tools/blender/modelkit.py` (TILE), `Engine/TextureLibrary.cs`, `Engine/Model.cs` (name→material map only), `Assets/Textures/{denim,canvas,leather,knit,hair}_*.png` | S7 (Sonnet) |
| Asset validator | `Tools/validate_skinned.py` | H1 (Haiku) |
| Data: wardrobe + clip metadata | `Assets/Models/garments/wardrobe.json`, `Assets/Models/anims/clips.json` | H2 (Haiku) |
| Docs update after integration | `README.md` (NPC section), `Docs/npc_outfits_anim/STATUS.md` | H3 (Haiku), wave 3 |
| Merge, compile on the PC, in-game verification | everything | Opus |

## 4. Delegation workflow

### Why this split

* **Opus** (orchestrator) does what needs the whole picture: the architecture, the exact contract, merging, compiling on the PC,
  looking at screenshots, and judging whether the result is right. It does not write the bulk code.
* **Sonnet** gets the subtasks that need engineering judgement inside a fixed boundary: C# systems, shader code, Blender
  geometry and animation. Each is given exact signatures, file ownership and numeric acceptance checks, so its judgement is
  spent on *how*, never on *what*.
* **Haiku** gets fully specified, mechanical work: data files transcribed from tables, a validator whose every check is listed,
  and documentation from finished reports. If a Haiku task needs a design decision, the brief is wrong, not the model.

### Waves

| Wave | Runs in parallel | Needs |
| --- | --- | --- |
| 1 | S1, S2, S4, S5, S6, S7, H1, H2 | only CONTRACTS.md (contract-first: C# agents code against the signatures, not each other's code) |
| 2 | S3 (more garments); fix-up messages to wave-1 agents from compile errors | S2's clothkit, S7's texture sets |
| 3 | H3 (docs) | integrated, verified build |

### How each agent runs

* **Isolation:** each agent runs in its own git worktree of the working copy `/home/claude/brutal` and commits to its own
  branch at the end. Opus merges the branches. File ownership (above) means merges don't conflict.
* **Tools available to agents:** bash, file tools, Python 3.13 with `bpy` 5.2.3 (headless Blender, same LTS line as the
  PC's 5.2.2), numpy. There's **no `dotnet`** in the cloud workspace: C# agents can't compile, so they must
  stick exactly to the API shapes in CONTRACTS.md and the existing code. Opus compiles on the PC and sends errors back.
* **Agents never touch the user's PC** (no remote-device or Blender MCP tools). Only Opus moves files to `E:\Brutal FPS`.
* **Every brief has the same parts:** Model · Goal · Read first · Owns (may edit) · Must not touch · Steps · Acceptance checks ·
  Report.
* **Report format** (the agent's final message, ≤ 200 words, nothing else):
  ```
  STATUS: DONE | PARTIAL | BLOCKED
  BRANCH: <branch>   COMMIT: <sha>
  FILES: <changed files>
  CHECKS: <each acceptance check: PASS/FAIL + the number measured>
  DEVIATIONS: <any departure from CONTRACTS.md, or "none">
  QUESTIONS: <for Opus, or "none">
  ```
  A deviation from the contract is only allowed when it's BLOCKED otherwise, and must be listed.

### Opus's integration checklist

1. Merge branches in order S7 → S6 → S1 → S5 → S2 → S4 → H2 → H1 (data and assets last).
2. Run `python3 Tools/validate_skinned.py` (H1). It must print `ALL PASS`.
3. Push to the PC (unique staging folder, MD5 compare), `dotnet build -c Debug` via the Blender MCP's Python. Send compile
   errors back to the owning agent with SendMessage (its context is kept), not to a fresh agent.
4. Screenshots: `BRUTALFPS_START=6.0,0,3.3,0` with `BRUTALFPS_NPCANIM` and `BRUTALFPS_OUTFIT` overrides (CONTRACTS §6) for
   each clip and each garment, plus the existing shooting script, to check fabric wounds.
5. Frame time with the 9 people dressed: 13.8–15 ms today, budget +1.5 ms.
6. Wave 3: H3 updates the docs, and Opus updates the project doc `claude/human-npc.md`.
