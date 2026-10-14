using System;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// How much a driver is helped: a player's own setting, applied to whatever they drive
    /// (Y cycles it while driving). The vehicle (its parts and physics) stays the same; only the
    /// helpers change, so a casual player and an expert can drive the same build.
    ///
    /// - Casual: traction control that keeps grip free for cornering (power never spoils a turn),
    ///   ABS, strong stability control (it pulls the car out of slides), steering lock held at the
    ///   tyres' limit, rollover assist from 20° of lean, a gentle throttle.
    /// - Standard: traction control against wheelspin only (power can still push the back out
    ///   in a corner), ABS, light stability control, steering lock just past the limit, rollover
    ///   assist from 30°.
    /// - Expert: no traction control, no ABS (brakes lock), no stability control, much more
    ///   steering lock at speed, no rollover assist, a sharp throttle.
    /// </summary>
    [Serializable]
    public class DrivingAssists
    {
        public enum Level { Casual, Standard, Expert }

        public Level level = Level.Standard;
        [Tooltip("Traction control: a driven wheel is pushed at most this fraction past its grip (0: never spins it, -1: off).")]
        public float tractionSlack;
        [Tooltip("Traction control also leaves the grip a tyre needs for cornering (no power understeer or oversteer).")]
        public bool tractionKeepsCornering;
        [Tooltip("ABS: brakes never lock the wheels.")]
        public bool abs = true;
        [Tooltip("Stability control: pushes the car back towards where it's going once it slides past stabilityAngle (1/s² per degree past it). 0: off.")]
        public float stability = 0.04f;
        public float stabilityAngle = 10f;
        [Tooltip("Understeer limiter: while the steered tyres slide sideways, the lock eases off until they grip again (holding full lock gives the tightest turn the tyres allow, not a plough).")]
        public bool understeerLimiter = true;
        [Tooltip("Steering lock at speed, as a multiple of what the tyres can take.")]
        public float steerGripMargin = 1.05f;
        [Tooltip("Rollover assist: past this lean (degrees) the body is pushed back towards its wheels, this hard (0: off).")]
        public float rolloverAssistAngle = 30f;
        public float rolloverAssist = 0.6f;
        [Tooltip("Keyboard: seconds to full throttle, and to full lock at speed.")]
        public float throttleRiseTime = 0.5f, steerTimeFast = 0.6f;

        public static DrivingAssists For(Level level) => level switch
        {
            Level.Casual => new DrivingAssists
            {
                level = level, tractionSlack = 0f, tractionKeepsCornering = true, abs = true,
                stability = 0.12f, stabilityAngle = 6f, steerGripMargin = 0.95f,
                rolloverAssistAngle = 20f, rolloverAssist = 1f, throttleRiseTime = 0.8f, steerTimeFast = 0.8f,
            },
            Level.Expert => new DrivingAssists
            {
                level = level, tractionSlack = -1f, tractionKeepsCornering = false, abs = false,
                stability = 0f, steerGripMargin = 1.8f, understeerLimiter = false,
                rolloverAssist = 0f, throttleRiseTime = 0.25f, steerTimeFast = 0.4f,
            },
            _ => new DrivingAssists { level = Level.Standard },
        };

        /// <summary>The level after this one (Casual, Standard, Expert, Casual...).</summary>
        public static Level Next(Level level) => (Level)(((int)level + 1) % 3);
    }
}
