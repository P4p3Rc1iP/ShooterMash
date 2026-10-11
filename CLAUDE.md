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
  (1.8 m, 6.4 m/s run, 20 m/s² gravity...) plus building-driven changes: 0.52 m step height (run
  up 50 cm steps) and a 0.6 m prone hull (fits under a floor on 1 m pillars, 0.8 m clearance).
  `EtExact` is kept for comparison; F4 toggles them in play mode.
- **No velocity rounding:** ET's "125 fps jump" quirk was removed on purpose.
- **Strafe jumping** is ET-faithful; `MoveConfig.maxAirSpeed` can tame it later.
- **`MoveModifiers`** (speed, staminaMax, staminaCost, staminaRegen) live in `PlayerState`. Future
  systems (food, encumbrance, injuries) affect movement through these, never by calling into the
  movement code, which has to stay deterministic for networking.

## Building (structures)

Code is in `Assets/Scripts/Building/`; read its `README.md` first. First prototype, Valheim-inspired.

Agreed with the owner:
- Everything is on a universal 0.5 m grid. Every piece (floor, wall, pillar, beam, 45° roof,
  stairs) comes in 2.5 m, 1 m and 0.5 m, and snap points sit every 0.5 m along edges, so
  structures never drift off the grid (a test checks this). Storeys are 2.5 m (a floor on top
  leaves 2.3 m headroom for the 1.8 m player). Stairs are 27°, 1 m wide, and rise 0.5, 1 or
  2.5 m over twice that length (the owner liked the 27° slope and the in-between heights).
- Valheim-style pieces that snap, or place
  freely with Shift. Q/E pick the snap point (and then allow overlaps), R cycles rotation steps
  (90, 45, 22.5, 15, free). Hold RMB to remove, MMB opens the menu. Rotation is always
  world space, also when snapping; Shift+MMB copies the aimed piece (shape, material,
  rotation). Placeholder texture and colour per material. Sizes may change once they've been
  tried in first person.
- Valheim's support model per material (wood, stone, iron for now; Gorkamorka materials like
  scrap and plastic later). Integrity is solved exactly and in real time.
- Structures must rest on terrain or static world geometry. Digging out the ground under a
  structure collapses it. Pieces sit freely on voxel terrain (no voxel snapping), and voxel
  size is unrelated to piece size. The voxel world must report changed regions to
  `BuildSystem.TerrainChanged`.
- Undo is narrow on purpose: your own actions, 60 s window, refused once anyone else changed
  that spot. A removal's undo restores everything that collapsed with it.
- Collapsed pieces vanish for now; physical, fading debris is wanted later (cosmetic only).
- Same networking shape as movement: `BuildSystem.Execute(BuildCmd)` is pure and deterministic.
- Vehicles are a separate, later step. The player controller has no moving-platform support
  yet, which is the main risk there.

## To-do / open questions

- **Next session (agreed 2026-10-12), remind the owner to look into:** stair width (stairs are
  1 m wide only), ladders (as buildable pieces), and roofs (only plain 45° slabs so far).

- Sprinting in big open terrain. Ideas raised but not built: soft sprint falloff as stamina runs
  low, open-world tuning (bigger bar, regen while walking), and a long-term fatigue layer that
  food and water feed into (like Valheim's food-based max stamina).
- **Voxel block size:** step height is 0.52 m (ShooterMash) and the jump is 1.16 m. If voxels are 1 m blocks,
  players can't walk up single blocks. Needs agreeing with the voxel developer.
- **Licensing:** the controller closely follows ET's GPL source (`bg_pmove.c`, `bg_slidemove.c`,
  `cg_view.c`). Fine for a prototype; shipping closed-source would need a clean-room rewrite or
  legal advice.
- Weapons, interaction, resources and vehicles don't exist yet.
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

## Testing

- Edit-mode tests in `Assets/Tests/EditMode` check the controller against ET's numbers (speeds,
  jump, stamina, steps, slopes, fall damage, tunnels, lean, ladders, water, modifiers, presets,
  determinism), and the building system (support, collapse, undo, snapping) in
  `Assets/Tests/EditMode/Building`. Run them in Window > General > Test Runner.
- The Unity editor is usually open, so batch-mode Unity can't run on this project folder. To
  test from the command line, copy `Assets`, `Packages` and `ProjectSettings` to a scratch
  folder and run there:
  `Unity.exe -batchmode -nographics -projectPath <copy> -runTests -testPlatform EditMode -testResults <file>`
- To *see* something without the owner, drop the `-nographics` flag and run a throwaway
  play-mode test in the copy that renders a camera to a RenderTexture and saves a PNG. After
  copying `Assets` again, rebuild the test map in the copy first: script GUIDs in the copied
  `.meta` files differ from the copy's old scene.
- Test map: menu **ShooterMashup > Build Movement Test Map** regenerates
  `Assets/Scenes/MovementTest.unity` (stairs, ramps, crates, tunnels, ladders, fall towers,
  water, slick floor, strafe lane, and a building area with diggable dirt and bridge cliffs).
