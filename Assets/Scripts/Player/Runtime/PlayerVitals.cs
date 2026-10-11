using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Minimal health: takes fall damage from movement events, dies, and respawns.
    /// A placeholder until there's a real damage/survival system.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerVitals : MonoBehaviour
    {
        public int maxHealth = 100;
        public float respawnDelay = 3f;

        public int Health { get; private set; }
        public bool IsDead => Health <= 0;

        PlayerMotor motor;
        float deathTime;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            Health = maxHealth;
        }

        void OnEnable() => motor.MoveEvent += OnMoveEvent;
        void OnDisable() => motor.MoveEvent -= OnMoveEvent;

        void OnMoveEvent(PmEvent e)
        {
            if (e.type == PmEventType.FallDamage)
                Damage(e.param);
        }

        public void Damage(int amount)
        {
            if (IsDead)
                return;
            Health -= amount;
            if (Health <= 0)
            {
                Health = 0;
                deathTime = Time.time;
                motor.SetPmType(PmType.Dead);
            }
        }

        public void Respawn()
        {
            Health = maxHealth;
            motor.Respawn();
        }

        void Update()
        {
            if (IsDead && Time.time - deathTime > respawnDelay)
                Respawn();
        }
    }
}
