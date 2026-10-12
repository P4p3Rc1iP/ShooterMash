# Player controller (ET-style)

A first-person controller modelled on *Wolfenstein: Enemy Territory*'s `Pmove`
(`bg_pmove.c` / `bg_slidemove.c` / `cg_view.c`). It uses the same constants (converted
exactly to metres), the same order of operations and the same quirks, so it feels like ET.

## Try it

`ShooterMashup > Build Movement Test Map` creates `Assets/Scenes/MovementTest.unity`
(it rebuilds the scene each time). Press Play.

| Key | Action |
| --- | --- |
| WASD | move |
| Space | jump |
| C / Left Ctrl | crouch |
| X | prone (or double-tap W while crouched; double-tap S to get up) |
| Left Shift | sprint |
| Left Alt | walk (silent) |
| Q / E | lean |
| F1 / F2 / F3 | noclip / respawn / toggle HUD |
| F4 | switch between the ShooterMash (default) and ET-exact values |
| Esc | free the mouse (click to recapture) |

## Layout

```
Simulation/   Pure, deterministic movement code. No MonoBehaviours.
  PmoveTypes.cs        PlayerCmd, PlayerState, MoveModifiers, events, IPmoveWorld
  MoveConfig.cs        every tunable, with ET defaults, plus feature toggles
  PlayerMove*.cs       the simulation, one partial file per area
World/        Collision back-end
  UnityPmoveWorld.cs   IPmoveWorld on Unity physics (axis-aligned box sweeps)
  PmSurface.cs         ladder / slick / no-damage / footstep material on a collider
  LiquidVolume.cs      water, slime or lava trigger volumes
Runtime/      Unity glue for a local player
  LocalPlayerInput.cs  keyboard + mouse -> PlayerCmd (ET mouse scale: sensitivity * 0.022)
  PlayerMotor.cs       fixed-tick loop (default 125 Hz), render interpolation, events
  PlayerView.cs        camera: view height, bob, step/stance smoothing, landing dip, lean, FOV
  PlayerVitals.cs      placeholder health for fall damage
  PlayerDebugHud.cs    speedometer, stamina, debug keys
```

## Things to know

- **Units.** Metres, m/s, seconds, degrees. ET works in Quake units (1 qu = 1 inch); every
  value was converted exactly, so the player is 1.83 m tall, steps up 0.46 m, runs 6.5 m/s,
  sprints 8.9 m/s and jumps 1.16 m. ET's tiny internal tolerances are written in the code
  as multiples of `Inch` so they can still be compared with the original source.
- **Presets.** `PlayerMotor` has a preset dropdown (F4 in play mode). *ShooterMash* is the
  default: ET rounded to metric (1.8 m tall, 6.4 m/s run, 20 m/s² gravity, fall damage from
  6 m...) plus changes for building: 0.52 m steps (so 50 cm steps can be run up) and a 0.6 m
  prone hull that fits under a floor on 1 m pillars. Stamina is more forgiving: a full bar
  lasts 10 s of sprint (ET: 4 s) and refills twice as fast. *ET exact* is the original, for comparison.
  Picking one overwrites those values in `movement`; see `MovePresets.cs`.
  (ET's prone hull is 1.02 m, taller than its eyes; `MoveConfig.proneHeight` sets it separately.)
- **The transform is the simulation origin**, 0.61 m above the feet, as in ET.
- **Stamina** is 0..1 for a full ET bar (4 s of sprint, 0.125 per jump).
- **Modifiers.** `PlayerState.modifiers` (speed, staminaMax, staminaCost, staminaRegen) is how
  other systems (food, encumbrance, injuries) affect movement. Set `PlayerMotor.modifiers`;
  it's applied every tick and can be edited in the Inspector during play. They are part of
  the simulated state, so they'll network like everything else, with the server deciding them.
- **Fall damage** is configured as fall heights in metres (`MoveConfig.fallDamage`).
- **Networking.** `PlayerMove.Move(ref state, cmd)` is a pure function of state, command and
  world, so the same code can run on the server and as client prediction. `PlayerCmd` is
  what you'd send, and `PlayerState` is what you'd reconcile against.
- **Voxel world.** Implement `IPmoveWorld` (box sweep, point contents, depenetration) on the
  voxel data to skip PhysX entirely, or keep using colliders.
- **Collision layers.** The player must not have colliders on layers in `PlayerMotor.solidMask`.
- **Tests.** `Assets/Tests/EditMode` checks speeds, jump height, cooldown, stamina,
  step/slope limits, fall damage, tunnels, lean, ladders, water and determinism against ET's
  values. Run them in Window > General > Test Runner.

## Deliberate departures from ET

- No velocity rounding. ET rounds velocity to whole qu/s each tick (a bandwidth saving),
  which made jump height framerate-dependent: the "125 fps jump". Ours is always 1.16 m.
- `CorrectAllSolid` actually moves you out of solids (ET's version never moves the player),
  with a PhysX depenetration fallback. Players will get stuck in freshly placed voxels.
- Mouse look is applied every rendered frame and the body is interpolated between ticks.
- The prone key is edge-triggered (one press = one toggle).
- Stepping up rises only as far as there's headroom (Quake 3's later `PM_StepSlideMove`), and
  if a ceiling starting right above the step blocks the full-height step, lower steps are
  tried. ET tested only the full step height, so a low ceiling blocked even small steps, e.g.
  crawling prone onto a floor piece with another floor on 1 m pillars above it.
- `MoveConfig.maxAirSpeed` can cap strafe-jump speed gain (0 = ET behaviour).
- No weapons yet, so ET's weapon-specific movement rules (heavy weapons, mounted MG42,
  firing blocking lean) are left out. `MoveModifiers.speed` is the hook for slowdowns.
