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
  up 50 cm steps), a 0.6 m prone hull (fits under a floor on 1 m pillars, 0.8 m clearance),
  and gentler stamina (drain 0.1/s = 10 s of sprint, regen 0.05/s; ET: 0.25 and 0.025).
  `EtExact` is kept for comparison; F4 toggles them in play mode.
- **No velocity rounding:** ET's "125 fps jump" quirk was removed on purpose.
- **Strafe jumping** is ET-faithful; `MoveConfig.maxAirSpeed` can tame it later.
- **`MoveModifiers`** (speed, staminaMax, staminaCost, staminaRegen) live in `PlayerState`. Future
  systems (food, encumbrance, injuries) affect movement through these, never by calling into the
  movement code, which has to stay deterministic for networking.

## Building (structures)

Code is in `Assets/Scripts/Building/`; read its `README.md` first. First prototype, Valheim-inspired.

Agreed with the owner:
- **Two piece kits are being compared (2026-10-12)**, switched with keys 2 and 3 (which also take
  out / put away the build tool; B is no longer used). The owner found dividing by 5 tedious
  and is trying a 1 m grid; 2.5 m can't be on it, hence 3 m.
  - 2.5 m kit (key 2): universal 0.5 m grid; every piece (floor, wall, pillar, beam, 45° roof,
    stairs) in 2.5, 1 and 0.5 m; storeys 2.5 m (2.3 m headroom for the 1.8 m player).
  - 3 m kit (key 3): 1 m grid; every piece in 3 and 1 m (no 2 m for now); storeys 3 m. No
    stone block (0.5 m wide). Stone slabs stay 0.5 m thick, so heights still go in 0.5 m.
  - Snap points sit every grid step along edges, so structures never drift off their kit's
    grid (a test checks this). Stairs are 27° and rise each size over twice that length (the
    owner liked the 27° slope), in every width of the kit.
- Valheim-style pieces that snap, or place
  freely with Shift. Q/E pick the snap point (and then allow overlaps; hold for auto). T toggles the
  aimed-face rule (default on, being compared: the piece goes on the face you aim at, so a slab
  top stacks and a side face or the ground goes beside). R cycles rotation steps
  (90, 45, 22.5, 15, free). Hold RMB to remove, MMB opens the menu. Rotation is always
  world space, also when snapping; Shift+RMB copies the aimed piece (shape, material,
  rotation). Placeholder texture and colour per material. Sizes may change once they've been
  tried in first person.
- Valheim's support model per material (wood, stone, iron for now; Gorkamorka materials like
  scrap and plastic later). Integrity is solved exactly and in real time. A piece resting on
  another (flat contact) counts as vertical support even when offset, e.g. running-bond blocks.
  Losses are per metre (per 2.5 m, so 2.5 m pieces match Valheim's per-piece numbers), so small
  pieces reach as far as big ones (owner's choice). Stone's horizontal loss is 4 per 2.5 m so it
  still barely overhangs (~0.5 m), like Valheim's no-overhang stone.
- Pieces come in set materials (enforced by `BuildSystem`): wood has every shape (prototyping);
  stone has slabs (0.5 m thick floors that snap on both faces), a 1x0.5x0.5 m block, pillars
  and stairs; iron has pillars and beams.
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

- **Open building topics (raised 2026-10-12):** stair width (2.5/1/0.5 m variants added
  2026-10-12, still being tried out), ladders (as buildable pieces), and roofs (only plain 45°
  slabs so far).

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
  methods through `eval`/reflection, or ask the owner. `capture_game_view --save_path` must be
  inside the project folder.
- Fallback when no Editor is connected: batch-mode Unity can't run on this project folder while
  the Editor has it open. Copy `Assets`, `Packages` and `ProjectSettings` to a scratch folder and
  run there:
  `Unity.exe -batchmode -nographics -projectPath <copy> -runTests -testPlatform EditMode -testResults <file>`
- To *see* something without the owner, drop the `-nographics` flag and run a throwaway
  play-mode test in the copy that renders a camera to a RenderTexture and saves a PNG. After
  copying `Assets` again, rebuild the test map in the copy first: script GUIDs in the copied
  `.meta` files differ from the copy's old scene.
- Test map: menu **ShooterMashup > Build Movement Test Map** regenerates
  `Assets/Scenes/MovementTest.unity` (stairs, ramps, crates, tunnels, ladders, fall towers,
  water and slime tanks, slick floor, strafe lane, and a building area with diggable dirt and bridge cliffs).
