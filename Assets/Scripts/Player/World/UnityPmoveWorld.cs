using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// IPmoveWorld on top of Unity physics. Sweeps an axis-aligned box, like the Quake engine
    /// does with its player hull.
    ///
    /// PhysX box sweeps are not precise enough for Quake-style movement: against large colliders
    /// they report contact up to ~1.5 cm early, and a sweep that starts within that tolerance
    /// reports "overlapping, distance 0" with no normal. So the sweep is only used to find what
    /// we'll hit (and its normal). It starts a few cm behind the box so resting contacts still
    /// register, and the exact contact distance is then found by bisection with OverlapBox,
    /// which is exact. Like Quake's SURFACE_CLIP_EPSILON, the hull stops Skin short of surfaces
    /// (it floats ~3 mm off the ground, which is invisible).
    /// </summary>
    public sealed class UnityPmoveWorld : IPmoveWorld
    {
        // Distances in metres. (Quake's epsilon is 0.125 in = 3 mm; PhysX needs a bit more.)
        const float Skin = 0.00635f;
        const float Shrink = 0.003175f;
        const float Backup = 0.0508f;
        const int RefineIterations = 10;

        readonly int solidMask;
        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[32];
        readonly Dictionary<Collider, PmSurface> surfaceCache = new Dictionary<Collider, PmSurface>();
        static BoxCollider probe; // shared, hidden trigger used for penetration queries

        public UnityPmoveWorld(int solidMask)
        {
            this.solidMask = solidMask;
        }

        public TraceResult Trace(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs)
        {
            Vector3 centerOffset = (mins + maxs) * 0.5f;
            Vector3 halfExtents = (maxs - mins) * 0.5f - Vector3.one * Shrink;
            Vector3 c0 = start + centerOffset;

            int n = Physics.OverlapBoxNonAlloc(c0, halfExtents, overlaps, Quaternion.identity, solidMask, QueryTriggerInteraction.Ignore);
            if (n > 0)
            {
                var stuck = new TraceResult
                {
                    allSolid = true,
                    startSolid = true,
                    fraction = 0f,
                    endPos = start,
                    normal = Vector3.up,
                };
                FillSurface(ref stuck, overlaps[0]);
                return stuck;
            }

            Vector3 delta = end - start;
            float dist = delta.magnitude;
            if (dist < 1e-6f)
                return Clear(end);

            Vector3 dir = delta / dist;
            float limit = dist + Skin;
            Vector3 castStart = c0 - dir * Backup;
            int count = Physics.BoxCastNonAlloc(castStart, halfExtents, dir, hits, Quaternion.identity,
                Backup + limit, solidMask, QueryTriggerInteraction.Ignore);

            // Nearest candidate. Ignore overlaps at the back-up start (behind us) and surfaces
            // facing away from or parallel to the move, which can't block it.
            int best = -1;
            float approx = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance <= 0f || Vector3.Dot(hits[i].normal, dir) > -1e-4f)
                    continue;
                float d = hits[i].distance - Backup;
                if (d < approx)
                {
                    approx = d;
                    best = i;
                }
            }

            // Bisect for the exact contact distance: lo is always free, hi always blocked.
            float hi = limit;
            if (IsFree(c0, dir, hi, halfExtents, out Collider blocker))
            {
                // The end is free. Either there's nothing in the way, or the real contact is beyond
                // this move, or the box passes right through something thin (a ladder, a plank) on
                // the way. If the sweep saw something, look just past where it says it is.
                if (best < 0 || !FindThinBlocker(c0, dir, approx, limit, halfExtents, out hi, out blocker))
                    return Clear(end);
            }

            float lo = best >= 0 ? Mathf.Clamp(approx, 0f, limit) : 0f;
            if (lo > 0f && !IsFree(c0, dir, lo, halfExtents, out _))
                lo = 0f; // the sweep reported late; the start itself is known to be free

            for (int i = 0; i < RefineIterations; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (IsFree(c0, dir, mid, halfExtents, out Collider b))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                    blocker = b;
                }
            }

            // The sweep's normal for whatever we actually ran into, as a fallback.
            int hit = best;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == blocker && hits[i].distance > 0f && Vector3.Dot(hits[i].normal, dir) <= -1e-4f)
                {
                    hit = i;
                    break;
                }
            }
            Vector3 normal = hit >= 0 ? hits[hit].normal : -dir;

            // Sweep normals can be tilted by several degrees (landing flat on a big box gives a
            // visible sideways push), so get the real one from the barely-touching position.
            if (blocker != null && ContactNormal(c0 + dir * hi, halfExtents, blocker, out Vector3 exact)
                && Vector3.Dot(exact, dir) < 0f)
                normal = exact;

            float fraction = Mathf.Clamp01((lo - Skin) / dist);
            var result = new TraceResult
            {
                fraction = fraction,
                endPos = start + delta * fraction,
                normal = CleanNormal(normal),
            };
            FillSurface(ref result, blocker != null ? blocker : hits[hit].collider);
            return result;
        }

        /// <summary>
        /// PhysX normals carry ~1e-4 of noise. Quake's movement relies on flat ground having an
        /// exactly vertical normal (otherwise landing turns fall speed into a sideways hop), so
        /// snap near-zero components to zero. 0.001 is under a tenth of a degree.
        /// </summary>
        static Vector3 CleanNormal(Vector3 n)
        {
            const float epsilon = 0.001f;
            if (Mathf.Abs(n.x) < epsilon) n.x = 0f;
            if (Mathf.Abs(n.y) < epsilon) n.y = 0f;
            if (Mathf.Abs(n.z) < epsilon) n.z = 0f;
            return n.normalized;
        }

        /// <summary>Direction that pushes a box at <paramref name="center"/> out of a collider it overlaps.</summary>
        static bool ContactNormal(Vector3 center, Vector3 halfExtents, Collider other, out Vector3 normal)
        {
            BoxCollider box = Probe();
            box.size = halfExtents * 2f;
            PhysicsPose(other, out Vector3 position, out Quaternion rotation);
            return Physics.ComputePenetration(box, center, Quaternion.identity,
                other, position, rotation, out normal, out _);
        }

        /// <summary>
        /// Where a collider really is for physics. ComputePenetration takes the pose to test at, and
        /// a moving rigidbody's transform is its interpolated (drawn) pose, up to a physics step away
        /// from its colliders: on a truck at 6 m/s that's 12 cm, enough to turn the floor's normal
        /// into a 33° slope that flings you sideways on landing. So use the body's physics pose.
        /// </summary>
        static void PhysicsPose(Collider c, out Vector3 position, out Quaternion rotation)
        {
            Transform t = c.transform;
            Rigidbody body = c.attachedRigidbody;
            if (body == null)
            {
                position = t.position;
                rotation = t.rotation;
                return;
            }
            Transform b = body.transform;
            Quaternion toBody = Quaternion.Inverse(b.rotation);
            position = body.position + body.rotation * (toBody * (t.position - b.position));
            rotation = body.rotation * (toBody * t.rotation);
        }

        static TraceResult Clear(Vector3 end) => new TraceResult { fraction = 1f, endPos = end, normal = Vector3.up };

        bool IsFree(Vector3 center, Vector3 dir, float distance, Vector3 halfExtents, out Collider blocker)
        {
            int n = Physics.OverlapBoxNonAlloc(center + dir * distance, halfExtents, overlaps,
                Quaternion.identity, solidMask, QueryTriggerInteraction.Ignore);
            blocker = n > 0 ? overlaps[0] : null;
            return n == 0;
        }

        /// <summary>
        /// The sweep hit something at about <paramref name="approx"/> but the end of the move is
        /// free: probe a little past the hit (the sweep reports up to ~1.5 cm early) for a thin
        /// obstacle the box would pass through. Finds the blocked distance to bisect towards.
        /// </summary>
        bool FindThinBlocker(Vector3 center, Vector3 dir, float approx, float limit, Vector3 halfExtents,
            out float blocked, out Collider blocker)
        {
            float from = Mathf.Max(0f, approx);
            for (float probe = 0.01f; probe <= 0.08f; probe *= 2f)
            {
                blocked = Mathf.Min(limit, from + probe);
                if (!IsFree(center, dir, blocked, halfExtents, out blocker))
                    return true;
            }
            blocked = limit;
            blocker = null;
            return false;
        }

        public Contents PointContents(Vector3 point)
        {
            Contents contents = Contents.None;
            if (Physics.CheckSphere(point, 0.001f, solidMask, QueryTriggerInteraction.Ignore))
                contents |= Contents.Solid;
            contents |= LiquidVolume.ContentsAt(point);
            return contents;
        }

        static BoxCollider Probe()
        {
            if (probe == null)
            {
                var go = new GameObject("PmoveProbe") { hideFlags = HideFlags.HideAndDontSave };
                go.layer = 2; // Ignore Raycast, so it never shows up in our own queries
                probe = go.AddComponent<BoxCollider>();
                probe.isTrigger = true;
            }
            return probe;
        }

        public bool TryDepenetrate(ref Vector3 origin, Vector3 mins, Vector3 maxs)
        {
            BoxCollider box = Probe();
            Vector3 centerOffset = (mins + maxs) * 0.5f;
            box.size = maxs - mins;

            for (int iteration = 0; iteration < 4; iteration++)
            {
                Vector3 c = origin + centerOffset;
                int n = Physics.OverlapBoxNonAlloc(c, box.size * 0.5f, overlaps, Quaternion.identity, solidMask, QueryTriggerInteraction.Ignore);
                if (n == 0)
                    return true;

                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    Collider other = overlaps[i];
                    PhysicsPose(other, out Vector3 otherPosition, out Quaternion otherRotation);
                    if (Physics.ComputePenetration(box, c, Quaternion.identity,
                            other, otherPosition, otherRotation,
                            out Vector3 direction, out float distance))
                    {
                        Vector3 push = direction * (distance + Skin);
                        origin += push;
                        c += push;
                        moved = true;
                    }
                }

                if (!moved)
                    return false;
            }

            return Physics.OverlapBoxNonAlloc(origin + centerOffset, box.size * 0.5f - Vector3.one * Shrink,
                overlaps, Quaternion.identity, solidMask, QueryTriggerInteraction.Ignore) == 0;
        }

        void FillSurface(ref TraceResult result, Collider collider)
        {
            result.hitObject = collider;
            if (collider == null)
                return;

            if (!surfaceCache.TryGetValue(collider, out PmSurface surface))
            {
                surface = collider.GetComponentInParent<PmSurface>();
                surfaceCache[collider] = surface;
            }

            if (surface != null)
            {
                result.surfaceFlags = surface.flags;
                result.surfaceType = surface.surfaceType;
            }
        }
    }
}
