# ShooterMashup

Prototype of a co-op sandbox survival game, in the spirit of Valheim but with big differences.
Unity 6.6 (6000.6.5f1), URP, new Input System only.

## Vision (still loose; expect it to change)

- **Setting:** post-apocalyptic, inspired by the tabletop game *Gorkamorka* (orks, scrap, trukks
  and buggies in a desert). Not finalised.
- **Feel:** early-2000s FPS (Quake 3, *Wolfenstein: Enemy Territory*) with modern QOL. The player
  controller is a faithful port of ET's movement.
- **Engineering focus:** gathering should mean building structures or contraptions (e.g. mining
  rigs), not hitting rocks with a pickaxe.
- **World:** fully diggable voxel terrain. A friend of the owner is building the voxel side;
  this repo focuses on gameplay. Collision is behind `IPmoveWorld` so the voxel world can plug in.
- **Large open terrain**, unlike ET's cramped maps, so travel distances are a design concern.
  Vehicles fit the Gorkamorka theme and are likely the long-distance answer.

## Working with the owner

- Ask before making design decisions that aren't obvious; the owner wants to be consulted.
- The owner thinks in metric. Everything is in metres, m/s, seconds and degrees.
- Keep features modular and easy to tweak or remove: one partial file per movement feature,
  with an enable flag in `MoveConfig`.

## Player controller

Code is in `Assets/Scripts/Player/`. Read its `README.md` first: it covers the layout, controls,
units, presets, modifiers and the deliberate departures from ET.

Key decisions so far:
- **Netcode-ready:** `PlayerMove.Move(ref PlayerState, PlayerCmd)` is a pure, deterministic step
  at a fixed 125 Hz tick. No netcode library has been chosen yet. Client prediction and server
  reconciliation are planned around `PlayerCmd` and `PlayerState`.
- **Metric**, converted exactly from ET's inches. ET's tiny internal tolerances are kept as
  multiples of an `Inch` constant so they can still be compared with the ET source.
- **Presets:** `MovePreset.ShooterMash` is the default and our own value set: ET rounded to metric
  (1.8 m, 6.4 m/s run, 20 m/s² gravity...) plus building-driven changes: a 0.7 m wide hull (ET 0.91;
  fits between pillars 1 m apart, 0.77 m gap), 0.52 m step height (run
  up 50 cm steps), a 0.6 m prone hull (fits under a floor on 1 m pillars, 0.8 m clearance),
  gentler stamina (drain 0.1/s = 10 s of sprint, regen 0.05/s; ET: 0.25 and 0.025), and no jump
  cooldown (ET blocks jumping for 850 ms after a jump, which bit when building under yourself:
  you land early and can't jump; you still have to re-press jump).
  `EtExact` is kept for comparison; F4 toggles them in play mode.
- **No velocity rounding:** ET's "125 fps jump" quirk was removed on purpose.
- **Strafe jumping** is ET-faithful; `MoveConfig.maxAirSpeed` can tame it later.
- **`MoveModifiers`** (speed, staminaMax, staminaCost, staminaRegen) live in `PlayerState`. Future
  systems (food, encumbrance, injuries) affect movement through these, never by calling into the
  movement code, which has to stay deterministic for networking.

## Building (structures)

Code is in `Assets/Scripts/Building/`; read its `README.md` first. First prototype, Valheim-inspired.

Agreed with the owner:
- **1 m grid with 3 m pieces** (decided 2026-10-12 after comparing with a 2.5 m kit on a 0.5 m
  grid, which was removed: dividing by 5 was tedious). Every piece (floor, wall, pillar, beam,
  45° roof, stairs) in 3 m and 1 m (no 2 m for now); storeys 3 m (2.8 m headroom). Stone slabs
  stay 0.5 m thick, so heights can go in 0.5 m. Snap points sit every metre along edges, so
  structures never drift off the grid (a test checks this). Stairs are 27° and rise 1 or 3 m
  over twice that length (the owner liked the 27° slope), 1 or 3 m wide. B takes out the tool.
- Valheim-style pieces that snap, or place
  freely with Shift. Q/E pick the snap point (and then allow overlaps; hold for auto).
  Aimed-face rule (decided, no toggle): the piece goes on the face you aim at, so a slab top
  stacks and a side face or the ground goes beside. R cycles rotation steps
  (90, 45, 22.5, 15, free). Hold RMB to remove, MMB opens the menu. Rotation is always
  world space, also when snapping; Shift+RMB copies the aimed piece (shape, material,
  rotation). Placeholder texture and colour per material. Sizes may change once they've been
  tried in first person.
- Valheim's support model per material (wood, stone, iron for now; Gorkamorka materials like
  scrap and plastic later). Integrity is solved exactly and in real time. A piece resting on
  another (flat contact) counts as vertical support even when offset, e.g. staggered slabs.
  Losses are per metre (per 2.5 m, so 2.5 m pieces match Valheim's per-piece numbers), so small
  pieces reach as far as big ones (owner's choice). Stone's horizontal loss is 4 per 2.5 m so it
  still barely overhangs (~0.5 m), like Valheim's no-overhang stone.
- Pieces come in set materials (enforced by `BuildSystem`): wood has every shape (prototyping);
  stone has slabs (0.5 m thick floors that snap on both faces), pillars and stairs; iron has
  pillars, beams and diagonal beams (45° and 27°, rising 1 or 3 m, snapping at both ends of
  their centre line like roofs; beams snap by their centre line; horizontal beams are 0.25 m,
  pillars 0.23 m and diagonals 0.21 m, all a bit more than the 0.2 m walls and each a touch
  thinner than the last so faces never coincide and z-fight). Wood also has see-through
  1 m wide ladders (1 and 3 m tall; climbable through
  `SurfaceFlags.Ladder`, which the piece view adds as a `PmSurface`). Roofs snap by their centre
  plane, so roofs, diagonals and beams snapped to one point cross there (the owner's choice
  over resting the roof's underside on its edges; it sits half its thickness into a wall top).
  Roof pieces (wood) in three pitches, as ratios so edges stay on the grid: 45° (1:1), 27° (1:2,
  like the stairs) and 63° (2:1; 3:1 at 72° was too steep). Each set: plain slopes, hip and
  valley corners, a 1 m ridge cap for odd widths, right-triangle gable walls and a gable cap
  under the ridge cap. 45° and 27° come in 3 and 1 m; 63° only with a 1 m run (3 m would rise
  6 m), in 3 and 1 m widths. Names give the plan size (width x run), gables width x height.
  The build menu scrolls when it doesn't fit the screen. Non-box shapes are wedges (`Obb.cut`: a
  box halved along a diagonal), with convex mesh colliders; everything else is still boxes.
  The showcase (`BuildShowcase.Plan`) is three structures that together use every piece in
  every material (farmstead 45°, barn 27°, watchtower 63°); a test enforces that, so new
  pieces need a place in it. Iron needs an iron or stone path to the ground: hung off wood it
  loses too much support (its losses scale with its 1500 max).
- Structures must rest on terrain or static world geometry. Digging out the ground under a
  structure collapses it. Pieces sit freely on voxel terrain (no voxel snapping), and voxel
  size is unrelated to piece size. The voxel world must report changed regions to
  `BuildSystem.TerrainChanged`.
- Undo is narrow on purpose: your own actions, 60 s window, refused once anyone else changed
  that spot. A removal's undo restores everything that collapsed with it.
- Collapsed pieces vanish for now; physical, fading debris is wanted later (cosmetic only).
- Same networking shape as movement: `BuildSystem.Execute(BuildCmd)` is pure and deterministic.
- The build tool builds on a `BuildHost`: the world (`StructureManager`), a vehicle or a lift,
  whichever the aimed collider belongs to, in that host's local space. `PieceViews` keeps any
  host's piece GameObjects.

## Vehicles (contraptions)

Code is in `Assets/Scripts/Vehicles/`; read its `README.md` first. First prototype (2026-10-13).

Agreed with the owner:
- **Scrap Mechanic's lift, copied** (L): place it, build on it (the first piece starts a vehicle,
  frozen on the lift), raise/lower it, put any vehicle back on it upright (recovery after a
  flip), take it away to drive. Vehicles can only be built on while on a lift.
- **Trailmakers-style driving:** no player-made joints or springs. A vehicle is one rigid body;
  the wheel piece is raycast suspension done in code, springs sized to the vehicle's weight,
  all wheels drive, the front half steer, forward is where the seat faces. Assists (low centre
  of mass, grip forces raised towards it, self-levelling, upright in the air, downforce, extra
  gravity, parking hold) make anything drive. All numbers in `VehicleTuning`.
- Vehicles use the normal building pieces (a shed on a truck) plus vehicle-only parts (wheel,
  driver seat; the world refuses them). Integrity on vehicles: connected is enough (zero
  support loss, one anchor piece counts as grounded). One wheel size for now; car-scale pieces
  (a 0.5 m grid) may come later.
- F sits in a seat / gets out; W/S/A/D/Space drive; V cycles follow camera (default), first
  person and free orbit.
- **Multiplayer plan:** driver authority (the driver's machine simulates the vehicle, others see
  a smoothed copy), unlike the deterministic player movement.
- **Riding (moving platforms, 2026-10-13):** `PlatformRider` in the player runtime carries a
  player standing on anything with a Rigidbody (vehicles, lifts, `MovingPlatform`s): moved and
  turned with its physics pose each frame before the ticks (the drawn player and the view are
  moved and turned to the platform's interpolated pose; `PlayerView` must use `RenderOrigin`, or
  riding jitters at the physics rate; was a 10 cm sawtooth on the truck); the view and the
  player's (relative) velocity turn with it. Jumping or stepping off keeps you in its frame while
  you're above it (a downward cast hits it), so jumps on a turning truck land where you took off;
  past its footprint you let go and keep its velocity. Landing on a platform from elsewhere sticks
  (owner's choice: you stand still on it; `PlayerMotor.stickyLandings`, off = physically right).
  It sits beside the deterministic simulation (which only reports `GroundObject`): the platform's
  motion is an input. Toggle: `PlayerMotor.ridePlatforms`.

## To-do / open questions

- **Open building topics (raised 2026-10-12):** stair width (1 and 3 m wide stairs now, still
  being tried out), ladders (wood ladder pieces added 2026-10-12, more to try), and roofs (45°, 27° and 63° sets with corners, caps and
  gables since 2026-10-13; still to be tried in play).

- Sprinting in big open terrain. Ideas raised but not built: soft sprint falloff as stamina runs
  low, open-world tuning (bigger bar, regen while walking), and a long-term fatigue layer that
  food and water feed into (like Valheim's food-based max stamina).
- **Voxel block size:** step height is 0.52 m (ShooterMash) and the jump is 1.16 m. If voxels are 1 m blocks,
  players can't walk up single blocks. Needs agreeing with the voxel developer.
- **Licensing:** the controller closely follows ET's GPL source (`bg_pmove.c`, `bg_slidemove.c`,
  `cg_view.c`). Fine for a prototype; shipping closed-source would need a clean-room rewrite or
  legal advice.
- Weapons, interaction, resources, engines and fuel don't exist yet.
- Building ideas not yet done: falling debris, a collapse "creak" warning, piece health,
  showing nearby snap points, a workbench / build-range rule, resource costs.

## Unity physics gotchas (learned the hard way)

`World/UnityPmoveWorld.cs` works around PhysX imprecision. Read its comments before changing it.
- Box sweeps against large colliders report contact up to ~1.5 cm early, and report "overlapping
  at distance 0" with no normal when starting within a few mm of a surface. So the sweep starts
  behind the box, and the exact contact distance is found by bisection with `OverlapBox`.
- Sweep normals can be tilted by several degrees. The real normal comes from
  `ComputePenetration` at the touching position, and near-zero components are snapped to 0.
  Quake logic needs flat floors to have exactly vertical normals; otherwise every landing
  turns fall speed into a sideways hop.
- When a trace's end position is free, the whole path used to count as clear, so long traces
  skipped thin obstacles (a 14 cm ladder with nothing behind it was invisible to the 1.2 m
  ladder check). Now, if the sweep saw a hit, it probes just past it before trusting that.
- `Physics.ComputePenetration` takes the collider's pose explicitly. A moving rigidbody's
  transform is its interpolated (drawn) pose, up to a physics step from its colliders, so pass
  the body's physics pose (`PhysicsPose`); with the transform, a truck's floor gave a 33° normal.
- Landing without the move touching the floor (it ended within the ground check's 6 mm, e.g. a
  deck rising to meet you) left the fall speed in place, and ET's walk code ("keep your speed on
  slopes") then turned it into a 6 m/s run along any slightly tilted floor. Landing now clips the
  velocity against the ground (`GroundTrace`), as hitting it would have.

## Testing

- Edit-mode tests in `Assets/Tests/EditMode` check the controller against ET's numbers (speeds,
  jump, stamina, steps, slopes, fall damage, tunnels, lean, ladders, water, modifiers, presets,
  determinism), and the building system (support, collapse, undo, snapping) in
  `Assets/Tests/EditMode/Building`, and vehicle build rules in `Assets/Tests/EditMode/Vehicles`.
  Driving is checked live in play mode (the demo buggy, via `eval`: set `Vehicle.Driver` and
  `SetControls`, read speed and pose). Run them in Window > General > Test Runner.
- **Preferred: drive the owner's open Editor** with the Unity CLI (`unity` on PATH) through the
  `com.unity.pipeline` package (installed 2026-10-12). `unity status` should show state "ready";
  `unity command` lists ~200 commands (run_tests, editor_play/stop, simulate_key, capture_game_view,
  console, get_scene_hierarchy, eval...). Pass `--caller plugin --skill unity-cli` on each
  `unity command` call. Edit scenes through the Editor, not by hand-editing `.unity` YAML. The
  Editor imports changed scripts/packages when its window gets focus, so ask the owner to click
  into Unity if a change hasn't been picked up; `unity command recompile` works unfocused.
  Play-mode gotchas: with Unity in the background the game doesn't advance until
  `Application.runInBackground = true` (set via `eval`, lasts for that Play session).
  `simulate_key` (key names like `W`, `Digit3`) moved the player via Input Actions, but never
  showed up on `Keyboard.current` reads (the build tool's keys), so test those by calling the
  methods through `eval`/reflection, or ask the owner. `capture_game_view --save_path` is relative to
  `Assets/` and refuses `..` (it logs an error), so it always lands inside Assets: move the
  PNG out and delete the folder and its `.meta` files right after. It captures the IMGUI
  (menus, HUD); for clean scene shots render a Camera to a RenderTexture via `eval` instead.
- Fallback when no Editor is connected: batch-mode Unity can't run on this project folder while
  the Editor has it open. Copy `Assets`, `Packages` and `ProjectSettings` to a scratch folder and
  run there:
  `Unity.exe -batchmode -nographics -projectPath <copy> -runTests -testPlatform EditMode -testResults <file>`
- To *see* something without the owner, drop the `-nographics` flag and run a throwaway
  play-mode test in the copy that renders a camera to a RenderTexture and saves a PNG. After
  copying `Assets` again, rebuild the test map in the copy first: script GUIDs in the copied
  `.meta` files differ from the copy's old scene.
- Test map: menu **ShooterMashup > Build Movement Test Map** regenerates
  `Assets/Scenes/MovementTest.unity`. You spawn at a hub at the origin with a buggy, a truck
  circling behind it to ride on, three portals (walk through; each area has one back) and roads
  to three areas, each built in its own space: **character tests** (z +140, arriving in the
  middle with groups around you: stairs, ramps and crates; tunnels and lean corner; ladder and
  fall towers; water and slime tanks and slick floor; moving platforms: elevator, slider,
  carousel), **building** (x -190: the showcase, diggable dirt, bridge cliffs; the StructureManager stays at the world origin and the
  showcase gets world corners), and **driving** (x +190: the rough test track and a closed race
  circuit with a table-top jump and bumps (washboard, side ridges, dunes, rocks), laid out from
  straights and arcs so it always closes; a race truck laps it at up to 22 m/s (`VehicleAutopilot`
  following the centre line) and stops at the start line each lap so you can ride it), and
  **off-road** (z -200, fourth hub portal: a 960 m loop with long bumpy straights; a rally truck on
  big wheels drives it only while someone stands on it, ~120 km/h on the track, 150 km/h on flat).
