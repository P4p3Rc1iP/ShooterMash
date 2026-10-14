using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// The engine's revs, for the rev counter and the engine sound. It's for show: the drive force
    /// comes from power and pull (see Vehicle), not from these. An automatic gearbox shifts on the
    /// vehicle's speed over the ground, and the revs follow how fast the driven wheels turn, so
    /// wheelspin (or the wheels leaving the ground) flares them towards the redline while the gear
    /// stays put. Pulling away, the clutch slips, so the revs rise before the wheels turn.
    /// </summary>
    public sealed class EngineRevs
    {
        public float Idle { get; private set; } = 800f;
        public float Redline { get; private set; } = 6500f;
        public int Gears { get; private set; } = 5;
        public int Cylinders { get; private set; } = 4;
        /// <summary>Revs per minute now.</summary>
        public float Rpm { get; private set; } = 800f;
        /// <summary>1..Gears forwards, -1 in reverse.</summary>
        public int Gear { get; private set; } = 1;
        /// <summary>On the rev limiter (the revs would go past the redline).</summary>
        public bool Limiter { get; private set; }

        float topSpeed = 26f;
        float shiftTimer;

        /// <summary>Its character: from the vehicle's most powerful engine.</summary>
        public void Configure(float idle, float redline, int cylinders, int gears, float topSpeed)
        {
            Idle = idle;
            Redline = redline;
            Cylinders = Mathf.Max(1, cylinders);
            Gears = Mathf.Max(1, gears);
            this.topSpeed = Mathf.Max(topSpeed, 1f);
            Gear = Mathf.Clamp(Gear, -1, Gears);
            if (Gear == 0) Gear = 1;
        }

        /// <summary>How fast the vehicle goes at the redline in a gear (m/s).</summary>
        public float GearSpeed(int gear)
        {
            int g = Mathf.Clamp(Mathf.Abs(gear), 1, Gears);
            return topSpeed * Mathf.Pow(g / (float)Gears, 0.85f);
        }

        /// <param name="groundSpeed">The vehicle's speed along its forward (m/s, negative backwards).</param>
        /// <param name="wheelSpeed">How fast the driven wheels' treads turn (m/s), including wheelspin.</param>
        /// <param name="throttle">-1..1 as given (negative: braking, or reversing when slow).</param>
        /// <param name="driving">The engine is pushing (not braking or parked).</param>
        /// <param name="free">No driven wheel is on the ground: the engine revs freely.</param>
        public void Update(float dt, float groundSpeed, float wheelSpeed, float throttle, bool driving, bool free, bool running)
        {
            if (!running)
            {
                Rpm = Mathf.MoveTowards(Rpm, 0f, Redline * dt);
                Limiter = false;
                return;
            }

            // The automatic gearbox: shift up near a gear's top, down well below the one under it.
            bool reverse = driving && throttle < 0f && groundSpeed < 0.5f || groundSpeed < -0.5f;
            shiftTimer -= dt;
            if (reverse)
                Gear = -1;
            else
            {
                if (Gear < 1) Gear = 1;
                if (shiftTimer <= 0f)
                {
                    if (Gear < Gears && groundSpeed > GearSpeed(Gear) * 0.92f)
                    {
                        Gear++;
                        shiftTimer = 0.35f;
                    }
                    else if (Gear > 1 && groundSpeed < GearSpeed(Gear - 1) * 0.7f)
                    {
                        Gear--;
                        shiftTimer = 0.35f;
                    }
                }
            }

            float target = Mathf.Max(Idle, Redline * Mathf.Abs(wheelSpeed) / GearSpeed(Gear));
            if (driving && Mathf.Abs(throttle) > 0f)
            {
                if (free)
                    target = Redline * 1.05f;                      // nothing to push against
                else if (Mathf.Abs(Gear) == 1)
                    target = Mathf.Max(target, Mathf.Lerp(Idle, Redline * 0.55f, Mathf.Abs(throttle))); // clutch slipping
            }
            Limiter = target > Redline;
            target = Mathf.Min(target, Redline);
            if (Limiter)
                target -= Random.value * Redline * 0.06f;           // bouncing off the limiter
            // The engine's inertia: revs climb and fall at a rate, faster for small engines.
            float rate = Redline * (target > Rpm ? (driving ? 2.2f : 1.2f) : 1.6f);
            Rpm = Mathf.MoveTowards(Rpm, Mathf.Max(target, Idle), rate * dt);
        }
    }
}
