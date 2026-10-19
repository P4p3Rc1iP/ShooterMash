using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// A car chase: one quarry and some pursuers (vehicles with a ChaseDriver). It runs while a
    /// player rides any of them (standing on it) or drives one: the quarry flees across the area,
    /// the pursuers chase it, and whatever the player drives is theirs (drive the quarry and the
    /// pursuers chase you; drive a pursuer and join in). Otherwise they wait, parked.
    /// </summary>
    public class ChaseGroup : MonoBehaviour
    {
        [Tooltip("Where the quarry may go (world x/z): it picks points inside this...")]
        public Rect area = new Rect(0f, 0f, 1000f, 1000f);
        [Tooltip("...but not inside this (e.g. hills too steep for a chase). Zero size: nothing left out.")]
        public Rect exclude;
        [Tooltip("Top speeds (m/s) the quarry and the pursuers aim for (their own top speed caps it too).")]
        public float quarrySpeed = 30f, pursuerSpeed = 42f;

        readonly List<ChaseDriver> members = new List<ChaseDriver>();
        float activeChecked = -1f;
        bool active;

        public IReadOnlyList<ChaseDriver> Members => members;
        /// <summary>The vehicle being chased, or null.</summary>
        public Vehicle Quarry { get; private set; }

        public void Join(ChaseDriver driver)
        {
            members.Add(driver);
            if (driver.quarry)
                Quarry = driver.Vehicle;
        }

        public void Leave(ChaseDriver driver)
        {
            members.Remove(driver);
            if (Quarry == driver.Vehicle)
                Quarry = null;
        }

        /// <summary>Is a player riding or driving any of them? (Checked once per physics step.)</summary>
        public bool Active
        {
            get
            {
                if (activeChecked != Time.fixedTime)
                {
                    activeChecked = Time.fixedTime;
                    active = false;
                    foreach (ChaseDriver m in members)
                        if (m.Vehicle != null && (m.Vehicle.Driver != null || Riders.On(m.Vehicle.transform)))
                            active = true;
                }
                return active;
            }
        }

        /// <summary>Can the quarry go here?</summary>
        public bool Allowed(Vector3 point)
        {
            var p = new Vector2(point.x, point.z);
            return area.Contains(p) && !(exclude.width > 0f && exclude.Contains(p));
        }

        /// <summary>Where the pursuers are on average (the quarry flees from it), or the quarry's own position.</summary>
        public Vector3 PursuersCentre()
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (ChaseDriver m in members)
                if (!m.quarry && m.Vehicle != null)
                {
                    sum += m.Vehicle.transform.position;
                    n++;
                }
            return n > 0 ? sum / n : Quarry != null ? Quarry.transform.position : transform.position;
        }
    }
}
