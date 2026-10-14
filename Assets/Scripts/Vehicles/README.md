# Vehicles (contraptions)

First prototype of building and driving vehicles. The lift is copied from Scrap Mechanic; the
driving model takes after Trailmakers, where whatever you build drives decently.

## Try it

Rebuild the test map and press Play. A buggy stands to the right of the spawn hub; a flatbed
truck circles east of it (jump on the back to ride it). Walk through the portal behind the spawn
(or drive the road south) to the **off-road area**: a 1160 m rally loop with rollers, washboard,
rocks and a table-top jump, where a rally truck on big wheels drives up to ~150 km/h while you
stand on it, and stunt tests (a crash, a flip and a loop) by the arrival.

| Input | Action |
| --- | --- |
| F | sit in the seat you aim at / get out |
| W / S | throttle / brake, then reverse |
| A / D | steer |
| Space | handbrake (the rear slides) |
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
truck), plus two vehicle parts: the **wheel** (1 m, with suspension; snap it to a beam's side or
the end of an axle beam; turn it 180° for the other side) and the **driver seat** (it faces the
way the vehicle drives; it sits on top of a beam).

## Layout

```
Vehicle.cs            the vehicle: a BuildHost with its own BuildSystem in its local space, one
                      Rigidbody, mass from the pieces, raycast wheels and the driving assists
VehicleBuildWorld.cs  "ground" for a vehicle's pieces: connected to the anchor piece is enough
VehicleTuning.cs      every driving number, with tooltips
Lift.cs               the lift: a platform; holds one vehicle frozen; builds new vehicles
LiftTool.cs           the player's lift tool (L)
VehicleDriver.cs      sitting, driving controls and the three cameras
VehicleShowcase.cs    builds the demo buggy on a lift and drops it
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
  7800 kg/m³), plus 40 kg per wheel. Inertia comes from the pieces' boxes.
- **Wheels** are raycast suspension, no joints: a spring and damper sized to the vehicle's
  weight (every wheel carries its share, sitting 45% into its travel), grip in a friction
  circle, all wheels driven, the front half of the wheels steering (the frontmost most).
  Forward is where the seat faces.
- **Assists** (what makes anything drive): the centre of mass is held low, grip forces act
  partway up towards it (less body roll), the body levels itself to the ground and turns
  upright in the air, a little downforce (capped at 6 m/s², or fast vehicles squash their
  springs flat), extra gravity, a low-friction body (scraping the ground or a rock skids instead
  of braking hard: only the tyres grip), and it holds still when parked.
  Acceleration is the same for any weight until engines exist.
- **The lift** freezes the vehicle (kinematic body) and sets its pose; taking it away makes the
  body dynamic again. Putting a vehicle on it rests the vehicle's lowest point (usually the
  tyres at full droop) on the platform; adding parts that hang lower raises it.

## Known gaps and next steps

- **Riding** works (see the player controller's `PlatformRider`): stand on a vehicle and you're
  carried along, and jumps on it land where you took off, even while it turns. Jump off the side
  and you keep its speed; jump on from the ground and you land standing still on it. A truck
  circles east of the spawn hub to try it (`VehicleAutopilot`; sit in it to drive it yourself).
  Hard jolts (over 2 g) throw you. `StuntDriver` runs stunts once you stand on a vehicle (a crash
  into a concrete block, a flip off a one-sided kicker, a loop), steering in the vehicle's own
  frame so it works upside down, and puts the vehicle back afterwards (stunt tests in the
  off-road area). Flipping or looping drops you: past 45° of tilt you're no longer standing.
- **Multiplayer:** planned as driver authority: the driver's machine runs the vehicle's physics
  and others see a smoothed copy. Rigid-body physics can't be predicted like player movement.
- Two wheel sizes (Wheel, and Big wheel: taller travel, faster, twice the push), one seat, no
  engines or fuel.
- No crash damage; pieces don't break off. Vehicles don't damage buildings.
- Vehicles only drive on colliders (fine for voxel terrain with colliders).
