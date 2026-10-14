using ShooterMashup.Player;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>Is a player riding a vehicle (standing on it, or jumping above it)?</summary>
    static class Riders
    {
        static PlayerMotor[] players = new PlayerMotor[0];
        static float nextScan = float.NegativeInfinity;

        public static bool On(Transform vehicle)
        {
            // Look for players once a second (Time.time restarts with each Play session).
            if (Time.time >= nextScan || Time.time < nextScan - 2f)
            {
                players = Object.FindObjectsByType<PlayerMotor>();
                nextScan = Time.time + 1f;
            }
            foreach (PlayerMotor p in players)
                if (p != null && p.enabled && p.Platform == vehicle)
                    return true;
            return false;
        }
    }
}
