# Launch sheet (for Opus)

Each agent is started with the Agent tool: `isolation: "worktree"`, `model` as below, and this prompt with `<ID>` and `<FILE>`
filled in. The prompt is deliberately short: everything the agent needs is in the files it names.

```
You are agent <ID> on the Brutal FPS project (a C# / OpenGL FPS; assets built with Blender Python).
Your working copy is your current git worktree. Before doing anything else, read these three files in full:
  1. Docs/npc_outfits_anim/README.md       (section 4 "How each agent runs" applies to you)
  2. Docs/npc_outfits_anim/CONTRACTS.md    (fixed interfaces: never change them)
  3. Docs/npc_outfits_anim/tasks/<FILE>    (your brief)
Then do exactly what your brief says, within its "Owns" list. Run every acceptance check and record the measured numbers.
Create the branch named in your brief, commit your work to it, and end with ONLY the report block from README section 4.
Do not use any remote-device, Blender MCP, or browser tools. Headless Blender is `python3.13` with `import bpy`.
GLSL validator: /opt/tools/glslangValidator. There is no dotnet here.
```

| ID | Model | Brief | Wave | Notes |
| --- | --- | --- | --- | --- |
| S1 | sonnet | S1_engine_skinned_attachments.md | 1 | |
| S2 | sonnet | S2_regions_clothkit.md | 1 | longest-running (Cycles previews) |
| S4 | sonnet | S4_animation_clips.md | 1 | |
| S5 | sonnet | S5_animator_wardrobe_npcs.md | 1 | |
| S6 | sonnet | S6_renderer_fabric_wounds.md | 1 | |
| S7 | sonnet | S7_cloth_texture_sets.md | 1 | |
| H1 | haiku | H1_asset_validator.md | 1 | |
| H2 | haiku | H2_data_files.md | 1 | |
| S3 | sonnet | S3_more_garments.md | 2 | paste S2's report into the prompt; branch from the merged S2+S7 |
| H3 | haiku | H3_docs_update.md | 3 | paste all reports + integration notes into the prompt |

## Handling results
* **DONE:** merge in the order in README §4 and run the validator.
* **PARTIAL / a check FAILED:** SendMessage the same agent (its context is kept) with the failing numbers. Don't start a new agent.
* **BLOCKED:** read the reason. If the contract needs to change, Opus changes CONTRACTS.md, commits, and tells every affected agent.
* **Compile errors** after the merge: group them by owning file and SendMessage the owner with the exact compiler lines.
* Never let one agent fix another's file. Ownership is what keeps the merges conflict-free.
