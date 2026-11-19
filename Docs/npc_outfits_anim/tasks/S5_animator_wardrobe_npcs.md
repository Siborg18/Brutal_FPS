# S5: Animator, Wardrobe, and NPC integration

**Model:** Sonnet · **Wave:** 1 · **Branch:** `s5-game`

## Goal
Replace the hand-written Walk/Idle crossfade in `Npc.Update` with an `Animator` (CONTRACTS §6), give every person an
`Outfit` from a `Wardrobe`, and draw body + garments. Behaviour that players see today (routes, waiting for the player,
flinch, facing the shooter, wounds, blood, contact shadows) must be unchanged.

## Important: you code against APIs that other agents are writing at the same time
`SkinnedModel.Get(name, bindTo)`, `ClipLibrary`, `ClipInfo`, `AnimationClip.Info/Speed` (S1) and
`Renderer.DrawSkinned(..., hideRegions, woundStyle)`, `WoundStyle` (S6) **don't exist yet in your worktree**. Use them
exactly as CONTRACTS §4 and §5 declare them. Don't stub or re-implement them. There's no dotnet here, so be meticulous: Opus
compiles the merged result and sends you the errors.

## Read first
1. `Docs/npc_outfits_anim/CONTRACTS.md` §2 (slots, sidecar), §3, §4, §5, §6, §7.
2. `Game/Npcs.cs` (all), `Engine/SkinnedModel.cs` (`NodePose`, `AnimationClip.Sample`, `Skeleton`).
3. Don't read `Engine/Renderer.cs` (124 KB); the contract gives you everything you need from it.

## Owns
`Engine/Animator.cs` (new: `Animator`, `BoneMask`), `Game/Wardrobe.cs` (new: `Wardrobe`, `Outfit`, `OutfitItem`),
`Game/Npcs.cs`.

## Must not touch
Anything else (`Program.cs` included: `Npcs`' constructor and public methods keep their signatures).

## Design requirements
* **Animator.** Base layer: locomotion or a full-body one-shot. Locomotion samples Idle/Walk/Run at their own times and blends them by
  speed with smoothstep weights. Walk↔Run share one normalised phase, and the phase advances at `groundSpeed / blendedStride`
  cycles per second (this is what stops the feet sliding; it's the generalisation of the current `_walkTime` logic).
  Full-body one-shots crossfade in and out over locomotion with their own clock. The overlay is applied per node with `BoneMask` weights.
  Blending: `NodePose.Blend` (slerp). Pre-allocate every scratch `NodePose[]` in the constructor: **no allocations per frame**.
  Events: when a clip's time crosses an event time (with wraparound for loops), add its name.
* **Npc.**
  * Takes an `Animator` and an `Outfit`. It computes its actual ground speed per frame and calls `SetLocomotion(speed)`.
    The current speed logic (slowing in turns, waiting for the player) stays as it is.
  * Stopped at a waypoint for more than 4 s and no one-shot playing: every 5 s, a 25% chance (the per-NPC seeded `Random`) of
    `PlayFullBody("IdleLook" or "IdleShift")`.
  * Stopped and turning by more than 0.6 rad (to a waypoint `Face` or to the shooter): `PlayFullBody("ShuffleTurn")` once per turn.
  * The flinch spring and everything after `ComputeWorld` are unchanged.
  * Missing clips (Run, the variants): the feature quietly does nothing.
* **Wardrobe** (CONTRACTS §6, §7): reads `wardrobe.json` + the sidecars; `For(name, seed)` is deterministic; picks per slot by
  weight, then a colour from the palette. Respect the slot rule (at most one per slot). `HideRegions` = OR of `BodyRegions.Mask(sidecar.hides)`.
  `WoundStyle` from the sidecar's `wound`.
* **Drawing** (`Npcs.Draw`): body first with `hideRegions: npc.Outfit.HideRegions`, then each item with its tint and wound style,
  all at the same LOD and with the same `npc.Skin` and wound arrays.
* **Env vars** (CONTRACTS §6): `BRUTALFPS_OUTFIT`, `BRUTALFPS_NPCANIM`, `BRUTALFPS_NOCLOTHES`. Parse them once in the `Npcs`
  constructor. Bad entries are a `[WARN]`.
* Logging: one line at startup: people, garments loaded, clips available.
* Missing `wardrobe.json` or any garment: people are drawn unclothed, the same as today, with a `[WARN]`.

## Acceptance checks
1. **Contract conformance:** list every type and member you call from S1/S6, each with the CONTRACTS line it comes from. There must be no
   calls outside the contract.
2. **No per-frame allocations:** grep `new ` inside `Update`, `Draw` and the `Animator` methods called per frame; report the result (expected:
   none except the existing `_shadows` usage).
3. **Phase-sync logic test:** port the locomotion phase/rate math to a small Python script (`Tools/tests/locomotion_sim.py`),
   simulate a speed ramp 0 → 3.68 → 1.16 → 0 m/s over 6 s at 60 Hz with the contract's strides and durations, and report the
   max foot-slide error proxy: |groundSpeed − blendedStride × phaseRate|. It must be < 1e-3 m/s whenever speed > 0.05.
4. Behaviour diff: write a list of every behaviour in the old `Npc.Update` and where it lives now.
5. `git diff --stat` shows only your owned files (plus `Tools/tests/`).

## Report
Format in README §4. Commit on branch `s5-game`.
