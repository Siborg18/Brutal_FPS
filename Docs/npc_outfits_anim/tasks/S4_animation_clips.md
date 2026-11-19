# S4: New animation clips

**Model:** Sonnet · **Wave:** 1 · **Branch:** `s4-anims`

## Goal
Author the 8 new clips in CONTRACTS §3 (Run, IdleLook, IdleShift, ShuffleTurn, Cower, DeathFwd, DeathBack, Talk) on
`Human_Rig`, with the same `Poser` toolkit that made Walk and Idle, and export them to
`Assets/Models/anims/human_anims.glb`.

## Environment
Headless Blender: `python3.13 script.py` with `import bpy` (bpy 5.2.3). Render previews with Cycles CPU (16–32 samples). The
script must also run on the PC: `blender -b --factory-startup -P Tools/blender/build_anims.py -- [clip ...]`.

## Read first
1. `Docs/npc_outfits_anim/CONTRACTS.md` §3 (the clip table and its rules are your spec).
2. `Tools/blender/humankit/anim.py` (the whole file: `Poser`, `walk`, `idle`, `_cyclic`, `make_actions`). Your clips must look like
   the same author made them.
3. `Tools/blender/humankit/rig.py`, only the parts that name the controls and IK switches (grep `ik_leg`, `foot_ik`, `hand_ik`,
   `custom properties`).

## Owns
`Tools/blender/humankit/animlib.py` (new: one function per clip), `Tools/blender/build_anims.py` (new),
`Assets/human_anims.blend` (new source file), `Assets/Models/anims/human_anims.glb`,
`Tools/previews/models/anim_*.png`, `Tools/tests/`.

## Must not touch
`Assets/human.blend` (open it, never save over it), `humankit/anim.py`, `human.glb`, C#.

## Steps
1. `build_anims.py`: open `Assets/human.blend`, import `humankit.anim` and `humankit.animlib`, build the requested clips (default: all
   8), mute them as NLA tracks like `make_actions` does, **remove the Walk and Idle actions and tracks from the session** so
   they aren't exported, save as `Assets/human_anims.blend`, then export `human_anims.glb` with the rig only
   (`use_selection`, deform bones, `export_animation_mode='ACTIONS'`, `export_force_sampling=True`,
   `export_anim_slide_to_zero=True`, `export_yup=True`, `export_rest_position_armature=True`). If the exporter writes nothing for an
   armature without a mesh, include the `Human` mesh; the game ignores it.
2. Start and end poses: every clip that "starts and ends on Idle frame 1" must match Idle's frame-1 pose. Get it by evaluating the
   existing Idle action at frame 1, not by re-deriving it.
3. Run: IK legs (`ik_leg` = 1, the default). Planted foot moves back at exactly stride / duration (2.70 m / 0.7333 s = 3.68 m/s), a
   flight phase between the stances (stance ≈ 35% of the cycle), knee lift, forward lean, arms bent ~80° pumping opposite the legs.
4. Death clips: animate `pelvis`/`root` translation and rotation as needed; IK feet may be switched to FK if that's easier
   (key the `ik_leg` property). The final pose must rest on the ground.
5. Talk: key only `spine_02` and above (spine_02, spine_03, neck, head, clavicles, arms, hands, fingers). The game masks it.

## Acceptance checks (write them as `Tools/tests/check_anims.py`, run them, and report the numbers)
1. The glb contains exactly the 8 animations, by name; every channel targets a node whose name exists in `human.glb`'s nodes.
2. Duration of each (last key time) = frames / 30 ± 1 frame.
3. Loopers (Run, Cower, Talk): first frame = last frame pose (max bone rotation diff ≤ 0.5°).
4. One-shots that start and end on Idle (IdleLook, IdleShift, ShuffleTurn): first and last frames are within 2° of Idle frame 1 on every bone.
5. Run foot sliding: during each foot's stance, the world velocity of its `ball` bone in −Y is within ±5% of 3.68 m/s,
   and its height is ≤ 0.01 m above the lowest stance height. Report the measured mean speed.
6. Ground: with the `Human` mesh deformed, the lowest vertex z over all frames of all clips is ≥ −0.01 m; for the Death clips' last
   frame, the pelvis z is < 0.25 m.
7. ShuffleTurn: the net rotation of `pelvis` from first to last frame is ≤ 2°.
8. Previews: for each clip, a contact sheet `Tools/previews/models/anim_<Clip>.png` of 6 evenly spaced frames, side view, with
   the body mesh. Read them yourself and say in one line per clip whether the motion reads correctly.

## Report
Format in README §4, plus the per-check numbers. Commit on branch `s4-anims`.
