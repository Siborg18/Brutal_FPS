# BRUTAL FPS

A small first-person shooter prototype built on the [BRUTAL framework](https://github.com/ahwootech/BRUTAL).
BRUTAL has no engine, scene graph or renderer, so this project brings its own small stack:
**Brutal.Glfw** for the window and input, **Brutal.OpenGl** for rendering (OpenGL 4.1 core), **Brutal.Numerics** for the maths,
**Brutal.Stb** for loading textures and HDR skies, and **Brutal.Gltf** for loading the models made in Blender.
Physics is [Bullet](https://github.com/bulletphysics/bullet3) (BRUTAL's own physics package is Jolt, not Bullet), through the
**BulletSharp.x64** wrapper: see [Physics and bullet damage](#physics-and-bullet-damage).

## Run it

Prerequisites: the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a GPU with OpenGL 4.1+ drivers. It runs on Windows x64 and Linux x64
(the Bullet package only carries Windows' native library: on Linux the game runs without physics - shots use the level's
boxes, and there are no casings, magazines or debris).

### Visual Studio

1. Use **Visual Studio 2026** (VS 2022 can't build .NET 10 projects) with the **.NET desktop development** workload installed.
2. Open `BrutalFps.sln`.
3. Press **F5** to run with the debugger, or **Ctrl+F5** to run without it.

Visual Studio restores the NuGet packages on the first build.

### Command line

```sh
dotnet run
```

The first run restores the NuGet packages (`Ahwoo.Brutal.*` and `BulletSharp.x64`). A console window opens next to the game and logs every shot.


### Stress-test build

In Visual Studio, pick the **Stress test (Rio)** launch profile (next to *Firing range* and *Prison intro*): it sets `BRUTALFPS_STRESS=1` and starts you in the Arctic container, a few steps from the slate portal. Or `dotnet build -c StressTest` builds an optimised game with it always on (`STRESS_TEST` defined), into `bin/StressTest`. With it on, the moment you step into Rio, 100 armed hostiles appear along the favela's alleys (`Game/NpcsStressTest.cs`), and one second later (real time) every one of them fires in the same frame (`ArmedNpc.StressVolley`); after that they fight on as usual. You take no harm while it's on (as with `BRUTALFPS_GOD=1`), so you can stay and watch the frame rate. It happens again after a respawn, next time you come into Rio. The log marks it with `[STRESS]`. Once a second in Rio it also logs who's up, down and in a ragdoll, how many are in each AI tier, the physics bodies (awake, pooled, reused) and debris, and, with `BRUTALFPS_PERF=1`, the NPCs' time per second in senses, behaviour, animation and look/arms, and how many poses and dormant updates the tiers saved. A repeatable run: `BRUTALFPS_PERF=1 BRUTALFPS_NOVSYNC=1 BRUTALFPS_START=rio BRUTALFPS_SCRIPT="15::0"` with the StressTest build. Without the flag or that build, none of it happens.
### NPC testing build

In Visual Studio, pick the **NPC testing** launch profile (it sets `BRUTALFPS_NPCTEST=1`), or `dotnet build -c NpcTest` builds the game with it always on (`NPC_TEST` defined), into `bin/NpcTest`. It skips the day/night menu (each level keeps the time of day last picked there) and your flashbangs, frags and smoke never run out. You start outside the factory's outbuilding, behind the blue Transit van parked across its north doorway, out of sight of everyone inside; inside stand one of each kind of NPC: a civilian, a woman, a police officer, a prison officer (baton), a gunman (pistol), a rifleman (AK-47) and a commando (AR-15, armour and helmet). Nobody else is in the world (`Game/NpcsTestArea.cs`). Everyone takes hits as usual (wounds, bleeding, flinches, the fight), but nobody dies: a hit (or bleeding) that would kill puts them back to full health instead, logged as `[NPCTEST] ... would have died` (for you, `[PLAYER] would have died`, with a red subtitle). The police and hostiles leave each other alone to start with.

**Esc > NPC CONFIG** (`Game/NpcTestMenu.cs`): on the left the seven people (click a row's [X] to spawn or despawn them), with their gun, health, state and how often they've been saved, and SPAWN ALL / DESPAWN ALL / RESET ALL (everyone back at their spots, unhurt and calm, without moving you) / CALM POLICE (they forget you shot one) / NPC VS NPC (off: they only fight you; on: police and hostiles fight each other); on the right, the one chosen: SPAWNED, COUNT (1 to 9 of them, in a grid round their spot; the list then shows how many, the lowest health among them and their saves between them), WEAPON (unarmed, baton or any gun an NPC can carry), and their personality: AGGRESSION (defensive, cautious, aggressive), PLAN (auto, push, cover, hold back: only for the aggressive), TEMPERAMENT (bolter or freezer: whether the unarmed run or cower), LOST SIGHT (auto, search, hold), ACCURACY (0.1 to 1.0) and ARMOUR. Every change puts that person back at their spot as now set up. Keys: Up/Down move, Left/Right change (or switch side), Tab switches side, Esc goes back; or click (right click or the left half of a value steps back, the wheel steps either way). The + key does the same as RESET ALL: it resets only the NPCs (back at their spots as configured, unhurt and calm) and leaves you where you are. (You still respawn behind the van if you fall out of the world.) `BRUTALFPS_PAUSE=npc` starts in the menu (for screenshots), and `BRUTALFPS_NPCCOUNT=n` starts with n of every kind.

## Raid loadout

Before a factory raid or an Antwerp raid (not the prison intro) comes the **RAID LOADOUT** screen (`Game/RaidLoadout.cs`), drawn like the inventory. It comes up as soon as the way in is chosen, and the raid loads behind it: Program.cs calls `LoadingPump()` between the steps of loading, which gives the screen a frame; the guns' pictures appear once their models have loaded. Five sections along the top (Tab, or click): **ARMOUR** (none; light, a chest plate; medium, heavier chest plates; heavy, full body armour: each takes more of a hit and slows you more), **HEAD** (helmet, goggles, both or neither: without goggles, T does nothing), **PRIMARY** (any gun but the pistol: a submachine gun as the primary leaves only the pistol for the secondary), **SECONDARY** (the pistol or a submachine gun) and **GRENADES** (six shared among flashbangs, frags and smoke: click or Enter adds one, right-click or Backspace takes one off). Space or START RAID goes; if it's still loading, the raid starts as soon as it's loaded. The primary goes on 1 and the secondary on 2. **Weight:** everything has a weight (`RaidKit`: armour 4 / 8 / 16 kg, helmet 1.5, goggles 0.6, each grenade 0.45, each gun its own, the M249 21 kg with its belts), shown on a bar over START RAID, and the kit's weight sets your pace on the raid: 112% of the usual for the lightest kit (a submachine gun and the pistol, no armour: 3 kg), a point less for every kg more, down to 72%. The gun in hand only takes half its usual toll on top. Footsteps carry 0.8 to 1.5 times as far with weight. Heavy armour with the M249 is the heaviest kit and is *overloaded*: Shift doesn't sprint, and footsteps carry twice as far. The kit is saved in `raid_loadout.txt` next to the exe, and a respawn brings the same grenades again. Where a bullet hits is judged by its height on the body (`PlayerController.ArmouredDamage`, logged as `[ARMOUR]`). Test scripts and screenshot runs skip the screen unless `BRUTALFPS_LOADOUT` says otherwise: `=go` takes the saved kit straight away, `=open` shows the screen, `=shot:file.png` saves a picture of it once loaded and quits (`BRUTALFPS_LOADOUT_SECTION=0` to `4` opens on that section), and `=skip` leaves it out.

## Day and night

The game opens on a menu (`Game/StartMenu.cs`) that sets **day or night for each level**: the factory, container yard, outback (with its cellar), bunker, Arctic, container ship, jungle lab and police station. Click DAY or NIGHT, or use Up/Down and Left/Right (Enter or Space toggles a level); START loads the game and Esc or QUIT leaves. The first time it starts as the game has always been, the yard at night and the rest by day; after that it comes up with what you last pressed START on (saved in `timeofday.txt` next to the exe: delete it to go back to the defaults; QUIT saves nothing). The prison has no night version, nor does Rio (always a bright day). The firing range isn't in the menu because it stands on the outback's plain, so it has the outback's sky.

The choices are made before anything is loaded (`Game/TimeOfDay.cs`), since the skies, shadow maps and light lists are all set up once at startup, and they can't change during a run. Each level has its own, so a portal can look out of the night into the day. BRUTALFPS_TIME skips the menu: `night` or `day` for every level, or `outback=night,yard=day` for just those (the rest keep the defaults). Test scripts and screenshot runs skip it too, and none of these read or change the saved choices. `BRUTALFPS_START=intro` also skips it, since it's always day in the prison: the levels beyond keep the saved choices, which it reads but doesn't change. `BRUTALFPS_MENUSHOT=file.png` saves the menu as a picture and quits.

How each level does its night:

- **Factory, outback, ship, jungle**: the shader's `uNightAreas` (`Renderer.SetNightAreas`, a bit per area) grades the area's own sky down to dim blue (`skyRadiance`) and turns its sun into the moon (`sunColorOf`), keeping its direction and shadow map. Tune them with `NIGHT_SKY_LEVEL` and `MOON_LEVEL`. The visible sky gets stars where the day's sky is blue between the clouds (`nightSkyView`). The warehouse's hall keeps its lamps, with far less daylight at its windows. Round the ship, the sky, sea and haze are darker still, almost black (`SHIP_NIGHT_OUTSIDE`), while its decks keep their light.
- **Lamps that only matter in the dark** (`Level.NightLights`): the roadhouse's light pole, the jungle camp's bare bulb and two working lights over the ship's deck. They have no shadow maps.
- **Container yard**: the `yard_night` sky and floodlights, or by day the `yard` overcast with its sun.
- **Arctic**: a darker whiteout (`Level.WhiteoutNightColor`).
- **Bunker, police station**: lit from inside, so only their daylight changes (the vent and its light, the windows: `TimeOfDay.Daylight`). A shot window shows dark cracks while it's bright and white ones once it's dark outside (`Impacts`, by the pane's tint).

## Controls

| Input | Action |
| --- | --- |
| Mouse | Look |
| W A S D | Move |
| Shift | Sprint |
| Space | Vault: there is no jumping. Facing something low (ankle to chest height, up to 1.25 m) with room over it, you go over it, or up onto it where the level allows that (`Level.NoClimbAreas`). Prone: get up. On a ladder: let go |
| Ctrl (hold) | Crouch |
| C | Prone: lie flat (a crawl at about 1.1 m/s, quieter steps). A small, low target: NPCs aim at your head and shoulders, with a wider cone, and in testing hit about 70% less. The gun is braced: half the spread, about two thirds less aim wander and half the recoil climb. C again (or Space / Shift) gets up, Ctrl comes up to a crouch; it waits for headroom |
| Left mouse (click) | Fire (semi-automatic: one shot per click) |
| Right mouse (hold) | Aim: the gun comes up to your eye, sights down the middle of the view, and the view narrows a little (the AR-15's scope: the view goes through it, 4x to start with, with a duplex reticle, and turns slower; the G3's ACOG: a magnified circle in the middle of the view, 4x to start with, ringed by the black eyepiece, the world seen as it is round it, with a red chevron over a bullet-drop ladder) |
| R | Reload |
| G (hold, let go) | Flashbang: holding G plots the throw (a dashed arc to a ring where it will go off); letting go throws it. You carry 3; a respawn gives you 3 more |
| Q / X (hold, let go) | Frag grenade / smoke grenade, thrown the same way. You carry 3 frags and 2 smoke |
| E | Use: take up the M249 at the jungle's gun nest, or put it back on its tripod; open or shut the factory's sliding door, or a room's door at the outback motel (careful: two gunmen wait behind every one, and once you're within 15 m of any door the whole motel throws its doors open and opens fire); a keypad by the police vault's airlock doors (then 0-9 for the code, 1234; Backspace rubs out a digit); the PC in the police station's tech room or in its vault (stand at its desk) |
| Up / Down, Enter | At a police PC's terminal: choose a portal and connect that PC's portal (the HQ's navy one, or the vault's gold one) to it |
| 1-9, 0 | The guns on those numbers (the gun in hand is lowered, then the other comes up). To start with: pistol / AK-47 / shotgun / UMP40 / scoped AR-15 / G3 with ACOG, and the M249 on 7 once you have taken it off its tripod in the jungle. Change them at a gun locker |
| E at a gun locker | The police armoury's bench, or the green cabinet at the firing range: every gun in the game, including the nine British ones (L85A2, L85A3, L403A1, L143A2, L119A1, L129A1, L135A1, L92A1, L80A1). Up / Down or the wheel picks, a number key puts the gun on that number (replacing what was there), Backspace takes it off, Enter takes it in your hands, E or Esc shuts. Kept in `loadout.txt` next to the exe |
| I | Inventory, anywhere: what you're carrying, each on a card with its picture (the guns on your numbers, with the rounds loaded, and your flashbangs). The arrow keys or the wheel pick, Enter takes the gun in your hands, I or Esc shuts. The world carries on behind it |
| Mouse wheel | Cycle through the guns on your numbers, going round: towards you for the next, away for the previous. Using a scope, it only sets the scope's power, away from you for more: 2x, 4x, 8x or 16x. It never changes guns while you're aiming through a scope, even one with a single power |
| Middle mouse | Looking through a scope: night vision, then thermal, then off (each gun's scope stays as it's set). Only the scope's picture changes: on an ACOG, the world seen round the eyepiece stays as it is |
| T (hold, let go) | Goggles: a wheel comes up (the mouse moves its pointer instead of the view). Point at NONE, NIGHT VISION or THERMAL and it lights up; letting go of T puts them on. Letting go over the middle, or over what you're already wearing, changes nothing. They show the whole view (a scope with its own vision on shows that instead). Thermal can't see through a portal's liquid: it shows as a cool surface |
| Alt+Enter (or Alt+Shift+Enter) | Fullscreen / windowed. The game starts in a 1600x900 window; `BRUTALFPS_FULLSCREEN=1` starts it fullscreen |
| + | Respawn (the keypad's +, or the key that types + / = on the main keyboard) |
| Esc | Pause menu: Resume or Quit (click, or Up/Down and Enter; Esc again resumes) |
| Enter | Skip the prison intro (while it plays) |

**Level editor** (`Game/LevelEditor.cs`, `Game/LevelEditing.cs`): its own build, `BrutalFpsEditor.exe`, from
`Editor/BrutalFpsEditor.csproj` (in the solution beside the game; `dotnet run --project Editor`). It compiles the game's
source with `LEVEL_EDITOR` defined and opens straight into the editor, at the time of day the game was last started with
(`BRUTALFPS_TIME` and `BRUTALFPS_START=range` / `=raid` pick otherwise, as for the game). The game's build has no editor in it;
the settings, packages and assets both builds share are in `BrutalFps.props`. The world holds still
while you fly round it and rearrange any level: move, turn, copy and delete the boxes and models the levels are built from, and
drag more in from the asset list on the left (the Blender models in `Assets/Models`, and boxes in the levels' materials). The bar
along the top flies you to each level.

| Input | Action |
|---|---|
| Right mouse (hold) + mouse | Look round |
| WASD, Q / E (or Space), Shift | Fly; down / up; faster |
| Mouse wheel | Fly forward / back (scrolls the asset list over it; lifts what you're dragging while you drag) |
| Left click, drag | Pick something and move it across the floor. A model brings its invisible collision boxes with it |
| Ctrl+Shift+drag | Drag off a copy of what you click (or of everything picked, if it is), as one step to undo; let go without moving and there is no copy |
| Shift+click / Alt+click | Add to (or take from) what's picked / reach the ground and long walls (over 40 m) |
| Drag from the asset list | Drop a new one into the level, standing on whatever is under the pointer (let go over the list to cancel) |
| R / Shift+R | Turn a quarter turn / 15 degrees (models only: boxes stay lined up with the world) |
| Del, Ctrl+D, F | Delete, copy (placed alongside), fly to it |
| N | Name what's picked: type, then Enter to keep it (an empty name clears it) or Esc to cancel. The name shows wherever the editor says what something is, and is saved with the layout |
| P | Paint what's picked (see **Paint tool** below) |
| PgUp / PgDn, arrows | Raise / lower, nudge along the world's axes nearest the view |
| G | 25 cm grid on / off |
| Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) | Undo / redo |
| SAVE CHANGES / REVERT CHANGES | Save the layout, which the game loads from its next start / put back everything done since the last save. Closing the window with changes not saved warns once; close again to quit without them |

**Paint tool** (`Game/LevelEditorPaint.cs`): gives a box a texture set and a colour per face, a Blender model one per part (its
Blender material slots), and a code-built mesh one for the whole of it. Only what's being painted is drawn, and the view turns round it.
**P** with something picked paints *that one only* (a whole piece, a desk, together). A **click on a model in the asset list**
(let go without dragging it out) paints *every copy* of that model, in every level, shown on the copy nearest you; boxes in the
list are painted one at a time instead. In the tool, down the left: COLOUR (a swatch, or any colour on its R, G and B sliders) and TEXTURE (all of `Material` but Sky,
Ocean, Custom and Leaves). Each can be KEEP (left as it is: paint just the colour, or just the texture) or AS BUILT (put back).
Click a face to paint it with both; Shift+click paints all of it, Alt+click picks up a face's colour and texture. A colour is
drawn as it is (a model's own, not the colour the level draws it in); pure white shows a texture's own colours. Right mouse turns round it, the wheel goes closer and further; Esc, P or DONE goes back to the level. Each paint
is a step to undo, and SAVE CHANGES keeps it: an item's as `textures` (a `Material` per face or part, -1 as built) and `colors` (red, green, blue each, null as built) beside its
move in `level_edits.json`, every copy of a model's under `models` at the top of the file. A copy of one model painted on its own
wears its own paint over the model's. A model's own baked texture (`tex_*`) painted over is drawn with the level's tileable sets
through its UVs. Merged copies (a forest drawn as one mesh) take a model's paint from the next start. `BRUTALFPS_EDITOR=paint[:pitch]`
paints what's straight ahead (`=paintmodel:name` every copy of a model), checks undo puts it back (`[EDITOR] PAINT`), and
`+keep` saves it.

Saved changes go to `Assets/Levels/level_edits.json` in the project folder (found by walking up from the exe; else next to the exe,
or wherever `BRUTALFPS_EDITS` says), one section per world (`full`, a factory raid's `raid`, the firing range's `range`). Each item
is named by where the code built it (`B|centre|size|material`, `P|model|centre|size`), so editing the level's code elsewhere doesn't
lose the changes; ones whose item the code no longer builds are reported in the log. They're put back as the level loads, before
the shadows, Bullet physics and the NPCs' map are made. On Save the baked shadows are drawn again at once. The game (run from its build in the project) reads the same file as it loads; building it also copies the file next to its exe, for a copy run away from the project. Bullets and debris meet
the new layout from the next run. A ladder's climbing space goes with its prop when it's moved, turned, copied or deleted
(`Level.MoveLadders`); if it lands somewhere it can't be climbed (something solid in the way, or no floor to step off onto at
the top) the status line and the log say so, and the log warns again on load. Not editable: merged instances (the forests, the
quay's container stacks), doors, lights, portals. `BRUTALFPS_NOEDITS=1` loads the levels as the code builds them. In the editor's build, `BRUTALFPS_EDITOR`
`=shot:file.png` screenshots it and quits, `=test` runs its moves on what's straight ahead and checks undo puts it all back,
`=pick` picks what's straight ahead as a click would, and `=audit` checks every ladder (`[EDITOR] LADDER`) and logs every item over 3 m whose triangles fall into separate
clusters with gaps between them (`[EDITOR] AUDIT`: several things still merged into one mesh), then every wall still built as two
boxes (`[EDITOR] COAT`, `HALVES`). `test` and `pick` take a pitch to look along (`=pick:-17`), and any of them `+shot:file.png`.

Clicking picks by what's drawn (a model's or mesh's triangles), not by the box round it, and the outline is round the whole object.
The code-built furniture is merged into one mesh per material, so a room of desks is a handful of draws. The editor still gets each
thing in them on its own, as a *piece*: in the police station (and wherever `Level.Piece("name")` wraps something: filing cabinets,
chairs, extinguishers, guns) by name (a desk, a chair, a monitor, a smoke alarm); everywhere else, once the level is built
(`Game/LevelSplit.cs`), each merged mesh is split into the things in it (triangles that touch), and a thing in another material
lying inside a small one (a handle in a cabinet) goes with it. A model placed by the same transform as another (a van's number
plates) goes with it too. Pieces are still drawn merged with the rest of their material until one is moved; then that one is drawn
by itself, so this costs no draws. Left merged: foliage and trees, ground decals, and scenery over 300 m (the harbour's container
stacks, Rio's far hillsides). `BRUTALFPS_PROFILE=nopieces` and `=nosplit` turn them off, to compare.

A wall between two rooms is one box painted differently on each face (`LevelBox.Paint`, `PaintFaces`, `PaintMaterial`), not two
half-thickness boxes or a thin coat laid over its face (which z-fights from afar). The police station builds its walls that way;
everywhere else `Game/LevelPaint.cs` turns them into painted faces once the level is built: a coat of paint on a wall (a band, a
kick band, a whitewashed panel) becomes that face's paint, the wall cut at the coat's edges (its pieces still move as one in the
editor), and two halves side by side become one box. Only walls: coats on floors (paths, rugs) and small fittings stay as they
are. A box holds one paint, so a wall already painted one colour on one side keeps a coat of another on its other side.
`BRUTALFPS_PROFILE=nopaint` leaves them as built.

The window title is the HUD. It shows the ammo, flashbangs left, shots fired, portal splashes and FPS. Bottom right, a pip for every round in
the magazine and a bigger one for the chamber; they turn orange when you're low, flash red when the gun is empty (press R),
and a bar fills while you reload.

**Ladders**: walk into one, or drop onto it from above, to grab it. W climbs up and S climbs down; with your view tipped well down they swap, so you can walk forwards off a ledge and keep going down. Keep climbing at the top and you step off onto the floor; at the bottom, climb down onto the floor and walk away.

## What's in it

- **Level**: an old, run-down warehouse built from boxes: 10 m high stained concrete walls with piers, tall grimy windows, pipe runs, steel roof trusses, hanging lamps, a catwalk with a ladder at the far end beside a bright open doorway, wooden crates, pallets, rusty bins, rubble, structural columns, and a concrete loading dock you reach by jumping up the steps.
- **The factory outside** (`Game/LevelFactory.cs`): the warehouse is the main hall of an old, half-derelict factory standing in rough grass under the outback's blue sky and sun (the shader's area 8, "the factory grounds": anything at x < 100 outside the hall's walls or above its roof, with its own sun shadow map on texture unit 16). The hall has a red-brick plinth with pilasters, a concrete frame, rusty ochre corrugated cladding with two ribbon windows of steel-framed glass (a third of the panes broken out, the odd sheet hanging loose), a concrete eaves band and a glazed lantern along the roof. In its north end is a big corrugated **sliding door** on a top track: stand near it (inside or out) and press E to roll it open or shut (3 s, easing in and out; it won't shut on you in the doorway; `BRUTALFPS_DOOR=open` starts it open). The catwalk inside was raised to 5.6 m to clear it. To the north-east, a ruined two-storey brick boiler house joined to the hall by a concrete gallery carrying pipes, and a 60 m round brick chimney (iron bands, corbelled cap, soot towards the top, rungs and a lightning conductor); to the west, a long limewashed brick outbuilding with a steel double door and a raised clerestory; to the south-east, a second, shut-up hall. Concrete aprons and a road to a chained gate, concrete poles carrying sagging wires, bushes, trees and rubble, all inside a precast concrete perimeter wall. Someone wanders about outside. Thirteen Ford Transit vans are parked round the site (`BuildVans`, `AddVan`): six in the hall (north of the loading dock with its nose to the hall's middle, beyond the door's east side, two side by side along the west wall, two along the east wall), seven outside (along the hall's north wall and east side, by its north-east corner, backed up to the second hall's roller door and north of that hall, by the outbuilding's double door, on the road to the gate). Each has its own number plates, Albanian, Romanian, German or Dutch. The catwalk's ladder is climbable (`BuildNorthEnd`): it comes up through a gap in the railing, under a safety hoop that stops you walking out over the hall, so from the deck you walk into the hoop, drop onto the rungs and climb down. **The office** (`Game/LevelFactoryOffice.cs`) stands across the hall's south end, the far end from the sliding door: one long limewashed room (18 x 8 x 3 m) with a door standing open, steel-framed windows looking back up the hall, each pane either clear glass or gone (`OfficeWindow`; the glass is `ShadeMode.Glass`, blended over the scene by `Level.DrawGlass`, and it stops shots), a suspended ceiling with lit panels, carpet, two desks, filing cabinets and plastic buckets all round the walls. The shader lights everything inside its box from the ceiling instead of the hall's lamps (`indoorLight`; the box's numbers must match `Level.FactoryOffice`). New procedural materials: `Brick` (38), `Whitewash` (39), `Grass` (40).
- **Container yard**: the orange portal is in the closed end of a 40 ft shipping container standing in an outdoor yard (a separate area at x = 200). You come out inside the dim container, walk out through its doors (one swung open, one shut) into a small yard walled in by stacked, weathered containers, at night: four 24 m high masts outside the stacks flood it with deep orange sodium light from cross-arms of floodlights (a key light from the north-west and fills from the other corners, so the yard's floor is well lit but the stacks still throw shadows across it; the floods' visors keep their light off the tops of the stacks, which lie in shade but for a few dim patches, and off the quay along the water, which is lit only in pools under its own two masts, dark between them), under a near-black sky. The lamps glow in the hazy air with halos round them, the gravel is wet after rain and its puddles mirror the lamps, a yellow gantry crane lit by its own floodlights towers over the north stacks, and taller stacks and more lit masts further out across the port make the skyline. Inside the north and south rows (two high) runs a second row, two high, from the west block's door end to x 18.3, with a grey container on top of each, so the sides step up from the yard; between each inner row's east end and the east block is an alcove off the yard. Oil drums, pallets and a lone 20 ft container for cover. The east side is closed by three columns of 20 ft containers (`container_20`), end to end between the north and south rows and side by side up to the east edge's stacks, two high on the yard's side and three against the wall, so no pit is left behind it.
- **The outback**: in the yard's south-east pocket (between the lone green container and the east stack), a purple portal on the container wall leads to the Australian outback (a separate area at x = 3000). You step out of the back wall of a small corrugated-tin shack into a yard enclosed by a tall corrugated-iron fence you can't see over (with a rainwater tank in the corner), and leave through its open corrugated gate. Round the front, beside the shack's drum letterbox (with a deep red 4WD parked on the verge just up the road), a dead-straight dirt road runs to the horizon both ways. About 100 m north up the road is an old roadhouse: a weathered timber-and-tin store with a pitched tin roof and a verandah on green posts, two old fuel pumps on a concrete island, a long cream motel block with a green fascia and a covered walkway, tin sheds, a red-and-white radio mast, a faded billboard, a light pole, a flagpole, two parked four-wheel drives (pearl white and graphite, dusty from the road), drums and a water tank, all on a pale packed-dirt forecourt. Red earth, silvery saltbush and spinifex thick along the verges and scattered across the plain, big orange-trunked gum trees along a dry creek line to the west and dotted over the plain, low hills on the horizon, hard midday sun and heat haze.
- **The cellar**: at the north end of the roadhouse, past the motel, the tin shed's door stands open. Inside, under a fibreglass roof sheet, is a hatch in the floor with its steel lid up and a ladder going down a concrete shaft into a small cellar: bare concrete, a steel shelf, crates and a drum, lit by a bare bulb on its flex. The green portal in the cellar's far wall leads to the bunker.
- **The bunker**: a plain room of bare grey concrete deep underground (a separate area at x = 5000), 12 x 12 m, with no doors or windows: just one louvred vent in the ceiling with daylight falling from it, and four portals glowing in its walls: teal (west, back to the cellar), ice (north, to the Arctic), red (east, to the container ship) and lime (south, to the jungle lab). A white dome security camera hangs from the ceiling in the north-east corner (modelled in Blender: `cctv_dome` and `cctv_eye` in `build_props.py`), its conduit running into the corner: while you are in the bunker its black ball turns to follow you (at most 1.4 rad/s, so a fast mover gets ahead of it), otherwise it pans slowly to and fro. What it sees (`Game/SecurityCamera.cs`, rendered at 15 fps into its own texture with a "CAM 01 BUNKER" caption, the date and time, a blinking REC and TRACKING while it follows you) shows you too: a stand-in body only the camera sees (`Game/PlayerDouble.cs`: the NPCs' human in a navy hoodie like the view model's sleeves, jeans and work boots, walking or running at your speed, facing where you look, pistol up). It is on a CCTV monitor on the north wall of the police station's tech room, over the PC desk (`Level.PoliceCctvScreen`, drawn with `Renderer.DrawScreen`: shade mode 7 adds washed-out colour, scanlines, grain and a rolling bar).
- **The Arctic**: a whiteout on a high snowfield (a separate area at x = 7000). You come out of an old timber survey hut, half drifted over, beside an instrument mast, fuel drums, crates, a sledge and an old container store; a line of bamboo route wands with little red flags wanders off north towards a slope and rock bands that disappear into the blowing snow. There's no sun: everything is lit by the same flat white glare, fog swallows everything a few tens of metres off, streaks of drift race past on the wind and flakes whip by, and everything wears snow on its upward faces (except your gun and hands). Rocks are set down on the snow's actual surface where they stand, and the snow has drifted up round each of them, so none of them floats in the whiteout. Beside the old container store stands a second container, an ordinary faded one with nothing on it, its doors at the far end from the hut and one of them swung most of the way open: inside, in a false wall near its closed end, is the slate portal to Rio de Janeiro (`BuildArcticContainer`). It gives off no light and nothing outside says it is there. The blowing snow stays outside it (6 Nov 2026): the shader's whiteout (fog, drift and flakes, `arcticAir`) leaves out the part of each view ray that's inside a closed room (a `NoSun` shade volume, here `ArcticContainerShade`, via `shelteredRay`), so there are no flakes or white fog inside the container, nor seen through its open door or walls, and no snow settles on its floor; out in the open it snows just as before.
- **The container ship**: a 190 m geared container ship lying stopped at sea under a heavy grey overcast (a separate area at x = 12000). The pink portal is in the front of the wheelhouse: you come out on the walkway across the bridge front, 17 m above the deck, looking forward over nine bays of containers stacked two to five high (the yard's containers, in a lower-detail version, in a dozen colours), three yellow deck cranes with their jibs resting fore and aft, the forecastle and a blue hull, with the sea all round to the horizon. A ladder runs down the front of the accommodation block to the deck; walkways run along both sides between the stacks and the railings, past lashing bridges, bollards and the crane pedestals, to the forecastle (up another ladder: two anchor windlasses, each with its motor and gearbox inboard, a band brake, the chain wheel and a warping head, the anchor chain coming up out of its pipe in the deck, over the chain wheel, through the chain stopper and down the hawse pipe; bollards and the foremast) and aft past the superstructure, engine casing, funnel and freefall lifeboat to the mooring winches at the stern, with mooring rope wound on their drums. The sea is a shader: travelling swells and chop, reflecting the sky, carried on past the edge of the world to the horizon.
- **Rio de Janeiro** (`Game/LevelRio.cs`): a favela on a steep hillside (a separate area four kilometres down, at (0, -4000, 0), the shader's area 13). The magenta portal (once the warehouse's east-wall portal, now listed in the index as Rio de Janeiro, Brazil) is in a teal-painted block wall at the dead end of the bottom alley. Seven alleys 2 m wide run across the hill one above another, 2.7 m apart in height, walled in by houses two to four storeys high: rendered blockwork painted every colour (some with a different-coloured bottom storey), or bare brick in a grey concrete frame. They have barred, shuttered and unglazed windows, painted metal doors, the odd little bar with its shutter half up, balconies over the alley with washing on the rail, air conditioners, drainpipes, meter boxes with wires running up the walls, tags, and pots and gas bottles by the doors. On the roofs: blue water tanks, rebar left for the next storey, washing lines, satellite dishes and sheet roofs on posts. Wires are strung across every alley and fanned out from concrete poles. Narrow stairways (15 risers of 18 cm, with a painted handrail) climb from each alley to the next, the main one straight up the middle. There's a little garden of bananas behind a retaining wall, and at the top, off the highest alley, a flat roof (the laje) you can walk out onto, with plastic chairs, a tank and washing, over the low roofs below to the valley, a hillside covered in favela across it, the forest above and a granite peak beyond. Everything past the alleys is out of reach: the houses are solid, and the backdrop is merged meshes. It's always a bright day there: the outback's sky turned about the vertical so its sun stands over the north-east (`Level.RioSunDirection`, `uRioSkyTurn`), the outback's sun colour, its own shadow map on texture unit 20 (`Renderer.SetRioEnvironment`), and less of the grimy desaturation than elsewhere. The alleys are built from boxes merged into one mesh per colour and material (`RioBatch`), with invisible boxes for the player to collide with.
- **The jungle lab**: a clandestine cocaine camp in a clearing deep in the Amazon rainforest (a separate area at x = 20000, `Game/LevelJungle.cs`). The olive portal is in the back wall of a plank storage shack with a rusty tin roof; you step out into a muddy clearing. Ahead is the main shelter: an open frame of poles under sagging black plastic sheeting, a green tarp hung down its north side and a blue one at its west end, a heap of fresh green coca leaf on a plastic sheet, blue and black plastic drums on a plank stand, buckets, a basin, jerrycans, sacks on a pallet and a rough work table, with a bare bulb hanging from the ridge on a lead from a generator by the shack. A lean-to with a plank counter, drums and cans stands to the east; to the west, a tarp hut with two hammocks, washing on a line, a plastic chair and a cooking fire; a plank walkway disappears west into the forest. Three workers go about the camp, and twenty armed guards with AK-47s (hostile and aggressive, `NpcProfile.Rifleman`) walk short beats in a ring just under the trees round the clearing, watching it: they open fire on sight, and a shot brings them all in. All round, the rainforest closes in: buttressed giants with lianas, walking palms on cones of stilt roots, a layered canopy of broadleaf crowns, and undergrowth of ferns, bananas, stemless palms, saplings and shrubs (modelled in Blender: `build_jungle_trees.py`; without the models the old built-in trees come back), slim palms, bananas round the clearing's edge, a dense wall of undergrowth, ferns, and a canopy that shades everything but the clearing. The light is a high tropical sun (with its own shadow map, so the canopy dapples the forest floor) and a hazy sky; under the canopy the sky's light is mostly lost in the leaves and turns green, the humid air hazes everything a few tens of metres off, and the ground is strewn with dead leaves, moss and shining mud.
- **The police station**: a small police office (a separate area a kilometre down, at (0, -1000, 0), `Game/LevelPolice.cs`), reached through the navy portal in its tech room, which starts offline (its old partner, the magenta portal in the warehouse's east wall, is in Rio now: see [Portal pairs](#portal-pairs)); connect it from the PC there or let the prison intro connect it. The navy portal is in the tech room: the ring is wired into a PC on a desk (thick cables from the tower run across the floor under a yellow-and-black cable protector to a junction box beside the ring, and on in steel conduits into its rim; the monitor shows the portal's status), with a power cabinet feeding the other side of the ring, a server rack, a trolley with a laptop (against the wall by the desk), shelving and a whiteboard of portal sums. Stand at the PC's desk and a "[E] Use PC" prompt comes up: E opens the PC's terminal over the view (a Windows Terminal-style window: "Portal connection protocol:" and a `C:\PORTAL>` prompt with a blinking cursor; `Game/PcTerminal.cs`, text in `PcTerminal.Lines`, the zone is `Level.PolicePcZone`); E again or walking away closes it, and the gun won't fire while it's open. `BRUTALFPS_TERMINAL=open` keeps it open for screenshots; test scripts can press E. The terminal then types `portal --list` and prints the **portal index** (`Game/PortalIndex.cs`): every portal in `PortalSystem` with its number, colour, `prt://` address (fake IPv4 from the documentation ranges 192.0.2.x, 198.51.100.x and 203.0.113.x; the vault's gold portal is 203.0.113.32 underneath but shows as the vanity address prt://112.112.112.112), the number of the portal it links to and its location: the factory in Elbasan, Albania; the container yard in Antwerp; the outback in Western Australia; the bunker unknown (relayed via Tor, address hidden); Antarctica; the ship off the coast of North Korea; the jungle in the Colombian rainforest; the police HQ in Amsterdam. Add a new portal's entry to `PortalIndex` (unlisted ones show as "Unregistered"). Once the listing is printed, **UP/DOWN** pick a destination (highlighted) and **ENTER** connects the HQ's navy portal to it. Only two portals can share a link, so the HQ overrides: whatever the HQ portal was linked to, and whatever the destination was linked to, are cut off and go **offline** (a dark disc, no view; you can't walk into them) until something links to them again; the terminal prints what happened and the index shows `--` for offline portals (`PortalSystem.Connect`, `Portal.Online`, `Level.SetBlocker`). `BRUTALFPS_HQLINK=orange` starts with the HQ connected to that portal; in test scripts `<` `>` press up and down and `#` presses Enter. Through the doorway is the bullpen: six clusters of four desks with frosted-glass screens, light veneer tops, dark wood bases, black office chairs, monitors, phones and paperwork, under a suspended ceiling of fluorescent panels; a green accent wall with the copier, printer, notice board and filing cabinets; the department's sign (two sheriff's stars either side of POLICE DEPT.) over a credenza on the north wall, beside a door with a lit exit sign; windows with half-lowered blinds and radiators along the south. Across the bullpen another doorway (ARMOURY, authorised personnel only) leads to the armoury: a welded-mesh cage across the room with its counter-shelf door standing open, a clearing barrel by the wall, and behind the mesh steel gun lockers (two open, full of carbines, shotguns and an orange less-lethal shotgun, pistols on a rail), shelving of ammunition cans and crates, hi-vis and black POLICE vests on hooks and a cleaning bench. Someone is at the copier, an officer walks the bullpen and the armourer works at the bench. Lit by its ceiling panels (`OfficeLight`) with a soft occlusion map from straight above (the shade under desks and chairs). **The vault**: in the bullpen's north wall, west of the sign (under a red RESTRICTED sign), a steel sliding door leads into a 30 m corridor of painted blockwork with strip lights, and at its far end a second door opens into a big, empty room of raw concrete (16 x 16 x 5 m, formwork tie holes, high-bay lamps) with nothing in it but the gold portal in its north wall (`Game/LevelPoliceVault.cs`). The two doors are an **airlock**: only one can be open at a time. Each has a keypad on both sides (status light green when its door is open, red when shut, amber blinking while it moves): press E at one whose door is shut and the keypad comes up over the view (`Game/AirlockKeypad.cs`): type the code, **1234** (`Level.AirlockCode`), on the number keys (top row or numpad; Backspace rubs one out). After the fourth digit it shows GRANTED and the door opens (if the other door is open, the airlock cycles: that one shuts first, then this one opens), or DENIED with a red flash, and the entry clears. E, Esc or walking away puts it down; while it's up the gun won't fire or change. E at an open door's keypad just shuts it, no code (not while you stand in the doorway). A dome security camera (CAM 02, the same as the bunker's) watches the vault from the corner over the inner door, following you while you're in there; its feed is on a second CCTV monitor beside the bunker's, over the tech room's PC desk. **The observation rooms**: at the corridor's far end a door on each side (OBSERVATION 1 west, OBSERVATION 2 east; `Game/LevelPoliceObservation.cs`) leads into a small, dim room against the vault's south wall, with a counter and two chairs under a window into the vault. Each door slides along the wall on the room side and has a keypad each side: the corridor's takes the same code (1234), the one inside just opens it; E at either shuts an open door. The windows are one-way mirrors (`Game/OneWayMirrors.cs`): from the vault they're mirrors (whenever one is on screen the scene is drawn again reflected in its plane, with your body in it, into a texture the glass shows in screen space; `Renderer.DrawMirror`, `ShadeMode.Mirror`), from the room they're clear glass (the glass draws nothing from behind). In portal views and on the CCTV the glass shows as plain silvered glass. The glass stops shots (it is added to the physics world as glass in `Level.BuildPhysics`, since the mirror has no level box). **For now every keypad door stands open** and the keypads do nothing (`KeypadDoorsHeldOpen` in `Game/LevelPoliceVault.cs`; set it false to bring them back). When two keypads are in reach (across the corridor), the one you're looking towards is used. The vault's PC is on a steel table against the far wall of OBSERVATION 2, wired to the ring by conduits through the ceiling. It is a second portal terminal (`Level.PoliceVaultPcZone`): it lists the same index and connects the **gold** portal, which starts offline. `BRUTALFPS_START=vault` starts you in the vault facing the portal, `BRUTALFPS_AIRLOCK=outer` (or `inner`) starts with that door open, and `BRUTALFPS_TERMINAL=vault` holds the vault's terminal open.
- **Materials and lighting**: every surface uses a tileable 2K PBR texture set baked in Blender (`Assets/Textures`: albedo with a paint mask, normal map, and occlusion/roughness/metalness), see [Graphics pipeline](#graphics-pipeline): painted concrete with peeling paint and cracks; a concrete floor with aggregate, oil and polished patches; rusty and grimy steel; weathered, nailed planks; chipped paint over rust; ribbed container steel; corrugated galvanised iron; gravel; red outback earth with pebbles and a dirt road; scrub foliage; wind-packed snow; dark frost-shattered rock; well-kept ship's paint over welded plate; and gunmetal, stippled polymer, skin and cloth for the pistol and hand. Some models also have their own baked texture sets used through their UVs: the hand's skin (creases, nails, knuckles), the grip stipple, the tyre tread, the honeycomb grille, gum bark, the 4WD's paint-and-dust, and leaf atlases (a saltbush sprig and a gum branchlet) for alpha-tested foliage cards. Boxes are textured by world-space triplanar mapping, Blender models through their UVs. On top of the textures, world-space noise still adds large-scale wear (water stains and grime streaks on walls, oil and joints on the floor, rust creeping up container bottoms, glossy puddles in the yard), and big ground areas blend two differently rotated samples so they don't visibly tile. Lighting is physically based (GGX specular, energy-conserving diffuse) in linear HDR with ACES tone mapping. Outdoors, each area is lit by its sun and by its sky: the HDR sky cubemap (rendered in Blender) gives the ambient light and the reflections, and the sun's strength comes from the same render, so the lighting matches the sky. Indoors it's dim bounce light, cool daylight from the side windows and warm pools from the hanging lamps, plus a dusty haze. The outback has real sun shadows from a shadow map, and the night yard's four floodlights each have a spot-light shadow map (one 2 x 2 atlas, `Engine/FloodLights.cs`), all rendered once at startup since the level doesn't move (`Engine/ShadowMap.cs`). At night the lamps' light scattered by the air is added along every view ray (a closed-form integral per lamp), plus a halo round each lamp in view.
- **Props**: modelled in Blender (`Assets/Models/*.glb`): wooden crates, EUR pallets, skip bins, industrial pendant lamps, oil drums, 40 ft shipping containers with working-looking doors and locking bars, a rainwater tank on its stand, old fuel bowsers, a window air conditioner, a street light, spinifex and an old flat-deck ute (not placed at the moment) (`build_props.py`); a modern full-size four-wheel drive, parked in three colours (`build_wagon.py`: lofted body with recessed glass and shut lines, black arch flares, honeycomb grille, slim LED lamps, roof rails, side steps, six-spoke wheels on all-terrain tyres, and a baked layer of red dust; unbranded); and leaf-card saltbushes and three eucalypts (`build_flora.py`). The level still uses boxes for collision; the models are drawn in their place (if a model file is missing, the old boxes come back).
- **Skies**: physically based skies with clouds, rendered in Blender to HDR cubemaps (`Assets/Sky`): a dark night sky over the container yard (a low cloud deck lit orange from below by the port, an orange glow along the horizon, a few stars in the gaps; the daytime overcast is kept as a fallback) and a deep blue sky with fair-weather cumulus and high cirrus over the outback, with the sun where the shadows say it is.
- **Player controller**: a kinematic AABB body with mouse look, acceleration, air control, sprint, jumping, gravity and ladder climbing. Collision is resolved one axis at a time, so you slide along walls. (Standing right on a floor counts as grounded even when a frame's fall is smaller than the collision skin, so it no longer flickers at high frame rates.)
- **Areas and drawing**: each area is its own `Zones` flag (warehouse, yard, outback, cellar, bunker, Arctic, ship, jungle, police, prison, flats, Rio), worked out from where a box or prop is. Only the areas you can see are drawn: the camera's own (from the outback, also the cellar; down in the cellar, only the cellar), and in each portal's view the area its exit is in. The shader picks each area's lighting the same way, by x (`areaOf`): sun and sky outdoors, local lights only in the bunker, a whiteout in the Arctic, the sea's sky over the ship. Local lights (`LocalLight`: the cellar bulb, the shed's roof sheet, the bunker's vent and portals) light only inside their own box, and `ShadeVolume`s dim the sky's light inside the shed and the cellar.
- **Weapon**: a semi-automatic 9 mm pistol, 17 rounds in the magazine plus one in the chamber, modelled in Blender: `Assets/pistol.blend` is the source and `Assets/Models/pistol.glb` is what the game loads (built by `Tools/blender/build_pistol.py`; the older `Assets/pistol.obj` is still used if the .glb is missing). It's a generic polymer-frame striker pistol with no markings, held two-handed in a thumbs-forward grip. Both hands are built from a hand skeleton's proportions with every finger joint posed round the actual gun, fleshed out with tapered bones, thenar and palm pads blended into one smooth surface, pressed flat against the gun and each other, and given a baked skin texture with knuckle creases, pores and fingernails. The right hand grips high on the back strap (fingers round the front strap, index finger on the trigger, thumb along the left of the frame); the left (support) hand fills the gap on the left of the grip, its fingers wrapped over the right hand's and its thumb lying forward along the frame under the right thumb. Both arms are in light blue fleece sleeves (their own baked `fleece` set) that run up to the wrists, skinned between the hand and the forearm so the cuffs follow the hands. The magazine is a real part (polymer body, steel feed lips, an orange follower and the top round) that sits hidden in the grip.
  - **Shooting**: the trigger finger squeezes and the trigger swings back, the shot goes off with a muzzle flash and a wisp of smoke, the slide slams back while the barrel rides back and tilts down, a brass case flips out of the ejection port (a Bullet body: it bounces, rolls and lies on the floor), and everything springs back. On the last round the slide locks back; the trigger then just clicks.
  - **Recoil**: each shot flips the muzzle up about the wrists, rolling it a little to the left, and pushes the gun back into the hands; it's a pair of springs (about 24 rad/s, damping ratio 0.6), so the gun snaps up in a few hundredths of a second, overshoots a touch coming back down and settles. The hands flip with the gun while the arms only take the push back, and the view gets a small jolt. Aiming, the kick is smaller.
  - **Flash**: as the slide comes back a short orange flash of burning gas spurts out of the ejection port (on top of the muzzle flash), and for a few hundredths of a second the shot lights up the hands, the gun and whatever is close by (`uFlash`, a point light at the muzzle) - most visible in the night yard.
  - **Lighting**: the gun and hands are drawn last, over everything (so they never poke into walls), but lit where they are in the world like anything else, with three differences: they take the sun's and the floodlights' shadow at your eyes, all at once (the shadow maps are far too coarse for something this close, and left dark patches over the hands); their ambient light comes from a little higher up the sky or the hall, plus a little bounced off your own body, so the sides of the hands that face you don't sink into the dark; and they don't collect snow in the Arctic.
  - **Reloading (R)**: the gun tips in and up, the left hand lets go and drops away, the magazine drops out of the grip and falls to the floor (where it stays, as a physics body), the left hand comes back up holding a fresh one, slides it into the magazine well and slaps it home, then takes its grip again; after an empty reload the slide is released and runs forward to chamber a round (1.55 s). With a round still chambered it's quicker (1.3 s). R does nothing with a full magazine.
  - **AK-47 (2)**: fully automatic, 600 rounds a minute while the left button is held, 30 + 1 rounds, both hands on it (right on the grip, finger on the trigger; left cupping the lower handguard, thumb along its side). Each shot cycles the bolt carrier and charging handle, throws a case out to the right and pushes the gun into the shoulder; held down, the shots climb and drift. **Reloading (R)**: the gun is tipped up and rolled so the magazine faces the left hand, which takes it, rocks it forward off its catch and pulls it away (it drops and stays on the floor), goes down for a fresh one, hooks its front lug into the well and rocks it home (2.2 s); after an empty magazine the hand then reaches under to the charging handle and racks it (2.8 s).
  - **Spread** (both guns, `ShotSpread` in `Game/Gun.cs`): every shot leaves within a small cone of its own (AK aimed about 3.5 MOA, a 9 cm group at 100 m; much looser from the hip), and shots in quick succession open that cone up (the crosshair's gap shows it); it closes again within a second or so of letting go. Slow aimed shots group tight, long bursts spray. A settled first shot gets the tightest cone there is and no climb. The pistol is a 25 m gun: careful aimed shots land within about 25 cm at 25 m, and within about 50 cm at 50 m, where more than half miss a man's body, or the range's boards, which are about that wide (50 cm square).
  - **Steadiness** (all the guns, `GunFeel`'s `AimSway`, `UnsteadySpread*` and `SettleRate`): moving (fully at a sprint, on a ladder or in the air), swinging the aim quickly with the mouse (slow tracking doesn't count), bringing the gun up or down from the eye and landing all unsettle the hands, and the shot's cone widens (the crosshair's gap shows it). They settle again in about a second once that stops (the M249 about two), so a shot straight after stopping is still loose. The aim also wanders all the time, a little when settled and further when not: the view moves with it and the shot goes where the crosshair then is, and at the hip the gun itself shows the wander.
  - **Moving**: the gun lags behind the mouse, bobs with your stride (more when sprinting), dips when you land, drops and turns aside while you sprint (straight back up when you fire), breathes a little when you stand still, and is raised into view after a respawn (which also gives you a full gun).
  - **Aim**: shots leave the muzzle but are aimed, allowing for the drop, at whatever is under the crosshair, so what you aim at is what you hit.
- **Bullets** (`Game/Projectiles.cs`): 9 mm at 360 m/s with gravity, so long shots drop below the crosshair past the sights' zero (see [Physics and bullet damage](#physics-and-bullet-damage)). Each frame a bullet's path is swept through the Bullet physics world, the portals' water and the targets, and whatever it meets first takes the hit. Shallow hits on hard surfaces (steel, concrete, stone, glass) can glance off once, leaving a gouge and losing much of their speed. A faint streak shows each bullet's path over the frame. Hitting something loose (a dropped magazine, a casing) knocks it about.
- **Bullet damage**: every surface you can see takes it - walls, floors, props, vehicles, trees, the ship, and the portals' rings and plates - each by what it's made of (see [Physics and bullet damage](#physics-and-bullet-damage)). The portal's water is the exception: a bullet that hits it stops dead in a splash of droplets, sends out the ripple, flattens against the surface for a moment and then drops to the floor as a squashed slug.
- **Portals**: five linked pairs of round doorways (see [Portal pairs](#portal-pairs)). The first: blue standing on its own on the warehouse's loading dock (a heavy ring, its front facing the steps up), orange in the end wall of a shipping container out in the yard (in a slim rim that fits inside the container). Each portal shows a live view of what's beyond the other one. Walk into one and you come out of the other with the same speed, now facing out of the exit. The rings are gritty: a rusted, pitted body, a grimy steel band, and chipped paint on the coloured trim. Each portal's surface is water, with a faint murky film and specks of dirt drifting on it: a gentle swell bends the view through it, and every shot that hits it stops there with a splash and sends out a ripple, and the squashed bullet drops to the floor. Both portals are one body of water, so the ripple also appears on the other portal, mirrored. **Freestanding portals** (`PortalStyle.Freestanding`; so far only the blue one) stand on the floor rather than in a wall: walk into the front and you go through as usual. From behind, the water is a plain window: you see straight through it to what's really on its far side, still bent by the swell and ripples (`Portal.ViewThrough`: no mapping, and the view looks out of the portal's own front), shots splash on it from that side too, but you can't walk into it from there (`PortalSystem.UpdateBackBlockers` closes the gap while you're behind it).
- **Targets**: none placed at the moment. The `Target` class (`Game/Target.cs`) is still there; add instances to the `targets` list in `Program.cs` to bring them back.

## Project layout

```
Program.cs                 Window/GL setup and the main loop
Engine/
  GlLoader.cs              Loads OpenGL through GLAD, or falls back to GLFW's loader
  Renderer.cs              One PBR shader; DrawCube / DrawMesh / DrawModel / portal surfaces / overlay pass / screenshots
  Mesh.cs                  GPU meshes (with or without UVs + tangents) and MeshBuilder (portal rings, hills, ...)
  Model.cs                 Loads Blender models (.glb) with Brutal.Gltf: one mesh per object and material
  Texture.cs               Loads textures (Brutal.Stb), builds mips, compresses (with an on-disk cache), HDR cubemaps
  TextureLibrary.cs        The PBR texture sets per Material, and the outdoor skies (SkyEnvironment)
  ObjModel.cs              Loads OBJ files (the old pistol) into one mesh per object
  ShadowMap.cs             Sun shadow maps for the outdoor areas
  FloodLights.cs           The night yard's floodlights (spot lights), their shadow atlas, glowing lamps; local lights and shade volumes
  Log.cs                   Writes to the console, to brutalfps.log next to the exe and to a file of the run's own in Logs\ (on a thread of its own: a console can take milliseconds over a line)
  FrameStats.cs            Always on: records every frame, reports hitches and what caused them, watches for freezes (see "Finding a lag spike")
  PerfTimer.cs             The named timers inside the frame (Perf.NavSearch, Perf.FlareShadows, ...)
  GpuTimer.cs              How long the graphics card took over each frame (OpenGL timer queries)
  Precompile.cs            Compiles the game's methods on background threads while it loads (no JIT hitch on the first shot)
  Camera.cs                Position + yaw/pitch -> view, projection and world matrices
  Input.cs                 Event-driven keys and mouse, mouse capture
  Aabb.cs                  Bounding boxes, overlap and ray/slab tests
  RenderTarget.cs          Off-screen framebuffer (colour texture + depth) for portal views
  PhysicsWorld.cs          The Bullet world (BulletSharp): static level geometry, small moving bodies, ray casts
Game/
  NpcsPrisonYard.cs        The prison yard's crowd: circles, ring walkers, officers, and Rook, the prisoner being rescued
  RookEscape.cs            Rook's run from the undercroft's portal through the door and up the stairwell
  Level.cs                 Level layout, collision boxes, the props' models, raycasts, wall carving, zones and ladders
  LevelUnderground.cs      The shed over the cellar, the shaft and its ladder, the cellar, and the bunker
  LevelFlats.cs            The block of flats: its undercroft, stairwell, doors and the street round it
  LevelRio.cs              Rio de Janeiro: the favela's alleys, stairways, houses, the laje and the hills round it
  LevelRioHideouts.cs      Rio's hiding places: the little dark rooms behind open doors off the alleys (HidingPlace)
  LevelFlatsBarricades.cs  The cars, bins and junk blocking the undercroft's two open sides
  LevelFactoryOffice.cs    The office at the south end of the factory's hall, and the buckets round its walls
  LevelArctic.cs           The Arctic: the hut, snow, rocks, route wands
  LevelArcticStation.cs    The Arctic research station: red modules on stilts, the deck, steps, door and mess room
  LevelShip.cs             The container ship: hull/superstructure/crane placement, containers, deck collision, ladders
  LevelJungle.cs           The jungle lab: the storage shack, the shelters and camp, the rainforest round the clearing
  LevelJungleShack.cs      Inside the jungle shack: its door, the olive portal's casing, the PC wired to it, the whiteboard
  Foliage.cs               FoliageCover: the leaves as soft balls, so NPCs can't see through them (bullets still can)
  LevelPolice.cs           The police station: the tech room (portal wired to a PC), the bullpen, the armoury cage
  LevelPoliceKit.cs        Its building blocks: merged oriented boxes per material, block lettering, chairs, long guns
  LevelPoliceVault.cs      The airlock (two interlocked sliding doors with keypads), the corridor and the concrete vault with the gold portal and its PC
  LevelPoliceObservation.cs The vault's two observation rooms: keypad doors off the corridor, one-way mirrors into the vault
  OneWayMirrors.cs         The one-way mirrors: the reflected view rendered each frame from the vault side, clear glass from behind
  AirlockKeypad.cs         The vault's keypads (airlock and observation rooms), close up: entering the code
  Portals.cs               The portal pairs: doorways, views, teleporting the player
  SecurityCamera.cs        The dome cameras (the bunker's and the police vault's): each ball follows the player in its room; their feeds on the tech room's two CCTV monitors
  PlayerDouble.cs          The player's body as the security camera sees it
  PlayerController.cs      Movement and collision
  FactoryRaid.cs           The factory raid: over the fence or in the back of a delivery van (the van's ride, its load space, the hands on the wall)
  Weapon.cs                The pistol: ammo, firing, reloading and the view model's animation
  Rifle.cs                 The AK-47, the UMP40, the scoped AR-15 and the G3 (RifleSpec): full-auto fire, recoil, the reloads, the hands
  Lmg.cs                   The M249: belt feed, the feed cover and pouch reload, the weight
  GunNest.cs               The M249 on its tripod in the jungle: taking it off and putting it back
  BritishGuns.cs           The nine British service guns' RifleSpecs, made from their <id>_points.json
  GunLocker.cs             The gun lockers (armoury and range): every gun, and what's on which number key (loadout.txt)
  Inventory.cs             The inventory (I): what you're carrying, drawn like a gun locker
  Gun.cs                   IGun (what Program and the HUD use of either gun) and ShotSpread (spread that opens up with rapid fire)
  FiringRange.cs           The firing range's targets and its reset button (the rest is LevelFiringRange.cs)
  Projectiles.cs           Bullets in flight: sweeps through the physics world, portals and targets; drag, ricochets
  Relevance.cs             What the player can see (the camera, the zones drawn last frame) for the AI tiers and debris LOD; FrameBudget
  Cartridge.cs             The calibres: bullet mass, drag and punch, what they do to what they hit, their cases
  Surfaces.cs              What each surface is made of, for bullets (concrete, steel, wood, glass, dirt...)
  LevelPhysics.cs          Puts the level's boxes and meshes into the physics world, tagged with their surfaces
  Impacts.cs               What a hit does, by surface: hole, dust, sparks, chips, debris; ricochets; portal squash
  BulletHoles.cs           The bullet holes (decals): at most 400, the oldest fade out and are reused
  Particles.cs             Dust, chips, droplets, sparks, tracers (at most 700)
  Debris.cs                Bullet bodies: chips, splinters, shards, casings, magazines, squashed bullets (capped)
  Target.cs                Target boards: scoring, flash and swing
  Hud.cs                   Crosshair, hit marker and the window-title HUD
  TimeOfDay.cs             Day or night in each level (BRUTALFPS_TIME reads here)
  LevelDarkness.cs         How much light falls on a point, for people's eyes (Level.LightAt)
  NpcsFactory.cs           The factory's crew: fifty hostiles in and around it, their beats checked at startup
  NpcsRio.cs               Rio's people: sixty civilians and forty hostiles along the favela's alleys, checked at startup
  NpcHiding.cs             Running for a hiding place and crouching in it (civilians), falling back into one to regroup (hostiles)
  NpcsShipYard.cs          Hostiles at the docks: ten in the Antwerp container yard, fifty on the container ship, on the stacks too
  NpcSeat.cs               Sitting at the yard's card table: the SitCards clip, getting up when alerted, the gun by the chair, the cards in hand
  LevelYardCards.cs        The yard's white garden table and chairs, and the cards, cash and cans on it
  StartMenu.cs             The menu the game opens on: day or night for each level, then START
Assets/
  Textures/                PBR texture sets (<name>_albedo / _normal / _orm .png)
  Sky/                     HDR sky cubemaps (<sky>_px ... _nz .hdr) and their sun/sky light levels (<sky>.json)
  Models/                  The props, flora, vehicles and the pistol (.glb)
  pistol.blend, wagon.blend  Sources of the pistol and the 4WD
Tools/blender/             The Blender scripts that make all of the above (see below)
Tools/previews/            Preview renders of the textures, skies and models
```

## Graphics pipeline

All art is generated by Python scripts run in Blender (tested with Blender 5.2), so it can be tweaked and rebuilt. Run them from the project folder; each one writes straight into `Assets`, and the next build copies the results next to the exe.

| Script | Makes | Command |
| --- | --- | --- |
| `bake_textures.py` | `Assets/Textures`: 24 PBR sets (tileable ones, plus the sets some models use through their UVs). Each is a procedural Blender material (in `RECIPES`) baked with Cycles from a UV plane; every noise is sampled on a 4D torus so the maps tile seamlessly. Normal maps and cavity AO are derived from the baked height. `Tools/previews/textures.png` is a contact sheet of every set (baking a few sets keeps the others' tiles); `-- sheet` just rebuilds it from the PNGs. | `blender -b --factory-startup -P Tools/blender/bake_textures.py -- [names \| sheet]` |
| `render_sky.py` | `Assets/Sky`: the yard and outback skies. Blender's physical sky (multiple scattering) with the sun calibrated onto the game's sun direction, under a shaded cloud layer, rendered to six HDR faces; also measures the sun's and the sky's irradiance (the `.json`) so the game can light each area to match. `yard_night` is a procedural night sky instead (no sun); `sea` is the heavy grey overcast over the container ship. | `blender -b --factory-startup -P Tools/blender/render_sky.py -- yard_night 1024` (and `outback`, `sea`, or `yard` for the old daytime yard) |
| `build_ship.py` | `ship_hull` (hull with flare, raked stem and transom, blue topsides and a red boot-top, deck, forecastle with bulwark, windlasses and chains, railings, bollards, winches, foremast), `ship_house` (accommodation block, wheelhouse with a gap in its front for the portal, bridge-wing railings and brackets, radar mast, engine casing, funnel, freefall lifeboat on its ramp) and `ship_crane` (yellow deck crane on a tall pedestal: house, cab, A-frame, tapered jib, wires and hook). Laid out in the same coordinates as `Game/LevelShip.cs`, which adds the containers, hatch covers, lashing bridges, walkway floors and all the collision. | `blender -b --factory-startup -P Tools/blender/build_ship.py -- [names]` |
| `build_port.py` | `flood_mast` (24 m high mast with a caged ladder, a service platform and a cross-arm of ten sodium floodlights) and `gantry_crane` (rubber-tyred container crane with cab, trolley house, spreader, stairs and lamps). | `blender -b --factory-startup -P Tools/blender/build_port.py -- [names]` |
| `build_props.py` | `Assets/Models`: the props, with UVs scaled to the texture sets and preview renders. | `blender -b --factory-startup -P Tools/blender/build_props.py -- [names]` |
| `build_pistol.py` | `Assets/Models/pistol.glb` and `Assets/pistol.blend`: the pistol (with its magazine, follower and top round, which the game slides out on a reload), both hands (posed from joint positions; their volumes are blended with signed-distance smooth unions, then pressed against the gun and each other) with their baked skin sets `hand` / `hand_index` / `hand_left`, the two fleece sleeves (with a skinning weight in each vertex, so the cuffs can follow the hands) and `Tools/previews/models/pistol_pivots.json` (the wrists and arm directions that `Game/Weapon.cs` uses). The previews (`Tools/previews/models/pistol_hip.png`, `_aim`, `_back`, `_left`, `_right`, `_below`, `_front`) are rendered from the game's own hip and aim views (`GAME_VIEWS`, which must match `HipPosition` / `AimPosition`). `-- preview [tag]` builds both hands and renders the previews without baking; `-- support` rebuilds only the left hand and its sleeve in the saved `pistol.blend`; `-- magazine` re-adds just the magazine and re-exports, leaving the hands as they are. | `blender -b --factory-startup -P Tools/blender/build_pistol.py [-- preview [tag] \| support \| magazine]` |
| `build_ak47.py` | `Assets/Models/ak47.glb`, `Assets/ak47.blend` and `Assets/Models/ak47_points.json`: the AK-47 (milled receiver, wood furniture in the `gun_wood` texture set). Parts the game moves: `Magazine`, `BoltCarrier`, `Trigger`, `Selector`. Run `build_ak47_hands.py` after it. | `blender -b --factory-startup -P Tools/blender/build_ak47.py [-- nopreview]` |
| `build_ak47_hands.py` | Adds the hands to `ak47.blend` and re-exports `ak47.glb`: the right hand from `build_pistol.py`'s hand builder round the AK's grip, a new left hand cupping the handguard, both sleeves, baked skin sets `ak_hand` / `ak_hand_index` / `ak_hand_left`; adds the wrists and finger knuckle to `ak47_points.json`. `-- preview [tag]` renders `Tools/previews/models/<tag>_*.png` without baking. | `blender -b --factory-startup -P Tools/blender/build_ak47_hands.py [-- preview [tag]]` |
| `build_ump.py` | `Assets/Models/ump.glb`, `Assets/ump.blend` and `Assets/Models/ump_points.json`: the UMP40 (polymer receiver traced from a side photo, skeleton stock, rail, ghost-ring rear sight). Its grip, trigger, guard and handguard section are the AK's, so it's held in the AK's hands (`RifleSpec.Ump40` in `Game/Rifle.cs`). Parts the game moves: `Magazine`, `BoltCarrier`, `ChargingHandle`, `Trigger`. Previews include `ump_hands_*.png` with the AK's hands. | `blender -b --factory-startup -P Tools/blender/build_ump.py [-- nopreview]` |
| `build_ar15.py` | `Assets/Models/ar15.glb`, `Assets/ar15.blend` and `Assets/Models/ar15_points.json`: the AR-15 carbine traced from a side photo (flat-top upper, 1-4x scope in rings on a riser, quad-rail handguard with an angled foregrip, railed gas block, ported muzzle shroud, skeleton stock on the buffer tube, curved 30-round magazine). Its grip, trigger, guard and the handguard's lower half are the AK's, so it's held in the AK's hands (`RifleSpec.Ar15` in `Game/Rifle.cs`; the left hand 20 mm higher). Parts the game moves: `Magazine`, `BoltCarrier`, `ChargingHandle` (the T-handle, racked after an empty magazine), `Trigger`. Previews include `ar15_hands_*.png` with the AK's hands. | `blender -b --factory-startup -P Tools/blender/build_ar15.py [-- nopreview]` |
| `build_m249.py` | `Assets/Models/m249.glb`, `Assets/m249.blend` and `Assets/Models/m249_points.json`: the M249 SAW, its proportions from a side photo (long barrel, slotted flash hider, gas block with a hooded front post, bipod, carrying handle swung up and to the left, ring rear sight, polymer stock, a soft pouch under the receiver). Its grip, trigger, guard and the handguard's lower half are the AK's, so it's held in the AK's hands, unmoved (`Game/Lmg.cs`). Parts the game moves: `FeedCover`, `Pouch`, `Belt0`..`Belt10` (a round and link each, so the belt's last rounds can go), `BoltCarrier`, `ChargingHandle`, `Trigger`. Previews include `m249_hands_*.png` with the AK's hands. | `blender -b --factory-startup -P Tools/blender/build_m249.py [-- nopreview]` |
| `build_gun_nest.py` | `Assets/Models/gun_nest.glb` and `Assets/gun_nest.blend`: the jungle's gun nest (161 sandbags in a horseshoe, a plank floor, an empty tripod, a crate of ammunition tins, draped belts and spent cases, a concrete bunker with a firing slit behind). `Level.BuildGunNest` places it and repeats its dimensions for the colliders. | `blender -b --factory-startup -P Tools/blender/build_gun_nest.py` |
| `build_g3.py` | `Assets/Models/g3.glb`, `Assets/g3.blend` and `Assets/Models/g3_points.json`: the Heckler & Koch G3 battle rifle traced from a side photo (stamped receiver with the cocking tube over the barrel, slim vented green handguard, fixed green stock, diopter drum rear sight, hooded front post, slotted flash hider, 20-round steel magazine), with an ACOG 4x32 prism sight (fiber-optic channel, turrets, rail clamp) on an HK claw mount and Picatinny rail over the drum. Its grip, trigger, guard and the handguard's lower half under the left hand are the AK's, so it's held in the AK's hands (`RifleSpec.G3` in `Game/Rifle.cs`; the left hand 50 mm further forward and 12 mm higher). Parts the game moves: `Magazine`, `BoltCarrier`, `ChargingHandle` (on the left of the tube, racked after an empty magazine), `Trigger`. Previews include `g3_hands_*.png` with the AK's hands and `g3_sights.png` (the sight picture through the drum). | `blender -b --factory-startup -P Tools/blender/build_g3.py [-- nopreview]` |
| `build_wagon.py` | `Assets/Models/wagon.glb`, `Assets/wagon.blend` and the `wagon_body` paint/dust texture set. | `blender -b --factory-startup -P Tools/blender/build_wagon.py [-- preview]` |
| `build_transit.py` | `Assets/Models/transit.glb`, `Assets/transit.blend` and the `transit_body` paint/grime texture set: a Ford Transit panel van (Mk8, long wheelbase, high roof), lofted in horizontal slices, with glass, shut lines, grille and lamps laid on the same surface. The blue oval badge has no lettering. Previews are `Tools/previews/models/transit_*.png`. `nobake` keeps the existing texture set; `preview` renders without exporting. | `blender -b --factory-startup -P Tools/blender/build_transit.py [-- preview] [nobake]` |
| `build_plates.py` | The vans' number plates: the `van_plates` texture set (an atlas of made-up Albanian, Romanian, German and Dutch plates, drawn in Blender in Windows' Bahnschrift; contact sheet `Tools/previews/models/van_plates.png`) and `Assets/Models/transit_plate_<name>.glb`, one van's front and rear plates each, which `AddVan` draws with the van. Registrations are in `PLATES`. | `blender -b --factory-startup -P Tools/blender/build_plates.py` |
| `bake_leaves.py` | Leaf atlases (`saltbush_sprig`, `gum_branchlet`, `jungle_leaves`, `palm_frond`, `fern_frond`, `banana_leaf`, `big_leaf`): a modelled sprig rendered straight down into albedo + coverage, normal and AO/roughness. | `blender -b --factory-startup -P Tools/blender/bake_leaves.py -- [names]` |
| `build_flora.py` | `saltbush_a`, `saltbush_b`, `gum_tree_a/b/c`: leaf cards with soft, rounded normals; trees from a branching generator with bark UVs. | `blender -b --factory-startup -P Tools/blender/build_flora.py -- [names]` |
| `build_jungle_trees.py` | The jungle's trees: `jungle_giant_a/b` (kapok, strangler fig) and `jungle_tree_a/b` (fluted trunks flaring into tall plank buttresses and surface roots, strangler roots, limbs to broadleaf-card crowns, hanging lianas), `walking_palm_a/b` (stilt roots, ringed trunk, crownshaft, chained frond cards), `jungle_canopy` and `jungle_crown_small` (masses of leaf cards); the undergrowth: `jungle_fern_a/b`, `jungle_banana_a/b`, `jungle_ground_palm`, `jungle_sapling_a/b`, `jungle_shrub_a/b` (leaf atlases bent onto curved, folded strips, and leaf cards). Bark is `bark_kapok` / `bark_palm` (`bake_textures.py`). Each writes `<name>_points.json`: the trunk radius and the buttresses' outlines, which the game turns into colliders. | `blender -b --factory-startup -P Tools/blender/build_jungle_trees.py -- [names]` |

Models follow a material naming convention (see `Engine/Model.cs`): a texture set's name (`corrugated`, `wood_planks`, ...) uses that set through the UVs, `<set>_tint` takes the colour the game draws the model with (container and car paint), `flat_*` is untextured (base colour, roughness, metalness from Blender), `emit_*` glows, `tex_<set>` uses the model's own texture set through its UVs exactly as baked (with `_tint`, the albedo's alpha is paint coverage and the paint takes the game's colour outright, so baked dust keeps its own colour), and `leaf_<set>` / `cut_<set>` are alpha-tested, two-sided cards (leaves, grille mesh). In Blender, +Z is up and the game's forward (-Z) is Blender's +Y.

Textures are block-compressed on the GPU (about a quarter of the memory). The driver's compression is slow, so the compressed textures are cached in `TextureCache` next to the exe after the first run (a cache file is rebuilt when its PNG changes); `BRUTALFPS_NOCOMPRESS=1` turns compression off.

Indoor lights (the police station's and the prison's ceiling fittings, `OfficeLight`) live in a uniform buffer, the `OfficeLights` block (`Renderer.SetOfficeLights`): room for 256 between them, where plain uniforms only left room for 48. They're sorted with the police station's first, and a point in either building only loops over its own building's lights (`uOfficeSplit`), so adding lights to one doesn't slow the other down. Each light still only lights inside its box, so they cost little away from their rooms. The log says how many there are at startup (`[RENDER] 55 office lights ...`).

### BRUTAL libraries used for graphics

- **Brutal.Stb** (stb_image): PNG textures and Radiance HDR sky faces; also writes the PNG screenshots.
- **Brutal.Gltf**: reads the `.glb` models exported from Blender.
- **Brutal.OpenGl**: textures, cubemaps, block compression, anisotropic filtering.
- Also available if needed later: **Brutal.Ktx** / **Brutal.Texture** (pre-compressed KTX2 textures), **Brutal.MeshOptimizer** (mesh simplification / LODs), **Brutal.ImGui** (a real HUD).

## How the portals work

- **Doorways**: each portal cuts a square hole through its wall (`Level.CarveOpening`), and a plate mesh with a round hole (`MeshBuilder.SquarePlateWithHole`) fills the corners in the wall's own material, so the doorway looks round. Invisible collision boxes narrow the walkable gap to a rectangle that fits inside the circle. Each portal has a `PortalStyle` (wall depth, ring size, plate material); both portals must have the same radius and centre height so the teleport lines up. They are sized to fit inside a container: 1.15 m radius, centre 1 m above the floor.
- **Rendering**: every frame, each visible portal draws the scene into its own texture from a *virtual camera*: the player's camera moved through the portal pair. A clip plane at the exit portal hides the wall and anything else behind it. The portal surface then shows that texture in screen space, so it lines up exactly with the opening.
- **Teleporting**: when the player's eye passes through a doorway from the front, their position, look direction and velocity are carried through to the other portal (`PortalSystem.TryTeleport`).
- **Water**: the water is drawn entirely in the fragment shader (`waterHeight` in `Renderer.cs`); there's no texture image. The surface height is a few sine waves plus an expanding ring for each recent hit (up to 16 per portal, each lasting 3 seconds). The slope of that surface shifts where the portal view is sampled, which is what makes it look like refraction, and also adds highlights and crests. Tune the ripple speed, size and fade in `waterHeight`.
- **Portals within portals**: a portal seen *through* another portal shows its own view too, so it looks the same as when you see it directly: before drawing a portal's view, each portal visible in it gets a view of its own, from the virtual camera moved on through it. Only a third level shows a flat glow. Every view is drawn only inside its portal's outline on screen (a scissor rectangle), which keeps the extra views cheap.
- **Air**: a portal's surface (and its glow) gets the same air as everything else at that distance (haze, the yard's lamp glow, the Arctic's whiteout), so a portal fades into the blizzard like the hut it's set in. In a portal's view the air only starts where the view comes out of the exit portal, not at the virtual camera behind its wall, so nothing is fogged twice. Sky boxes take the camera's area, since a sky box can reach over an area's edge.
- **Warning lamps**: each ring has a small red lamp at its top centre, on the front and on the back (the back band of a freestanding ring, or the far face of the wall). It flashes (0.8 s cycle) while the portal is connected, starting lit the moment it connects, and is a dark red lens while it's offline (`PortalSystem.Draw`; tune `LampFlashPeriod` and the lamp colours there).
- **People react to a portal coming on**: when a PC (or anything else) connects a portal, `PortalSystem.PortalOpened` fires for both ends and `Npcs.PortalOpened` tells the people in the room it opens into (within 40 m with a clear line to it, 15 m round a corner). Armed NPCs go on guard: a pusher, an aggressive one or one who'd search moves up to stand a few metres off it with the gun on the water; a hold-back or a holder takes cover with the gun on it. Someone unarmed within 8 m who can see it backs off (runs or cowers); further off they stop and look. A script-posted guard just raises the gun, and the prison intro's own portal coming on is ignored.
- **Limits**: portals are assumed to be upright (on walls, not floors or ceilings).

- **The prison and its cinematic intro** (`Game/LevelPrison*.cs`, `Game/Cinematic.cs`, `Game/PrisonIntro.cs`): a courtyard prison two kilometres down (`Level.PrisonOrigin` = (0, -2000, 0); the shader's area 9 for its grounds and yard under a pale overcast with a soft sun and its own shadow map on texture unit 17, area 10 inside the rooms you walk through, lit like the police station). Grey blockwork wings with dark slate hip roofs round an open yard (a ring path of grey pavers round a tarmac sports court, grass, young trees, a curved hedge, lamp posts, benches), a white gatehouse with the staff entrance, a perimeter road and wall. Inside: the gatehouse (control booth with an officer on duty, metal detector arch, x-ray belt), the staff locker room, and the cell corridor (cream blockwork, blue-grey pilasters and cell doors, ducts and pipes, barred windows onto the yard, one cell open). A shipping container stands in the middle of the yard with the crimson portal in its closed end. `BRUTALFPS_START=intro` plays the intro: you're walked in through the staff entrance, past the booth, to your locker (fade while you change), through the grille, down the corridor and out into the yard, where a Chinook lowers the container in (see [The Chinook](#the-chinook-4-nov-2026)) and its doors swing open and the portal comes online. The mouse can look around a little on the way; Enter skips. `BRUTALFPS_START=prison` (outside the entrance, every door open) and `=prisonyard` start you there to walk about; `BRUTALFPS_INTRO_STEP=<n>` starts the intro at its n-th step. New materials: `Blockwork` (41), `RoofSlate` (42).

## Portal pairs

| Pair | Ends | Where |
| --- | --- | --- |
| 1 | Blue / Orange | Freestanding on the warehouse's loading dock, facing the steps / inside the closed end of a shipping container in the yard |
| 2 | Purple / Yellow | South-east pocket of the container yard / back wall of the outback shack, inside its fenced yard |
| 3 | Green / Teal | South wall of the cellar under the roadhouse shed / west wall of the bunker |
| 4 | Ice / White | North wall of the bunker / north wall of the timber hut in the Arctic Both offline at the start: a terminal has to connect the white portal (to the navy portal, say), and the research station's crew come out to it when one does (see [The Arctic research station](#the-arctic-research-station-12-nov-2026)) |
| 5 | Red / Pink | East wall of the bunker / front of the container ship's wheelhouse, on the bridge-front walkway |
| 6 | Lime / Olive | South wall of the bunker / north wall of the plank storage shack in the jungle clearing |
| 7 | Slate / Magenta | False wall near the closed end of the second container beside the Arctic's old container store / the wall at the dead end of the bottom alley of the Rio favela. Magenta was the warehouse's east-wall portal: its index entry now says Rio de Janeiro, Brazil |
| - | Navy | West wall of the police station's tech room. Offline at the start: its PC (or the prison intro) connects it. Its old partner, magenta, is in Rio now; the warehouse end is still in the code (`Level.WarehouseEastPortal*`, `WarehouseStyle`) for a portal of a new colour |
| - | Crimson | In a false wall halfway along the shipping container in the prison yard (the rear half behind it is sealed off; a small satellite dish stands on the roof above it). Offline until the prison intro brings it online (it connects to the cobalt portal at the flats; `BRUTALFPS_INTRO_LINK=<colour>` picks another, such as `navy`, the police HQ's, which it used to be), or the HQ terminal connects to it |
| - | Cobalt | West wall of the undercroft at the block of flats, facing the stairwell's door. Offline until the prison intro connects the crimson portal to it, or the HQ terminal connects to it |
| - | Gold | North wall of the police station's vault, behind the airlock. Offline until the vault's own PC connects it (the same terminal as the tech room's, connecting the gold portal instead of the navy one) |

Portals outside the view (or more than 70 m away, or facing away) aren't rendered, so even in the bunker, with three portals in front of you, a frame stays well under 10 ms on a laptop RTX 3050.

## Physics and bullet damage

**Bullet.** BRUTAL doesn't include Bullet (its physics package, `Brutal.Jolt`, wraps the Jolt engine and is marked
experimental), so the game uses [bullet3](https://github.com/bulletphysics/bullet3) itself: version 2.89 through the
**BulletSharp.x64** NuGet package (a C# wrapper of Bullet's C API that ships Bullet's native library for Windows x64,
built in double precision so the far-off areas at x = 12000 are as precise as the warehouse). `Engine/PhysicsWorld.cs`
wraps it.

- **What's in the world**: everything that is drawn, as static geometry. Every visible level box is a box shape; every
  mesh (each part of each Blender model, code-built meshes such as ladders and hills, the forecourt slabs, and the
  portals' rings and plates) is a BVH triangle mesh, so bullets and debris meet exactly what you see. A model drawn many
  times shares one BVH (scaled copies use a scaled view of it). Invisible collision boxes stay out (they're the player's
  stand-ins for models), as do leaf cards and scrub (shots pass through foliage) and the warehouse's bright open doorway.
  The sea round the ship is a water surface. That's about 1400 objects and 1.9 million triangles, built in about a
  second at startup. Each object is tagged with its surface (`SurfaceTable`).
- **The player** still moves with `PlayerController`'s own box collision; Bullet holds what bullets hit and the small
  loose things. It steps at a fixed 120 Hz; small fast pieces use continuous collision so they can't tunnel through
  walls, and everything goes to sleep once it settles.
- **Surfaces** (`Game/Surfaces.cs`) come from each box's material or each model part's texture set (flat Blender
  materials by how metallic and glossy they are). What a hit does (`Game/Impacts.cs`):

| Surface | Hole | Flies off |
| --- | --- | --- |
| Concrete (walls, floors, slabs, the bunker) | a chipped crater round a dark hole, fresh grey stone inside, hairline cracks | dust, grit, 2-4 stone chips (bodies) |
| Rock (the Arctic's outcrops) | a dark crater | dust, dark chips |
| Bare or rusty steel (pipes, trusses, the portal rings) | a lead-smeared dent in a ring polished bright | sparks, a little smoke |
| Painted steel (paintwork, the ship, portal trim) | the same, with a jagged ring of paint chipped off (primer and rust) | sparks, paint flakes |
| Sheet metal (containers, corrugated and galvanised iron) | punched through, petals bent in, paint cracked off round it | sparks |
| Wood (crates, pallets, planks, gum bark) | a dark hole, light raw wood torn out along the grain | splinters (bodies), wood chips, dust |
| Glass (windows, lamps, car windows) | a crack star round a crushed ring (dark cracks on bright panes, white on dark glass) | shards (bodies), glitter |
| Dirt, gravel, the road | a small pit of damp earth, clods round it | a puff of soil, clods |
| Snow | a blue-shadowed pit | a puff of snow |
| Plastic, rubber, cloth | a small hole | a little dust |
| The sea | - | a splash of droplets and mist |
| A portal's water | - (it ripples instead) | droplets; the bullet flattens against it and drops |

- **Ricochets**: a shot that meets a hard surface at less than about 15 degrees usually glances off (once), leaving a
  gouge that runs the way it was going (bright on steel, grey on stone) and a spray of sparks or grit.
- **Bullet holes** are drawn in one batch (`Game/BulletHoles.cs`): a small quad laid on the surface, shaded procedurally
  in the main shader (`shadeDecal`, mode 5) and lit like the surface under it. Near an edge a hole shrinks until it sits
  on the surface. **Particles** (`Game/Particles.cs`: dust, chips, droplets, sparks, tracers, the muzzle flash) are quads
  that the vertex shader turns to face each camera (mode 6), so portal views see them face-on too; sparks and chips
  bounce off the physics world.
- **Calibres** (`Game/Cartridge.cs`, models from `Tools/blender/build_rounds.py`): the long guns each fire their own round,
  the armed NPCs carrying them too. The pistol and the shotgun are still plain 9 mm ball with no drag.

  | Gun | Round | Bullet | Muzzle | At 200 m | Punch at the muzzle |
  | --- | --- | --- | --- | --- | --- |
  | G3 | 7.62 x 51 mm NATO (M80), brass case | 9.5 g | 800 m/s | 667 m/s | 2.5 |
  | AK-47 | 7.62 x 39 mm (M43), lacquered steel case | 7.9 g | 715 m/s | 541 m/s | 2.0 |
  | AR-15 | 5.56 x 45 mm NATO (M855, green tip), brass case | 4.0 g | 900 m/s | 706 m/s | 1.8 |
  | UMP40 | .40 S&W (165 grain), brass case | 10.7 g | 360 m/s | 238 m/s | 1.2 |

  Punch is the square root of the round's energy against 9 mm's, worked out at the speed it hits with, so it falls with
  range. It multiplies the damage to people and the player, and sets the size of the hole and how hard loose things get
  shoved. In flight a round is still swept through the Bullet world each frame (too fast for a rigid body), with its own
  drag. **The sights are zeroed**: each gun's shot leaves at a fixed angle above the line of sight (from `Cartridge.Drop`), so
  it rises a little above the crosshair, crosses it at the zero (`RifleSpec.ZeroDistance`, `Weapon.ZeroDistance`,
  `Shotgun.ZeroDistance`), and falls away below it beyond: past that range you hold high. The G3's ACOG ladder is worked out
  from the same numbers (`IGun.HoldUnder`): its marks are 400 to 800 m. NPCs judge the range for themselves and hold over for it.

  | Gun | Zero | Below the crosshair at 100 m | 200 m | 300 m | 400 m |
  | --- | --- | --- | --- | --- | --- |
  | G3 | 100 m | - | 0.18 m | 0.59 m | 1.25 m |
  | AR-15 | 100 m | - | 0.15 m | 0.49 m | 1.09 m |
  | AK-47 | 100 m | - | 0.25 m | 0.83 m | 1.85 m |
  | UMP40 | 50 m | 0.23 m | 1.57 m | | |
  | Pistol | 25 m | 0.28 m | 1.32 m | | |
  | Shotgun | 25 m | 0.23 m | | | |

  **The firing range** (`Game/LevelFiringRange.cs`, `Game/FiringRange.cs`) is for trying this out: `BRUTALFPS_START=range` (the
  `Firing range` launch profile), or walk 200 m north up the road from the roadhouse. Started that way the range stands alone:
  only its plain (the ground, the scrub and the hills) and the range itself are loaded, with no other levels, portals, people or
  security cameras (`Level(firingRangeOnly: true)`). There's no day and night menu, it's daylight unless `BRUTALFPS_TIME` says
  otherwise, you start and respawn on the firing line, and the M249 is on 7 from the start, even after a respawn. From the firing line, targets stand at 25,
  50, 100, 150, 200, 300, 400, 500, 600 and 800 m (`Level.RangeLanes`), fanned out so none is in front of another, their centres 2 m
  up, each with its range on a sign beside it (bigger the further out) and a line across the ground. Boards stay still when hit, so
  their holes stay where the shots landed, and score by ring; a shot that misses goes on to the ground or the berm at the far end. Every round fired on the range is a **tracer**: a bright head and a glowing orange line along the path it really flew, drop and ricochets included, that hangs in the air for 2 s and fades.
  While you're on the range, a **readout** at the top of the screen shows the last hit: the board, ring and points, that board's
  running hits and score, the round, how far out, how far from the board's centre (low or high, left or right), and its speed,
  energy and time of flight. On the right of the screen is the board that was last hit, seen square on, with a dot for each hit on
  it (the newest ringed in yellow) and that board's distance under it. Misses don't show. At the bench, E presses the red **reset button**: the boards' holes and
  scores, the readout and the board on the screen are cleared (`[RANGE]` in the log). With `BRUTALFPS_DEBUG=1`, each hit also logs a `[BULLET]` line, and `[SHOT]` the aim's recoil.

  It becomes a **Bullet rigid body** once it slows: a ricochet tumbles (four times the drag) and carries on as a body below 90 m/s, and a round that stays in
  one piece on something hard drops as a mushroomed spent bullet (`SpentChance`). Cases and spent bullets are convex hulls
  made from low-poly outlines in the model, with the origin at their real centre of mass and their real mass, so a
  bottlenecked case rolls in an arc. After re-running the script, build again. With `BRUTALFPS_DEBUG=1` each strike logs a
  `[BULLET]` line with its range, speed, energy and punch.
- **Limits**, so it never lags: at most 400 bullet holes (the oldest 40 fade out as new ones arrive, then are reused) and
  700 particles (new ones replace those closest to dying); per kind of body, 70 stone chips, 40 splinters and 40 glass
  shards (each fading away after 14-18 s), 40 casings (45 s), 40 spent bullets (45 s), 30 squashed bullets (45 s) and 6 dropped magazines (90 s).
  Spawning past a cap removes the oldest of that kind. Debris that falls into the sea sinks; sleeping bodies cost next to
  nothing, and debris is only drawn in the areas being drawn. In the night yard after 126 shots (105 holes, 61 bodies),
  frames took 9 ms on average (RTX 3050, 1600 x 900, vsync off).
- **Tuning**: hole sizes and what flies off per surface in `ImpactEffects.Hit`; hole looks in `shadeDecal` (Renderer.cs);
  the caps in `BulletHoles.Capacity`, `Particles` (`capacity`) and `DebrisSystem.Limits`; bullet speed and the ricochet
  angle in `Weapon.ProjectileSpeed` and `ProjectileSystem.Advance`; reload timing in the constants at the top of
  `Weapon.cs`.

## Notes for what comes next

- To move the portals, change the two `new Portal(...)` lines in `PortalSystem`'s constructor (centre, facing direction, radius, colour, style). The yard layout is in `Level.BuildYard`.
- Set the environment variable `BRUTALFPS_DEBUG=1` to log the player's position four times a second, and every bullet hole and ricochet.
- `BRUTALFPS_PERF=1` logs where the frames went at exit (`[PERF]`): the time per section of the frame, draw calls, garbage per frame, collections and the slowest frame. With `BRUTALFPS_NOVSYNC=1 BRUTALFPS_START=jungle BRUTALFPS_SCRIPT="5::0:90"` it's a repeatable benchmark. `swap (gpu)` is the frame waiting for the graphics card. `BRUTALFPS_PROFILE` turns things off to compare: `nomsaa`, `noportals`, `noprops`, `nobush`, `flat`, `noprepass`.
- **Finding a lag spike** (9 Nov 2026; `Engine/FrameStats.cs`, `Engine/PerfTimer.cs`, `Engine/GpuTimer.cs`). Always on, so a spike that happens once in ordinary play is on record. Every run keeps its own files in `Logs\` next to the exe (the last 30 runs): `<date>_<time>.log` is the whole log, and `<date>_<time>.frames.csv` has a row a second (frame times, GPU, garbage, heap, system CPU, zone, how many people, bodies, bullets and so on, and the time per section and timer). Search the `.log` for these:
  - `[HITCH]`: a frame over 40 ms and over 2.5 x the recent average (`BRUTALFPS_HITCH_MS` changes the 40). STUTTER under 100 ms, SPIKE to 500, FREEZE beyond. `Cause:` is the verdict; below it the game thread's CPU time against the frame's (much less means it was waiting, not working), the GPU's time, GC pause, JIT, garbage, each section and timer, what was going on in the world, what was logged that frame, and the frames round it. Six in full per 5 s, then a line each.
  - `[STALL]`: the watchdog thread, while a frame is still stuck (at 250 ms, 1 s, 3 s, ...): where the game's thread is. So a freeze that ends in a force-quit still leaves its mark. Off under a debugger.
  - `[SLOW]`: a whole second under 30 fps. `[EXCEPTION]`: the first three of each kind thrown during play, caught or not. `[GL]`: OpenGL errors, checked once a second. `[ZONE]`, `[WINDOW]`: moving between areas, losing focus, going fullscreen. `[CRASH]`: where it was and the last frames.
  - `[SUMMARY]` at exit: percentiles, memory, the hitches' causes by total time, and the ten worst.
  - To time something new, add a `PerfTimer` to `Perf` and put `using var timer = Perf.Thing.Time(name);` at the top of the method (`name` is optional: the report names who took longest). Timers marked `container: true` only hold others, so the verdict looks inside them first.
  - `BRUTALFPS_DIAG=0` turns all of it off. `BRUTALFPS_TESTHITCH=frame` sets off a test of each kind from that frame on (a sleep, a busy loop, a garbage collection, a 1.5 s stall). The replaced files are in `Backup_before_diagnostics.zip`.
- **How drawing stays cheap:** the level draws in two passes: depth only first (`Renderer.BeginDepthPrepass`), then colour, so only the frontmost surface at each pixel gets the expensive shader, however many layers of leaves are behind it. Skies and the sea go last, for the same reason. Each prop and box is tested against what's on screen (`Renderer.InView`: the view, its scissor and its clip plane). `Model.MergeInstances` merges copies of a model per 30 m patch of ground rather than all together, or the whole forest would be drawn whichever way you looked. Models keep their vertices shared (`Mesh`'s `indices`), so the GPU shades each once rather than once per triangle. New big scatterings should go through `MergeInstances`; a single `MeshBuilder` mesh of a whole area can't be culled.
- **How a big firefight stays cheap** (6 Nov 2026; the Rio stress test went from 19 fps to about 90-100, its worst frame from 480 ms to 80):
  - *AI update tiers* (`NpcTier`, `Npcs.TierOf`, set on each person every frame): **Attacking** (in a firefight with the enemy in sight) looks round 10 times a second; **Searching** (fighting without sight of them, wary, startled, fleeing, cowering, on the way back) about 7, as everyone used to; **Idle** (calm) 2, or about 7 within `Npcs.NearRange` (15 m) of the camera, so someone close by reacts as quickly as ever; **Far** (calm, over `Npcs.FarRange` = 40 m from the camera) once every 2 s; **Dormant** (calm, in a part of the level nobody can see, not even through a portal) never looks round at all: only what they hear rouses them, and they're updated `Npcs.DormantRate` (10) times a second with the time saved up. What a look takes in grows with the time since the last one, so a slower tier notices as surely, only up to a look later. Anyone scripted or on a ladder or stairs is always updated every frame. Movement is never thinned out for anyone in view (bigger steps could slip through a wall).
  - *Posing* (the animator, the skeleton, the skin: `Npc.AnimInterval`): every frame within 20 m, then 30, 15 and 10 times a second out to 40, 70 m and beyond; 10 for someone off screen (not drawn last frame), 4 in a part of the level out of sight. A scope's power brings those distances in. Between poses they're drawn in the last one where they are now, gun and all.
  - *What's in view* (`Game/Relevance.cs`): the camera and every zone drawn last frame (the main view and the portals', noted by `Npcs.Draw`).
  - *Object pooling*: debris bodies taken out (`PhysicsWorld.ReleaseBody`) wait in a pool of up to 320 for the next `AddBody` to set up afresh, rather than being freed and made again; the debris' own pieces are pooled as well.
  - *Debris level of detail* (`DebrisSystem.Thin`): chips, splinters and shards past 12 m come out half as many, past 25 m none at all (they'd be under a pixel; times the scope's power), and no more than 12 a frame; spent bullets past 25 m aren't made.
  - *Physics steps*: no more fixed steps in a frame than fit in 6 ms (`PhysicsWorld.StepBudgetMs`, going by what a step has been costing). Before, a slow frame asked for up to 6 steps, which made the next frame slower still; now the debris goes into slow motion for a moment instead.
  - *Rays*: a gunshot only looks for a wall between it and a listener when that could change what they hear (`Npc.WouldHearShot`): not within 22 m, where it's loud either way, nor when one that reaches them sooner is already on its way, and no more than 2 a listener a frame (`Npc.TakeHearingRay`). A bullet isn't tested against its own shooter's skin on its way out of the muzzle. A volley of a hundred shots was about 10,000 rays; now it's about 160.
  - *Sounds*: no more than 12 NPC gunshots and 10 bullet strikes start in a frame (`FrameBudget`), and strikes further off than 60 m aren't played. Each took one of the 64 voices and an occlusion ray: a hundred at once only stole voices from each other.
  - Retargeting is a little uneven per person, so a crowd that saw the player together doesn't all look again on the same frame.
- **Loading:** the physics trees are built on all cores (`PhysicsWorld.PrepareMeshes`) and the game's methods are compiled in the background (`Engine/Precompile.cs`).
- You start (and respawn) in the police station's tech room by the navy portal, looking east down the bullpen (`Level.SpawnPoint`). Set `BRUTALFPS_START=yard`, `outback`, `roadhouse` or `range` to start in front of the purple portal, the yellow portal, on the road by the outback roadhouse, or on the firing line of the firing range; `factory` (outside, in front of the sliding door), `shed`, `cellar`, `bunker`, `arctic`, `ship` or `jungle` start you outside the cellar's shed, in the cellar, in the bunker, outside the Arctic hut, on the ship's bridge front, outside the jungle storage shack, or (`police`) in the police station's tech room by the navy portal; `rio` in front of the magenta portal at the foot of the favela, `arcticcontainer` inside the Arctic's second container by its doors; or give `x,y,z,yawDegrees` (in Visual Studio: Debug properties, environment variables).
- `BRUTALFPS_CLIMBSCAN=file` writes every place a vault can climb up onto something (or over into somewhere new) that you can get to, grouped by place, then quits (`Game/ClimbScan.cs`; what it found and what was decided: `Docs/climb_points.md`).
- Test scripts: `BRUTALFPS_SCRIPT="seconds:keys:pitchDegrees[:yawDegrees];..."` holds those keys (W, A, S, D, J to vault (Space pressed every frame of the step), H for Shift, C to crouch; L at the start of a step presses C, prone or back up) with the view at that pitch (and yaw) for that long, step by step, logs the player's position, ladder state and frame time ten times a second, then quits. T holds G (a flashbang's arc: a step without T throws it). At the start of a step, F fires, R reloads, E uses, + respawns and P saves a screenshot (`script_<step>.png` next to the exe). For example, from `BRUTALFPS_START=3032.6,0,-128.6,90` (in the shed, beside the hatch), `1.6:W:-40;1.4:W:-40;0.8:S:0;0.7:W:0;2.0:W:0` walks into the hatch, climbs down, backs off, and climbs back up and out.
- The new areas each sit at their own x (bunker 5000, Arctic 7000, ship 12000, jungle 20000); the shader's `areaOf` and `Level.ZoneAt` must agree on the boundaries (100, 1500, 4500, 5500, 9000, 16000). The police station is the exception: it's everything below y = -500 (`Level.PoliceBelowY`), at (0, -1000, 0) (and likewise the prison below -1500, the flats below -2500 and Rio below -3500, at (0, -4000, 0)), because out at x = 30050 a 32-bit float only resolves about 2 mm and its fine detail (whiteboards, screens, lettering) z-fought. Keep new areas with millimetre detail near x = z = 0 the same way. `Level.KillHeightAt` gives the fall-out height per area. The police station (area 7) is an interior with its own lighting (`officeLight`): up to 24 `OfficeLight`s (`Renderer.SetOfficeLights`; ceiling panels shine down only), a bounce-light level, and an occlusion map from straight above of the furniture on texture unit 15 (`Renderer.SetOfficeOcclusion`, drawn with `Level.DrawPoliceOccluders`: everything in the station's furniture batches plus boxes below 2.3 m). New procedural materials for it: `Carpet` (34), `CeilingTile` (35), `Veneer` (36) and `WallPaint` (37). The jungle's clearing (`Level.JungleClearingCenter` / `JungleClearingRadii`) is passed to the shader with `Renderer.SetJungleEnvironment`, which opens the canopy over it; its sun's shadow map is on texture unit 14. The cellar's box is `Level.CellarBounds`. The ship's layout constants in `Game/LevelShip.cs` must match `Tools/blender/build_ship.py`, and `Level.SeaLevel` must match `SEA_LEVEL` in the shader.
- The game loads its assets from the copy next to the exe, which a build refreshes: after re-running a Blender script, build again before running the game.
- Floor or ceiling portals need the player's "up" to rotate during a teleport. For now `TryTeleport` assumes upright portals.
- For a real HUD, add `Ahwoo.Brutal.ImGui` and replace the window-title text in `Hud.cs`.
- Containers cutting into each other: `BRUTALFPS_CONTAINERCHECK=1` logs every pair of containers round the yard (its own, the quay's stacks and the terminal's blocks) that overlap by more than 2 cm (`[CONTAINERS]`), and how many it checked.
- Screenshots without playing: `BRUTALFPS_SHOTS="name.png@x,y,z,yawDegrees,pitchDegrees;..."` renders each view, saves it next to the exe (with frame timings in the log) and quits. `BRUTALFPS_NOVSYNC=1` turns vsync off; `BRUTALFPS_DUMPVIEWS=1` also saves every portal's view textures (its own and its second-level one) and the security camera's feed (`_cctv.png`, also for test-script screenshots) next to each screenshot; `BRUTALFPS_PROFILE` takes `nomsaa`, `noprops`, `nobush`, `flat`, `noportals`, `noscene` to switch parts of the rendering off when checking performance. `BRUTALFPS_WEAPONPOSE` holds the gun in one pose for screenshots: `reload:0.3` (seconds into a reload), `empty:1.1` (an empty reload), `draw:0.2`, `sprint`, `locked` (slide locked back), `aim`, or `recoil:0.03` / `aimrecoil:0.03` (that long after a shot, at the hip or aiming). In test scripts, M holds aim for the step, G holds the trigger down (full auto), 1 / 2 switch guns at the start of a step, and V / Y turn the mouse wheel a notch towards / away from you. `BRUTALFPS_WEAPON=ak` starts with the AK-47, `=shotgun` the shotgun, `=ump` the UMP40, `=ar` the AR-15 and `=m249` the M249 (the weapon poses then apply to it; `aim` on the AR-15 is the view through its scope).
- Tuning the look: `Renderer.YardWhiteLevel` / `OutbackWhiteLevel` (how bright each outdoor area is by day), `Renderer.YardSunFactor` (how much sun gets through the overcast); for the night yard, `Level.FloodIntensity`, `Level.MastStrength` and `Level.SodiumColor` (the floodlights), `yardFloodReach` in the shader with `Level.YardFloodReach` to match (where the floods are cut off: the stack tops and the quay), `Level.QuayMastRange` (the quay masts' pools), `Renderer.YardNightAmbient` (how dark the shadows are), `Renderer.NightSkyDisplay` (how dark the sky itself looks), `Renderer.NightGlowScatter` and `Renderer.NightGlare` (the lamps' glow and halos); `INDOOR_LEVEL` in the shader (the warehouse), and the reference tints in `Renderer.RefTints` (the brightness each texture set is scaled to).

## Review and optimisation pass (11 Nov 2026)

A pass over the whole project, measured with the benchmark runs (`BRUTALFPS_PERF=1 BRUTALFPS_NOVSYNC=1` and a `BRUTALFPS_SCRIPT`), on the RTX 3050. The sources as they were are in `Backup_before_review.zip`.

- **Physics, the level's static objects** (`PhysicsWorld.AddStatic`): each one is now put to sleep (`ActivationState.IslandSleeping`), as Bullet does with its own static rigid bodies. A plain collision object starts out active, and even with `ForceUpdateAllAabbs` off Bullet works out every active object's bounds again on every fixed step: about 1.1 ms a step for the 8,900 objects, with nothing moving. `physics.step` went from 3.4 to 0.75 ms a frame in the jungle (42 fps, three steps a frame) and from 0.75 to 0.15 ms in the outback. Shots, debris, ragdolls and the doors' moving parts meet them as before (`MoveDoorPart` updates a part's bounds itself).
- **The first frame** no longer pays for the NPCs' map and Bullet's first step (`npc.navFrame` 25 to 90 ms and `physics.step` 30 to 70 ms, a [HITCH] at frame 0 of most runs): `Program.cs` runs `NpcNav.BeginFrame` and `PhysicsWorld.WarmUp` once at the end of loading.
- **Garbage per frame** (outback 15 to 4 KB, jungle with nobody fighting 8.8 to 6.5 KB): `Model.Parts`, `SkinnedModel.Parts`, `Level.Boxes`, `Level.MovingBoxes`, `Npcs.People`, `NpcSenses.People/Portals/PlayerCrossings`, `PortalSystem.Portals` and a few more are now the `List<T>` itself rather than `IReadOnlyList<T>` / `IEnumerable<T>` (a `foreach` over the interface boxes its enumerator every time, and tiered PGO, which could avoid that, is off). `MovingBoxes` keeps one list of the door and the blockers instead of a `Concat` per call; `NpcNav.BeginFrame` keeps its two delegates; the mouse wheel's list and the title's texts are only made on the frames that use them; the named portals (`Hq`, `Vault`, ...) are looked up once.
- **BRUTAL**: the 31 `BRUTALCORE0011` warnings from BRUTAL's analyzer are gone (JSON property names are UTF-8 literals: `GetProperty("id"u8)`), and the build is down from 35 warnings to one (the unreachable code behind `KeypadDoorsHeldOpen`, on purpose). The maths, window, GL, textures, glTF, mesh LODs and audio were already BRUTAL's. What stays plain .NET, and why: `Engine/Log.cs` (Brutal.Logging is a facade over Microsoft.Extensions.Logging with no sink of its own for this run's files and CSV, so swapping it in would change every line's format for no gain); `System.Text.Json` for the small asset files (none of the BRUTAL packages the game references reads JSON); `List`/`Dictionary` for game state (Brutal.Collections' `NativeList` is unmanaged memory for interop buffers); Bullet through BulletSharp (as the README's top says).
- **Bugs**: the helmet's material `shell_tint` had no texture set and was drawn flat, with a [WARN] on every run: `shell` now maps to the polymer set (`TextureLibrary.MaterialForSet`). The factory's `gunman yard` walked a beat that went through the bonnet of the van parked at the hall's door ([WARN] "the way to the next stop is blocked", twice a run): his east stop moved from z = -41 to -43.2 (`Npcs.cs`). Two possible null dereferences the compiler pointed at (`BritishGuns.cs`, `BRUTALFPS_WEAPON` in `Program.cs`).
- **Shade volumes, per view** (`Renderer.SetShadeVolumes`, `SelectShadeVolumes`): the biggest find. The shader goes through every shade volume it has for every pixel, twice (`sunShutOut`, `interiorAmbient`), and it had all the levels' volumes at once: 54, of which 34 are Rio's. That was about half the graphics card's time everywhere outdoors. Each view is now only given the volumes in the parts of the level seen from its camera (`Level.ZonesSeenFrom`, plus any within 500 m), sent again only when the choice changes (a portal's view of another place, then back). The picture is the same: screenshots of the jungle, the outback's shed, the Arctic container (with Rio through its portal), the factory grounds and Rio differ by no more than two runs of the same build do. Frames per second, before and after (RTX 3050, 1600x900, 4x MSAA, vsync off, nobody fighting): jungle 42 to 82, outback 219 to 345, factory grounds 117 to 188, Arctic container 169 to 343, prison intro 284 to 355, Rio 67 to 72 (it still has its own 34). `BRUTALFPS_PROFILE=allshade` gives every view all of them again, to compare. More than 64 volumes in the whole game is now fine, as long as no one view has more. (Also leaving out the volumes outside the view's frustum was tried for Rio and taken out again: 81-86 to 83-94 fps, not enough to be worth the risk through portals.)
- **Seen, not fixed**: from Rio, looking through the slate portal into the Arctic container (`BRUTALFPS_START=rio`, yaw 240), the container's floor comes out dark on some runs and lit on others (a screenshot 1.2 s in: its middle averages 14 or 37 out of 255). It does so with `allshade` too, so it was there before this pass; what decides it wasn't found (the drift's phase, or whether the container's shade volume takes in that view, are the first things to look at).
- **Where the time goes now**: the jungle is still the slowest place after Rio, 12 ms a frame, nearly all of it the graphics card (the game's own thread needs under 3 ms). Its leaf cards are most of that (with `BRUTALFPS_PROFILE`: `nobush` 3.7 ms, `flat` 7.1 ms, `nomsaa` 10.2 ms); they are already merged in 30 m patches and culled by the view, and drawing the depth prepass nearest-first was tried and made no difference (taken out again). Rio's 14 ms would come down the same way as the rest if its 34 volumes were merged or chosen by alley. Fewer or cheaper leaves far off, or 2x MSAA, would help the jungle further, but they change the picture, so they are left for a decision.

## People: clothes and animation

Each NPC wears an **outfit**: one garment per slot (hair, head, torso_base, torso_outer, legs, feet), each with a colour. Outfits come from `Assets/Models/garments/wardrobe.json` (palettes, archetypes such as labourer, security, local and trucker, and fixed picks per person). A garment is a skinned mesh on the body's own 59-joint skeleton, so it moves with every clip. It hides the body regions it covers, and shows bullet holes and blood soaking into the fabric. Garments are `Assets/Models/garments/<id>.glb`, with a `<id>.json` sidecar written by the build.

Clips are in `Assets/Models/anims/human_anims.glb` (Run, IdleLook, IdleShift, ShuffleTurn, Cower, DeathFwd, DeathBack, Talk); Walk and Idle are in `Assets/Models/human.glb`. Their metadata (loop, stride, tags, footstep events) is in `Assets/Models/anims/clips.json`. The `Animator` blends them.

Environment variables for testing:

* `BRUTALFPS_OUTFIT="test subject:tshirt,trousers_work,boots_work"` puts that outfit on the named person.
* `BRUTALFPS_SPECOPS="test subject;patrol"` dresses those people as the raids' allies are (the factory raid's officers, the Antwerp boat team): special-operations kit (`Npcs.SpecOpsKit`: helmet, plate carrier in ranger green or coyote, multicam-toned shirt and trousers, coyote boots) and the helmet gear drawn on the head bone (`Game/NpcHeadGear.cs`: night-vision goggles, down over the eyes when that raid is at night and flipped up by day (`Npc.GogglesDown`), mount, battery pack, strobe, rails, headset ear cups).
* `BRUTALFPS_NPCANIM="test subject:Run"` makes the named person play that clip, looped, in place.
* `BRUTALFPS_NOCLOTHES=1` turns clothes off.

Rebuild the assets (Blender, from the project root):

```
blender -b --factory-startup -P Tools/blender/build_human.py -- regions
blender -b --factory-startup -P Tools/blender/build_clothes.py tshirt trousers_work boots_work
blender -b --factory-startup -P Tools/blender/build_clothes_more.py
blender -b --factory-startup -P Tools/blender/build_anims.py
blender -b --factory-startup -P Tools/blender/build_react_anims.py -- preview
blender -b --factory-startup -P Tools/blender/bake_textures.py -- denim canvas leather knit hair
blender -b --factory-startup -P Tools/blender/bake_textures.py -- sheet
```

Built, measured numbers, test commands and known issues: [Docs/npc_outfits_anim/STATUS.md](Docs/npc_outfits_anim/STATUS.md).

## NPC health, damage and senses (23 Oct 2026)

Files: `Game/Npcs.cs` (state machine), `Engine/Animator.cs` (snapshot crossfade), `Program.cs` (line of sight, gunshots).
The replaced files are in `Backup_before_npc_health_senses.zip`.

### Health and bullet damage
- 100 HP each. Damage per hit (±15%, ×0.85 if the round goes through):

  | Part | Damage | Bleed (HP/s, fades over ~25 s) |
  | --- | --- | --- |
  | Head | 100 (kills) | 1.5 |
  | Torso | 40 (about 3 to kill) | 0.8 |
  | Leg | 24 (and a limp: runs slower) | 0.5 |
  | Arm | 18 | 0.3 |

  Exit wounds add half as much bleeding again. People can bleed out.
- At 0 HP: `DeathFwd` if hit from behind (or bled out), `DeathBack` if hit from the front; head shots fall 25% faster.
  The body stays down, can still be shot (wounds, small flinch), and the player can walk over it.
- **Respawn (+) brings everyone back** alive and unhurt on their routes, for re-testing.

### Senses
- **Sight:** ~160° field of view, up to 32 m, needs a clear line (Bullet static geometry + portal water). Awareness builds
  faster the closer you are, 3× when the gun is pointed at them, slower at the edge of vision, instantly within 2.5 m.
- **Darkness:** how far off the player can be made out depends on the light on them (`Level.LightAt`, checked 5 times a
  second, in `Game/LevelDarkness.cs`): 32 m in daylight, about 13 m under the moon, 30 m under the hall's lamps at night (about 12 m in its dim corners), 7-8 m in a
  dark building by day, about 4 m in one at night, and always within 2 m. It follows the level's time of day, the
  shade volumes, the hall's lamps and the local lights. This also applies to anyone already fighting, who then
  can't aim at a player they can't see. Other NPCs aren't hidden by the dark (only the player is, so far).
  `BRUTALFPS_NODARK=1` turns it off; `[SIGHT] Light on the player ...` is logged when it changes.
- **Hearing:** every shot (`npcs.HearGunshot`) reaches people in the same zone after the speed of sound plus a
  reaction moment. In the open it scares them within 60 m (heard to 95 m); through a wall 22 m (heard to 45 m).
  For a bystander, distant/muffled shots only make them **Wary** (stop, look, IdleLook) unless another follows. Footsteps and other
  noises: see **World sounds and hearing**.
- **The player's gunshots, to an armed enemy** (`ArmedNpc.HeardPlayerShot`): anyone hearing one, and with nobody in sight:
  - **Sees the muzzle flash** (a clear line, not behind them, within 40 m by day to 120 m in the dark) **but can't see the player:**
    turns and fires in that direction for 2.5 s (each new flash starts it again), up to 70 m, 60% wider than aimed fire. Then holds
    aim there or goes to look. `saw the muzzle flash 17.5 m away but can't see the player (light 0.24): firing at the flash`
  - **Hears it but doesn't see the flash:** runs for cover, the nearest spot within 16 m where something solid within 2.5 m hides
    head and chest from the shot, standing on the ground, one person to a spot. Stays there 8-14 s, gun on where the shot
    came from (a pusher half that; more shots add 2 s, up to 20 s), then holds aim or goes to look. With no cover in reach, they do
    that at once. A post-holder (the flats' door gunmen) stays put. `heard a shot 32.5 m away, no sight of the flash: going for cover`
  - **Can see the player:** an ordinary fight, as before.

  Rays are shared out: 2 cover searches a frame (`CoverSearchesPerFrame`), the rest wait. Flashbangs don't count: to them, those are still the old alarm.
- **Knowing their own side** (`Npc.Recognises`: armed people of the same faction; this stands in for shouting and the radio until people have voices, and a callout would come in at `ArmedNpc.HeardFriendlyShot`). Nobody reacts to their own shots. A bystander still takes any gun for a threat. For an armed person, a friend's shot is never the enemy:
  - **They can see the friend:** they look where the friend's gun is pointing (`ArmedNpc.AimPoint`), and a searcher goes there. `factory 9: factory 1 is firing: looking where they're aiming`
  - **They can't:** they come towards the friend. If they're already in the fight, they carry on with it. `factory 7: factory 1 is firing: out of sight, going by the sound`
  - **A friend's bullets striking nearby** aren't a noise to them. **Hit by a friend's round**, they take the damage but look where that friend was aiming, not at the friend (`hit by factory 1's fire: not the enemy`).
  - **A friend where their shot could land** (`FriendInLine`: within 0.6 m of the line, plus the shot's spread at that distance, in front of the target or up to 12 m behind it): they hold fire and step aside for 0.7 s. A post-holder only holds fire.
- **Looking into the lit hall:** from outside the factory's hall, someone in it can be made out from three times as far (`NpcSenses.IntoLight`, `Level.LooksIntoHall`): up to 96 m by day, less in dim light. The walls and the shut sliding door still block the view. The sentry on the road by the gate (`factory 35`, area `gate` in `FactoryBeats`) faces the door from both stops, and spots someone in the doorway at about 72 m once it's open, then comes down the road after them (a pusher). The test for it: `BRUTALFPS_NPCS=passive BRUTALFPS_DOOR=open BRUTALFPS_START=0,0,-24,0 BRUTALFPS_SCRIPT="24::0"`, which logs `factory 35: Calm -> Startled (saw the gun)`; without `BRUTALFPS_DOOR`, it never does.
- **Panic spreads:** seeing someone running away, or a body, within 14 m sets them off too.

### Behaviour (`NpcState`)
Calm → Startled (0.2–0.55 s, turns to the threat) → **Fleeing** (`Run`, steers round obstacles with 12-way ray
probes, stays in its zone and within 24 m of its route) or **Cowering** (`Cower`). Fleeing ends in Cowering when
safe (26 m and out of sight), cornered, or after 16 s. After 14 s without seeing the gun they **return** along the
path they ran, then carry on with the route (and notice the gun faster next time).

Temperaments: **Bolter** (runs) — patrol, road, pumps, car, motel; **Freezer** (cowers) — test subject, dock,
verandah, billboard. Even a bolter freezes when the gun is within 3 m (unless already shot).

### Test switches
- `BRUTALFPS_NPCMOOD="test subject:flee;patrol:cower"` overrides temperaments.
- `BRUTALFPS_NOSENSES=1` turns senses off (old behaviour: stop and face the shooter); damage and death still work.
- **NPCs can carry any of the player's guns** (`Equipment`, `NpcGun` in `Game/NpcProfile.cs`): the pistol, AK, shotgun, UMP, AR-15, G3, M249 (`Lmg`) and the nine British guns from the gun locker (`L85A2` ... `L80A1`). Each fires as yours does: the rifles' `NpcGun`s are built from the same `RifleSpec` (`NpcGun.FromSpec`: round, muzzle velocity, magazine, cyclic rate, loudness), so they can't drift apart; the British guns are registered at startup from their specs (`NpcGun.Register`, before the people are made). Semi-automatics (L129A1, L135A1) fire single shots; the rest fire bursts at their own cyclic rate. Range is per gun (`NpcGun.Handling`: 30 m shotgun to 220 m for the .50). Suppressed guns are quieter and carry less far to people's ears. A gun whose model is missing is drawn as the nearest standard gun (`NpcGun.Fallback`). The magazine is always the gun's (`NpcProfile` no longer has its own).
- **.50 through walls and armour** (`Cartridge.WallPenetration`, `CloseRange`; `Level.ShootThrough`): within 50 m of the muzzle a .50 BMG round (the L135A1) goes through up to 0.6 m of brick's worth of wall, a hole each side, and flies on slower to whatever is behind. Each level box's material weighs it (wood 0.2, sheet iron 2, concrete 1.2, steel 15 per metre), so the outbuilding's 0.4 m brick goes, at an angle too, but not a thick steel door. Only upright walls built of level boxes, not floors, roofs or models. Up to three walls, out of the same budget. Within the same 50 m it goes through NPCs' plates and helmets (`Npcs.Hit(pierced:)`): full damage, sparks and blood. `BRUTALFPS_DEBUG=1` logs `went through (x of y m brick's worth)`.
- **Friendly IR markers** (`NpcHeadGear.DrawMarker`): armed NPCs friendly to the player (raid officers, the boat team) have a small infrared light on the back of the helmet that only shows through night vision or thermal (goggles or a scope): white in night vision, icy cyan in thermal (a colour no heat shows as), glowing and flashing once every 1.4 s, never smaller than a few pixels however far off. `BRUTALFPS_GOGGLES=night` or `thermal` starts with the goggles on (for screenshots).
- **Hostiles carry a mix** (`Npcs.MixedGun`): the level still says rifleman or gunman, and the gun is rolled by name (the same every run) from a weighted table. Riflemen: AK about a third, then G3, AR-15, UMP, shotgun, the British rifles and SMGs, the L129A1 marksman rifle and the M249, and rarely the .50; those in armour lean to the heavier guns. Gunmen: mostly the pistol, else a shotgun, UMP or MP5. The police and the two-handed (scripted) raiders and door gunmen keep their own. Each NPC's gun is in the start-up roster line.
- `BRUTALFPS_NPCGUNS=ak` (or `pistol`, `shotgun`, `ump`, `ar`, `g3`, `m249`, or a British gun by name: `l85a2`, `l135a1`, ...) gives every armed NPC that gun; `mixed` hands them all out in turn. `standard` keeps the level's own guns. The gun is the player's model without its hands (`NpcGun` in `Game/NpcProfile.cs`), held one-handed in the pistol pose (no two-handed clips yet).
- `BRUTALFPS_NPCS=passive` (the `Passive NPCs` launch profile) makes everyone a bystander: guards, gunmen and officers are built as plain bystanders, so they never fight or shoot and carry no visible gun, and cower or flee by temperament.
- `BRUTALFPS_NPCANIM` still forces a clip (and disables senses for that person).
- The log shows every state change and hit, e.g. `[NPC] patrol: Startled -> Fleeing (running from 6.9 m away)`,
  `[NPC] test subject hit in the torso: -38 HP, 62 left, cowering`.

### Sounds
See **Audio** and **World sounds and hearing** below.

## NPC ragdolls and more animations (29 Oct 2026)

Files: `Engine/Ragdoll.cs` (new), `Engine/PhysicsWorld.cs`, `Game/Npcs.cs`, `Game/ArmedNpc.cs`,
`Tools/blender/humankit/reactlib.py` and `Tools/blender/build_react_anims.py` (new). The replaced files are in
`Backup_before_ragdoll_anims.zip`.

### Ragdolls
- BRUTAL does have ragdolls (`Brutal.JoltApi.Ragdoll` in the experimental `Ahwoo.BRUTAL.Jolt` package), but everything
  the bodies must land on is in the Bullet world, so the ragdoll is built in Bullet instead: no second physics engine
  holding a copy of the 1.9 million level triangles.
- 11 bodies, 66 kg: boxes for the pelvis and chest, capsules for the head, upper arms, forearms (hand included), thighs
  and shins (foot included). Cone-twist joints at the waist, neck, shoulders and hips (the hip and shoulder cones are
  tipped forward, since limbs swing much further forward than back), hinges at the elbows and knees. The joint frames
  come from the rest pose, so the limits are anatomical whatever pose the person dies in. The layout is
  `Npcs.BuildRagdollRig`.
- On death the body goes limp on the next frame, from the pose last drawn and moving as it was (each limb's velocity
  and spin from the last two frames), so a runner tumbles forwards and someone cowering folds up where they crouch. The
  killing shot shoves the limb it hit (`Npc.KillPush`, 26 N s), and every shot into a body after that shoves it again
  (`Npc.BodyPush`, 14 N s): shotgun pellets add up. A body's own limbs don't collide with each other (the joint limits
  keep the shape); they land on the level and on other bodies, and debris bounces off them. Shots still test the skin.
- Bodies stay where they fall, asleep (and cost nothing) once settled. Respawn (+) removes them.
- Without Bullet (or with `BRUTALFPS_NORAGDOLL=1`) the dead play DeathFwd / DeathBack as before.

### New clips (`Assets/Models/anims/human_react.glb`)
| Clip | Length | Used for |
| --- | --- | --- |
| HitFront, HitBack | 0.6 s | shot (not killed) from the front: knocked back a step; from behind: lurching forward a step |
| HitLeg | 0.8 s | shot in the leg: the knee buckles, a hand goes to the wound |
| Startle | 0.8 s | the jump of fright on first noticing a gun, a shot or panic (before running or cowering) |
| CheckWatch, Stretch | 4 s, 5 s | idle variants, picked at random with IdleLook and IdleShift (any clip tagged `variant` joins in) |
| ReloadPistol | 2.2 s | armed NPCs reloading (upper body): gun in, a magazine from the belt, slide racked, back to the aim |

People stand still while a hit reaction plays (an armed NPC drops the aim for it and comes back to it), except when
cowering (they stay down). `BRUTALFPS_NPCANIM="test subject:HitFront"` plays any of them looped, as before.

### Looking at things
The head (60%) and neck (40%) turn towards the threat while wary, startled or cowering, and a calm person glances at a
player standing within 5.5 m in front of them (up to 66 degrees either way, eased in and out).

## Hiding in the leaves, searching and holding aim (29 Oct 2026)

Files: `Game/Foliage.cs` (new), `Game/LevelJungle.cs`, `Game/Npcs.cs` (`NpcSenses.Visibility`), `Game/ArmedNpc.cs`,
`Game/NpcProfile.cs` (`LostSight`), `Program.cs`. The replaced files are in `Backup_before_foliage_cover.zip`.

### Leaves are concealment, not cover
- **Bullets go straight through foliage**, as before: `Material.Bush` and leaf parts (`leaf_*` materials) are left out
  of the physics world and `Surfaces.ForPart`, so nothing changed there. (A guard shot through 4.6 "metres of shrub",
  below, still dies.)
- **Eyes don't.** `Level.Foliage` (`FoliageCover`) holds the jungle's leaves as soft balls: every Blender undergrowth
  model placed (ferns, bananas, stemless palms, saplings, shrubs, the wall's front shrubs) has its leaf triangles
  binned into 0.5 m cells (model space, done once per model), and each cell with leaves in it becomes a ball as dense
  as the leaf area in it (0.5 per m of path per m² of leaf per m³). Bare stems and banana trunks hide nothing; a crown
  above head height hides nothing at eye level. The vegetation wall's solid back-row blobs are dense balls, and the
  built-in fallback plants get rough ellipsoids. About 62,000 balls, in a 2 m grid on the ground plane; a sight line
  walks the grid cells under it (2D DDA). No measurable frame cost.
- `NpcSenses.Visibility(eye, at)` = 0 if a wall is in the way (`Clear`, as before) or if the **foliage depth**
  (density × metres along the line) is 1 or more (more within 3 m: +0.6 per metre closer); otherwise
  `exp(-1.5 × depth)`, which scales how fast they notice the gun (glimpsed through thin leaves = slower). The first
  1.2 m at the watcher's end don't count: a guard standing in the undergrowth peers through the leaves in front of
  their face. Used for spotting the player's gun, glancing at a nearby player, enemies spotting each other
  (`SenseMore`) and picking a target (`PickTarget`, which now takes the head if the chest is hidden but the head shows).
- So: **crouch in a bush and they don't see you**; stand up and your head shows over it. Thin leaves only slow them.
  Moving makes noise (footsteps, see **World sounds and hearing**), and gunshots are heard: shooting from a bush
  gives your position away (they come to look, or swing their aim onto it) without revealing you.
- Only the jungle has foliage cover so far. The factory grounds' bushes (`Level.cs`) and other levels have none, so
  everything there behaves as before (walls block sight, nothing else does).

### Losing sight (`ArmedNpc`)
When the enemy they're fighting drops out of sight (a wall, the leaves, another zone) or they come to fight one they
haven't seen yet (after a shot or a comrade's alarm), an armed NPC either **searches** or **holds**
(`NpcProfile.OnLostSight`: `Search`, `Hold` or `Auto`):
- **Search:** walks (gun up) to where the enemy was last seen, up to 3 m further on in the direction they were moving,
  looks round there for 3.5 s (sweeping ±75° either side of the way they came), then sweeps 3 more spots 3–7 m round
  that point (each one they can walk to, in the same zone), looking round at each. If the enemy shows again they fight
  as before (and a searcher who walks within ~3 m of a hidden player finds them: up close it takes more leaves). After
  28 s or the last spot, they give up and walk back the way they came (Returning) and carry on with their route.
- **Hold:** stays put, turned to and aiming at the point where the enemy vanished, primed: the line-up time keeps
  counting while they face it, so when the enemy reappears there the first shot comes at once. After 22–32 s an
  aggressive holder gets impatient and **moves up to search** (as above, 70% as long); anyone else gives up and returns.
- **A shot heard meanwhile is a new lead** (not one of their own side's shots, nor near where they're already looking):
  a searcher goes there, a holder swings the aim onto it.
- **Auto** (every profile now): a defensive NPC always holds, a cautious one always searches; an aggressive one holds with a 25% chance, +20% for
  a Freezer; rolled once per person from their name, so the same people always do the same. In the jungle that is
  about a quarter of the twenty guards holding. `ArmedNpc.Tactic` says which.
- Logged like the other `[NPC]` lines, e.g.
  `[NPC] guard 4: lost sight of the player (in the leaves, 6.0 m): holding aim on where they were for 25 s`,
  `[NPC] guard 12: after a sound: searching`, `[NPC] guard 7: heard something 14.2 m away: going to look`,
  `[NPC] guard 4: nothing for 25 s: moving up to search`, `[NPC] guard 3: the player back in sight after 2.1 s (searching)`,
  `[NPC] guard 15: gives up the search after 28 s`.

### Test switches
- `BRUTALFPS_NOCONCEAL=1`: leaves don't hide anyone (sight as before: walls only).
- `BRUTALFPS_NPCLOST=search` or `hold`: every armed NPC does that when they lose sight.
- `BRUTALFPS_SIGHTDEBUG=1`: once a second, for every armed NPC within 32 m of the player in the same zone,
  `[SIGHT] guard 4: 6.1 m, calm (hold), walls clear, leaves 4.59 -> hidden` (distance, state, tactic, wall
  clearance, foliage depth past the first 1.2 m, and the result).
- `BRUTALFPS_GOD=1`: NPC bullets hit the player (logged `[PLAYER] hit for 24 (BRUTALFPS_GOD: no harm done)`) but do
  no harm, so a test script can sit among twenty riflemen long enough to watch them search.
- Test scripts: `C` holds crouch (Ctrl).
- The checks this was tested with (all from `bin/Debug/net10.0`, a bush 6 m in front of guard 4):
  - `BRUTALFPS_START=20012.7,0,-23.6,100 BRUTALFPS_SIGHTDEBUG=1 BRUTALFPS_SCRIPT="0.6:C:0;8:C:0"`: crouched, nobody
    notices in 8 s (depth 4.6 from guard 4). The same with `::0` (standing): guard 4 sees the gun in ~1.5 s (depth
    0.47). Crouched with `BRUTALFPS_NOCONCEAL=1`: seen within half a second.
  - `BRUTALFPS_START=20012.7,0,-23.6,90 BRUTALFPS_SCRIPT="0.8:C:2;0.4:CFP:2;0.4:CF:2;0.4:CF:2;0.4:CF:2;12:C:2"`:
    shooting guard 4 through the bush from cover: three torso hits, dead.
  - `BRUTALFPS_GOD=1 BRUTALFPS_NPCLOST=hold` (or `search`) `BRUTALFPS_START=20012.7,0,-23.6,90
    BRUTALFPS_SCRIPT="2.2::0;3:C:0;0.1:CP:0;6:C:0;0.1:CP:0;40:C:0;0.1:CP:0"`: seen standing, then crouch: guards lose
    sight "in the leaves", hold (or search), move up, find the player or give up.

## Audio (23 Oct 2026; reverb and occlusion added the same day)

- **Engine:** `Engine/Audio.cs` on OpenAL Soft (`Ahwoo.BRUTAL.OpenAl` 2026.10.3, which needed `Ahwoo.BRUTAL.Numerics`
  bumped to 2026.10.2). `AudioEngine.TryCreate` opens the default device (no device: a [WARN] and silence); 64 voices,
  oldest stolen when all are busy; the listener follows the camera. `Play` = 2D/as recorded, `PlayAt` = in the world
  (mono mix, inverse-distance fall-off). Delays and start offsets line sounds up with animation; `Cancel(tag)` stops a
  group (a reload cut short by a respawn). `WavFile` reads 8/16/24/32-bit PCM and 32-bit float WAVs into 16-bit.
- **Gun sounds** (`Game/Weapon.cs`): each shot plays `Weapons/pistol_shot` (pitch ±3%); a reload plays
  `Weapons/pistol_mag_out` with its click on the magazine release (0.22 s) and `Weapons/pistol_mag_in` with its click
  on the seating (0.90 s).
- **Clips:** cut from your recordings by `Tools/audio/cut_sounds.py` (trim round the click, 110 Hz high-pass, fades,
  16-bit). The originals stay at the top of `Assets/Sounds` and aren't copied into the build; the subfolders are.
  Details in `Assets/Sounds/README.md`.
- **Reverb rooms** (OpenAL EFX): `Game/LevelAcoustics.cs` gives every zone a reverb preset (warehouse = factory large
  room, yard and ship = alley, outback and Arctic = open plain turned well down, bunker = factory medium room) and the
  small rooms inside them their own (container, shack, store, shed, cellar, Arctic hut). A point belongs to the
  smallest room containing it. Each room has its own effect slot, so a tail rings out where it started when you walk
  into another room. A sound sends to the reverb of the room it's made in (2D sounds: the listener's room); a sound
  made in another room also feeds 30% into the listener's room's reverb. Tune a room by changing its preset or `Wet`.
- **Occlusion:** sounds placed with `PlayAt` are muffled (dry level 45%, treble 10%) while the line from the listener
  is blocked (the same test people use for line of sight, so portals block sound too), easing in and out over ~0.1 s;
  re-checked every 0.08 s. A hit within 0.35 m of the sound (the surface it's on) doesn't count. World sounds also get
  air absorption (distant sounds lose treble).
- **Switches:** `BRUTALFPS_VOLUME=0.5`, `BRUTALFPS_MUTE=1`, `BRUTALFPS_REVERB=0` (dry, to compare);
  `BRUTALFPS_DEBUG=1` logs `[AUDIO]` per sound started (with its room and whether it's occluded) and each room change.
- Backups of the replaced files: `Backup_before_audio.zip`, `Backup_before_reverb/`.

## World sounds and hearing (31 Oct 2026)

- **Sounds made in code** (`Engine/SoundSynth.cs`): layers of filtered noise bursts, struck resonances (decaying, optionally
  gliding sines) and crackle (scattered clicks), seeded so each recipe always gives the same sound. `AudioEngine.Provide(name,
  make)` registers one; `Get(name)` uses a WAV at `Assets/Sounds/<name>.wav` if there is one, else makes it. So any of these
  can be replaced by a recording just by dropping in a file of the same name (e.g. `Assets/Sounds/Footsteps/metal_2.wav`).
- **What's there** (`Game/GameSounds.cs`, 4 takes of each unless noted, all made at start-up in ~0.3 s):
  - `Footsteps/{concrete,metal,sheet,wood,dirt,snow,water,soft}_N`: heel + scuff, by what's underfoot (a ray down into the
    physics world -> `SurfaceKind`; carpet and rubber are "soft").
  - `Impacts/{concrete,rock,metal,sheet,wood,glass,dirt,snow,water,soft,flesh}_N`, `Impacts/ricochet_N` (3: the whine),
    `Impacts/flyby_N` (3: the crack of a bullet going past).
  - `Body/fall` (someone killed: the thud ~0.5 s later), `Body/land` (the player landing from a height), `Body/hurt`.
- **The player hears:** their own steps (2D, in time with the view bob: one per 1.96 m; quieter crouched, louder
  sprinting) and landings; everyone else's steps within 35 m, placed at the foot coming down (one per half stride of the
  Walk or Run clip, by how fast they're going); every bullet striking something, by surface; ricochets; an NPC's bullet
  passing within 3 m of the head; bodies falling. World sounds go through the reverb rooms and occlusion as before.
- **People hear noises** (`Npcs.HearNoise(at, carries, byPlayer, loud, frightens)`), in their zone, after the speed of sound
  plus a moment. A noise carries `carries` m in the open and 40% of that through a wall:
  | Noise | Carries | |
  |---|---|---|
  | Player's step: crouched / walking / sprinting | 2.5 / 9 / 17 m | x1.4 on metal, x1.15 wood or water, x0.75 dirt, x0.55 snow/carpet |
  | Player landing | 7-17 m (by how hard) | loud from a big drop |
  | Bullet striking | 16 m hard, 10 m soft | loud; within 4 m it's as bad as a gunshot |
  | Ricochet | 22 m | loud; within 6 m it's as bad as a gunshot |
  | Body falling | 12 m | loud |
  People's own steps aren't noises to each other.
- **Reactions** (`Npc.Noticed`, `Stimulus.Noise`): a bystander going about their business stops and turns to look (**Wary**,
  "heard something 6.9 m away"), so sneaking up behind someone works crouched and not at a run; once they've turned, sight
  takes over (the gun). Noise alone never makes a bystander run. The player's noises only count for people the player's gun
  would worry (an officer doesn't turn at a citizen's footsteps unless the player is wanted). An **armed** NPC goes to see,
  gun up (Fighting, hunting "after a sound"), at a loud noise or a second one while already wary; while hunting the player
  out of sight, the player's footsteps are a new lead (a holder swings the aim onto them, a searcher walks there).
- **Tested** (`BRUTALFPS_SCRIPT`): walking up beside the warehouse test subject -> `Calm -> Wary (heard something 6.9 m away)`
  -> turns -> `saw the gun`; walking up behind `gunman watch` -> Wary -> turns -> Fighting; his shots: `Impacts/flyby` +
  `Body/hurt` per hit. `BRUTALFPS_DEBUG=1` logs every sound played (`[AUDIO]`).
- Tuning: `GameSounds.FootstepGain` / `ImpactGain`; the recipes are at the bottom of `GameSounds.cs`.

## Enemy strategies and body armour (3 Nov 2026)

- **Strategies** (`NpcStrategy`, `ArmedNpc.Manoeuvre`): each armed NPC has one, rolled once from their name (`NpcProfile.Rolled`).
  - **Push:** keeps coming to 5 m, running beyond 12 m (shots 40% wider at a run). Always searches on losing sight, running the
    first leg, and for 1.5x as long.
  - **Cover:** sticks within 10 m of a fighting ally (a pusher by choice, within 25 m) instead of closing in; when the enemy
    vanishes, fires on the spot for 3.5 s to pin them; then follows 4 m behind the ally on the hunt. With no ally to cover, they
    fight the ordinary way.
  - **Hold back:** never closes in; shots 15% tighter. An enemy within 12 m makes them run back for 2 s (no shooting), then stand
    and shoot for at least 3.5 s; cornered, they stand and fight. They always hold aim on losing sight, and never move up.
- **Aggression** (`Aggression`, `ArmedNpc.Manoeuvre`): **Defensive** holds their position and shoots any enemy in range: never closes in, never searches (holds aim where the enemy vanished), shots 15% tighter, and only goes for cover (or the M249 nest) within 4 m. **Cautious** moves up slowly (55% of walking pace) to 10 m, only between bursts, standing still to shoot; on losing sight they always search, at that slow pace, looking round 1.6x as long at each spot and for 1.6x as long in all. **Aggressive** fights by their strategy, as below. Everyone armed fights (there is no passive any more); being badly hurt makes only the self-preserving back off.
- **Who does what:** hostiles without a set plan are, in armour, 70% aggressive, 20% cautious, 10% defensive; without, 40 / 35 / 25. The police are defensive. Strategies are for the aggressive: armoured, 60% push, 30% cover, 10% hold back; without armour, 35 / 40 / 25.
- **Body armour** (`NpcProfile.Armoured`, `ArmourCovers`): one guard in three and the gunman on watch wear the plate carrier
  (`plate_carrier` garment, `Tools/blender/clothkit/garments_more.py`), in place of any jacket. A torso hit does 35% of the damage
  and 25% of the bleeding and doesn't go through: sparks and a metallic hit instead of blood. Head, arms and legs are unprotected.
  The garment fails the clothing checks in crouched poses (Cower), by about as much as `hivis_vest` does.
- **Test switches:** `BRUTALFPS_NPCPLAN=push`, `cover` or `hold` gives everyone armed that strategy; `BRUTALFPS_NPCARMOUR=all` or
  `none` overrides the armour on hostiles. Each armed NPC's temper is logged at the start: `[NPC] guard 1: rifle, aggressive, push, armoured, self-preserving` (gun, aggression, and for the aggressive their strategy).
- **Tested** (jungle, `BRUTALFPS_START=20012.7,0,-23.6,90 BRUTALFPS_GOD=1`): torso hits on guard 4 did 12-16 HP armoured, 36-39
  without (dead in 3). With `BRUTALFPS_NPCPLAN=push` guards end up at 5 m; with `hold` they stay at 15-30 m and fall back.

## Pursuit through portals (31 Oct 2026)

- **The player's crossings are recorded:** `PortalSystem.PlayerCrossed` -> `Npcs.PlayerUsedPortal` keeps the last 8 (portal
  walked into, time) in `NpcSenses.PlayerCrossings` (cleared on a respawn).
- **Following** (`ArmedNpc.LookForCrossing` / `ChasePortal`): an armed NPC hunting the player as a **searcher** (holders keep
  their aim on the spot; an aggressive holder who runs out of patience becomes a searcher and then follows too) checks
  for a crossing since they lost sight. The first must be through a portal within 10 m of where the player vanished
  (they saw them go that way). They jog to just in front of its water and step through: out of the linked portal,
  facing out, in its zone (`Npc.Zone` can now change; `HomeZone` is where their route is). On the far side they
  search from 3 m out of the portal with a fresh search timer, and follow any further portal the player took from there.
  A portal going dark on the way ends the chase (they search where they are).
- **Going home** (`Npc.ReturnThroughPortal`): each portal they came out of is remembered with the trail they had before. Giving up
  (Returning), they walk back along this side's trail, through that portal, and on along the old trail to their route.
  Calm while still away -> Returning. Going back through the portal they last came out of (chasing the player back)
  retraces that step instead of stacking a new one. If the way back has been relinked at a terminal meanwhile, they're
  stranded: they stay where they are.
- **Tested** (`BRUTALFPS_START=police BRUTALFPS_HQLINK=orange BRUTALFPS_NPCLOST=search BRUTALFPS_GOD=1`, shoot down the bullpen
  until the police are after you, back into the Navy portal): `armourer: the player went through the Navy portal: going
  after them (22.1 m)` -> 7.4 s later `through the Navy portal -> Orange portal (Yard)` -> `the player back in sight`. With
  the player hidden from them (a temporary test patch): `gives up the search` -> `through the Orange portal -> Navy
  portal (Police, heading home)`.
- **The police HQ's officers are on your side** (`NpcProfile.FriendlyToPlayer`: the desk officer and the armourer): they
  never target you, even if you're wanted or shoot them, and don't draw on trouble within 4 m of you (your own gunfire,
  someone running from you). They still fight hostiles. The prison's officers still come after you when you're wanted. So
  the tests above that have the armourer chasing the player were made before this; use another officer to repeat them.
- Only armed NPCs hunting the **player** follow (not civilians, not NPC-vs-NPC fights). They go through the same as the
  player does, but they don't path-find: across a cluttered room they may stall, and after 1.5 s stuck within 2.5 m of
  the portal they step through anyway.
- **Seeing through portals** (`NpcSenses.ThroughPortal`, `Portal.LineThrough`): the line of sight is the one the portal's
  view shows, tested for walls and leaves on both sides of the water, one portal deep. Everyone who notices the player's
  gun notices it this way too (as far off as the whole line is long, and with the water as the threat: a bystander runs
  from the portal). The water stops bullets, so an armed NPC doesn't fire: a **searcher** goes through after them, a
  **holder** keeps the gun on the water (an aggressive one runs out of patience and comes through; anyone else holds as
  long as they can see the player). From behind a freestanding portal it's only a window: they walk up and watch.
- **Looking into portals** (`ArmedNpc.PortalToLookIn`): at each next spot, a searcher first goes to any working portal
  within 16 m of them or of where the enemy vanished, that they haven't checked and can walk straight up to, stands 2.5 m
  in front and looks across the doorway for the usual 3.5 s. If the player shows on the far side, they go through;
  otherwise they carry on searching on this side. Logged: `[NPC] armourer: going to look in the Navy portal (6.2 m)`,
  `[NPC] armourer: sees the player through the Navy portal (11.4 m): going after them`.

## Flashbangs (31 Oct 2026)

- **Throwing** (`Game/Flashbangs.cs`): hold **G** and the throw is plotted from your hand: glowing dashes along its path,
  bouncing off whatever it meets and on through any portal it flies into, to a ring where it will go off. Let go and it's
  thrown (13 m/s, a little above where you look, with 60% of your own speed). The fuse runs 1.6 s from the throw. You
  carry 3, shown in the window title; a respawn restocks them (`BRUTALFPS_FLASHBANGS=infinite`: they never run out). Sounds: the pin as you take hold, the spoon pinging off as
  it's thrown, a clank on each bounce (a thud on dirt, snow or carpet), all made in code (`GameSounds`).
- **One simulation for the arc and the throw**: the grenade isn't a Bullet body. Both use the same fixed 1/60 s steps:
  gravity, a ray through the static physics world for the step, and a bounce that loses most of the speed into the
  surface and some along it. The grenade lands where the arc says unless something moves into its path. Portal water
  carries it through to the linked portal (`Portal.ToLinked`); an offline portal's water is something to bounce off.
  It's drawn as a small olive can with its fuse and spoon, tumbling in flight and lying on its side once it stops.
- **The bang**: a white star, sparks, a smoke cloud, a light on everything round it (`Renderer.SetFlash`, for 0.15 s),
  and a crack heard through the reverb rooms. `Flashbangs.Effect(distance, facing, inSight)` decides how hard it hits
  anyone:
  - The **glare** is full within 2 m and nothing past 16 m. Looking away cuts it to a fifth. Out of sight, only the glow
    off the walls reaches you, a little within 6 m.
  - **Deafness** goes by distance: full within 1 m and nothing past 12 m, 60% through a wall.
- **The player**: the view washes white (`Renderer.DrawWhiteout`). It holds for 0.4-4 s, then fades over 2.5 s.
  Your ears ring (`Body/ringing`), and every other sound is muffled: quieter and stripped of treble (`AudioEngine.Deafness`,
  which needs EFX). The muffling sets in a quarter of a second after the bang, so you still hear the bang itself, and
  it fades over a few seconds. Thrown into another part of the map (through a portal), it doesn't reach you.
- **People** (`Npcs.Flashbang` -> `Npc.Flashbanged`): everyone dazzled is blind for 1-6 s. They double over with an arm
  across their eyes (the Cower clip). They can't see the player, run, or shoot (an armed NPC lowers the gun:
  `ArmedNpc.OnBlinded`).
  - Anyone deafened (1-6 s) hears nothing: no gunshots, no footsteps.
  - Afterwards it's a fright like a gunshot beside them: bystanders run or cower, armed people fight.
  - Anyone further off just hears a bang (`HearGunshot`).
  - Dazzling a civilian or an officer makes you wanted, as shooting one does. It hurts no one.
- **Tested** (`BRUTALFPS_START=police BRUTALFPS_GOD=1`, scripts with T):
  - Aimed at the tech room's door frame, the arc showed it bouncing back. It went off where the arc ended, 2.4 m
    behind me.
  - Thrown into the bullpen, it went off 15 m away: the desk officer was blind 3.8 s, the armourer 1.7 s, the
    copier 1.3 s. Then the officers fought and the copier fled, then cowered.
  - Thrown into the Navy portal (`BRUTALFPS_HQLINK=orange`), the arc carried on into the container, and it went off
    in the yard.
  - At 7.9 m, looking at it, the view went white (0.58, eased to 0.82 on screen) and the ringing played.
- Not done: NPCs don't throw them; no burned-in afterimage; with no physics world it falls back to the level's
  collision boxes (untested).

## Frag and smoke grenades (11 Nov 2026)

- **Throwing** (`Game/Throwables.cs`): hold **Q** (frag) or **X** (smoke) to plot the throw, the same way as a flashbang
  (the same `Flight`). Let go to throw. One throwable is aimed at a time: G, Q and X don't mix. You carry 3 frags and 2 smoke
  grenades, and a respawn refills them. `BRUTALFPS_GRENADES=infinite` stops them running out. The window title shows the counts. In test scripts **Q** and
  **X** hold those keys, like **T** does for G.
- **Frag**: goes off 3.2 s after the throw. Within **2 m** (`BlastRadius`) it does its full force (130 HP in the open). Past that,
  fragments do less and less, out to 5 m (`FragmentReach`). The plotted ring is the 2 m blast radius. **Confined spaces hit
  harder**: `Throwables.Confinement` casts 35 rays round the horizon and upwards (open ground doesn't count), and the nearer the walls and ceiling,
  the higher the score. Damage scales up to x2.25 (`ConfinedBoost`), the reach grows by up to 30%, and in a shut-in room the pressure
  gets round cover that would stop it in the open. Measured: the police tech room x2.17, the container yard x1.42, the open outback x1.14.
  People (`Npcs.Blast`) get a wound where the blast meets them, plus stray fragment wounds. Bodies get thrown. Survivors near it are knocked
  senseless and deafened (`Npc.Flashbanged`). Hurting a civilian or officer makes you wanted. It hurts the player
  too, shoves you away and leaves your ears ringing. Loose debris is flung (`DebrisSystem.Blast`).
  Logged: `[FRAG] Boom at ... confinement 0.93 (x2.17 damage)`, `[NPC] copier caught by a grenade 1.7 m off: -100 HP`.
- **Smoke**: pops 1.2 s after the throw, once it's come to rest. It pours smoke for 16 s, and the smoke thins away over the next 14 s. It does no
  damage. The cloud is soft `ParticleKind.Smoke` puffs in their own particle pool. Each puff stops short of the first wall in its
  direction, so in a room the cloud fills the room instead of leaking through the walls. Its size is set by the walls round it, up to
  4.5 m. **It blocks normal sight** for everyone: standing in it greys out your view, and people can't see through it
  (`NpcSenses.Smoke`, from `Throwables.SmokeBetween`, feeds `Visibility`). Night vision doesn't help. **A thermal scope sees
  straight through**: the shader discards smoke particles in thermal, and the in-cloud grey is skipped while you look
  through a thermal scope.
- **How the cloud stays cheap** (11 Nov 2026; standing in one went from 53 ms a frame to about 10, looking at one close by from 20 ms
  over the scene's own cost to 1-2, at 1600x900): a cloud is some 180 puffs, metres across and all overlapping, and running the full lighting for every
  pixel of every one was the cost. Each puff is now **lit once**, where its centre is: `Renderer.DrawParticles(smokeScreen: true)` first draws
  the puffs into a 32 x 32 texture, a texel each (`uParticleBake` 1; the puff's index in the pool rides in its normal's y), then draws them
  with that colour (2), leaving only the puff's soft noisy shape to work out per pixel. Puffs **fade out as the eye gets inside them** and
  aren't drawn at all once it's well in (the vertex shader moves them off screen, likewise in thermal), so the in-cloud grey
  (`DrawWhiteout`) does that job alone. There are a third fewer puffs, slightly bigger. With fewer than 22 texture units the puffs are lit per pixel as before.

## Flares (5 Nov 2026)

- **Who**: about a third of armed people carry two (rolled by name, so the same people every run). `BRUTALFPS_FLARES=all` or `none` overrides that.
- **When** (`ArmedNpc.UpdateFlare`): they're fighting the player, have lost sight of them, and the light where they think
  the player is (`NpcSenses.LightAt`) is under 0.3, which takes in open ground under the moon. There must be no flare
  already within 11 m of there. They think of it every 1.2-2.4 s.
- **How**: with a pistol or shotgun they stop, strike it and throw it from the off hand (up to 22 m, the `ThrowFlare`
  clip, `riflelib.py`). A long gun fires it low and flat out of its launcher (up to 50 m). Seeing the player, being shot or
  being dazzled mid-throw calls it off.
- **Aim** (`Flares.Plan`): each throw is flown ahead through a few angles with the same steps as the real flight, so a
  wall in front or a low ceiling rules that angle out. It has to end within 7 m of the target and at least 3.5 m from the thrower.
- **Flight** (`Game/Flight.cs`): shared with flashbangs, which used to hold this code themselves, and it behaves the same.
- **The flare** (`Game/Flares.cs`) burns for 30 s, sputtering out over the last 4 s, with sparks, red smoke and a hiss (made in
  code, `GameSounds`). Four can burn at once; a respawn clears them.
- **Its light on screen** (`Renderer.SetFlares`, the shader's `flareLights`): red, flickering, reaching 22 m. Once it
  settles, the level is drawn once into six faces of its row in the `FlareShadowAtlas` (texture unit 19), so walls stop the
  light and window frames throw shadows. In flight it has no shadows, so its reach is kept to 9 m. People cast no shadows from it, and a
  door that moves afterwards doesn't update its shadow.
- **Its light to people's eyes** (`Flares.LightOn`): added to `Level.LightAt` up to 18 m, with a clear line. Standing near one
  makes the player visible from up to 32 m. Bystanders don't react to flares.
- **Tested** (all with `BRUTALFPS_GOD=1`):
  - Jungle at night, shooting from the bush (`BRUTALFPS_TIME=night BRUTALFPS_START=20012.7,0,-23.6,90`): with
    `BRUTALFPS_FLARES=all`, guards fired them and they landed near the player; `BRUTALFPS_NPCGUNS=pistol` threw them instead. At the default odds two guards fired. By day nobody did.
  - `BRUTALFPS_FLARETEST=x,y,z` drops one there at the start, to look at its light: outside the factory's office at
    night the window frames show on the office's ceiling and far wall, and the side of the wall facing away stays dark. In the bunker it fills the room. In the lamp-lit hall it hardly shows.
  - The flare logic takes 0.03 ms a frame on average (1.9 ms at worst, with the plans). Frames stayed at 17-18 ms as flares landed in the jungle.
- Not done: the player can't throw them; no flare is drawn in a launcher or lying spent after it burns out.

### Commandos (11 Nov 2026)

- **What** (`NpcProfile.Commando`, `IsCommando`): a hostile who is accurate (0.85), always aggressive (70% push, 30% cover;
  never self-preserving), in a plate carrier and a **helmet**, with a heavy gun shouldered: an AR-15 mostly, now and then a
  G3 or an M249 (`NpcProfile.CommandoGun`), unless the level gives them something else. The log marks them `commando`.
- **The helmet** is a new garment (`helmet`, in `Tools/blender/clothkit/garments_more.py`, built with
  `build_clothes_more.py -- helmet`; it passes the clothing checks, and the preview is `Tools/previews/models/garment_helmet.png`). A hit on the top, sides or
  back of the head, above its rim, is stopped as the plate stops one (sparks, 35% of the damage): `NpcProfile.HelmetCovers`, from the hit's bind-pose point,
  with the same plane as the garment's. Face, jaw and neck are bare. `ArmourCovers(part, bind)` is used for bullets, blows and blasts.
- **The uniform** (`Npcs.CommandoUniform`): near-black long sleeves and trousers, a coyote-brown carrier, a dark olive helmet, cropped hair.
- **Where**: the factory's three office guards who walk the women out on a raid, and the gunman on watch by the boiler
  house; the outback's portal yard (four, with shotguns, `yard commando 1-4`, in place of the five shotgunners), and motel room 1;
  the jungle's nest guard; the first of the flats' door gunmen; Rio's first hostile; one in the Antwerp yard (`yard guard 8`) and one on the ship (`deckhand 1`).
  On top of those, an ordinary hostile is a commando by chance, 3% (`CommandoChance`, rolled by name: the same people every
  run). In the full game that's 16 commandos of 193 armed people. The bunker, the cellar and the police station have no hostiles, so no commandos either.
- **The Arctic** (`arctic commando 1-3`, in `AddStationCrew`): three commandos in **red arctic kit** (`Npcs.ArcticKit`): a red parka with a black
  yoke and a reflective band round each arm, its fur-ruffed hood up, red insulated trousers, black boots. No helmet
  (`NpcProfile.NoHelmet`: nothing on the head stops a round); their armour is under the parka. Since 17 Nov 2026 they are
  inside the research station with its crew (by the bunk, at the heater, at the window), not out on the snow.

### The ship's crew in hi-vis (11 Nov 2026)

- Every `deckhand` wears **hi-vis waterproofs** (`Npcs.HiVisKit`, chosen per name through `Npcs._kitFor`): a jacket with reflective tape (two
  hoops round the body, two round each arm) and trousers black from the knees down with two reflective hoops round each shin,
  yellow (one in four orange), work boots, and a cap, a beanie or bare hair. Those in body armour wear the plate carrier over a
  hi-vis long-sleeved top instead of the jacket (a commando keeps his helmet). The Antwerp yard's crew dress as before.
- **New garments** (`clothkit/garments_more.py`, built with `build_clothes_more.py -- <id>`, previews in `Tools/previews/models/garment_<id>.png`):
  `hivis_jacket`, `hivis_trousers`, `parka`, `snow_trousers` and `parka_hood`, with two new fixed-colour materials, `flat_black` and `flat_fur` (`clothkit/export.py`).
  The hood's face opening and hem are plane cuts, and it hides nothing (the face cut opens the front of the scalp).
- **Their checks**: the hood passes. The other four fail the clothing checks in crouched poses (Cower) and while walking, as `hivis_vest` and
  `plate_carrier` do, and by more (they stand further off the body); the parka also fails P2 at rest. They look right standing and walking in the game.
  The hi-vis jacket (21k triangles at LOD0) and trousers (11k) are over their budgets because of the tape's extra edges.
  Looking down the deck at the whole crew, frames take 6.6 ms.

### Firing range by gun (11 Nov 2026)

- **How far they fire** is each gun's `NpcGun.Range`: pistol 40 m, shotgun **20 m** (was 30: they hold their fire until it'll tell),
  UMP 50, AK 80, AR-15 85, G3 95, M249 100, the British guns 45-220 (`NpcGun.Handling`).
- **How far they see** follows the gun (`Npc.SightReach`, overridden in `ArmedNpc`): in a fight or on the alert, as far as
  their gun reaches (at least the old 64 m), so a rifleman makes the player out at 80-95 m in full light; not on the alert,
  half that (at least the old 32 m). The light on the player still scales it, so the dark hides them as before.
  Beyond their gun's range they keep the player targeted (`PickTarget`) and hold fire.
- **How close they come** while aiming also follows the gun: 45% of its range (9 m with a shotgun, 18 with a pistol, 36-43 with a rifle);
  a cautious one 35% (at least 10 m). Pushers and those holding back are as before.
- **Friends in the way**: the check used the shot's whole cone of error, metres wide at 30 m and more, so a rifleman with
  any of their own downrange kept sidestepping and never fired. Now at most 0.035 rad of it counts (`FriendCone`), plus a shotgun's pellet spread.
- The log has `[NPC] <name> opens fire on the player at <d> m (gun range <r> m)`, once per target.
- **Tested** (factory by day, `BRUTALFPS_GOD=1 BRUTALFPS_NPCGUNS=<gun> BRUTALFPS_START=factory`, firing to start the fight): rifles opened fire from up to 65 m
  (none were further off there), pistols up to 38.6 m, shotguns only within 18.7 m, though they had the player in sight from 64 m.

### Illumination flares (11 Nov 2026)

- **Who**: a flare carrier with a long gun now fires an **illumination flare** instead of a flat shot: up to 150 m off,
  it tops out **100 m** over the ground where they think the player is (`Flares.PlanHigh`: about 44 m/s up, a few rays
  along the climb so a roof or branch rules it out). Hand throwers still throw the red ground flare.
- **The flare**: at the top it pops and lights, white (magnesium), and hangs under a small parachute for **4 minutes**,
  sinking 0.15 m/s (to about 64 m) and drifting 0.1 m/s, with a smoke trail and burning drips. If something stops it on the
  way up, it lights there and falls as a thrown one does. Another isn't fired while one in the sky (or on its way)
  is lighting within 150 m of the place.
- **Its light**: **5 lux** on the ground straight under it, lighting a circle **500 m across** (`SkyRadius` 250 m, fading
  over its outer part), and only outdoor areas. The shader's `flareLights` takes `uFlarePos.w < 0` as one in the sky. Shadows: its own atlas
  row with a far-reaching projection (`FlareShadowAtlas.SkyNear`/`SkyFar`, `uFlareDepthSky`) and a normal offset that grows
  with distance, drawn again whenever it has moved 1.5 m (the light is placed where they were drawn from).
- **Lux to the game's units**: the night is graded the way the eye adapts, so lux are compressed: full moonlight (0.25 lux)
  is about 0.2 in game units, and game irradiance goes as lux^0.37 (`Flares.SkyGamma`, `SKY_FLARE_GAMMA`). So 5 lux is about
  three times the moon's light on screen, and the edge of the circle about the moon's. Inverse-square becomes 1/d^0.74.
- **To people's eyes** (`Flares.LightOn`): anyone in the open under one, with a clear line up to it, is lit (fading over the
  outer part of the circle).
- `BRUTALFPS_SKYFLARETEST=x,y,z` hangs a lit one there at the start.
- **Tested**: jungle at night (`BRUTALFPS_TIME=night BRUTALFPS_GOD=1 BRUTALFPS_FLARES=all BRUTALFPS_START=20012.7,0,-23.6,90`, firing
  from the bush): a guard fired one, which lit at 100.4 m over the player; the clearing and the canopy tops lit up and the forest
  floor under the canopy stayed in its shadow. Drawing its shadows took 4.7 ms, in one frame. No hitches from it.

## Rio's storage room (11 Nov 2026)

- **Where**: behind the red door on the purple house just along the bottom alley from the magenta portal (block 0, the house
  whose front takes in x = -24.1, `Level.RioStoreX`; the door is where the facade always put it, x -24.14). `Game/LevelRioStore.cs`.
- **The door**: E opens it (inwards, 95 degrees, hinged on the right jamb seen from the alley) and shuts it again, like the jungle shack's
  (`UpdateRioStoreDoor`, `RioStoreDoorPrompt`). It won't shut on you in the doorway. It stops bullets and hides you like other doors (`LeafDoors`).
- **The room**: 3.6 x 3.25 m, 2.5 m high, whitewashed, a cement floor, a bare bulb. No sun gets in; the alley's light fades from the door
  (a shade volume, to 0.32, rather than a local light: the shader's 12 are spoken for on some nights).
- **The clothes**: the Arctic's red kit on walnut rails of double coat hooks round three walls: 13 hooks, 7 parkas hung by their hoods and 6 pairs of
  insulated trousers by their waistbands, each a shade different and a little crooked. They're static models made from the garments,
  `hanging_parka` and `hanging_snow_trousers` (`Tools/blender/build_hanging_clothes.py`: built on the body, posed with the arms hanging, flattened
  against the wall, the hood collapsed, given a cloth inside so nothing is see-through, and decimated to about 12k and 5k triangles).
  Previews: `Tools/previews/models/hanging_parka.png`, `hanging_snow_trousers.png`.
- **The favela is unchanged**: the facade rolls as before (the house's lower paint, if any, is drawn round the doorway afterwards), so every other house is where it was.
  If its front ever got no door, the house is built solid (`BuildRioStoreFallback`).
- **Tested**: from the alley, E opened the door and the walk in reached the back wall. In the debug build, frames take 14.7 ms looking along the alley and 17.3 ms inside the room.

## The jungle shack's interior (3 Nov 2026)

- **The door** in the east wall opens with E (`Level.UpdateBodegaDoor`): a hinged leaf swinging out to rest by the rain barrel. It won't shut on you. It stops bullets and, when shut, hides you from NPCs, like the other moving doors (`PhysicsWorld.AddDoorPart`), and a bullet hole in a door moves with it.
- **The olive portal** stands in a plank casing built 35 cm out from the north wall (`JunglePortalDepth`), so the wall behind it is whole. It needs that depth: the player has to fit behind the water (0.3 m radius) for their eye to cross it.
- **The PC** on a plank desk against that wall is a `PcTerminal` wired to the olive portal, a wire running up the wall and out through a hole to the casing (`Level.JunglePcZone`, label `CAMP`; `BRUTALFPS_TERMINAL=camp` holds it open). Like the other two, it connects its portal to any other, dropping what either was linked to.
- **The whiteboard** on the west wall is an old, dirty kanban board in worn red tape with sticky notes, all handwritten in Spanish: one textured quad, because at x = 20000 a 32-bit float only resolves about 2 mm, too coarse for handwriting in geometry. `Tools/make_whiteboard.ps1` paints `whiteboard_albedo/_normal/_orm.png` with System.Drawing (edit the notes there and rerun it; it needs the Ink Free, Segoe Print and Bradley Hand fonts, which Windows and Office ship).
- `MAX_SHADE` in the shader went from 6 to 7 for the shack's shade volume.
- **The main view now projects relative to the camera** (`BeginView(..., relativeTo: true)`, `uProjectFrom` in the vertex shader). At x = 20000 the usual matrix cancels huge numbers, and depth came out wrong by centimetres at any slant to the x axis: the whiteboard vanished into the wall behind it. Views through portals, mirrors and security cameras still use the old matrix, so fine detail far from the origin can still flicker in those.

## The gun nest and the M249 (3 Nov 2026)

- **The nest** stands in the north-west corner of the jungle clearing (`Level.GunNestOrigin`, 22 m from the shack), inside the clearing's ellipse so that no tree comes up through it: a horseshoe of sandbags chest high, open at the back, looking south-east over the clearing at the shack's door and the olive portal. Inside are a plank floor, a tripod, a crate of ammunition tins (one open, its belt drawn out in a loop), belts draped over the bags and spent cases underfoot; behind it is a small stained, mossy concrete bunker with a firing slit, its door shut, with ferns on its roof. The ring of guards walks its beats behind the bunker, and one more man in body armour stands at the gun (`nest guard` in `Npcs.Populate`). The bags stop the player (invisible boxes, `Level.BuildGunNest`) and bullets (the mesh), and they are high enough to crouch behind.
- **The M249** (5.56 x 45 mm NATO, `Cartridge.Nato556`, at 915 m/s) is on the tripod. Stand within 2 m and press E. It's fully automatic only, 750 rounds a minute, and its belt holds 200 rounds + 1; the HUD shows a pip for every five rounds. A small round in a heavy gun kicks little: the climb stops at about 6 degrees however long you hold the trigger (`AimWalk.MaxRise`), so you can use the whole belt, though the spread opens up to about 3.8 degrees from the hip and 1.3 degrees aimed, within about a second of fire. With no bipod it's the least accurate gun in the game: a settled first aimed shot lands in a group of about 40 cm at 100 m. The last 11 rounds can be seen going, as the belt draws up out of its pouch.
- **It's heavy.** With it in your hands every speed is 68% of normal (`Lmg.MoveScale`, read by `PlayerController.LoadScale`), on ladders too. Measured in the game with test scripts, against the pistol:

  | | Without | With the M249 |
  | --- | --- | --- |
  | Walking | 6.0 m/s | 4.0 m/s |
  | Crouching | 3.0 m/s | 2.05 m/s |
  | Sprinting | 9.45 m/s | 6.46 m/s |

  It also handles like a heavy gun: it takes 0.95 s to come up and 0.42 s to reach the eye, lags the mouse further, swings back slowly and bobs more with your stride.
- **Switching and putting it back.** Once you've taken it, it's on 7 and the mouse wheel like the other guns, and it keeps its rounds. The weight only counts while it's in your hands, so you can change to a lighter gun to run. Until you've taken it, 7 does nothing. At the nest, E with it in your hands puts it back on the tripod and brings back the gun you had before. A respawn puts it back on the tripod, with a full belt the next time it's taken up.
- **Reloading (R)** takes 3 s, or 3.5 s after an empty belt. The left hand lifts the feed cover, pulls off the pouch (which falls, as a physics body, with what's left of its belt), hangs a fresh one with its belt laid into the tray, and shuts the cover. After an empty belt it also pulls the charging handle.
- Not done: you can't fire it from the tripod (you have to take it up), and spent links don't fall with the cases. The guards do use the gun: see the next section.
- `BRUTALFPS_WEAPON=m249` starts with it in your hands, and the weapon poses apply to it (`reload:0.6`, `empty:3.0`, `aim`). Screenshots from the game are in `Tools/previews/models/game_*.png`.

## Guards on the M249 (5 Nov 2026)

The jungle's guards can now fire the M249 from its tripod (`Game/NpcGunner.cs`, a part of `ArmedNpc`; `Game/GunNest.cs` keeps track of who has it).

- **Who goes to it.** The `nest guard` is posted on it (`ArmedNpc.PostedAtGun`). When a fight starts with the threat in the gun's arc, he goes to it from up to 30 m away, even if he has only heard something. With the gun empty, any other hostile within 18 m who can see their enemy runs for it, one at a time (`GunNest.Claim`). They won't go if the enemy is within 7 m of the gun, or unless they are at least 3 m nearer to it than the enemy is.
- **On it.** His AK goes on his back and he plays `ManGun`, a full-body clip with bent knees and staggered feet, the butt at the right shoulder, the right hand on the grip and the left hand flat on the receiver by the feed tray. `ManGun` and `ManGunReload` are in `Assets/Models/anims/human_gunner.glb` (`Tools/blender/build_gunner_anims.py`, `humankit/gunnerlib.py`). He is placed from the tripod (`GunNest.Seat`) and goes round it with the gun. It traverses 60 degrees either side of the nest's facing at up to 66 degrees a second, and lays from 8 degrees down to 15 degrees up. The clip only has the gun level, so `PoseOnMount` tips his chest and arms about the tripod's head to follow it and its kick.
- **Firing.** Bursts of 5 to 11 rounds at 750 a minute (`NpcGun.M249`), with a pause of about a second between them. Shots use the M249's bullet speed and round, its sound pitch and a bigger flash, and they come off the gun's own belt, so the rounds he fires are gone when you pick it up. Every fourth round is a tracer (`Projectiles.Spawn(tracer:)`). Each round also makes smoke and throws a case, and the bolt and belt move on the model. The first rounds of a burst land wide and walk in. Against someone standing still at 17.5 m in daylight, with nobody else firing, the nest guard (accuracy 0.45) hit with 17 of about 70 rounds, so about 24%, and his first round comes about 0.7 s after he has the target lined up. Each hit does 30 to 45 damage. An empty belt takes 5.5 s to replace (`ManGunReload`, the feed cover going up), from the tins in the crate.
- **Losing sight.** He fires on where you went for 2.5 s, then holds the gun on that spot for 18 to 28 s. A muzzle flash he sees swings it there for 2.5 s of firing. He holds fire while a friend is in the line in front of the target, and logs it.
- **Leaving it.** He lets go when:
  - the enemy in view has been outside the arc for 0.7 s (`flanked` within 25 m or behind him, otherwise `out of its arc`), or a heard threat has been out there for 3 s
  - the enemy is within 4 m
  - the bags have blocked the line for 2.5 s
  - he has seen nothing for the whole hold
  - his health is below 45
  - a flashbang dazzles him

  His hand then goes over his shoulder for the AK (`DrawRifleBack`, 1.6 s with no firing), and he won't return to the gun for 12 s. A hit that doesn't take him below 45 only stops the burst for 0.45 s, with the spring flinch and no stagger clip.
- **You and the guards.** Only one of you can have it:
  - With someone on it, the prompt reads "The M249 is manned" and E does nothing but log.
  - Someone only on their way to it doesn't stop you. Take it first and they go back to their AK.
  - While you're carrying it, no guard can use it. Put it back and they can.
  - When a gunner dies or leaves, the gun stays laid where it was, with its nose sagging about 5 degrees, and with whatever is left on the belt. It's free at once, for you or the next guard.
  - A respawn clears it all, with a full belt.
- **Log lines.** `[NPC] nest guard: going for the M249, ...`, `on the M249, ...`, `M249 burst of 8 at the player ...`, `flanked: the player 15,4 m away, 76 deg off its line: leaving the M249: ...`, `shot off the M249: ...`, and `[GUN] Can't take the M249: ...`.
- **Test switches.**
  - `BRUTALFPS_NESTMAN=1` starts the nest guard on the gun, and `1,30` does so with 30 rounds on the belt.
  - `hold,yawDeg,pitchDeg` keeps him there with it laid that way, not firing, for looking at the pose (`hold,55,12`).
  - With `BRUTALFPS_NOSENSES=1` the other guards stay quiet, so all you see is him.
  - In test scripts, `E` now presses E at the start of a step.
  - For example, `BRUTALFPS_GOD=1 BRUTALFPS_START=19998.3,0,-3.2,-27 BRUTALFPS_SCRIPT="16::-1:-27"` stands in his arc at 18 m.
- Not done: his feet slide as he goes round the tripod (it's one clip with no steps, though big swings are the only time it shows), the tripod's saddle stays still under the gun as it traverses, the fresh belt is only the feed cover and the hand (no pouch is swapped), and the guards don't lead a running target (the aim updates four times a second).

## The armoured car and its turret gunner (18 Nov 2026)

- **The car** (`Tools/blender/build_armoured_car.py`: `Assets/Models/armoured_car.glb` and `armoured_car_turret.glb`, `Assets/armoured_car.blend`) is a desert-sand 4x4 MRAP-style patrol vehicle: V-shaped belly, slab sides with tumblehome, armoured glass, a black honeycomb grille and steel bumper, big run-flats on black beadlock wheels, flared arches, a locker and steps, firing ports, antennas, and on the roof an armoured turret (an open-backed box shield with a gun window and vision blocks) on a bearing ring. It's parked on the outback plain just south of the shack's fenced yard, nose to the road (`Level.ArmouredCarAt`, `Game/LevelArmouredCar.cs`). Its collision is a lower box up to the gunner's floor and the cabin in pieces round the hatch, so the gunner stands in a hole: their legs are only reachable from above, and they fall into it when killed. The turret's armour (`GunNest.AddPhysics`) is in the physics world as parts that follow it round as it turns, the way the doors' are (`MoveArmour`), and reads as painted metal: rounds spark and ring off the shield, ricochet, and hide the gunner behind it, so only what shows above it (head and shoulders) can be hit from in front. The dark disc drawn over the hatch is left out, so the gunner still drops into it. The gun itself has no collision; its own rounds start at the muzzle, out through the window.
- **The turret** is a second `GunNest` (`GunNest.ArmouredCar`, with its own M249 and belt): it goes right round (`AllRound`), lays from 24 deg down to 31 deg up, and the gun is on a pintle 0.8 m ahead of the ring's middle, so the gun swings round the ring and the turret model is drawn turned with it. The jungle's tripod is `GunNest.Jungle`; `NpcSenses.Nests` holds both.
- **The gunner** ("turret gunner", a commando) is `TurretCrew`: from the first frame they're on the gun in the hatch (`ArmedNpc.CrewHold`), and they never leave it. On watch they sweep the turret slowly to and fro over the yard; a sight or a sound swings it round onto that, and then it's the nest's fight (`UpdateOnGun`: bursts, walking the rounds in, holding on where you went, new belts), except that nothing makes them get off: not being hurt, dazzled (they duck down behind the shield), flanked or too close. Once there's been nothing to fire at for a while they go back on watch. They can't flee or cower from up there (`Npc.CrewStates`). Other guards never go for a vehicle's gun, and you can't take it. A respawn lays the turret back at rest with a full belt.

## The Chinook (4 Nov 2026)

The CH-47 that lowers the container into the prison yard in the intro: `Tools/blender/build_chinook.py` builds `Assets/Models/chinook.glb` (about 33,000 triangles), `chinook_points.json` and `Assets/chinook.blend`; `Game/Chinook.cs` draws it and `Game/PrisonDelivery.cs` flies it. To see the flight without walking through the prison, `BRUTALFPS_START=intro BRUTALFPS_INTRO_STEP=39` starts the intro at the `helicopter:approach` cue. To look at it on its own, `BRUTALFPS_START=prisonyard BRUTALFPS_CHINOOK=0,11.87,0,90` hovers one over the yard's container with the sling on its corners (the numbers are x, y, z in the prison's coordinates, and a yaw in degrees, anticlockwise from above).

- **The flight** (`PrisonDelivery`, all a function of the time since the cue): 14 s in from the north-west over the corner of the wings, slowing and turning to line up with the court, a short hover, 7 s straight down, and the container lands 22.4 s after the cue, when it signals `container:landed` and the script goes on to the doors and the portal. 1.5 s later it lets go, the sling drops onto the roof and stays there, and it bobs up and climbs away east, gone 41 s after the cue. How it leans comes from how its path changes (nose up to slow down, into its turns). The camera follows it with the cinematic's new `Track` step. Enter or a jump past the cue puts everything where it ends (`Finish`).
- **The container** (`Game/LevelPrisonContainer.cs`): its meshes and the boxes in its space are drawn apart from the level, through `Level.PrisonContainerPose`, so what lands is the same container that stands there if the intro doesn't play, with no swap. It hangs rigidly from the helicopter, so it swings forward as the helicopter slows, and it's exactly level when it lands. Its textures are mapped in world space, like the doors', so they slide a little over it in flight. Its colliders never move; the intro has you on rails and nothing can reach them. The crimson portal is hidden (`Portal.Hidden`) and the dim volume inside the container is off until it lands.
- **Shadows and dust**: the prison's sun shadow map is rendered once like the others, but while the helicopter is flying it's rendered again every frame, so the helicopter and load throw moving shadows. (The intro held about 60 fps at 1600x900 like this.) Dust blows out from the container's foot under the downwash, with a burst as it lands.

- **Size and parts**: real size (18.29 m rotors, 11.94 m apart). The nose points along -Z, the origin is on the ground under the middle. Its parts are `airframe`, `rotor_fwd_hub`, `rotor_fwd_blades`, `rotor_aft_hub`, `rotor_aft_blades` and `sling`. The paint takes the tint it's drawn with (`Chinook.Tint`).
- **The rotors**: the forward one turns anticlockwise from above and the aft one clockwise, at 225 rpm, geared 60 degrees apart. They overlap by a third of their width, and the build fails if the blades would pass within 1 m of each other (they clear by 2.28 m). The rotor shafts lean forward (6 and 4 degrees) and the blades are coned, bent and twisted.
- **The blur**: above a quarter of full speed (`Chinook.RotorSpeed`) the solid blades are swapped for a mesh rebuilt each frame (`Chinook.BuildBlur`), drawn by `Renderer.DrawRotorBlur` (shade mode 11). Each blade becomes the wedge it swept during the frame, and the opacity at each radius is the share of the frame the blade's chord covered that point, so the blur is dark near the hub and faint at the tip. A faint disc stays on behind them. It's worked out from the frame time, so it holds up at any frame rate.
- **The sling**: `Chinook.ContainerOrigin` is where a 40 ft container's origin hangs, 10 m below the hooks, lengthways under the helicopter. With the container on the ground the Chinook's origin is 11.87 m up, which puts the rotors at about 17 m, clear of the north wing's roof. `Chinook.SlingAttached = false` takes the sling off it once the load has been let go.
- **Not done**: it has no sound (there's no recording of one in `Assets/Sounds`), its blades cast no shadow, and bullets can't hit it (the intro has you holstered).

## The raiders in the container (4 Nov 2026)

You start a run that begins with the intro with no gun: only a baton (`Game/Baton.cs`), drawn as the raiders open the container, which left click swings until you charge. You take your first gun in the scripted fight at the doors (see below). A blow reaches 1.7 m from the eye (a little either side of the crosshair) and does no bleeding: 55 HP to the head, 22 to the body (a third of that through a plate), less to the limbs, against their 100. The number keys and the mouse wheel change guns only once you've gone through a portal, so until then it's the AK you took. `BRUTALFPS_WEAPON=baton` starts with it anywhere, and `BRUTALFPS_WEAPONPOSE=swing:0.25` holds it that far into its swing, for screenshots.

When the Chinook has set the container down in the intro, it's opened from the inside: `Game/PrisonSquad.cs` runs four armoured riflemen (plate carrier, AK-47) through it, and then they're ordinary enemies. To see it, `BRUTALFPS_START=intro BRUTALFPS_INTRO_STEP=43` starts just before the doors.

- **The opening** (timed to the doors, which take 3.6 s): two stand behind the shut doors with their AKs slung on their backs. They push with both palms (`PushDoor`) and walk out behind the doors, letting go after 1.25 s, when the doors are swinging on their own. Still walking, they reach over the right shoulder and draw (`DrawRifleBack`, 1.6 s), turn on the two officers in front of the doors and spread out either side of the doorway. Two more stand inside behind them with their rifles already up. You now stay where you are until the charge.
- **The slung rifle**: `ArmedNpc.ScriptSling` works out where the gun sits on the chest bone from where the right hand is at the moment of the grab (`DrawGrabTime`, riflelib's `GRAB_FRAME`), so the hand arrives exactly on the grip and the gun doesn't jump when it changes over.
- **New clips** (`Assets/Models/anims/human_rifle.glb`, `Tools/blender/build_rifle_anims.py`, `humankit/riflelib.py`; upper body only, so they play over Idle and Walk): `AimRifle`, `FireRifle`, `ReloadRifle`, `PushDoor`, `DrawRifleBack`. An NPC uses the rifle ones with `NpcProfile.TwoHanded`. So far only the raiders have it; the jungle's guards still hold their AKs like pistols.
- **Being scripted**: `Npc.Scripted` turns off someone's senses and route, and nobody takes any notice of them (the yard officer would otherwise open fire on them mid-cutscene). `Npc.Hidden` isn't drawn and can't be hit. `ArmedNpc.ScriptStart`, `ScriptWalk`, `ScriptFace`, `ScriptPlay`, `ScriptEngage` and the rest are general, for other scenes.
- **Opening fire** (`PrisonSquad.OpenFire`, 2.95 s after the doors start): with their rifles up all four shoot from where they stand (`ArmedNpc.HoldsPost`) at anyone outside their side who isn't spared (`NpcProfile.ShootsAnyone`). The nearest are the door officers and the crowd round the doors, so they go first. They aim lower at anyone cowering (`Npc.AimHeight`). The player is left alone until the intro ends (`ArmedNpc.SparePlayerUntil`), and a stray round does no harm while you're on rails.
- **The fight at the doors** (the script in `PrisonIntro`, the clock from the grab in `PrisonSquad.UpdateGrip`): 8 s after the doors start you charge the nearer raider outside (`GrabSpot`), and your free left hand takes his rifle's barrel and forces it aside (`Baton.Grab`: the AK model's left hand, turned to the barrel, its sleeve stretched back to your shoulder). The other raider outside turns, fires 7 rounds at you (`ArmedNpc.ScriptShoot`) and hits his friend, who's between you. His friend goes down and you have the AK (`GunBase.TakeFrom`: 22 rounds, swinging into the hold). The baton just goes when that happens; it isn't dropped. The other raider plays `ReloadRifle`, the view goes to him, the rifle comes up to your eye and the script holds its real trigger (`Input.ScriptedFire`) for 0.35 s. These two can't lose health until the script says so (`Npc.KeepAlive`, `ScriptKill`), because bullets hit anyone and the raiders inside could otherwise kill them early. After that you look at the doorway.
- **After the intro** (`PrisonSquad.Engage`): you get control, AK in hand, only once Rook is through the portal (`WaitFor("rescue:through")`). The two raiders left inside then fight as normal enemies, the first able to target you 1.2 s later and the other 0.45 s after that. Skipping the intro leaves everything as if it had played: Rook gone, the two outside dead, the AK in your hands. After a respawn all four stand guard at their posts and start again on the crowd. They don't exist unless the intro has played.

## The prison yard's crowd (4 Nov 2026)

In a run that starts with the intro the yard holds 50 people (`Game/NpcsPrisonYard.cs`, directed by `Game/PrisonSquad.cs`): 42 prisoners in their own clothes (Dutch prisoners don't wear a uniform), Rook, and seven officers in navy (the existing yard officer, four more on beats round the edge of the yard, two for the doors). The prison's officers carry batons, not guns (`NpcProfile.PrisonOfficer`, `Equipment.Baton`), so under fire they run or cower like the prisoners. The four raiders are on top of that. To see it, `BRUTALFPS_START=intro BRUTALFPS_INTRO_STEP=38` starts as you walk out into the yard. Without the intro the yard stays empty.

- **Talking in circles** (`YardCircles`): 36 prisoners stand in eight circles of three to eight, facing the middle and taking turns with the `Talk` overlay (`NpcProfile.Talks`, `Npc.Chat`). The other six walk the west half of the ring path in pairs (`RingWalkers`). All of it keeps clear of where the container lands, your walk in from the yard door and your view of the doors. Add or move a circle by editing the list.
- **The crowd at the doors** (`FrontCircles`, `PrisonSquad.FrontPosts`): 0.8 s after it lands, the last two circles, 14 people, walk over and stand in front of the doors, either side of the way Rook comes and clear of your charge.
- **The helicopter**: from 7 s after it sets off, everyone stops, a few at a time, and stares at it (`Npc.Watch`), until the doors open.
- **The player** (`PrisonIntro.EndPosition`) waits on the court at the container's south side, to the left of it as you look at its doors, until the charge. The camera stays on the doors. The portal comes online inside when you grab the barrel.
- **The two door officers** walk over 1.5 s after it lands and stand 2.3 m in front of the doors, clear of their swing, one in front of each leaf, batons in hand, until the raiders open fire.
- **Rook** (`Npcs.RescueName`, the placeholder name of the prisoner being rescued; white top, jeans) stands alone by the east wall. 5 s after it lands he walks straight for the container (`PrisonSquad.UpdateRescue`), between the two officers, in at the doors and between the two raiders inside, and through the crimson portal as soon as it's online, just before you get control. He runs the last few metres to it. At the far end he runs on to the stairwell's door at the flats: see [The block of flats and Rook's escape](#the-block-of-flats-and-rooks-escape). (If `BRUTALFPS_INTRO_LINK` sends the portal anywhere else he walks on 2.5 m to leave the way clear, and stays there, ignored by the people there as he's still under the script.) After a respawn he's where his way out ends. A script holds him the whole time, so he doesn't react to anything. Nobody targets him and NPCs' bullets pass through him (`NpcProfile.Spared`), so stray rounds can't hit him. The player can still hurt him.
- **The panic** is the ordinary NPC behaviour: the shots send prisoners and officers running or cowering by temperament. Raiders' bullets hit anyone, so who dies differs from run to run.
- **The player's gun**: `NpcSenses.PlayerArmed` is now false during the intro. It was always true, which would have made the crowd panic as you walked through.
- **Not done**: the raiders don't follow Rook out, except any who chase the player through the portal. How the raiders treat the crowd is their ordinary fight behaviour (`NpcProfile.Rifleman(0.6f)` in `AddRaider`), so how many die varies. Officers with batons never use them on anyone.

## Hostiles at the docks: on the stacks in Antwerp and on the ship (5 Nov 2026)

- **The crews** (`Game/NpcsShipYard.cs`): 10 hostiles in the container yard at Antwerp and 50 on the container ship, mostly AKs
  (one in four a pistol, one in four armoured). `BRUTALFPS_NODOCKCREW=1` leaves them out.
  - **Yard:** six up on the stacks (three on the west block over the open container, one along each three-high middle of the
    north and south rows, one on the east stacks) and four on the ground. There are no ladders in the yard, so the ones on top
    stay up there and fight from the stacks.
  - **Ship:** on each stack with a ladder, one walks its top and one goes up and down; 12 walk the walkways either side of the
    hatches, 4 the stern, 2 the forecastle, 2 the deck under the bridge; the rest (14 or so) stand watch on the tallest other
    tops, with no ladder down (they fight from up there).
- **The yard's west block is one floor on top** (`Level.BuildYardRoofs`): an invisible slab bridges the gaps between its
  containers (and the ends of the north and south rows), so the guards up there can walk along it.
- **Ladders onto the ship's stacks** (`LevelShip.cs`), which the player can climb too: one up the end of a stack wherever the deck in front of it is open (the aft end of the first bay and of each bay
    behind a crane, the forward end of each bay in front of one): 7 in all, each up that bay's widest run of stacks.
- **The ship's stacks collide at their own heights now.** Each bay used to be one box as tall as its tallest stack (you'd stand
  in the air over a lower one); now each run of side-by-side stacks of the same height is a box of its own, and an empty slot is
  a gap. `Level.ShipStacks` and `Level.ShipBays` hold the runs and the bays for the crew's placement.
- **`BRUTALFPS_NAVCHECK=1` follows ladders and stairs** when it checks each route leg, so a leg from the deck to a stack top
  reads `round by N legs, 1 links` instead of stopping short at the first floor.

## Fifty in the factory, and fighting in the dark (5 Nov 2026)

- **The crew** (`Game/NpcsFactory.cs`): 50 hostiles in and around the factory, each walking a short beat: 11 in the hall, 2 in its
  office, 6 in the boiler house, 7 in the outbuilding, 7 in the second hall and 17 on the grounds. One in three carries an AK, the
  rest pistols; one in four wears armour. `Npcs.CheckFactoryCrew` checks every beat at startup (on the ground, not
  inside anything, room to stand, a clear walk) and logs `[WARN]` for any that isn't, then
  `[NPC] The factory: 50 hostiles (...); 0 bad spots on their beats`. `BRUTALFPS_NOFACTORYCREW=1` leaves just the three gunmen.
- **The smaller buildings can be walked into:** the outbuilding has lost the black box that filled it (in by its two open
  doorways), and the second hall is now a shell instead of a solid block, with its roller door stuck part-way up (van alongside, 2 m
  clear each side), columns and crates. They're dark inside (`Level.FactoryShadeVolumes`, with `ShadeVolume.NoSun`), the boiler
  house's ground floor too. They have no reverb rooms of their own yet: they sound like the grounds.
- **Frame time**, Debug build, RTX 3050, uncapped (`BRUTALFPS_NOVSYNC=1`), standing in the hall at night, door open,
  firing a shot every 2 s: 4.8 ms (208 fps, 99% 8.3, worst 11.7) with the crew, 3.1 ms (319 fps) without. NPC updates take 2.2 ms against 0.8:
  every person is updated in every level, so the other levels pay that extra 1.4 ms too. Outside, looking at the hall: about 7 ms.
- **Senses:** see **Senses** above for darkness and the reactions to the player's shots. Standing still on the
  grounds 16-20 m from a patrol at night for 20 s: nobody noticed (with `BRUTALFPS_NODARK=1`: 50 startled, 10 hits on the player).

## The factory raid (8 Nov 2026)

In Visual Studio, pick the **Factory raid** launch profile (it sets `BRUTALFPS_START=raid`). The game opens on one screen, asking how you're getting in, then begins outside the factory's wall. There's no day and night menu: a raid is always at night (without touching what the start menu has saved). It loads the factory and the police HQ and nothing else: the factory's blue portal opens onto nothing, the HQ's navy portal starts offline, the vault portal is there behind its airlock, and no other level, portal, person or texture set is loaded, so it starts faster than the full game. The guns all load, as the HQ's armoury has them on its locker. Esc on that screen quits. Nobody can spot you until you have control (`NpcSenses.PlayerConcealed`), and if you die after that you come back where you got in, not at the police HQ. Enter skips the way in.

- **Over the fence**: you walk up to the west wall out of the grass, jump, get your hands on top of it, haul yourself over and drop down at the edge of the grounds, under a tree, with the whole site ahead of you. About 11 seconds.
- **In the van**: you sit on the floor of a white Transit's load space, against the bulkhead, under a dim bulb, looking out of the back windows. It stops at the north gate while the leaves are unchained and swing in, drives on past the sentry (you can watch the gate shut behind you), then leaves the road, loops round over the grass and parks 30 m north-west of the hall with its back to the open ground. The back doors open and you climb out. About 35 seconds.

`Game/FactoryRaid.cs` builds both scripts from the same `Cinematic` as the prison intro, with two new steps: `Carried` takes the player's body out of the physics (`PlayerController.Carried`) and `Glide` moves it through anything. `DeliveryVan` is the van: its road, sampled by distance with a speed limit at every sample for the bends and the stops; its lined load space (plain colours, as the textured materials are mapped in world space and would crawl across the walls of something moving); and the doors. Its body isn't drawn while you're inside it. The van that parks is the same one as the 14th of `BuildVans`, which is solid all along but not drawn until it arrives (`Level.ArrivingVanHidden`), so people's paths and bullets know about it from the start.

**The women in the office** (`Game/FactoryEvacuation.cs`): four civilians stand in the hall's office, on a raid only (`Npcs.AddOfficeWomen`), with three guards of their own (`Npcs.OfficeGuards`, in place of the office's two patrols). There's one body mesh, so the women are that body reshaped through the skeleton per person (`NpcProfile.Woman`: 5% smaller, wider hips, narrower waist and shoulders; the clothes follow) with the new `hair_long` garment (`clothkit/garments_more.py`). The guards are held in the office by the script, deaf to whatever goes on outside. The player setting foot in the hall, by any way in (the rear stairs or the front), sets them off: they rush the women in a line across the hall and up the dock's steps into the blue portal, covering them from behind and then from posts on the dock. A woman who reaches the water is gone from the world. The player never sees where it goes: on a raid the portal is `Portal.Dark`, with live-looking water and lamps over pure black from every angle, linked to nothing, so the player can't enter it and no PC can dial it. When the last woman is through the guards go back to fighting. A respawn puts the women and guards back.

**The rear entrance** (`Game/LevelFactoryRear.cs`): in the hall's south wall, east of the office. A steel staircase up the outside of the wall (the only steps in the factory the player can walk up: `Level.HasStairs`) to a landing, a steel door (E) at the height of the concrete band, a walkway along the inside of the wall, and a ladder down to the floor. The crew can use all of it too.

**The way to the HQ**: since the portal can't be entered, the white van parked north-west of the hall is how you get to the police HQ on a raid, however you came in. E at its back doors fades out and you're standing inside the HQ's exit door (the one signed EXIT in the bullpen's north wall); E there brings you back to the van (`FactoryRaid.UpdateWayToHq`). Nobody can spot or shoot you during the ride.

**The army** (`Game/FactoryArmy.cs`, `Game/NpcsFactoryArmy.cs`): a hidden clock starts when the raid goes loud: in the van, when it drives off having dropped you; over the fence, the first time any of the factory's crew is hit, killed, startled or fighting, or a shot's fired. Four minutes later the north gate opens and a column comes up the road (west of its middle) and stops nose to tail from z -60 back to -120: four of the desert armoured cars, each with a gunner left up in its hatch (holding post), and three olive army trucks with canvas tilts (`ArmyTruck`, built from boxes and cylinders, after an Indian Army 4x4), fifteen troops jumping down from the back of each and walking for the front of the hall's big door. They're on the factory's side. Nothing on screen counts it down. The vehicles have no collision yet, and the gunners don't work the turret's gun (they fight with rifles). `BRUTALFPS_ARMY_DELAY=s` changes the four minutes; `=-1` has the column already parked and empty, for looking at it.

Debug: `BRUTALFPS_START=fence` or `=van` skips the choice and goes straight to that way in; `BRUTALFPS_INTRO_STEP=n` starts at its n-th step, as with the intro; `BRUTALFPS_START=raid` with `BRUTALFPS_MENUSHOT` saves the choice screen.

## The Antwerp raid (13 Nov 2026)

In Visual Studio, pick the **Antwerp raid** launch profile (it sets `BRUTALFPS_START=antwerp`). The game opens on one screen asking how you're getting into the container yard at Antwerp; Esc quits, Enter skips the way in once it's playing. Unlike the factory raid it loads the whole world. Under the two cards, DAY or NIGHT sets the yard's time for either way in (Up / Down, W / S or N swap it, or click one); it starts as the yard was last played and isn't saved. `BRUTALFPS_START=boat` or `=abseil` uses the saved time, or `BRUTALFPS_TIME`. The opening captions follow it. Nobody can spot you until you have control, and a death from then on brings you back near the end of the way in.

- **By boat** (`Game/AntwerpRaid.cs`, `RaidBoat`): you sit in the port bow of a rigid inflatable (grey tubes, console, twin outboards, built from boxes in code) with three of your own, the **boat team** (`BoatTeam`, `Npcs.AddBoatTeam`: police faction, friendly to you, helmets and plates, carbines). It crosses the dock from the south-west, swings in and ties up nose-north against the quay north-west of the yard, against a concrete landing ledge at the foot of the quay's face, with rubber fenders, and two steel ladders up from it to the kerb (`Level.BuildQuayLanding`, `Game/LevelAntwerpRaid.cs`; only to look at: you can't get down to it in play). Everyone steps off onto the ledge: the team member beside you goes up the north ladder just ahead of you, the other two take the south ladder one after the other, and they take cover by the ladders while you climb. From then on they come with you: each keeps a few paces back along the way you've walked (`Npc.MovePost`), and fights whoever shows. Five more of the yard's crew walk the open quay where you land (`Npcs.AddQuayGuards`, `quay guard 1-5`, one a commando), so you fight your way in. About 30 seconds.
- **Down the rope**: alone, on top of the four-high stacks along the yard's east edge. You look down into the lane, clip on, back up to the edge, lean out and go down the end of the container in four bounds (`AbseilRope`: tied off on top, over an edge protector, the spare in a coil at the bottom), into the dead end of the lane between those stacks and the stacks under the crane, then turn to look down the lane. About 15 seconds.

Debug: `BRUTALFPS_START=boat` or `=abseil` skips the choice; `BRUTALFPS_INTRO_STEP=n` starts at its n-th step; `BRUTALFPS_START=antwerp` with `BRUTALFPS_MENUSHOT` saves the choice screen.

**The rest of the terminal** (`Game/LevelYardHinterland.cs`, `Level.BuildHinterland`): for show only, the quay goes on 650 m north and south of the play area along the water and back east to x 900 (from the yard's origin), covered in blocks of stacks (4 long, 6 rows, 1-5 high, lanes between), with flood masts over the lanes (lit by night, with pools of light under them), five straddle gantries, and a container ship tied up at each end of our quay. Nothing out there is solid; the yard's bounds still stop you at its own stacks. The blocks within 70 m of the play area use `container_lod`; the rest are plain boxes (four sides and a top), merged into one mesh per colour.

## The block of flats and Rook's escape (4 Nov 2026)

Modelled on Google Street View photographs of a Dutch post-war block (`Game/LevelFlats.cs`, `Level.FlatsOrigin` = (0, -3000, 0), `Zones.Flats`): where the crimson portal in the prison yard's container now leads when the intro brings it online.

- **The block**: four storeys of red-brown brick (mesh, 48 x 12 m) with white-framed windows, some curtained, thin black rails, blue strips between the bays and a grey sill band at each floor, over a white fascia with a small blue street sign and a number board. The ground floor is an open undercroft 18 m wide and 12 m deep on three rows of square columns, under a 3.1 m ceiling, between dark grey walls. Its tarmac floor is 0.6 m below the street: a ramp comes down between two concrete planters full of shrubs. Past the rear of it are more tarmac, grass, hedges and the next block. In front: paving, staked young trees, a grey utility box, the road, a parked car and the block across the street.
- **Where you can go**: only the undercroft and the stairwell. Both open sides are blockaded (`Game/LevelFlatsBarricades.cs`) with rusty cars, big wheeled refuse bins modelled after the 1100-litre kind, pallets, drums, tyres and scrap. An invisible wall runs along each side from wall to wall, under the fascia, so you can't get out over or between them; bullets stop on what's actually there. The rest of the block is solid. The street and rear are still built (and still have their own edges) for when a barricade comes down.
- **Light**: it's always day, as in the prison, so it isn't in the start menu's list. The shader's areas 11 (outdoors, undercroft) and 12 (stairwell) are picked by `areaOf` below y = -2500. Area 11 uses the prison's sky and sun and its own shadow map (texture unit 18). The undercroft's dark is two `FlatsShadeVolumes` meeting in the middle, each fading from its own open side down to 2% of the sky's light. `MAX_SHADE` went from 7 to 9 for them: the list was already full. Area 12 uses the office lighting path with almost no bounce, lit by a caged lamp on every landing (`FlatsLights`) that flickers in the shader (`flatsLampFlicker`): those on even floors hold steady with the odd stutter, those on odd floors are failing and mostly out. The cobalt portal's glow is a local light, added last because there's room for only 12. On a night when the outback, jungle and ship are all dark it's the one that drops out.
- **The door and stairwell**: a pale steel door in the undercroft's east wall (E opens or shuts it, and it won't shut on you) leads to a cramped dog-leg stair, 2.6 m wide with a spine wall, up four floors at 1/6 m a riser. Flats' front doors stand on the landings, and a wall closes off the lane that would carry on up from the top one. The one on the top landing is Rook's and says "Locked".
- **Steps**: the player couldn't step up anything before this, so `PlayerController` can now step up 0.2 m and down 0.26 m, with the view easing to follow. This only applies where `Level.HasStairs` says so, which is the flats (y < -2500). Everywhere else moves as it did. The ramp is a sheet of tarmac over twelve 5 cm steps. A script walking someone to a point now takes them to its height too, so Rook goes up the stairs in a straight line from point to point; his feet don't land on the treads.
- **Rook's escape** (`Game/RookEscape.cs`, started by `PrisonSquad.RescueGoesThrough`): out of the cobalt portal he runs 16 m across the undercroft and stops at the door, looking back. He waits there, for as long as it takes, until the player is in the flats zone, so you see him go however long the raiders kept you. Then he opens it, runs through, it shuts 1.2 s later (unless you're in the doorway), he runs up all four floors, about 20 s, unlocks the door on the top landing and is gone behind it. After a respawn he's in the dark hall behind that door. A bullet of yours can still kill him; his script just stops then.
- **Reaching it**: `BRUTALFPS_START=flats` puts you in the undercroft (the portal is offline in that run). The portal index lists cobalt as `198.51.100.113`, so the HQ's terminal can connect to it. `BRUTALFPS_INTRO_LINK=navy` puts the old link back.
- **The way on**: 3 s after you come out of the cobalt portal the link to the prison drops (`PrisonIntro.UpdateLink`; step back through in time and the clock stops, so you can't be cut off in the prison). A PC on a table against the west wall, north of the portal and wired into its ring (`Level.FlatsPcZone`, `BuildFlatsPc`), is the way out. "[E] Use PC" opens the usual terminal, but there's nothing to choose (`PcTerminal`'s `autoConnect`): once the index has printed the player types `portal --connect prt://112.112.112.112` by themselves, the cobalt portal connects to the gold portal in the police HQ's vault, and the window closes.
- **Not done**: Raiders chasing you through the portal can walk the flat undercroft but can't climb the stairs or open the door. Rook has no door-opening animation.

## Getting about: paths, walls and arms (5 Nov 2026)

People used to walk in a straight line at whatever they were after, and stop dead if one ray at knee height hit something. Nothing kept their shoulders, arms or guns out of a wall. On a route they didn't even look: walking back to the nearest stop after a scare could take them through a wall. Now (`Game/NpcNav.cs`, `Npc.GoTowards`, `Npc.MoveAlong`, `Npc.KeepArmsOutOfWalls`):

- **Paths:** if a body can walk the straight line to where they're going, they do. If not, they find a way round: through doorways, round corners and round a building to its door. A search has two steps, so a long way costs little more than a short one: first over 6.4 m chunks, from each connected piece of open floor to the pieces next door, for a corridor; then A* over the 0.2 m cells of that corridor, pulled tight into straight legs. If there's no way there, they go to the nearest place they can get to.
- **Who uses them:** everything that heads for somewhere: closing in on an enemy, covering an ally, running for cover, searching, chasing through a portal, walking back along their trail, and walking their route. Fleeing still steers by rays (`Steer`), but won't pick a line a body doesn't fit along or that runs off a ledge. Scripted walks (`ScriptWalk`) go exactly where the script says.
- **The map:** the same collision boxes the player walks on, kept per floor and built as people first go there. A cell is open if there's a floor under it and nothing within `NpcNav.WallRadius` (0.3 m) between 0.2 m and 1.8 m above the feet.
- **Doors:** what moves (`Level.MovingBoxes`) is looked at every frame, so a shut door stops a path and an open one lets it through. People don't open doors themselves: at a shut one, they stop at the nearest point.
- **Other floors:** people stay at the height they're at, as they always have. They don't climb stairs or ladders by themselves, so an enemy on another floor gets as near as that floor allows.
- **Walls:** a step (`NpcNav.Slide`) goes an axis at a time, so they slide along a wall at their own pace instead of stopping, and don't step off the edge of a floor. Someone put too near a wall is eased out. What isn't a collision box (a prop with no collider, a portal's water) is still felt for with a ray and stays on the map for 20 s after they bump into it (`NpcNav.Learn`).
- **Arms and guns:** within 40 m of the player, each frame, the line from the chest to each forearm, hand and the gun's muzzle is checked against the level, in the pose as animated. What goes through a wall in front of them is brought back by swinging both arms down about the line through the shoulders (which keeps a two-handed grip on the gun), by just as far as it takes. If they've come to a stop, they also step back off the wall until there's room. A gun swung down more than 0.3 rad doesn't fire: a round from there would start beyond the wall.
- **Crowds:** walking, people ease aside from anyone within 0.65 m (`Npc.MakeRoom`), so a crowd going through a doorway doesn't end up all in one place.
- **Cost:** at most 3 searches and 3 ms of searching a frame; the rest walk on or wait for the next frame.
- **Debug:** `BRUTALFPS_NAVCHECK=1` logs (`[NAV]`) each route leg that needs a path or stops short, three test ways through doorways in the factory, and every 5 s how many poses still have an arm through a wall, who got stuck and where, and what the searches cost. `BRUTALFPS_NAVMAP=x0,z0,x1,z1,y` also logs that part of the map as text. Test with vsync on: at several hundred frames a second, `BRUTALFPS_NOVSYNC` makes the numbers look worse than they are.
- **Tested** (Release, in the game): `BRUTALFPS_NAVCHECK=1 BRUTALFPS_GOD=1 BRUTALFPS_DOOR=open BRUTALFPS_START=5,0,24,190 BRUTALFPS_SCRIPT="0.5:F:0;25::0"`, in the factory's office. Enemies come round the outside of the hall, in by its door and through the office door to the player. Without `BRUTALFPS_DOOR` the outside ones stop at the shut door. Searches took about 0.2-0.7 ms (worst 6.6 ms), and an arm or gun was still through a wall in up to 9 poses per 5 s window of about 7000, for a frame or two while the arms swing. A fight in the jungle (`BRUTALFPS_START=20012.7,0,-23.6,90`) gave similar numbers. The police station and the prison intro ran without trouble, but nobody fought there in those runs.
- The "motel" bystander's first stop was inside the motel's solid block: it's now beside it.
- The replaced files are in `Backup_before_npc_nav.zip`.

## Ladders, stairs and doors (5 Nov 2026)

People can now change floors and open doors by themselves (`Game/NpcNav.cs`, `Game/LevelNavLinks.cs`, `Npc.GoTowards` and `Npc.UpdateLink` in `Game/Npcs.cs`).

- **Floors and links.** Each floor is still a flat grid. A `NavLink` joins two floors: every ladder in `Level.Ladders` gets one automatically (where to stand at the foot, and where the floor starts at the head, are found by probing; a ladder with nowhere to stand logs a warning), and each flight of stairs is added by hand with `AddStairFlight` (the flats' stairwell has eight). A new ladder needs nothing else; new stairs need their flights added. A flight's steps are marked as keep-off on the floors at either end, so nobody walks out level over them.
- **Finding the way.** The goal's height says which floor it's on (`NpcNav.FloorHeight`). If it's on another floor, or can't be reached on this one, a Dijkstra search over the links picks the first one to take, and the path leads to its near end. At the far end they search again. Whether two points on a floor are joined is remembered by the pieces of floor they're on, until a door or blocker moves.
- **Going along a link.** On a link the body follows its points directly, not by sliding. On a ladder they climb at 1.6 m/s with the new `Climb` clip (`Tools/blender/build_climb_anims.py`, `Assets/Models/anims/human_climb.glb`) and the gun goes on their back. Only one direction can use a ladder at a time, and climbers keep 1.9 m apart. While on a link they don't shoot or react: a shot only makes them pause.
- **Doors.** The body is stopped by doors as they are, but searches treat a door people can open as already open (`Level.NavPlanBoxes`). When a shut door is in the way (`NpcNav.DoorInWay`) they open it with the `PushDoor` clip and wait until it's clear. They can open the factory's sliding door, the shack's door, the flats' stairwell door and, once the intro has finished, the prison's entrance, sally, grille and yard doors. They can't open the flat's door on the top landing, lockers or the container. The police airlock and observation doors still stand open. Nobody shuts a door behind them.
- **The flats' gunmen.** They used to stay on the door whatever happened, which would have meant nobody ever used the stairs. Now, once they're fighting and the player has got more than a metre above them, they leave their post (`ArmedNpc.FollowsUpstairs`), and they hold it again the next time a fight starts.
- **Debug.** `BRUTALFPS_NAVCHECK=1` also logs every link and door, warns about any link a search won't use, and traces paths up to the catwalk and the top of the stairs. `BRUTALFPS_NAVHOME=seconds` calls everyone off at that time, senses off, and logs 30 s later who isn't back on their own floor.
- **Bodies and calling for help.** Someone who comes across a body doesn't panic on the spot. An armed one of its side walks over, gun up, checks it, then calls for help: the 4 nearest of their side within 24 m (12 m through a wall) who aren't already busy come and search round it. Nobody else hears it. Someone unarmed stares at it for a second or two, cries out (the same call) and then runs or cowers. Only an ally in an actual firefight (they've seen the enemy or been fired on) draws in those near them; someone searching doesn't, so a search can't spread down the level person by person. Someone hit or shot at by a gunman they never saw goes looking for where the shots came from (even one who'd otherwise hold aim), searches longer and calls the nearby ones in to help. `BRUTALFPS_KILLAT="seconds:name"` drops that person dead without a sound at that time, to watch who finds them.
- **The jungle shack's door.** People used to struggle to get into the shack. Two reasons: the sacks inside and the open leaf outside (resting at -85 degrees, its end leaning back across the doorway) left a way through only one nav cell (0.2 m) wide, so one guard standing in it to shoot kept everyone else out. And a nav search from west or south of the shack stopped 2 m short, outside its back wall. That way leaves the goal's chunk and comes back into it, and `NpcNav.FindCorridor` let the second visit overwrite the chunk's "way on", so every cell there, the goal's own included, looked two chunks off. The A* also broke off at the goal without taking it as its result, so it returned the path to the cell with the lowest (wrong) estimate. Now the door is 1 m wide, the leaf opens square to the wall, the sacks are against the south wall and the rain barrel is clear of the leaf; a chunk keeps the way on from its visit nearest the goal, and reaching the goal ends the search there. `BRUTALFPS_NAVCHECK=1` traces six ways into and out of the shack (from the clearing, and from behind, west and south of it). `BRUTALFPS_SHACKWALK=1` adds an unarmed "shack walker" pacing in and out by the door, and logs (`[SHACKWALK]`) every half second where it and anyone within 4 m of the door are, and whether they're inside. Tested: the walker does four laps in 90 s without stalling (`BRUTALFPS_NPCS=passive BRUTALFPS_START=20022,0,22,0`). With the player inside in god mode (`BRUTALFPS_START=19998.6,0,2.6,90`), the first guard is in at 17 s (24.5 s before), and the ones from behind the shack now come round to the door.
- **Not blocking doorways** (`Game/ArmedNpcDoorway.cs`). A doorway is any door people can open (`Level.NpcDoors`), and "in its way" means in the opening or within 1.4 m of it on either side. A pusher used to stop as soon as it was `PushRange` (5 m) from the enemy, which with the player inside the shack was the doorway itself, so it stood there shooting and the rest queued behind it. Now a pusher doesn't stop in a doorway's way: it comes on into the room, and stops once it's through. Anyone whose magazine runs dry in a doorway's way steps to the side of it to reload (`ArmedNpc.StepAsideToReload`). That spot is against the wall on their own side (in the opening itself, the side away from the enemy), 0.5-0.8 m past the jamb, somewhere a body can stand that nobody else is standing in, preferably out of the enemy's sight. Then they come back to the fight as usual, and the next one can shoot through or go in. It's logged as "reloading in the shack doorway: stepping aside to (x, z)".
- **Round a wall's corner** (`NpcNav.Slide`). Someone going round the outside corner of the shack stopped dead there: only 2-3 cm into the wall's margin, which counts as clear (`Skin`), they were still eased out of it first, by the nearest way, straight back against their own step. They rocked to and fro and never slid round. Now only a body deeper than the skin is eased out.
- **Tested** (Debug, in the game, player inside the shack in god mode, as above): the first guard is in at 6.5 s, eleven are in by 60 s, and 2 stalls are logged (15 before these two changes). In a run before the pushers stopped holding the doorway, guard 10 stepped aside to reload three times: once to the wall outside by the north jamb, and twice to the wall inside it. Each time the doorway was clear while it reloaded. The factory office test (`BRUTALFPS_DOOR=open BRUTALFPS_START=5,0,24,190`, three runs against two with the old `Slide`) gave about the same arms-through-a-wall counts (about 50 poses against about 40, out of about 2000 per 5 s window) and fewer stalls (1-5 against 6-7). All the `BRUTALFPS_NAVCHECK` ways still get there.

Tested by running the game with logging and a few screenshots: a factory fight with the player on the catwalk (door opened, enemies queue up the ladder, and come back down when called off), the flats (gunmen open the door, climb all eight flights, and come back), the jungle shack's door, and the prison intro (no NPC opens a door during it). The cellar's and the ship's ladders have only had the path check, as no fight has used them yet.

Known limits: nobody shoots from a ladder or stairs; a crowd at a ladder's foot just waits in a bunch; fleeing still steers by rays, so frightened people don't use links or open doors; scripted walks are unchanged.

## Rio's people and its hiding places (6 Nov 2026)

- **The people** (`Game/NpcsRio.cs`, `AddRioCrowd`): 100 in the favela for normal play, 14 along each of the seven alleys and 2 out on the laje. 60 are civilians (`rio local N`): two in three walk a stretch of their alley and back, stopping at each end, and the rest stand about chatting; a third are freezers, the rest bolters. 40 are armed hostiles (`rio gang N`): long guns and sidearms rolled as usual, every fifth in armour, each watching a short stretch of their alley. On the bottom alley, where the magenta portal brings you in, the gang are only at the far end (over 35 m off, out of sight of the portal), so you come in among civilians. Every stop is checked against the level at startup (`CheckRioCrowd`: `[NPC] Rio: 100 people, 14 hiding places; 0 bad spots on their routes`). With the stress test on (`BRUTALFPS_STRESS=1` or the StressTest build) the crowd isn't added, so the stress test's numbers stay comparable; `BRUTALFPS_NORIOCROWD=1` leaves it out too (for comparing frame times).
- **The hiding places** (`Game/LevelRioHideouts.cs`): in two houses on every alley (14 in all), the front door stands open, its leaf swung right back inside against the jamb, on a little room cut into the house: 2 m across, 2.6 m deep, 2.4 m high, through a 0.94 m doorway. Inside it's whitewashed gone grey, with a cement floor, a bare bulb on its flex and a couple of crates in the back corner. The houses are picked by `PickRioHideouts` (the one on each alley whose middle door is nearest x = -9 and 16, or -19 and 6 on the odd alleys) without touching the favela's random rolls, so everything else looks as it did; a wire pole that would have stood in front of a door is moved aside. Each room has a `NoSun` shade volume (`Level.RioShadeVolumes`, which were empty) so no sun gets in and the alley's light dies away from the door, and the same volumes go into `Level.LightAt`, so someone crouched at the back is hard for NPCs to make out. The shader now has room for 40 shade volumes (`MAX_SHADE`, was 24; there are 34). The rooms are on the alley's own floor, so NPCs get in and out by the ordinary path search; no links are needed. Each is a `HidingPlace` with three spots to crouch in, one person to a spot (`NpcSenses.HidingPlaces`).
- **Hiding** (`Game/NpcHiding.cs`): a frightened civilian (a shot, the gun, someone panicking) runs for the nearest hiding place with a free spot on their own alley, within 30 m and not on the far side of the danger, a freezer too unless the gun is right on them; in there they crouch (Cower) for twice as long as usual, then walk back out the way they came and carry on. A hostile hurt below 45 HP with one within 14 m falls back into it to regroup (as does one badly hurt who'd back off anyway), stays 8 s, then comes back out fighting, once; after that they back off the ordinary way. Someone who can't get in (no nearer for 3 s, 8 s within 3 m of the door while others file in, or 30 s gone) gives up and runs or cowers as before. A respawn empties the rooms. The log: `[NPC] rio local 4: making for the alley 0 hideout at x -7,5 (3,3 m off, 2 of 3 spots taken)`, `reached the alley 0 hideout at x -7,5, hiding`, `can't get into the ...: running on`, `leaving the ...`, `hurt (29 HP): falling back to regroup`, `regrouped in the ...: back into the fight`.
- **Tested** (Release, in the game):
  - `BRUTALFPS_PERF=1 BRUTALFPS_NOVSYNC=1 BRUTALFPS_GOD=1 BRUTALFPS_START=rio BRUTALFPS_SCRIPT="3::0;0.3:F:0;0.3:F:0;0.3:F:0;25::0"`: the shots set off the alleys; 37 civilians made for a hiding place and all 37 got in, and the gang went into the fight. 162 fps on average (6.2 ms, 99% 10.3 ms, worst 27 ms), against 203 fps (4.9 ms) with `BRUTALFPS_NORIOCROWD=1`.
  - Screenshots of the alley 0 room at x = -7.5: `BRUTALFPS_GOD=1 BRUTALFPS_START=-9.6,-4000,0.4,150 BRUTALFPS_SCRIPT="1.5::0:150;0.1:P:0:150;1::0:150"` (the open door from the alley) and `BRUTALFPS_START=-7.5,-4000,2.45,180 BRUTALFPS_SCRIPT="1.5::-12:180;0.1:P:-12:180;0.5::-12:180"` (inside, from the doorway). (In Rio a start yaw of 0 looks down the hill, -z; 180 looks up it.)
  - `BRUTALFPS_STRESS=1 BRUTALFPS_PERF=1 BRUTALFPS_NOVSYNC=1 BRUTALFPS_START=rio BRUTALFPS_SCRIPT="12::0"`: no crowd, the 100 gunmen spawn and fire their volley as before (82 fps in Release); the hurt ones fall back into the hiding places too. (Coming back out after regrouping, `back into the fight`, wasn't seen in these short runs.)
- **Known limits**: the rooms are open doorways, not doors anyone shuts; nobody looks for a hiding place on another alley (up or down a stairway); a civilian already cowering in the alley doesn't think again and go for one; a hostile holding a spot near the door can block others for a few seconds.

## The Arctic research station (12 Nov 2026)

- **Where** (`Game/LevelArcticStation.cs`, built from `BuildArctic`): 50 m north-east of the survey hut, its front (the door and
  a small window beside it) facing south towards the white portal. Red insulated modules with white roof bands, up on steel
  stilts: the main module (12 x 6 m, floor 0.9 m above the snow) with a steel deck and five steps down, railings round both;
  a red module on a taller braced frame to the west, a white link and a red store module with its own steps to the east
  (both shut, solid). A flue and a whip aerial on the roof, a lamp over the door, fuel drums on a pallet, crates and a
  marker flag. Nobody can crawl under it.
- **Inside**: a mess room: four lockers and parkas on hooks, a galley counter (sink, hob, kettle, a shelf of tins), a table
  and two benches with mugs and a map, the radio desk (HF set, handset, monitor, chair), a bunk and a heater, under two
  fluorescent panels. It's a `NoSun` shade volume (`Level.ArcticStationShade`), so no blowing snow or whiteout inside.
  The window is dark glass, like the other huts' (you can't see through it).
- **The door**: white insulated steel, hinged on its west edge and swinging out to lie back against the front wall. E opens
  and shuts it (`UpdateStationDoor`, `StationDoorPrompt`); NPCs open it themselves (it's in `BuildNpcDoors`); a respawn shuts it.
  The steps are a nav link (`AddStairFlight("station steps")`), so the crew can come and go in an ordinary fight too.
  The player climbs them because the steps and deck are in `Level.HasStairs` (`StationStepsArea`): step-up only works inside
  the areas listed there. The steps are 1.6 m wide and run out from the deck's east end, so their railings carry on
  from the deck's with no invisible rail end to walk into.
- **The crew** (`Game/NpcsStation.cs`): every hostile on the level is inside (17 Nov 2026: the three `arctic commando`s moved in too). Four go out to the portal, the rest stay in. The four (`station crew 1-4`: two commandos without
  helmets, two riflemen) at the lockers, the galley, the table and the radio. **When the white portal is connected** (any
  `PortalSystem.Connect` that involves it, from a terminal: the HQ's, the vault's, the jungle's or the flats'), the door is
  thrown open (`Npcs.StationCrewOut` -> `Level.OpenStationDoor`) and they come out one after another, 0.9 s apart, guns
  up: to the aisle by the front wall, out of the door, across the deck, down the steps (1.6 m/s) and across the snow
  (2.1 m/s) to an arc of posts 6-8 m in front of the portal (`StationPosts`). Each one's route then becomes that post
  (`Npc.Repost`), and they react to the portal as anyone armed does when one comes on near them (`Npc.NoticePortal`):
  move up to cover the water, then settle back on their posts facing it. They take about 26-35 s to get there.
- **Trouble on the way**: the walk is scripted, so `UpdateStationMarch` watches for them: if one of them is hurt, or the
  player is in sight of any of them (ahead of them in a wide cone, or within 4 m), the script lets them all go at once
  and they fight, with the posts by the portal as their route. Walk through the white portal while they're on the way and
  you'll meet them head on. It happens once a life; a respawn puts them back inside behind the shut door. The white
  portal starts offline (and so does its old partner, the bunker's ice portal: `Portals.cs`, right after their `Link`), so the
  crew come out the first time a terminal connects it.
- **Debug**: `BRUTALFPS_START=station` starts you on the snow south-west of the station, looking at its door.
  `BRUTALFPS_LINKAT="seconds:a:b"` connects portal `a` to `b` that long into the run, as a terminal would (unlike
  `BRUTALFPS_HQLINK`, people react to it). `BRUTALFPS_NOSENSES=1` also stops the march's own check for the player.
  The log marks it with `[STATION]`.
- **Tested** (Release): `BRUTALFPS_GOD=1 BRUTALFPS_START=arcticcontainer BRUTALFPS_LINKAT="2:navy:white" BRUTALFPS_SCRIPT="45::0"`:
  the door opens, all four come out, reach their posts 26-35 s later and move up to cover the portal. Watching from the
  snow by the steps (`BRUTALFPS_NOSENSES=1 BRUTALFPS_GOD=1 BRUTALFPS_START="7039.5,0,-32.5,-58"`, same link) shows them
  file down the steps with their rifles shouldered. From the station's roof without `NOSENSES`, the arctic commandos saw
  the player and their fire hit one of the crew, and the crew broke off and fought, as they should.
- The replaced files are in `Backup_before_arctic_station.zip`.

## Cards in the container yard (13 Nov 2026)

- **The table** (`Game/LevelYardCards.cs`): a white resin garden table and four armchairs (`Tools/blender/build_garden_furniture.py`
  -> `garden_table.glb`, `garden_chair.glb`) in the lane between the lone green container and the east stacks, a few metres
  past the pallets (`Level.YardCardTable`). On it are the deck, the cards played, each player's tricks, some notes and three cans.
  The fourth chair is pushed back and empty. The table and the empty chair are solid; the chairs that are sat in aren't, so
  their players can stand up out of them.
- **The players** (`Game/NpcsShipYard.cs`, `yard card player 1-3`): three more of the yard crew, sat at it (`Npc.SeatAt`,
  `Game/NpcSeat.cs`) playing the `SitCards` clip (`Tools/blender/build_seated_anims.py`, `humankit/seatedlib.py` ->
  `anims/human_seated.glb`, 10 s loop). Each starts at a different point in it and plays at a slightly different speed. They hold a
  fan of five cards in the left hand (drawn by `Npcs.DrawCardsInHand`), draw one and lay it on the table, and now and then
  look up. Their guns lie on the ground by their right hands (`Level.GunBesideChair`). Being wary (a far-off noise) only turns their heads.
  Anything more (a startle, a fight, a flight) and they get up (0.6 s, holding still), the gun is in their hands, and they
  carry on as anyone would. Calm again, they walk back and sit down.
- **Tested**: `BRUTALFPS_NOSENSES=1 BRUTALFPS_START=541.5,0,2.9,195` shows them playing; with senses, walking up the lane, they
  see you at about 10 m, log `up from the card table (startled)` and fight. With `BRUTALFPS_NPCS=passive`, firing a shot and
  hiding behind the lone container: they flee, cower, come back, log `back in their seat at the card table`, and get up again
  when they next see you.

## Smarter fighting: shouts, callouts, squads and tactics (18 Nov 2026)

The sources as they were are in `Backup_before_npc_ai_upgrade.zip`.

- **Going on the alert is by shouting, not by zone.** It used to be that once anyone of a side was in a firefight, everyone of that side in that part of the level was on the alert and saw twice as far. Now someone in contact shouts it every 2 s (`ArmedNpc.ShoutContact`, Game/ArmedNpcAlert.cs), and only those of their side within 24 m in the open, 12 m through a wall, hear it (`HearShout`). They're on the alert for 40 s: they know the player for the enemy at a look, but see **no further** than usual. Those going about their business come and **search** for the player from where the shouter last had them; those already searching take it as a fresher lead. Someone who has only been shouted to doesn't pass it on until they've had contact themselves. (The old `NpcSenses.Alerted` / `RaiseAlert` are gone; `NpcSenses.Squads` holds what each side knows.)
- **Aiming** (`ArmedNpc.Shoot`): they lead a moving target (by how it's been moving, more fully the better the shot), the first shots at someone who's just shown are wild (2.6 times the cone) and settle over a second or two of keeping them in sight, and someone crossing their line of fire is harder to hold.
- **Callouts** (Game/NpcBarks.cs): "Contact!", "Reloading!", "He is reloading!", "Flanking left!", "Covering!", "Frag out!", "Grenade!", "Man down!", "Fall back!", "Lost him!", "All clear."... logged as `[BARK]`, shown as subtitles at the bottom of the screen when the player is within 32 m (16 m through a wall), and played if there's a recording at `Assets/Sounds/Voice/<Bark>_<n>.wav` (`Contact_0.wav` is the first line of `Contact`; there are none yet). One person doesn't talk again for 3.5 s, and the same callout isn't repeated by their side for 4 s. `BRUTALFPS_BARKS=0` silences them.
- **What a side knows together** (Game/SquadIntel.cs): where the player was last seen, how they were moving, and how long they've stayed put. Someone losing sight starts from the freshest sighting their side has. When the last of them loses sight a **search** starts: rings of places round where the player went, out to as far as they could have got, leaving out what the one who lost them could see. Searchers each claim the nearest place nobody has looked at or is heading for, and what any of them can see on the way or from where they stop is crossed off (`[SQUAD]` and `searching (...)` in the log).
- **Tactics** (Game/ArmedNpcTactics.cs): a few times a second each fighter scores Engage (the usual fight), Flank, Rush, Grenade and Retreat, with a lean towards what they're already doing, and goes with the best (`tactic Engage -> Flank (score ...)` in the log). **Flank**: one at a time on a side, while someone else keeps the player busy, they go round to the player's side, by a way out of the player's sight where there is one. **Rush**: they heard (9 m) or saw the player start a reload: the bold close in at a run, firing as they come, and those in cover lean out at once. **Grenade**: hostiles carry frags now and then (45%, a commando two; police none; `BRUTALFPS_NPCFRAGS=all` or `none`): thrown at a player who has stayed behind the same cover, or just gone from sight, 7-24 m off, never within 5.5 m of anyone of their side, lobbed from a planned arc (`Throwables.PlanNpcThrow`) and off by a metre or three by skill. **Retreat**: broken, they fall back.
- **Morale**: a side's own dead (more if they saw it), hits and long spells under fire wear it down; it comes back slowly when it's quiet. With the odds it makes up their courage: the shaken move up only the cautious way, are easier to pin down and prefer cover further back; the broken fall back.
- **Cover** (`FindFiringCover`): the spots with somewhere to lean out from are now weighed against each other: a short run, a good range for their gun (a pusher nearer, the shaken further), and not in sight of another enemy.
- **Tested** (`bin/Debug/net10.0`, `BRUTALFPS_MUTE=1 BRUTALFPS_GOD=1`): in the jungle (`BRUTALFPS_START=20012.7,0,-23.6,90 BRUTALFPS_SCRIPT="2.2::0;0.4:F:2;0.4:F:2;0.3:R:0;3.5:S:0;0.1:CP:0;30:C:0"`) the shouts reach only those near, the reload is called out and rushed, a flanker goes round with "Covering!" from another, and the deaths of their own shake those who see them. On the ship (`BRUTALFPS_NPCFRAGS=all BRUTALFPS_START=ship BRUTALFPS_SCRIPT="2::0:0;0.4:F:0:0;5:WH:0:0;0.1:CP:0:0;25:C:0:0"`) a deckhand throws a frag 23 m with "Grenade out!", and throws are held back while their own are near the player. The Rio stress test (`BRUTALFPS_STRESS=1 BRUTALFPS_PERF=1 BRUTALFPS_NOVSYNC=1 BRUTALFPS_START=rio BRUTALFPS_SCRIPT="15::0"`): people 7.5 ms a frame on average, against 8.2 ms before.

## The hall's side door, its guards, and the outbuildings coming in (18 Nov 2026)

The sources as they were are in `Backup_before_factory_west_door.zip`.

- **The side door** (`Game/LevelFactorySideDoor.cs`): a plain steel door at floor level through the hall's south wall, in the bay between the office and the west wall (x -14.1 to -12.9), with a concrete surround and a lamp over it outside. It opens into the hall, hinged on its east side; E on either side opens or shuts it (`[DOOR] Opening the hall's side door`), and people open it as they do the rear door. On a raid, the player opening it sets off the office evacuation, as the other two doors do.
- **Its guards** (`factory door guard 1` and `2`, `Game/NpcsFactoryDefence.cs`): one either side of it, outside, both in armour. Guard 1 holds his post whatever happens (`HoldsPost`: he fights from there); guard 2 fights as anyone would and may leave to chase the player. If the one holding the door goes down, the other breaks off whatever he's doing, runs back and holds it (`the other door guard is down: back to the side door`), so one is always at the door while either lives.
- **The outbuildings reinforce the hall.** Once the hall is attacked (anyone in the hall or office, or either door guard, hit or killed, or fighting the player in the hall or within 12 m of it), everyone still standing in each outbuilding leaves it and pushes into the hall by the door nearest them: the outbuilding's crew by the side door, the second hall's up the outside steps and in by the rear door, the boiler house's by the big sliding door (`[FACTORY] The hall's under attack (...)` in the log). They run there by the usual paths, fight whoever they see on the way, and once through go after the player; afterwards they stay in the hall. Clear an outbuilding first and nobody comes from it. It happens once each life.
- **Orders** (`ArmedNpc.OrderGoTo`, `Game/ArmedNpcOrders.cs`): a script can send an armed NPC somewhere, on the alert and at a run, to hold it as a post or to go after the player from there; while they're on their way it comes before searching, shots heard and cover.
- **Tested** (`BRUTALFPS_MUTE=1 BRUTALFPS_GOD=1 BRUTALFPS_TIME=day BRUTALFPS_START="-5,0,12,180" BRUTALFPS_SCRIPT="3::0;0.3:F:0;0.3:F:0;0.3:F:0;45::0"`): all 23 from the outbuildings set off, and the side, rear and big doors are opened by them; with `BRUTALFPS_KILLAT="18:factory door guard 1"`, guard 2 is back holding the door 4 s later. A factory raid over the fence starts as before.
