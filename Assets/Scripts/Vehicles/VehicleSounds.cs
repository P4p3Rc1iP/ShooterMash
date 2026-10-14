using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>Puts a vehicle sound bank in use for this scene.</summary>
    public class VehicleSounds : MonoBehaviour
    {
        public VehicleSoundBank bank;

        void OnEnable() => VehicleSoundBank.Current = bank;

        void OnDisable()
        {
            if (VehicleSoundBank.Current == bank)
                VehicleSoundBank.Current = null;
        }
    }
}
