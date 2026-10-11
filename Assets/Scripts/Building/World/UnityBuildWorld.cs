using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// IBuildWorld on Unity physics. Anything solid that isn't a build piece counts as ground:
    /// terrain, diggable blocks, the static map. Players block placement through the actor
    /// bounds the runtime hands in each frame (players have no colliders of their own).
    /// </summary>
    public sealed class UnityBuildWorld : IBuildWorld
    {
        readonly int solidMask;
        readonly Collider[] overlaps = new Collider[64];
        readonly List<Bounds> actors = new List<Bounds>();

        public UnityBuildWorld(int solidMask)
        {
            this.solidMask = solidMask;
        }

        /// <summary>Space taken by players and other characters this frame.</summary>
        public List<Bounds> Actors => actors;

        public bool IsGrounded(Obb[] boxes, float tolerance)
        {
            foreach (Obb box in boxes)
            {
                Vector3 half = box.halfExtents + Vector3.one * tolerance;
                int n = Physics.OverlapBoxNonAlloc(box.center, half, overlaps, box.rotation, solidMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (overlaps[i].GetComponentInParent<BuildPieceTag>() == null)
                        return true;
            }
            return false;
        }

        public bool IsBlocked(Obb[] boxes)
        {
            foreach (Bounds actor in actors)
            {
                var hull = new Obb(actor.center, actor.size);
                foreach (Obb box in boxes)
                    if (Obb.Overlap(box, hull, 0f))
                        return true;
            }
            return false;
        }
    }
}
