using System;
using System.Collections.Generic;
using ShooterMashup.Building;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// What a vehicle's build simulation sees as "the ground". A vehicle has no ground: every
    /// piece just has to be connected to the rest. So the vehicle's material losses are zero
    /// (support never runs out), and one piece, the anchor, counts as grounded. Anything not
    /// connected to it falls off. Before there's an anchor, the first piece must rest on the lift.
    /// Pieces are in the vehicle's local space.
    /// </summary>
    public sealed class VehicleBuildWorld : IBuildWorld
    {
        readonly Func<Obb[], float, bool> restsOnLift;
        readonly Func<Obb, Obb> toWorld;
        readonly List<Bounds> actors;

        /// <summary>The anchor piece's boxes, or null before the first piece.</summary>
        public Obb[] Anchor { get; set; }

        /// <param name="restsOnLift">Whether local boxes rest on the lift holding the vehicle.</param>
        /// <param name="toWorld">Moves a local box into the world, for checking players' space.</param>
        /// <param name="actors">Players' space this frame (world).</param>
        public VehicleBuildWorld(Func<Obb[], float, bool> restsOnLift, Func<Obb, Obb> toWorld, List<Bounds> actors)
        {
            this.restsOnLift = restsOnLift;
            this.toWorld = toWorld;
            this.actors = actors;
        }

        /// <summary>A vehicle's materials: as strong as normal, but support never drops with distance.</summary>
        public static BuildConfig Config()
        {
            var cfg = new BuildConfig { vehicleParts = true };
            cfg.wood = new MaterialStats(cfg.wood.maxSupport, cfg.wood.minSupport, 0f, 0f);
            cfg.stone = new MaterialStats(cfg.stone.maxSupport, cfg.stone.minSupport, 0f, 0f);
            cfg.iron = new MaterialStats(cfg.iron.maxSupport, cfg.iron.minSupport, 0f, 0f);
            return cfg;
        }

        public bool IsGrounded(Obb[] boxes, float tolerance)
        {
            if (Anchor == null)
                return restsOnLift(boxes, tolerance);
            foreach (Obb b in boxes)
            foreach (Obb a in Anchor)
                if (Obb.Overlap(a, b, tolerance))
                    return true;
            return false;
        }

        public bool IsBlocked(Obb[] boxes)
        {
            if (actors == null)
                return false;
            foreach (Bounds actor in actors)
            {
                var hull = new Obb(actor.center, actor.size);
                foreach (Obb box in boxes)
                    if (Obb.Overlap(toWorld(box), hull, 0f))
                        return true;
            }
            return false;
        }
    }
}
