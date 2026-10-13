using ShooterMashup.Building;
using ShooterMashup.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// The player's lift tool (L), after Scrap Mechanic. Each player has one lift.
    /// Aim at the ground and click to put the lift there (moving it, and anything on it, if it
    /// was somewhere else). Aim at a vehicle and click to put it on the lift, upright: that's how
    /// you get at the underside, and how you recover a vehicle that flipped. Q/E lower and raise
    /// the lift, scroll turns it, right click takes it away and drops the vehicle so it can drive.
    /// Build on a lift with the build tool (B): the first piece starts a vehicle.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlayerMotor))]
    public class LiftTool : MonoBehaviour
    {
        [Tooltip("How far away you can place the lift (m).")]
        public float reach = 15f;
        [Tooltip("Degrees per scroll notch when turning the lift.")]
        public float rotationStep = 15f;

        public bool Equipped { get; private set; }
        public Lift Lift => lift;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        LocalPlayerInput input;
        BuildTool buildTool;
        Camera cam;
        StructureManager structures;
        Lift lift;
        float yaw;
        GameObject ghost;
        MaterialPropertyBlock block;

        bool hasTarget;
        Vector3 targetPoint;
        float targetYaw;
        Vehicle targetVehicle;
        string aimText = "";
        string message = "";
        float messageTime = -10f;
        GUIStyle style;

        void Awake()
        {
            input = GetComponent<LocalPlayerInput>();
            buildTool = GetComponent<BuildTool>();
            cam = GetComponentInChildren<Camera>();
            block = new MaterialPropertyBlock();
        }

        void Start()
        {
            structures = FindAnyObjectByType<StructureManager>();
        }

        void OnDisable() => SetEquipped(false);

        void OnDestroy()
        {
            if (ghost != null) Destroy(ghost);
        }

        public void Unequip() => SetEquipped(false);

        void SetEquipped(bool on)
        {
            if (Equipped == on)
                return;
            Equipped = on;
            if (on && buildTool != null)
                buildTool.Unequip();
            if (input != null) input.leanEnabled = !on; // Q/E move the lift
            if (!on && ghost != null)
                ghost.SetActive(false);
        }

        void LateUpdate()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null || structures == null)
                return;

            if (kb.lKey.wasPressedThisFrame)
                SetEquipped(!Equipped);
            if (Equipped && buildTool != null && buildTool.Equipped)
                SetEquipped(false); // B took the build tool out
            if (!Equipped)
                return;

            Aim();
            UpdateGhost();
            if (!LocalPlayerInput.CursorLocked)
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                yaw = Mathf.Repeat(yaw + Mathf.Sign(scroll) * rotationStep, 360f);
            if (lift != null && kb.eKey.wasPressedThisFrame)
                lift.SetHeight(lift.Height + Lift.HeightStep);
            if (lift != null && kb.qKey.wasPressedThisFrame)
                lift.SetHeight(lift.Height - Lift.HeightStep);

            if (mouse.leftButton.wasPressedThisFrame && hasTarget)
                PlaceLift();
            if (mouse.rightButton.wasPressedThisFrame && lift != null)
            {
                bool hadVehicle = lift.Vehicle != null;
                lift.Remove();
                lift = null;
                Say(hadVehicle ? "lift removed: the vehicle is free" : "lift removed");
            }
        }

        /// <summary>What a click would do: put the lift on the ground there, or under a vehicle.</summary>
        void Aim()
        {
            hasTarget = false;
            targetVehicle = null;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, reach,
                    structures.solidMask, QueryTriggerInteraction.Ignore))
            {
                aimText = "aim at the ground, or at a vehicle";
                return;
            }

            Vehicle vehicle = hit.collider.GetComponentInParent<Vehicle>();
            if (vehicle != null)
            {
                if (vehicle.Lift != null)
                {
                    aimText = "that vehicle is on a lift already";
                    return;
                }
                if (!GroundBelow(vehicle, out targetPoint))
                {
                    aimText = "no ground under that vehicle";
                    return;
                }
                Vector3 f = vehicle.transform.TransformDirection(vehicle.Forward);
                targetYaw = f.x * f.x + f.z * f.z > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : yaw;
                targetVehicle = vehicle;
                hasTarget = true;
                aimText = "click: put this vehicle on the lift";
                return;
            }
            if (hit.collider.GetComponentInParent<Lift>() != null)
            {
                aimText = "aim at the ground beside the lift to move it";
                return;
            }
            if (hit.normal.y < 0.7f)
            {
                aimText = "too steep for the lift";
                return;
            }
            targetPoint = hit.point;
            targetYaw = yaw;
            hasTarget = true;
            aimText = lift == null ? "click: place the lift"
                : lift.Vehicle != null ? "click: move the lift here, with the vehicle on it"
                : "click: move the lift here";
        }

        /// <summary>The ground under a vehicle's middle, ignoring the vehicle itself.</summary>
        static bool GroundBelow(Vehicle vehicle, out Vector3 point)
        {
            point = default;
            Vector3 origin = vehicle.transform.TransformPoint(vehicle.LocalBounds().center) + Vector3.up * 2f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.attachedRigidbody != null || h.collider.GetComponentInParent<BuildHost>() is Lift)
                    continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    point = h.point;
                }
            }
            return best < float.MaxValue;
        }

        void PlaceLift()
        {
            if (lift == null)
            {
                lift = Lift.Create(structures, $"Lift ({name})");
                lift.SetHeight(Lift.DefaultHeight);
            }
            lift.Place(targetPoint, targetYaw);
            if (targetVehicle != null)
            {
                lift.Hold(targetVehicle);
                Say("vehicle on the lift: build on it with B");
            }
            else
            {
                Say(lift.Vehicle != null ? "lift moved" : "lift placed: build on it with B");
            }
        }

        void UpdateGhost()
        {
            if (!hasTarget)
            {
                if (ghost != null) ghost.SetActive(false);
                return;
            }
            if (ghost == null)
            {
                ghost = new GameObject("Lift ghost");
                ghost.AddComponent<MeshFilter>().sharedMesh = BuildMeshes.Box(Vector3.one);
                var r = ghost.AddComponent<MeshRenderer>();
                r.sharedMaterial = structures.GhostMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                block.SetColor(BaseColor, new Color(1f, 0.8f, 0.2f, 0.45f));
                block.SetTexture(BaseMap, Texture2D.whiteTexture);
                r.SetPropertyBlock(block);
            }
            float height = lift != null ? lift.Height : Lift.DefaultHeight;
            ghost.SetActive(true);
            Quaternion rotation = Quaternion.Euler(0f, targetYaw, 0f);
            ghost.transform.SetPositionAndRotation(targetPoint + Vector3.up * (height - Lift.Thickness / 2f), rotation);
            ghost.transform.localScale = new Vector3(Lift.PlatformSize.x, Lift.Thickness, Lift.PlatformSize.y);
        }

        void Say(string text)
        {
            message = text;
            messageTime = Time.time;
        }

        void OnGUI()
        {
            if (!Equipped)
                return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.LowerCenter };
                style.normal.textColor = Color.white;
            }
            string height = lift != null ? $"   lift height {lift.Height:0.0} m" : "";
            string text =
                $"<b>Lift</b>{height}\n" +
                aimText + "\n" +
                (Time.time - messageTime < 2.5f ? $"<color=#ffd966>{message}</color>" : "") + "\n" +
                "<color=#aaa>LMB place the lift / put the vehicle you aim at on it, RMB take the lift away (drops the vehicle),\n" +
                "Q/E lower/raise, scroll turn, B build on it, L put away</color>";
            GUI.Label(new Rect(0, Screen.height - 250, Screen.width, 120), text, style);
        }
    }
}
