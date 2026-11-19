# Climb points (no jumping, vault only)

From `BRUTALFPS_CLIMBSCAN=file` (`Game/ClimbScan.cs`): every place a vault (Space, obstacle up to 1.25 m) lets you
climb **up onto** something you can't otherwise walk onto, from somewhere you can walk to. Vaulting **over** a low,
thin obstacle (up to 1 m deep) back down to the floor is always allowed and isn't listed, except where it takes you
somewhere new. Positions are local to each area (`ClimbScan.OriginOf`), in metres; "rise" is the height of the climb.
Railings, post tops and other slivers are not climbable at all (a climb needs footing under the body).

**Decided** (in `Level.NoClimbAreas`, `Game/LevelClimbing.cs`): allowed are S1, S3, C1, C2, C4, V1 and V2; blocked are
S2, S4, C3, V3 and all furniture (F1-F4: no preference was given, so furniture is out; vaulting *over* a desk or a bed is
still fine). Not asked because they're needed: the jungle lab's raised floor (0.46 m) and the Arctic hut's doorstep.
Re-run the scan after changing a level: it applies the areas, so what it lists is what the player can do
(`BRUTALFPS_CLIMBALL=1` ignores them; `BRUTALFPS_CLIMBLISTWALKED=1` also lists climbs onto places you can walk to anyway).

## Structural

| # | Area | Place | Where (x, z) | Rise |
|---|------|-------|--------------|------|
| S1 | Factory hall | Loading dock steps, 0.7 / 1.4 / 2.0 m: **the only way up to the blue portal** (it used to say "reached by jumping") | -14, -2 .. -4 | 0.6-0.7 each |
| S2 | Outback roadhouse | Fuel island under the pumps (a 0.2 m kerb) | 7, -92 | 0.2 |
| S3 | Container ship | Forward container stack, from its lashing bridge (the other stacks you can already walk onto) | 64.5, -10 .. 10 | 1.1 |
| S4 | Container ship | Bridge wings: the console on each wing | -65.6, +/-14.6 | 1.15 |

## Furniture

| # | Kind | Places |
|---|------|--------|
| F1 | Desks and tables (0.55-0.85 m) | Police bullpen desks (about 24 spots) and tech-room desk; factory office desks; prison table and benches; flats table; jungle shack table and lab benches; yard table; Arctic station table |
| F2 | Chairs (0.9 m) | Police bullpen and observation room; factory office; yard |
| F3 | Beds and bunks (0.47-0.6 m) | Prison cell bunks (7); outback motel beds (7) and their bedside cabinets (0.8 m, 7) |
| F4 | Counters | Arctic station galley counter (0.9 m above its floor); jungle shack counter |

## Crates, drums, bins

| # | Kind | Places |
|---|------|--------|
| C1 | Crates and crate stacks (0.8-1.2 m) | Factory grounds (-43, -15), (-53, -8), (39, 33), (41.6, 16.4); yard (-27.5 / -25, 22.3); Arctic pallet of crates (50.6, -42.4); jungle gun-nest ammo boxes (0.98 m, 3) |
| C2 | Drums and barrels (0.88 m) | Yard (10.3, -4.3); outback (10, 3), (17, -98); cellar (4); police (10 / 11.6, -6.4); jungle (3.7, -14.2), (5.8, -21.4) and one on the lab platform |
| C3 | Open-topped bins (1.0-1.1 m) | Factory hall (15.5, 6), (15.2, 2.6), (-15.5, -20): a vault over the rim drops you inside |
| C4 | Range bench | Outback firing range (20.9, -341), 0.92 m |

## Vaults over that lead somewhere new

| # | Place | Where |
|---|-------|-------|
| V1 | Rio: over a roof terrace's 1 m parapet, dropping 0.9 m on the far side (3 spots) | (-20.3, 23.3), (-18.5, 5.3), (-18.2, 41.3) |
| V2 | Factory hall: over the catwalk railing, 4 m up | (11, 33) |
| V3 | Jungle: over roots, logs and sacks at the clearing's edge (11 spots) | round (-24, 2.7), (22, -11.7), (24.8, -9.2) and others |
