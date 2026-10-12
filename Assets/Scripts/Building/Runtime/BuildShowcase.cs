using System;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Builds two example structures when play starts, one per piece kit, through ordinary
    /// BuildCmds: they have real support, show the support colours and collapse like anything a
    /// player builds. Placements that fail are logged, which doubles as a check on the pieces.
    /// </summary>
    [RequireComponent(typeof(StructureManager))]
    public class BuildShowcase : MonoBehaviour
    {
        /// <summary>Owner id for showcase pieces, so they never mix with a player's undo history.</summary>
        public const int Owner = -2;

        [Tooltip("Where the 2.5 m kit house's foundation starts (its -x, -z corner).")]
        public Vector3 houseCorner = new Vector3(48f, 0f, 20f);
        [Tooltip("Where the 3 m kit pavilion's platform starts (its -x, -z corner).")]
        public Vector3 pavilionCorner = new Vector3(48f, 0f, 35f);

        StructureManager structures;
        int placed, failed;

        const BuildMaterial Wood = BuildMaterial.Wood, Stone = BuildMaterial.Stone, Iron = BuildMaterial.Iron;

        void Start()
        {
            structures = GetComponent<StructureManager>();
            BuildHouse(houseCorner);
            BuildPavilion(pavilionCorner);
            if (failed > 0)
                Debug.LogWarning($"Build showcase: {placed} pieces placed, {failed} failed (see above).");
            else
                Debug.Log($"Build showcase: {placed} pieces placed.");
        }

        /// <summary>
        /// 2.5 m kit: a house on a stone foundation with a doorway made of small pieces, a gable
        /// roof, and stone stairs up to a back deck on iron pillars.
        /// </summary>
        void BuildHouse(Vector3 o)
        {
            const int kit = 0;
            const float t = 2.5f, h = t / 2f;   // tile, half tile
            const float slabTop = 0.5f, wallTop = slabTop + 2.5f;

            // Foundation: 3 x 3 stone slabs.
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Place(kit, "Slab 2.5x2.5", Stone, o + new Vector3(h + t * i, 0, h + t * j));

            // Walls of the 2 x 2 tile house.
            for (int i = 0; i < 2; i++)
                Place(kit, "Wall 2.5x2.5", Wood, o + new Vector3(h + t * i, slabTop, 2 * t));       // back
            for (int j = 0; j < 2; j++)
            {
                Place(kit, "Wall 2.5x2.5", Wood, o + new Vector3(0, slabTop, h + t * j), 90);       // left
                Place(kit, "Wall 2.5x2.5", Wood, o + new Vector3(2 * t, slabTop, h + t * j), 90);   // right
            }
            Place(kit, "Wall 2.5x2.5", Wood, o + new Vector3(h, slabTop, 0));                      // front left

            // Front right: a 1 m wide, 2 m tall doorway framed with 1 m and 0.5 m pieces.
            float x0 = o.x + t;
            Place(kit, "Wall 1x1", Wood, new Vector3(x0 + 0.5f, slabTop, o.z));
            Place(kit, "Wall 1x1", Wood, new Vector3(x0 + 0.5f, slabTop + 1, o.z));
            foreach (float x in new[] { 0.25f, 0.75f, 1.25f, 1.75f })                               // top row and lintel
                Place(kit, "Wall 0.5x0.5", Wood, new Vector3(x0 + x, slabTop + 2, o.z));
            for (int k = 0; k < 5; k++)                                                             // right of the door
                Place(kit, "Wall 0.5x0.5", Wood, new Vector3(x0 + 2.25f, slabTop + 0.5f * k, o.z));

            // Upper floor over the house, and the back deck.
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                Place(kit, "Floor 2.5x2.5", Wood, o + new Vector3(h + t * i, wallTop - 0.2f, h + t * j));
            foreach (float x in new[] { 0f, t, 2 * t, 3 * t })
                Place(kit, "Pillar 2.5 m", Iron, o + new Vector3(x, slabTop, 3 * t));
            Place(kit, "Pillar 2.5 m", Iron, o + new Vector3(3 * t, slabTop, 2 * t));
            for (int i = 0; i < 3; i++)
                Place(kit, "Floor 2.5x2.5", Wood, o + new Vector3(h + t * i, wallTop - 0.2f, 2 * t + h));

            // Stone stairs up the side, arriving at the deck.
            Place(kit, "Stairs 2.5 m up, 1 m wide", Stone, o + new Vector3(2 * t + h, slabTop, t));

            // Railing round the deck: 1 m and 0.5 m walls.
            for (int k = 0; k < 7; k++)
                Place(kit, "Wall 1x1", Wood, o + new Vector3(0.5f + k, wallTop, 3 * t));
            Place(kit, "Wall 0.5x0.5", Wood, o + new Vector3(7.25f, wallTop, 3 * t));
            Place(kit, "Wall 1x1", Wood, o + new Vector3(3 * t, wallTop, 2 * t + 0.5f), 90);
            Place(kit, "Wall 1x1", Wood, o + new Vector3(3 * t, wallTop, 2 * t + 1.5f), 90);

            // Gable roof over the house, ridge along x.
            for (int i = 0; i < 2; i++)
            {
                Place(kit, "Roof 45° 2.5x2.5", Wood, o + new Vector3(h + t * i, wallTop, h));
                Place(kit, "Roof 45° 2.5x2.5", Wood, o + new Vector3(h + t * i, wallTop, t + h), 180);
            }
        }

        /// <summary>
        /// 3 m kit: an open pavilion on a raised stone platform with wide entrance stairs, wood and
        /// iron pillars and beams, a gable roof, a back wall and a side wall with a window.
        /// </summary>
        void BuildPavilion(Vector3 o)
        {
            const int kit = 1;
            const float t = 3f, h = t / 2f;
            const float deck = 1f, top = deck + 3f;   // platform top, pillar tops

            // Platform: two layers of 3 x 2 stone slabs, 1 m high.
            for (int layer = 0; layer < 2; layer++)
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
                Place(kit, "Slab 3x3", Stone, o + new Vector3(h + t * i, 0.5f * layer, h + t * j));

            // Wide stone stairs up to the middle of the front.
            Place(kit, "Stairs 1 m up, 3 m wide", Stone, o + new Vector3(t + h, 0, -1f));

            // Pillars on the front and back edges: wood, and iron along the right side.
            for (int i = 0; i <= 3; i++)
            foreach (float z in new[] { 0f, 2 * t })
                Place(kit, "Pillar 3 m", i == 3 ? Iron : Wood, o + new Vector3(t * i, deck, z));

            // Beams on top, flush with the pillar tops: along x on the front and back, along z on every row.
            for (int i = 0; i < 3; i++)
            foreach (float z in new[] { 0f, 2 * t })
                Place(kit, "Beam 3 m", i == 2 ? Iron : Wood, o + new Vector3(h + t * i, top - 0.2f, z));
            for (int i = 0; i <= 3; i++)
            for (int j = 0; j < 2; j++)
                Place(kit, "Beam 3 m", i == 3 ? Iron : Wood, o + new Vector3(t * i, top - 0.2f, h + t * j), 90);

            // Gable roof, ridge along x.
            for (int i = 0; i < 3; i++)
            {
                Place(kit, "Roof 45° 3x3", Wood, o + new Vector3(h + t * i, top, h));
                Place(kit, "Roof 45° 3x3", Wood, o + new Vector3(h + t * i, top, t + h), 180);
            }

            // Back wall, and a left wall with a window made of 1x1 pieces.
            for (int i = 0; i < 3; i++)
                Place(kit, "Wall 3x3", Wood, o + new Vector3(h + t * i, deck, 2 * t));
            Place(kit, "Wall 3x3", Wood, o + new Vector3(0, deck, h), 90);
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                if (row != 1 || col != 1)
                    Place(kit, "Wall 1x1", Wood, o + new Vector3(0, deck + row, t + 0.5f + col), 90);
        }

        void Place(int kit, string name, BuildMaterial material, Vector3 position, float yaw = 0f)
        {
            int type = Array.FindIndex(PieceCatalog.All, d => d.Kit == kit && d.name == name);
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
