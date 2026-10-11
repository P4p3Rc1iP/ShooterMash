using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>Uniform grid of piece ids, for finding neighbours without checking every piece.</summary>
    sealed class SpatialHash
    {
        readonly float cellSize;
        readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
        readonly HashSet<int> seen = new HashSet<int>();

        public SpatialHash(float cellSize)
        {
            this.cellSize = cellSize;
        }

        public void Insert(int id, Bounds bounds)
        {
            Range(bounds, out Vector3Int lo, out Vector3Int hi);
            for (int x = lo.x; x <= hi.x; x++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            {
                var key = new Vector3Int(x, y, z);
                if (!cells.TryGetValue(key, out List<int> list))
                    cells[key] = list = new List<int>();
                list.Add(id);
            }
        }

        public void Remove(int id, Bounds bounds)
        {
            Range(bounds, out Vector3Int lo, out Vector3Int hi);
            for (int x = lo.x; x <= hi.x; x++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            {
                var key = new Vector3Int(x, y, z);
                if (cells.TryGetValue(key, out List<int> list))
                {
                    list.Remove(id);
                    if (list.Count == 0)
                        cells.Remove(key);
                }
            }
        }

        /// <summary>Ids of pieces whose cells overlap the bounds, sorted so results are deterministic.</summary>
        public void Query(Bounds bounds, List<int> results)
        {
            results.Clear();
            seen.Clear();
            Range(bounds, out Vector3Int lo, out Vector3Int hi);
            for (int x = lo.x; x <= hi.x; x++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int z = lo.z; z <= hi.z; z++)
            {
                if (!cells.TryGetValue(new Vector3Int(x, y, z), out List<int> list))
                    continue;
                foreach (int id in list)
                    if (seen.Add(id))
                        results.Add(id);
            }
            results.Sort();
        }

        void Range(Bounds b, out Vector3Int lo, out Vector3Int hi)
        {
            lo = Vector3Int.FloorToInt(b.min / cellSize);
            hi = Vector3Int.FloorToInt(b.max / cellSize);
        }
    }
}
