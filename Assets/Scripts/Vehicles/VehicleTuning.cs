using System;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// How vehicles drive. One set for every vehicle for now; engines, tyres and the like will
    /// change these per vehicle later. The assists are what make anything a player builds drive
    /// decently, Trailmakers style, without them having to think about suspension geometry.
    /// </summary>
    [Serializable]
    public class VehicleTuning
    {
        [Header("Suspension (springs are sized to the vehicle's weight; travel comes with each wheel)")]
        [Tooltip("How far into its travel a wheel sits when the vehicle stands still (0..1).")]
        [Range(0.1f, 0.9f)] public float rideHeight = 0.45f;
        [Tooltip("Suspension damping as a fraction of critical damping.")]
        [Range(0f, 2f)] public float damping = 0.5f;

        [Header("Driving")]
        [Tooltip("Acceleration (m/s²) with standard wheels, whatever the vehicle weighs, until engines exist. Each wheel's power scales it; top speed comes from the wheels.")]
        public float acceleration = 7f;
        [Tooltip("Top speed in reverse (m/s).")]
        public float reverseSpeed = 8f;
        [Tooltip("Braking deceleration (m/s²).")]
        public float braking = 14f;
        [Tooltip("Coasting slows down by this fraction of the speed per second.")]
        public float rollingResistance = 0.3f;
        [Tooltip("Below this speed (m/s) with no throttle, the vehicle holds itself still.")]
        public float holdSpeed = 1.5f;

        [Header("Grip and steering")]
        [Tooltip("Tyre friction coefficient: grip force per unit of load.")]
        public float grip = 1.2f;
        [Tooltip("Fraction of a wheel's sideways sliding cancelled per physics step (0..1).")]
        [Range(0f, 1f)] public float lateralStiffness = 0.5f;
        [Tooltip("Steering angle of the frontmost wheels at low speed (degrees).")]
        public float maxSteer = 32f;
        [Tooltip("Steering angle at top speed (degrees).")]
        public float steerAtTopSpeed = 9f;
        [Tooltip("How fast the steering follows the keys (per second).")]
        public float steerRate = 5f;
        [Tooltip("Rear wheel grip with the handbrake on, as a fraction of normal.")]
        [Range(0f, 1f)] public float handbrakeGrip = 0.35f;

        [Header("Assists")]
        [Tooltip("The centre of mass is held no higher than this above the wheel centres (m), so tall builds don't tip over.")]
        public float maxCenterOfMassAboveWheels = 0.2f;
        [Tooltip("Grip forces act this fraction of the way from the tyre contact up to the centre of mass: less body roll in corners.")]
        [Range(0f, 1f)] public float gripForceHeight = 0.6f;
        [Tooltip("How hard the body levels itself to the ground while wheels touch it (1/s²).")]
        public float uprightStrength = 5f;
        [Tooltip("Damps rolling and pitching while wheels touch the ground (1/s).")]
        public float uprightDamping = 2.5f;
        [Tooltip("How hard the body turns upright in the air (1/s²), so jumps land on the wheels.")]
        public float airUpright = 1.5f;
        [Tooltip("Pushes the vehicle down at speed (m/s² per (m/s)²).")]
        public float downforce = 0.01f;
        [Tooltip("Downforce never pushes harder than this (m/s²), or fast vehicles squash their springs flat.")]
        public float maxDownforce = 6f;
        [Tooltip("Extra gravity on vehicles (m/s²), so they feel less floaty.")]
        public float extraGravity = 4f;
        [Tooltip("The body has little friction (0.3), so scraping the ground or a rock skids instead of braking hard (the tyres grip).")]
        public bool slipperyBody = true;

        [Header("Mass")]
        [Tooltip("Densities (kg/m³) of wood, stone and iron; a piece's mass is its volume times this.")]
        public float woodDensity = 500f;
        public float stoneDensity = 2400f;
        public float ironDensity = 7800f;
    }
}
