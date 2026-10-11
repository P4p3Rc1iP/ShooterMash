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
- **Presets:** `MovePreset.EtExact` (default) and `Rounded` (1.8 m, 6.4 m/s run, 20 m/s² gravity...).
  F4 toggles them in play mode so the owner can compare the feel. Not yet decided which to keep.
- **No velocity rounding:** ET's "125 fps jump" quirk was removed on purpose.
- **Strafe jumping** is ET-faithful; `MoveConfig.maxAirSpeed` can tame it later.
- **`MoveModifiers`** (speed, staminaMax, staminaCost, staminaRegen) live in `PlayerState`. Future
  systems (food, encumbrance, injuries) affect movement through these, never by calling into the
  movement code, which has to stay deterministic for networking.

## To-do / open questions

- Sprinting in big open terrain. Ideas raised but not built: soft sprint falloff as stamina runs
  low, open-world tuning (bigger bar, regen while walking), and a long-term fatigue layer that
  food and water feed into (like Valheim's food-based max stamina).
- **Voxel block size:** step height is 0.46 m and the jump is 1.16 m. If voxels are 1 m blocks,
  players can't walk up single blocks. Needs agreeing with the voxel developer.
- **Licensing:** the controller closely follows ET's GPL source (`bg_pmove.c`, `bg_slidemove.c`,
  `cg_view.c`). Fine for a prototype; shipping closed-source would need a clean-room rewrite or
  legal advice.
- Weapons, interaction and building systems don't exist yet.

## Unity physics gotchas (learned the hard way)

`World/UnityPmoveWorld.cs` works around PhysX imprecision. Read its comments before changing it.
- Box sweeps against large colliders report contact up to ~1.5 cm early, and report "overlapping
  at distance 0" with no normal when starting within a few mm of a surface. So the sweep starts
  behind the box, and the exact contact distance is found by bisection with `OverlapBox`.
- Sweep normals can be tilted by several degrees. The real normal comes from
  `ComputePenetration` at the touching position, and near-zero components are snapped to 0.
  Quake logic needs flat floors to have exactly vertical normals; otherwise every landing
  turns fall speed into a sideways hop.

## Testing

- Edit-mode tests in `Assets/Tests/EditMode` check the controller against ET's numbers (speeds,
  jump, stamina, steps, slopes, fall damage, tunnels, lean, ladders, water, modifiers, presets,
  determinism). Run them in Window > General > Test Runner.
- The Unity editor is usually open, so batch-mode Unity can't run on this project folder. To
  test from the command line, copy `Assets`, `Packages` and `ProjectSettings` to a scratch
  folder and run there:
  `Unity.exe -batchmode -nographics -projectPath <copy> -runTests -testPlatform EditMode -testResults <file>`
- Test map: menu **ShooterMashup > Build Movement Test Map** regenerates
  `Assets/Scenes/MovementTest.unity` (stairs, ramps, crates, tunnels, ladders, fall towers,
  water, slick floor, strafe lane).
