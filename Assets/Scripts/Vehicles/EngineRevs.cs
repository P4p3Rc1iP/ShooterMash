using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// The engine's revs and its gearbox. The drive force comes through the gears: in each gear
    /// the revs follow the vehicle's speed, and the engine's power depends on the revs (the most
    /// near the redline, little when it's lugging low in a tall gear), so there's a dip after each
    /// shift and no drive while a gear changes. The ratios are the engine's own or a gearbox
    /// part's; shifting is automatic (up at the redline, down when the revs drop) or the driver's
    /// (`Manual`: they shift, and at the redline the limiter cuts the power).
    ///
    /// For the rev counter and the sound the revs follow how fast the driven wheels turn, so
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
        /// <summary>The driver changes gear (ShiftUp/ShiftDown); otherwise it's automatic.</summary>
        public bool Manual { get; set; }
        /// <summary>A gear is changing: no drive for a moment.</summary>
        public bool Shifting => shiftLeft > 0f;
        /// <summary>How fast it goes at the redline in top gear (m/s).</summary>
        public float TopSpeed => topSpeed;

        float topSpeed = 26f;
        float shiftTime = 0.3f;
        float shiftLeft;      // s left of the gear change in progress
        float shiftTimer;     // s before the automatic may shift again
        float pullScale = 1f; // a shorter first gear than the engine's own pulls harder

        /// <summary>
        /// Its character, from the vehicle's most powerful engine, and its ratios: the engine's
        /// own (`gears` up to `engineTopSpeed`) or a gearbox part's (its gears, `length` times as long).
        /// </summary>
        public void Configure(float idle, float redline, int cylinders, int engineGears, float engineTopSpeed,
                              int gears, float length, float shiftSeconds)
        {
            Idle = idle;
            Redline = redline;
            Cylinders = Mathf.Max(1, cylinders);
            Gears = Mathf.Max(1, gears);
            topSpeed = Mathf.Max(engineTopSpeed * length, 1f);
            shiftTime = shiftSeconds;
            float ownFirst = Mathf.Max(engineTopSpeed, 1f) * Mathf.Pow(1f / Mathf.Max(1, engineGears), 0.85f);
            pullScale = Mathf.Clamp(ownFirst / GearSpeed(1), 0.5f, 2f);
            Gear = Mathf.Clamp(Gear, -1, Gears);
            if (Gear == 0) Gear = 1;
        }

        /// <summary>How fast the vehicle goes at the redline in a gear (m/s).</summary>
        public float GearSpeed(int gear)
        {
            int g = Mathf.Clamp(Mathf.Abs(gear), 1, Gears);
            return topSpeed * Mathf.Pow(g / (float)Gears, 0.85f);
        }

        /// <summary>
        /// The engine's power at a fraction of the redline revs, as a fraction of its full power:
        /// the most at 90%, a little less at the redline, much less low down.
        /// </summary>
        public static float PowerCurve(float revs)
        {
            float rise = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(revs / 0.9f) * Mathf.PI / 2f), 0.8f);
            return revs > 0.9f ? rise * (1f - (revs - 0.9f) * 0.5f) : rise;
        }

        /// <summary>
        /// The most force (N) the engines put through the wheels at this speed (m/s) in the
        /// current gear: their power at the revs that speed gives, no more than `pull` (scaled by
        /// how short the first gear is), none while shifting or past the redline.
        /// </summary>
        public float DriveForce(float speed, float power, float pull)
        {
            float v = Mathf.Abs(speed);
            if (Gear < 0)
                return Mathf.Min(pull, power / Mathf.Max(v, 0.1f));
            if (Shifting)
                return 0f;
            float gearSpeed = GearSpeed(Gear);
            float revs = v / gearSpeed;
            // Pulling away in first the clutch slips, so the engine runs at a third of its revs;
            // in a taller gear it can't, and lugging low down it has little to give.
            float running = Mathf.Max(revs, Gear == 1 ? 0.35f : Idle / Redline);
            float force = power * PowerCurve(running) / (running * gearSpeed);
            if (Gear > 1 && revs < 0.3f)
                force *= Mathf.Max(revs, 0.05f) / 0.3f;
            force *= Mathf.Clamp01((1f - revs) / 0.03f);                  // the limiter
            return Mathf.Min(force, pull * pullScale);
        }

        public void ShiftUp()
        {
            if (Gear < 0) Gear = 1;
            else if (Gear < Gears) Shift(Gear + 1);
        }

        public void ShiftDown()
        {
            if (Gear > 1) Shift(Gear - 1);
        }

        void Shift(int gear)
        {
            Gear = gear;
            shiftLeft = shiftTime;
            shiftTimer = shiftTime + 0.25f;
        }

        /// <param name="groundSpeed">The vehicle's speed along its forward (m/s, negative backwards).</param>
        /// <param name="wheelSpeed">How fast the driven wheels' treads turn (m/s), including wheelspin.</param>
        /// <param name="throttle">-1..1 as given (negative: braking, or reversing when slow).</param>
        /// <param name="driving">The engine is pushing (not braking or parked).</param>
        /// <param name="free">No driven wheel is on the ground: the engine revs freely.</param>
        public void Update(float dt, float groundSpeed, float wheelSpeed, float throttle, bool driving, bool free, bool running)
        {
            shiftLeft = Mathf.Max(0f, shiftLeft - dt);
            shiftTimer -= dt;
            if (!running)
            {
                Rpm = Mathf.MoveTowards(Rpm, 0f, Redline * dt);
                Limiter = false;
                return;
            }

            // Reverse is always automatic: back up from a standstill.
            bool reverse = driving && throttle < 0f && groundSpeed < 0.5f || groundSpeed < -0.5f;
            if (reverse)
                Gear = -1;
            else
            {
                if (Gear < 1) Gear = 1;
                if (!Manual && shiftTimer <= 0f)
                {
                    // Up at the redline, down once the revs drop low (sooner off the throttle,
                    // so there's pull when it comes back), straight to first when stopped.
                    float revs = groundSpeed / GearSpeed(Gear);
                    if (Gear < Gears && revs > 0.97f)
                        Shift(Gear + 1);
                    else if (Gear > 1 && Mathf.Abs(groundSpeed) < 1f)
                        Gear = 1;
                    else if (Gear > 1 && revs < (throttle > 0f ? 0.5f : 0.4f) && groundSpeed / GearSpeed(Gear - 1) < 0.9f)
                        Shift(Gear - 1);
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
