using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// A volume of water, slime or lava the player can swim in. Needs a trigger collider
    /// (box, sphere, capsule or convex mesh).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Collider))]
    public class LiquidVolume : MonoBehaviour
    {
        static readonly List<LiquidVolume> Active = new List<LiquidVolume>();

        public Contents contents = Contents.Water;

        Collider volume;

        Collider Volume => volume != null ? volume : (volume = GetComponent<Collider>());

        void Awake() => Volume.isTrigger = true;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public bool Contains(Vector3 worldPoint)
        {
            if (!Volume.bounds.Contains(worldPoint))
                return false;
            return (Volume.ClosestPoint(worldPoint) - worldPoint).sqrMagnitude < 1e-8f;
        }

        /// <summary>Combined liquid contents at a world-space point (metres).</summary>
        public static Contents ContentsAt(Vector3 worldPoint)
        {
            Contents c = Contents.None;
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active[i].Contains(worldPoint))
                    c |= Active[i].contents & Contents.AnyLiquid;
            }
            return c;
        }
    }
}
