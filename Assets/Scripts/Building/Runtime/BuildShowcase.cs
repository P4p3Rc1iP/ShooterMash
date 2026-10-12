using System;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Builds two example structures when play starts, through ordinary BuildCmds: they have real
    /// support, show the support colours and collapse like anything a player builds. Placements
    /// that fail are logged, which doubles as a check on the pieces.
    /// </summary>
    [RequireComponent(typeof(StructureManager))]
    public class BuildShowcase : MonoBehaviour
    {
        /// <summary>Owner id for showcase pieces, so they never mix with a player's undo history.</summary>
        public const int Owner = -2;

        [Tooltip("Where the house's foundation starts (its -x, -z corner).")]
        public Vector3 houseCorner = new Vector3(48f, 0f, 20f);
        [Tooltip("Where the pavilion's platform starts (its -x, -z corner).")]
        public Vector3 pavilionCorner = new Vector3(48f, 0f, 35f);
        [Tooltip("Where the lookout tower's legs start (its -x, -z corner).")]
        public Vector3 towerCorner = new Vector3(61f, 0f, 24f);

        StructureManager structures;
        int placed, failed;

        const BuildMaterial Wood = BuildMaterial.Wood, Stone = BuildMaterial.Stone, Iron = BuildMaterial.Iron;
        const float T = PieceCatalog.Grid, H = T / 2f; // a 3 m tile, and half of one

        void Start()
        {
            structures = GetComponent<StructureManager>();
            BuildHouse(houseCorner);
            BuildPavilion(pavilionCorner);
            BuildTower(towerCorner);
            if (failed > 0)
                Debug.LogWarning($"Build showcase: {placed} pieces placed, {failed} failed (see above).");
            else
                Debug.Log($"Build showcase: {placed} pieces placed.");
        }

        /// <summary>
        /// A house on a stone foundation with a doorway made of 1x1 walls, a gable roof, and stone
        /// stairs up to a back deck on iron pillars.
        /// </summary>
        void BuildHouse(Vector3 o)
        {
            const float slabTop = 0.5f, wallTop = slabTop + T;

            // Foundation: 3 x 3 stone slabs.
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Place("Slab 3x3", Stone, o + new Vector3(H + T * i, 0, H + T * j));

            // Walls of the 2 x 2 tile house.
            for (int i = 0; i < 2; i++)
                Place("Wall 3x3", Wood, o + new Vector3(H + T * i, slabTop, 2 * T));       // back
            for (int j = 0; j < 2; j++)
            {
                Place("Wall 3x3", Wood, o + new Vector3(0, slabTop, H + T * j), 90);       // left
                Place("Wall 3x3", Wood, o + new Vector3(2 * T, slabTop, H + T * j), 90);   // right
            }
            Place("Wall 3x3", Wood, o + new Vector3(H, slabTop, 0));                      // front left

            // Front right: a 1 m wide, 2 m tall doorway in the middle of a tile of 1x1 walls.
            for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                if (col != 1 || row == 2)
                    Place("Wall 1x1", Wood, o + new Vector3(T + 0.5f + col, slabTop + row, 0));

            // Upper floor over the house, and the back deck on iron pillars.
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                Place("Floor 3x3", Wood, o + new Vector3(H + T * i, wallTop - 0.2f, H + T * j));
            foreach (float x in new[] { 0f, T, 2 * T, 3 * T })
                Place("Pillar 3 m", Iron, o + new Vector3(x, slabTop, 3 * T));
            Place("Pillar 3 m", Iron, o + new Vector3(3 * T, slabTop, 2 * T));
            for (int i = 0; i < 3; i++)
                Place("Floor 3x3", Wood, o + new Vector3(H + T * i, wallTop - 0.2f, 2 * T + H));

            // Stone stairs up the side, arriving at the deck.
            Place("Stairs 3 m up, 1 m wide", Stone, o + new Vector3(2 * T + H, slabTop, T));

            // Railing round the deck.
            for (int k = 0; k < 9; k++)
                Place("Wall 1x1", Wood, o + new Vector3(0.5f + k, wallTop, 3 * T));
            for (int k = 0; k < 3; k++)
                Place("Wall 1x1", Wood, o + new Vector3(3 * T, wallTop, 2 * T + 0.5f + k), 90);

            // Gable roof over the house, ridge along x.
            for (int i = 0; i < 2; i++)
            {
                Place("Roof 45° 3x3", Wood, o + new Vector3(H + T * i, wallTop, H));
                Place("Roof 45° 3x3", Wood, o + new Vector3(H + T * i, wallTop, T + H), 180);
            }
        }

        /// <summary>
        /// An open pavilion on a raised stone platform with wide entrance stairs, wood and iron
        /// pillars and beams, a gable roof, a back wall and a side wall with a window.
        /// </summary>
        void BuildPavilion(Vector3 o)
        {
            const float deck = 1f, top = deck + T;   // platform top, pillar tops

            // Platform: two layers of 3 x 2 stone slabs, 1 m high.
            for (int layer = 0; layer < 2; layer++)
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
                Place("Slab 3x3", Stone, o + new Vector3(H + T * i, 0.5f * layer, H + T * j));

            // Wide stone stairs up to the middle of the front.
            Place("Stairs 1 m up, 3 m wide", Stone, o + new Vector3(T + H, 0, -1f));

            // Pillars on the front and back edges: wood, and iron along the right side.
            for (int i = 0; i <= 3; i++)
            foreach (float z in new[] { 0f, 2 * T })
                Place("Pillar 3 m", i == 3 ? Iron : Wood, o + new Vector3(T * i, deck, z));

            // Beams on top, flush with the pillar tops: along x on the front and back, along z on every row.
            for (int i = 0; i < 3; i++)
            foreach (float z in new[] { 0f, 2 * T })
                Place("Beam 3 m", i == 2 ? Iron : Wood, o + new Vector3(H + T * i, top - 0.2f, z));
            for (int i = 0; i <= 3; i++)
            for (int j = 0; j < 2; j++)
                Place("Beam 3 m", i == 3 ? Iron : Wood, o + new Vector3(T * i, top - 0.2f, H + T * j), 90);

            // Gable roof, ridge along x.
            for (int i = 0; i < 3; i++)
            {
                Place("Roof 45° 3x3", Wood, o + new Vector3(H + T * i, top, H));
                Place("Roof 45° 3x3", Wood, o + new Vector3(H + T * i, top, T + H), 180);
            }

            // Back wall, and a left wall with a window made of 1x1 pieces.
            for (int i = 0; i < 3; i++)
                Place("Wall 3x3", Wood, o + new Vector3(H + T * i, deck, 2 * T));
            Place("Wall 3x3", Wood, o + new Vector3(0, deck, H), 90);
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                if (row != 1 || col != 1)
                    Place("Wall 1x1", Wood, o + new Vector3(0, deck + row, T + 0.5f + col), 90);
        }

        /// <summary>
        /// A lookout tower: four legs three storeys high (stone at the bottom, wood above), braced
        /// by a ring of beams at each level, with a railed platform on top and ladders up the front.
        /// </summary>
        void BuildTower(Vector3 o)
        {
            const int levels = 3;
            const float top = T * levels;

            for (int level = 0; level < levels; level++)
            foreach (float x in new[] { 0f, T })
            foreach (float z in new[] { 0f, T })
                Place("Pillar 3 m", level == 0 ? Stone : Wood, o + new Vector3(x, T * level, z));

            // A ring of beams at the top of each level, flush with the pillar tops.
            for (int level = 1; level <= levels; level++)
            {
                float y = T * level - 0.2f;
                foreach (float z in new[] { 0f, T })
                    Place("Beam 3 m", Wood, o + new Vector3(H, y, z));
                foreach (float x in new[] { 0f, T })
                    Place("Beam 3 m", Wood, o + new Vector3(x, y, H), 90);
            }

            // Platform with a railing; the front has a gap where the ladder arrives.
            Place("Floor 3x3", Wood, o + new Vector3(H, top - 0.2f, H));
            for (int k = 0; k < 3; k++)
            {
                Place("Wall 1x1", Wood, o + new Vector3(0.5f + k, top, T));
                Place("Wall 1x1", Wood, o + new Vector3(0, top, 0.5f + k), 90);
                Place("Wall 1x1", Wood, o + new Vector3(T, top, 0.5f + k), 90);
                if (k != 1)
                    Place("Wall 1x1", Wood, o + new Vector3(0.5f + k, top, 0));
            }

            // Ladders up the middle of the front, facing out, against the beams (their faces are
            // 0.1 m in front of the beam line).
            for (int level = 0; level < levels; level++)
                Place("Ladder 3 m", Wood, o + new Vector3(H, T * level, -0.1f), 180);
        }

        void Place(string name, BuildMaterial material, Vector3 position, float yaw = 0f)
        {
            int type = Array.FindIndex(PieceCatalog.All, d => d.name == name);
            BuildResult r = type < 0
                ? BuildResult.Fail(BuildFailure.UnknownPiece)
                : structures.System.Execute(BuildCmd.Place(Owner, type, material, position, Quaternion.Euler(0, yaw, 0), Time.timeAsDouble));
            if (r.Ok)
            {
                placed++;
                return;
            }
            failed++;
            Debug.LogWarning($"Build showcase: {material} {name} at {position} (yaw {yaw}) failed: {r.failure}");
        }
    }
}
