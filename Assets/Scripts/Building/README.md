# Building (Valheim-style structures)

Place greybox pieces that snap together, need support from the ground, and collapse when
that support goes. Structures only for now; vehicles come later.

## Try it

Rebuild the test map (`ShooterMashup > Build Movement Test Map`); you spawn in front of the
building area (around x = 56). Press **B** to take out the build tool. A house, a
pavilion and a lookout tower are built there from real pieces when play starts (`BuildShowcase`).

Pieces are on a 1 m grid: every shape in 3 m and 1 m, storeys of 3 m (2.8 m headroom under a
floor).

| Input | Action |
| --- | --- |
| B | take out / put away the build tool |
| Middle mouse | build menu (material and piece, with pictures) |
| Shift + right mouse | copy the piece you aim at: its shape, material and rotation |
| Left mouse | place |
| Right mouse (hold) | show what would collapse; release to remove the piece, or dig a dirt block |
| Scroll | rotate |
| R | rotation steps: 90° → 45° → 22.5° (default) → 15° → free (5° per notch) |
| Q / E | choose which snap point of the held piece attaches where you aim (yellow marker); "auto" picks the best fit; hold Q or E to go back to auto. A chosen point may overlap other pieces (only near-duplicates are refused) |
| Shift (hold) | free placement, no snapping (also sprint) |
| Ctrl+Z / Ctrl+Y | undo / redo your own recent actions (Ctrl also crouches; harmless) |

Pieces come in set materials: wood has every shape, including diagonal beams (45° and 27°, rising
1 or 3 m, snapping at the ends of their centre line) and see-through 1 m wide ladders (1 and 3 m tall,
climbable, snapping 0.1 m out from what they lean on); stone has slabs (0.5 m thick floors),
pillars and stairs; iron has pillars and (diagonal) beams. Horizontal beams snap by their centre
line. Each material has a placeholder texture and colour: wood planks, stone bricks, riveted iron
plates (`StructureManager > Material Colors`). While the tool is out, the colour shows support
instead (blue grounded, then green, yellow, red as support runs out) and the pattern still shows
the material. Lean is off while the tool is out (Q/E are used for snapping).

The building area has a pad of **diggable dirt blocks** (a stand-in for voxel terrain) and two
dirt cliffs 30 m apart for bridge tests (wood needs a pillar in between; iron spans it).

## Layout

```
Simulation/   Pure, deterministic. No MonoBehaviours.
  BuildTypes.cs           BuildConfig (materials, tolerances, undo, feature flags), BuildCmd, BuildResult, IBuildWorld
  PieceCatalog.cs         the piece shapes: boxes + snap points, pivot at the bottom centre
  BuildSystem.cs          placed pieces, place/remove, terrain changes
  BuildSystem.Integrity.cs  support solver and collapse
  BuildSystem.Undo.cs     per-player undo/redo
  BuildSnapping.cs        Valheim-style snap-point matching
  Obb.cs, SpatialHash.cs  geometry helpers
World/
  UnityBuildWorld.cs      IBuildWorld on PhysX: anything solid that isn't a piece is ground
  DiggableBlock.cs        stand-in terrain that can be dug away
  BuildPieceTag.cs        marks a piece's GameObject
Runtime/
  StructureManager.cs     owns the BuildSystem, creates piece GameObjects, colours them
  BuildTool.cs            the player's tool: input, ghost, menu, HUD
  BuildMeshes.cs          greybox meshes and materials
  BuildTextures.cs        placeholder textures per material, generated at runtime
  PieceThumbnails.cs      build menu pictures, rendered by a hidden camera
```

## How it works

- **Commands, like movement.** Everything goes through `BuildSystem.Execute(BuildCmd)`:
  Place, Remove, Undo, Redo. With networking, clients send BuildCmds and the server runs them.
  Views only listen to `BuildSystem.Changed`.
- **Support (Valheim's model, per metre).** A piece touching terrain or static world geometry is
  grounded and gets its material's max support. Other pieces get their best neighbour's support
  (capped at their own max) minus `max * loss * length / 2.5 m`. The loss blends between the
  material's horizontal and vertical loss by the direction of the connection, and `length` is the
  piece's size that way (its height when stacked, how far it reaches out when held from the
  side). Unlike Valheim it's per metre, not per piece, so small pieces reach as far as big ones.
  A piece resting on another counts as stacked even when offset (e.g. staggered slabs). Below the
  minimum, a piece breaks.

  | Material | Max | Min | Vertical loss | Horizontal loss | Result |
  | --- | --- | --- | --- | --- | --- |
  | Wood | 100 | 10 | 12.5% | 20% | about 20 m high, 10 m out from a post |
  | Stone | 1000 | 100 | 12.5% | 400% | about 20 m high, overhangs 0.5 m at most |
  | Iron | 1500 | 20 | 7.7% | 7.7% | about 30 m out from a post |

  (Losses per 2.5 m.) Valheim's stone can't overhang at all; per metre its 100% would allow
  2.25 m, hence 400%. Because the loss scales with the piece's own maximum, iron hung off wood
  breaks (wood can carry at most 100, iron loses 115 per 2.5 m). Values are in
  `StructureManager > Config` and can be edited in play mode.
- **Real-time integrity.** Solved exactly with a Dijkstra-style pass from the grounded pieces,
  only over the structure that changed. Removing a pillar collapses everything it held at once.
- **Pieces touch** when their boxes are within 2 cm (`connectTolerance`). Connections don't have
  to be snapped; anything touching counts.
- **Snapping.** You aim at a snap point on a placed piece: the nearest one (within 1 m) is where
  the held piece attaches. With Q/E you pick which of the held piece's points goes there;
  otherwise it's chosen automatically: never sinking into an existing piece (more than 20% of
  either's volume), then on the face you aim at (Minecraft style: a stone slab's top stacks on
  it, its side face or the ground beside it goes beside), then the best fit (most snap points
  lining up), then the side of the surface you're looking at. Where nothing can sit on the
  aimed face (a floor beside a floor, a beam on a post), the fit decides. Pieces keep the
  rotation you gave them (world space) when they snap; to match an angled piece, copy it with
  Shift + right click.
- **Undo.** Your own actions only, within 60 s, and only if nobody else (or the terrain) changed
  anything within a metre of it since. Undoing a removal restores everything that collapsed with
  it. Undoing a placement is refused if something built on it since would fall.
- **Terrain changes** call `BuildSystem.TerrainChanged(bounds)`, which re-checks grounding in that
  area. The voxel world needs to report dug or filled regions the same way.
- **Collapse is visual only.** Pieces vanish for now; falling debris will be cosmetic and never
  networked.

## Tests

`Assets/Tests/EditMode/Building` checks support values per material, bridges, collapse and its
preview, digging, blocking by players, undo/redo rules, snapping and determinism.
