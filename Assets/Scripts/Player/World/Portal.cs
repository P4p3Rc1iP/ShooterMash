using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Walk through it to be teleported: a box (in this object's space) that sends any player on
    /// foot whose origin enters it to a destination. For getting around the test map quickly;
    /// players driving a vehicle aren't affected (their movement is off while seated).
    /// </summary>
    public class Portal : MonoBehaviour
    {
        [Tooltip("Where the player's feet land (world).")]
        public Vector3 destination;
        [Tooltip("Which way the player faces on arrival (degrees, 0 = +z).")]
        public float destinationYaw;
        [Tooltip("Size of the trigger box in this object's space (m).")]
        public Vector3 size = new Vector3(2.4f, 3f, 0.8f);

        PlayerMotor[] players = new PlayerMotor[0];
        float nextScan;

        void Update()
        {
            if (Time.time >= nextScan)
            {
                players = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
                nextScan = Time.time + 1f;
            }
            Vector3 half = size / 2f;
            foreach (PlayerMotor motor in players)
            {
                if (motor == null || !motor.enabled)
                    continue;
                Vector3 local = transform.InverseTransformPoint(motor.State.origin);
                if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
                    continue;
                // Teleport takes the origin, which is above the feet.
                motor.Teleport(destination - Vector3.up * motor.movement.mins.y, destinationYaw, false);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
