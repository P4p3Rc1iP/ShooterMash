# Vehicles (contraptions)

First prototype of building and driving vehicles. The lift is copied from Scrap Mechanic; the
driving model takes after Trailmakers, where whatever you build drives decently, but with
engines, driven and free wheels and tyres that let go, so cars can drift and spin.

## Try it

Rebuild the test map and press Play. A buggy stands to the right of the spawn hub, facing
an ice sheet (tyres keep only 15% of their grip on it: try turning). Walk through the portal behind the spawn
(or drive the road south) to the **off-road area**. West of where you arrive is a **lineup** of
six vehicles facing the rally track, each with a sign:

| Vehicle | Drive | Engine | Mass | Feel |
| --- | --- | --- | --- | --- |
| Buggy | rear | V6, 200 kW | 0.6 t | fast and loose: the back steps out under power, drifts |
| Saloon | front | V6, 200 kW | 1.6 t | quick, understeers, hard to unsettle |
| Van | rear | V8, 320 kW | 3.7 t | heavy, stable unless provoked |
| Rally truck | all | 2 x V8, 640 kW | 1.9 t | absurdly fast (0-100 in 3 s), four-wheel drifts |
| Heavy truck | 8x8 | diesel, 420 kW | 20 t (15 t of iron) | lumbering: 0-90 km/h in about 25 s |
| Semi | 6x4 | diesel, 420 kW | 19 t with its trailer (13 t of stone) | lumbering, the trailer swings behind |

The off-road area also has a 1160 m rally loop with rollers, washboard, rocks and a table-top
jump, where a rally truck drives up to ~150 km/h while you stand on it, and stunt tests (a crash,
a flip and a loop) by the arrival.

| Input | Action |
| --- | --- |
| F | sit in the seat you aim at / get out |
| W / S | throttle / brake, then reverse |
| A / D | steer |
| Space | handbrake (locks the rear wheels: they slide) |
| Y | driving assists: casual → standard → expert |
| T | traction control on / off (off: the engine can spin the wheels: burnouts, power slides) |
| F5 | telemetry overlay (for tuning) |
| V | camera: follow (default) → first person → free orbit (scroll zooms) |
| L | lift tool |

With the lift tool (L):

| Input | Action |
| --- | --- |
| LMB on the ground | place your lift there (or move it, with whatever is on it) |
| LMB on a vehicle | put it on your lift, upright: to work underneath it, or to recover it after a flip |
| RMB | take the lift away: the vehicle drops and can drive |
| Q / E | lower / raise the lift (0.5 to 3 m) |
| Scroll | turn the lift |

Build on a lift with the build tool (B). The first piece on an empty lift starts a vehicle. A
vehicle can only be built on while it's on a lift. Every building piece works (a shed on a
truck); **scrap** is the light material for bodies (sheet metal on a frame, 200 kg/m³). Vehicle
parts (in every material tab; their mass is their own):

- **Wheels** in four sizes: small (0.8 m, road), standard (1 m), big (1.5 m, off-road) and huge
  (2 m, trucks), each **driven** (the engines turn it) or **free** (it only rolls). Snap one to
  a beam's side or the end of an axle beam; turn it 180° for the other side. Driven rear wheels
  and free fronts make a rear-wheel-drive car, and so on.
- **Engines:** 4-cyl (130 kW), V6 (200 kW), V8 (320 kW) and diesel (420 kW, huge pull, 108 km/h
  top). Engines add up. Without one a vehicle still drives, as before engines existed.
- **Driver seat:** it faces the way the vehicle drives; it sits on top of a beam or a floor.
- **Fifth wheel and kingpin:** a fifth wheel on a tractor's deck, a kingpin under a trailer's
  front. Back the tractor under the trailer and they couple when they come within 0.4 m.

## Layout

```
Vehicle.cs            the vehicle: a BuildHost with its own BuildSystem in its local space, one
                      Rigidbody, mass from the pieces, engines, raycast wheels, tyres, assists, trailers
VehicleBuildWorld.cs  "ground" for a vehicle's pieces: connected to the anchor piece is enough
VehicleTuning.cs      every driving number, with tooltips
Lift.cs               the lift: a platform; holds one vehicle frozen; builds new vehicles
LiftTool.cs           the player's lift tool (L)
VehicleDriver.cs      sitting, driving controls and the three cameras
EngineRevs.cs         revs and an automatic gearbox, for show (rev counter, engine sound)
VehicleEffects.cs     tyre smoke where tyres slide; SkidMarks.cs: the marks they leave
VehicleAudio.cs       engine, tyre squeal and impact sounds, synthesised at start (no sound files)
VehicleHud.cs         the driver's gauges: speed, gear, rev bar, TC light, what each wheel is doing
VehicleTelemetry.cs   the F5 tuning overlay
DrivingAssists.cs     the player's driving assists (casual, standard, expert)
VehicleSoundBank.cs   recorded engine loops per engine piece (the asset is in Assets/Audio); VehicleSounds puts it in use
VehicleShowcase.cs    the demo designs, built on a lift and dropped (the semi's trailer is coupled)
VehicleAutopilot.cs   drives a vehicle round a path (the rally truck), optionally only with a rider
StuntDriver.cs        drives a stunt once someone stands on it, then resets (crash, flip, loop)
Riders.cs             is a player riding a vehicle?
```

## How it works

- **Building.** A vehicle is a `BuildHost` (see `Building/Runtime/BuildHost.cs`): the build tool
  aims at a collider and builds on the host it belongs to (the world, a vehicle, or a lift), in
  that host's local space. So snapping, the menu, removal previews and undo work as usual.
  `PieceViews` keeps the pieces' GameObjects under the vehicle's body, so their colliders make
  up its compound collider.
- **Connected is enough.** Vehicle materials have zero support loss, and one piece (the anchor)
  counts as grounded: anything connected to it holds, anything cut off falls off (vanishes,
  like collapsed building pieces). The first piece must rest on the lift.
- **Mass** is each piece's volume times its material's density (wood 500, stone 2400, iron
  7800, scrap 200 kg/m³); wheels and engines have their own. Inertia comes from the pieces' boxes.
- **Engines** drive the driven wheels on the ground, each by how hard it presses on the ground
  (the rear pulling away, the outside in a corner): with up to their pull at low speed (low
  gears), above that with their power (force = power / speed), up to their top speed, where a
  limiter cuts in. Air drag comes from the frontal area, so light fast cars are held back at
  speed and heavy trucks hardly at all; tyres have rolling resistance.
- **Wheels** are raycast suspension, no joints: a spring and damper per wheel, sized to the
  weight that wheel carries (sitting 45% into its travel). Forward is where the seat faces.
- **Steering geometry.** The vehicle turns about a point between its rear (non-steering)
  wheels, and every wheel points along its own circle round the turn's centre: the inner front
  wheel turns more than the outer (Ackermann), and on a long truck the wheels behind the pivot
  steer a little the other way, so no axle is dragged sideways (the 8x8 turns in 10 m). Up to
  38° of lock.
- **Tyres.** Each tyre works out the force that would stop it sliding sideways, plus the drive
  or brake asked of it, and gets as much as its grip allows: grip (0.75) times its load, about
  1 g of cornering. Asked for more it holds on at the limit, and only once it actually slides
  (over 1.5 m/s: sideways, spinning or skidding) does it let go: a sliding tyre grips less (75%)
  until the slide dies down, its force pointing along the slip, so a wheel spinning under power
  has little sideways grip left. That's what makes the back of a rear-wheel-drive car step out
  under power, a front-wheel-drive one push wide, and a handbrake turn spin.
- **Surfaces:** wheels on a surface marked `SurfaceFlags.Slick` (the players' slick flag, via
  `PmSurface`) keep only `slickGrip` (15%) of their grip: ice.
- **Driving assists** are the player's (`DrivingAssists`, Y cycles them while driving, and
  they're remembered); the vehicle is the same whoever drives it:

  | | Casual | Standard | Expert |
  | --- | --- | --- | --- |
  | Traction control | keeps grip free for cornering | stops wheelspin from drive alone | off |
  | ABS | on | on | off: brakes lock |
  | Stability control (ESC light) | strong, past 6° of slip | light, past 10° | off |
  | Understeer limiter | on | on | off |
  | Steering lock at speed | at the tyres' limit | just past it | much freer |
  | Rollover assist | from 20° | from 30° | off |
  | Throttle / steering build-up | gentle | medium | sharp |

  T toggles traction control on top of any level. The understeer limiter eases the lock off
  while the steered tyres slide sideways, so holding full lock gives the tightest turn the
  tyres allow instead of a plough; countersteer is always possible (lock grows by the slip angle).
- **Vehicle assists** (what makes anything drive, whoever drives it):
  - springs sized per wheel from where the weight sits (also a trailer's weight on its tractor),
    so a loaded truck sits level;
  - body roll is mostly real: the springs resist leaning less than bouncing (`rollStiffness`
    0.3: our vehicles are twice as wide as real ones, so they'd hardly lean otherwise; the demo
    designs set their own until suspension parts exist: buggy 1.2, saloon and rally truck 0.6,
    van 0.35), and the centre of mass is only held down for very tall builds. A tall, loaded van
    leans hard and lifts a wheel in a fast corner. Each wheel's roll spring pairs with the wheel
    across the same axle (the nearest one on the other side of the centre of mass, within half a
    metre along); an unpaired wheel just has its own spring;
  - the body turns upright in the air, a little downforce (at most 4 m/s²), extra gravity, a
    low-friction body (scrapes skid instead of braking hard), and it holds still when parked.
- **Keyboard.** The keys are on or off, so the controls build up (`VehicleDriver`): the throttle
  opens over half a second (tap W to feather it) and closes fast, the brake comes on faster,
  and the steering turns in more slowly the faster you go and comes back quickly.
- **Trailers.** A kingpin couples to a fifth wheel with a joint: free to turn, pitching over
  crests, rolling a little. A coupled trailer brakes and parks with its tractor. Putting either
  on a lift uncouples them; there's no key to uncouple yet.
- **What you see and hear** (so you can read the tyres): sliding tyres smoke and leave skid
  marks, more the faster they slide (a little mist on ice), in the ground's colour (from its
  `PmSurface` type, or its material), and squeal. The HUD shows speed, gear and a rev bar that
  goes red at the redline and flashes on the limiter, a TC light while traction control holds
  the wheels back, a steering strip (where the front wheels point against where the car goes:
  line them up to steer along a slide; the band is the lock there is), and the wheels from
  above: green gripping, orange sliding, red spinning under power, blue locked, grey in the
  air, driven ones edged white. F5 shows a telemetry overlay for tuning (graphs of speed, revs,
  inputs, slip, lean; per wheel its load, grip used and slip). The camera widens its view with
  speed, shakes on hard jolts and tilts into slides. The revs come from an automatic gearbox
  that's only for show (the drive force doesn't use it): they follow the driven wheels, so
  wheelspin or a wheel in the air flares them, and the clutch slips pulling away.
- **Sound.** Engines only run while someone (or an autopilot) is actually driving. Each engine
  piece plays a recorded loop from the sound bank (`Assets/Audio/Vehicle sounds.asset`: swap
  clips and pitch ranges in the inspector), stretched from idle to the redline, muffled off the
  throttle; the loops' silent ends (MP3 padding) are trimmed and the seam crossfaded. Without a
  loop it falls back to a made-up one. Lifting off at high revs pops the exhaust (with flames);
  tyres squeal as they slide; the body thumps when it hits something.
- **The lift** freezes the vehicle (kinematic body) and sets its pose; taking it away makes the
  body dynamic again. Putting a vehicle on it rests the vehicle's lowest point (usually the
  tyres at full droop) on the platform; adding parts that hang lower raises it.

## Backlog (next session, from the owner's test drive on 2026-10-15)

- **Heavy truck's lean returns slowly.** After a corner it rolls back upright too lazily, which
  feels off (look at roll damping and the soft roll stiffness on heavy, tall builds).
- **Casual is over-tuned.** It should still allow drifting (stability control and the
  cornering-aware traction control are probably too strong).
- **Standard makes driving fast too hard.** It's easy to lose control, especially when power
  spins the wheels while already going fast, and once drifting it's hard to get out of it.
  Most noticeable on the buggy; the rally truck feels much better by comparison.
- **More test cars:** a RWD muscle car, a FWD hatchback and a 4x4 rally car.
- **Rolling hills** to test the feel in more representative terrain than the flat floor.
- Still open: gearbox as a part (ratios) with automatic or manual shifting as a player setting
  (proposed, not agreed yet), and builder parts (tyres, suspension and anti-roll bars,
  differentials, brakes, wings, ballast).

## Known gaps and next steps

- **Riding** works (see the player controller's `PlatformRider`): stand on a vehicle and you're
  carried along, and jumps on it land where you took off, even while it turns. Jump off the side
  and you keep its speed; jump on from the ground and you land standing still on it. The rally
  truck in the off-road area only drives while you stand on it (`VehicleAutopilot`).
  Hard jolts (over 2 g) throw you. `StuntDriver` runs stunts once you stand on a vehicle (a crash
  into a concrete block, a flip off a one-sided kicker, a loop), steering in the vehicle's own
  frame so it works upside down, and puts the vehicle back afterwards (stunt tests in the
  off-road area). Flipping or looping drops you: past 45° of tilt you're no longer standing.
- **Multiplayer:** planned as driver authority: the driver's machine runs the vehicle's physics
  and others see a smoothed copy. Rigid-body physics can't be predicted like player movement.
- No real gears (the gearbox is only for show), clutch, fuel or ignition (an engine starts when
  someone drives); no differential (the drive is shared equally).
- No crash damage; pieces don't break off. Vehicles don't damage buildings.
- No uncouple key for trailers, and no landing legs: an uncoupled trailer rests on its nose.
- Vehicles only drive on colliders (fine for voxel terrain with colliders).
