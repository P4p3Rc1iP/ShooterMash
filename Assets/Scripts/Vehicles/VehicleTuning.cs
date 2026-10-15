using System;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// How vehicles drive, for every vehicle; what differs per vehicle comes from its parts:
    /// engines (power, pull, top speed), wheels (size, travel, driven or free, tyre grip) and the
    /// mass of its pieces. The assists are what make anything a player builds drive decently,
    /// Trailmakers style, without them having to think about suspension geometry. The driver's
    /// own helpers (traction and stability control, ABS, steering lock, rollover assist) are in
    /// DrivingAssists, a player setting.
    /// </summary>
    [Serializable]
    public class VehicleTuning
    {
        [Header("Suspension (springs are sized to the vehicle's weight; travel comes with each wheel)")]
        [Tooltip("How far into its travel a wheel sits when the vehicle stands still (0..1).")]
        [Range(0.1f, 0.9f)] public float rideHeight = 0.45f;
        [Tooltip("Suspension damping as a fraction of critical damping.")]
        [Range(0f, 2f)] public float damping = 0.5f;

        [Header("Driving (power comes from the engines)")]
        [Tooltip("Without an engine: acceleration (m/s²) up to 12 m/s, whatever it weighs, and a top speed of 26 m/s.")]
        public float acceleration = 7f;
        [Tooltip("Top speed in reverse (m/s).")]
        public float reverseSpeed = 8f;
        [Tooltip("Braking deceleration (m/s²), as far as the tyres allow.")]
        public float braking = 14f;
        [Tooltip("Driven wheels off the throttle slow down by this fraction of the speed per second (engine braking).")]
        public float engineBraking = 0.06f;
        [Tooltip("Rolling resistance: a fraction of each tyre's load.")]
        public float rollingResistance = 0.012f;
        [Tooltip("Below this speed (m/s) with no throttle, the vehicle holds itself still.")]
        public float holdSpeed = 1.5f;

        [Header("Tyres and steering")]
        [Tooltip("Tyre friction coefficient: grip force per unit of load (times each tyre's own grip). With the extra gravity 0.75 is about 1 g of cornering.")]
        public float grip = 0.75f;
        [Tooltip("A tyre sliding fast (slideFullSlip or more) grips this fraction as well: what lets cars spin and drift.")]
        [Range(0.3f, 1f)] public float slideGrip = 0.75f;
        [Tooltip("How fast (m/s) a sliding wheel spins over the ground per times its grip it is driven with. More: a wheel sliding under power loses more sideways grip (power oversteer).")]
        public float wheelspinSlip = 2f;
        [Tooltip("Below this speed over the ground (m/s) a tyre never slides, so parking and crawling stay sure-footed.")]
        public float slideMinSpeed = 2f;
        [Tooltip("A tyre asked for more than its grip gives all its grip until it actually slides this fast (m/s: sideways, spinning or skidding); only then does it start to let go. So cornering on the limit is crisp, and slides are slides.")]
        public float slideStartSlip = 1.5f;
        [Tooltip("Sliding faster than slideStartSlip, a tyre loses grip gradually, down to slideGrip at this speed over the ground (m/s), and gets it back the same way as the slide dies down (no snap when it catches).")]
        public float slideFullSlip = 5f;
        [Tooltip("Fraction of a wheel's sideways sliding cancelled per physics step (0..1).")]
        [Range(0f, 1f)] public float lateralStiffness = 0.5f;
        [Tooltip("Steering angle of the frontmost wheels at low speed (degrees).")]
        public float maxSteer = 38f;
        [Tooltip("How fast the steering follows the input (per second). The driver's keyboard input is shaped on top of this (VehicleDriver).")]
        public float steerRate = 8f;
        [Tooltip("Grip on slick surfaces (ice, marked SurfaceFlags.Slick), as a fraction of normal.")]
        [Range(0f, 1f)] public float slickGrip = 0.15f;

        [Header("Assists")]
        [Tooltip("The centre of mass is held no higher than this above the wheel centres (m), so very tall builds don't tip over. Less: less body roll.")]
        public float maxCenterOfMassAboveWheels = 1.5f;
        [Tooltip("Grip forces act this fraction of the way from the tyre contact up to the centre of mass: less body roll in corners.")]
        [Range(0f, 1f)] public float gripForceHeight = 0f;
        [Tooltip("How hard the body levels itself to the ground while wheels touch it (1/s²).")]
        public float uprightStrength = 0.5f;
        [Tooltip("Damps rolling and pitching while wheels touch the ground (1/s).")]
        public float uprightDamping = 1.2f;
        [Tooltip("Roll stiffness: how hard the springs resist leaning (a difference in compression across an axle), as a fraction of how hard they resist bouncing. Our vehicles are about twice as wide as real ones, so their springs alone hardly let them lean; below 1 they roll more. Above 1 it's an anti-roll bar.")]
        public float rollStiffness = 0.3f;
        [Tooltip("Damping of leaning, as a fraction of the damping that matches the roll stiffness. Below 1 the body springs back upright after a corner instead of easing back (a heavy load high up cancels much of the roll springs, which leaves them over-damped).")]
        [Range(0f, 2f)] public float rollDamping = 0.6f;
        [Tooltip("How hard the body turns upright in the air (1/s²), so jumps land on the wheels.")]
        public float airUpright = 1.5f;
        [Tooltip("Pushes the vehicle down at speed (m/s² per (m/s)²). It adds grip, so keep it small.")]
        public float downforce = 0.004f;
        [Tooltip("Downforce never pushes harder than this (m/s²), or fast vehicles squash their springs flat.")]
        public float maxDownforce = 4f;
        [Tooltip("Extra gravity on vehicles (m/s²), so they feel less floaty.")]
        public float extraGravity = 4f;
        [Tooltip("Air drag coefficient (times frontal area and speed squared). Boxy builds are about 0.6.")]
        public float dragCoefficient = 0.6f;
        [Tooltip("The body has little friction (0.3), so scraping the ground or a rock skids instead of braking hard (the tyres grip).")]
        public bool slipperyBody = true;

        [Header("Trailers")]
        [Tooltip("A trailer's kingpin couples to a fifth wheel within this distance (m).")]
        public float couplingReach = 0.4f;

        [Header("Mass")]
        [Tooltip("Densities (kg/m³) of wood, stone, iron and scrap; a piece's mass is its volume times this " +
                 "(engines and wheels have their own). Scrap is sheet metal on a frame, not solid: light.")]
        public float woodDensity = 500f;
        public float stoneDensity = 2400f;
        public float ironDensity = 7800f;
        public float scrapDensity = 200f;
    }
}
