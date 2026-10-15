using ShooterMashup.Building;
using ShooterMashup.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Lets the player drive: F on a vehicle's seat sits down (F again gets out). While seated the
    /// player's own movement is off, W/S/A/D/Space drive the vehicle, and the camera is a third
    /// person follow camera by default; V cycles follow, first person and free orbit.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(PlayerMotor))]
    public class VehicleDriver : MonoBehaviour
    {
        public enum CameraMode { Follow, FirstPerson, Orbit }

        [Tooltip("How far away you can reach a seat (m).")]
        public float reach = 3f;
        public CameraMode cameraMode = CameraMode.Follow;

        [Header("Assists (a player setting: Y cycles casual, standard, expert while driving)")]
        public DrivingAssists.Level assistLevel = DrivingAssists.Level.Standard;
        [Tooltip("Change gear by hand (E up, Q down) instead of automatically; G toggles it while driving. Remembered.")]
        public bool manualGears;

        // How fast the throttle opens and the steering turns in at speed come with the assists.
        [Header("Keyboard (the keys are on or off, so the controls build up)")]
        [Tooltip("Seconds for the throttle (or brake) to close when the key is let go.")]
        public float throttleReleaseTime = 0.12f;
        [Tooltip("Seconds from nothing to full brake while S is held.")]
        public float brakeRiseTime = 0.2f;
        [Tooltip("Seconds to turn the wheel to full lock at a standstill (at speed it's slower, set by the assists).")]
        public float steerTimeSlow = 0.25f;
        [Tooltip("Seconds for the steering to come back to the middle when the keys are let go.")]
        public float steerReturnTime = 0.18f;

        [Header("Follow camera")]
        [Tooltip("Distance behind the vehicle (m); bigger vehicles push it further out.")]
        public float followDistance = 7f;
        [Tooltip("Height above the vehicle's middle (m).")]
        public float followHeight = 2.2f;
        [Tooltip("Smoothing time (s): how loosely the camera trails the vehicle.")]
        public float followLag = 0.12f;
        [Tooltip("Seconds after the mouse stops before the camera swings back behind the vehicle.")]
        public float recenterDelay = 1.2f;

        [Header("Orbit camera")]
        public float orbitDistance = 9f;

        [Header("Camera feel")]
        [Tooltip("Extra field of view at speed (degrees), reached at fovSpeed (m/s).")]
        public float speedFov = 12f, fovSpeed = 45f;
        [Tooltip("How much a hard jolt (landing, impact) shakes the camera, and how fast it settles (per second).")]
        public float shakeStrength = 0.04f, shakeDecay = 5f;
        [Tooltip("The camera tilts into a slide by this fraction of the slip angle (degrees), at most maxTilt.")]
        public float slideTilt = 0.15f, maxTilt = 8f;
        public float minOrbitDistance = 3f, maxOrbitDistance = 30f;

        public Vehicle Vehicle { get; private set; }
        public bool Seated => Vehicle != null;

        PlayerMotor motor;
        LocalPlayerInput input;
        BuildTool buildTool;
        LiftTool liftTool;
        Camera cam;
        PlayerView view;

        float lookYaw, lookPitch, lastInputYaw, lastInputPitch, idle, orbitBaseYaw;
        float throttleNow, steerNow;          // the shaped keyboard controls
        DrivingAssists assists;
        const string AssistsKey = "ShooterMash.DrivingAssists";
        const string GearsKey = "ShooterMash.ManualGears";
        float baseFov, shakeAmount, tilt;    // the camera's feel
        Vector3 lastVelocity;
        float joltSinceFrame;
        Vector3 camPosition, camVelocity;
        float camReach = -1f;                // how far back the follow camera may be (eases in front of terrain, back out slowly)
        bool aimingAtSeat;
        GUIStyle style;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<LocalPlayerInput>();
            buildTool = GetComponent<BuildTool>();
            liftTool = GetComponent<LiftTool>();
            cam = GetComponentInChildren<Camera>();
            view = cam != null ? cam.GetComponent<PlayerView>() : null;
            // The assist level is remembered between sessions (it's the player's, not the vehicle's).
            assistLevel = (DrivingAssists.Level)Mathf.Clamp(PlayerPrefs.GetInt(AssistsKey, (int)assistLevel), 0, 2);
            assists = DrivingAssists.For(assistLevel);
            manualGears = PlayerPrefs.GetInt(GearsKey, manualGears ? 1 : 0) != 0;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;

            if (!Seated)
            {
                aimingAtSeat = FindSeat(out Vehicle v, out int seat);
                if (aimingAtSeat && kb.fKey.wasPressedThisFrame)
                    Enter(v);
                return;
            }

            input.SampleFrame(); // cursor and mouse look, as on foot
            float dYaw = input.Yaw - lastInputYaw, dPitch = input.Pitch - lastInputPitch;
            lastInputYaw = input.Yaw;
            lastInputPitch = input.Pitch;
            lookYaw += dYaw;
            lookPitch = Mathf.Clamp(lookPitch + dPitch, -80f, 80f);
            idle = dYaw == 0f && dPitch == 0f ? idle + Time.deltaTime : 0f;

            float throttle = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            float steer = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            Shape(throttle, steer, Time.deltaTime);
            Vehicle.SetControls(throttleNow, steerNow, kb.spaceKey.isPressed);

            // T toggles traction control: off, the engine can spin the wheels (burnouts, power slides).
            // F5: the telemetry overlay (for tuning).
            if (kb.f5Key.wasPressedThisFrame)
                VehicleTelemetry.Show = !VehicleTelemetry.Show;
            VehicleTelemetry.Sample(Vehicle);

            // Y cycles the assists (casual, standard, expert); T toggles traction control on top.
            if (kb.yKey.wasPressedThisFrame)
            {
                assistLevel = DrivingAssists.Next(assistLevel);
                assists = DrivingAssists.For(assistLevel);
                Vehicle.Assists = assists;
                PlayerPrefs.SetInt(AssistsKey, (int)assistLevel);
            }
            if (kb.tKey.wasPressedThisFrame)
                assists.tractionSlack = assists.tractionSlack < 0f ? 0f : -1f;

            // Gears: G toggles automatic / manual (remembered, like the assists); by hand E shifts up, Q down.
            if (kb.gKey.wasPressedThisFrame)
            {
                manualGears = !manualGears;
                Vehicle.Revs.Manual = manualGears;
                PlayerPrefs.SetInt(GearsKey, manualGears ? 1 : 0);
            }
            if (manualGears && kb.eKey.wasPressedThisFrame)
                Vehicle.Revs.ShiftUp();
            if (manualGears && kb.qKey.wasPressedThisFrame)
                Vehicle.Revs.ShiftDown();

            if (kb.vKey.wasPressedThisFrame)
            {
                cameraMode = (CameraMode)(((int)cameraMode + 1) % 3);
                lookYaw = lookPitch = 0f;
                orbitBaseYaw = Mathf.Atan2(VehicleForward().x, VehicleForward().z) * Mathf.Rad2Deg;
            }
            var mouse = Mouse.current;
            if (cameraMode == CameraMode.Orbit && mouse != null)
                orbitDistance = Mathf.Clamp(orbitDistance - Mathf.Sign(mouse.scroll.ReadValue().y) * (mouse.scroll.ReadValue().y != 0f ? 1f : 0f),
                    minOrbitDistance, maxOrbitDistance);

            if (kb.fKey.wasPressedThisFrame)
                Exit();
        }

        /// <summary>
        /// Jolts for the camera shake: the change in the vehicle's velocity per physics step (it
        /// only changes then; measured per frame, ordinary acceleration looked like jolts).
        /// </summary>
        void FixedUpdate()
        {
            if (!Seated || Vehicle == null || Vehicle.Body == null)
                return;
            Vector3 velocity = Vehicle.Body.linearVelocity;
            joltSinceFrame = Mathf.Max(joltSinceFrame, (velocity - lastVelocity).magnitude / Time.fixedDeltaTime);
            lastVelocity = velocity;
        }

        void LateUpdate()
        {
            if (!Seated)
                return;
            if (Vehicle == null || Vehicle.System?.Get(Vehicle.SeatId) == null)
            {
                Exit(); // the vehicle or its seat is gone
                return;
            }
            transform.position = SeatWorld(out _);
            UpdateCamera(false);
        }

        /// <summary>
        /// Keys are on or off; the controls build up from them. The throttle opens over
        /// the assists' throttleRiseTime (tap W to feather it) and closes quickly; braking comes on faster.
        /// Steering turns in more slowly the faster you go (finer at speed) and comes back quickly.
        /// </summary>
        void Shape(float throttleKeys, float steerKeys, float dt)
        {
            float rise = throttleKeys < 0f ? brakeRiseTime : assists.throttleRiseTime;
            if (throttleKeys == 0f || Mathf.Sign(throttleKeys) != Mathf.Sign(throttleNow) && throttleNow != 0f)
                throttleNow = Mathf.MoveTowards(throttleNow, 0f, dt / Mathf.Max(throttleReleaseTime, 0.01f));
            else
                throttleNow = Mathf.MoveTowards(throttleNow, throttleKeys, dt / Mathf.Max(rise, 0.01f));

            float speed = Vehicle != null ? Mathf.Abs(Vehicle.ForwardSpeed) : 0f;
            float turnIn = Mathf.Lerp(steerTimeSlow, assists.steerTimeFast, speed / 30f);
            bool returning = steerKeys == 0f || steerNow != 0f && Mathf.Sign(steerKeys) != Mathf.Sign(steerNow);
            steerNow = returning
                ? Mathf.MoveTowards(steerNow, 0f, dt / Mathf.Max(steerReturnTime, 0.01f))
                : Mathf.MoveTowards(steerNow, steerKeys, dt / Mathf.Max(turnIn, 0.01f));
        }

        // ------------------------------------------------------------------ seat

        bool FindSeat(out Vehicle vehicle, out int seat)
        {
            vehicle = null;
            seat = -1;
            if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, reach,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            vehicle = hit.collider.GetComponentInParent<Vehicle>();
            BuildPieceTag tag = hit.collider.GetComponentInParent<BuildPieceTag>();
            if (vehicle == null || tag == null || vehicle.Driver != null)
                return false;
            Piece p = vehicle.System.Get(tag.id);
            if (p == null || p.def.Seat == null || tag.id != vehicle.SeatId)
                return false;
            seat = tag.id;
            return true;
        }

        void Enter(Vehicle v)
        {
            Vehicle = v;
            v.Driver = this;
            v.Assists = assists;
            v.Revs.Manual = manualGears;
            if (buildTool != null) { buildTool.Unequip(); buildTool.enabled = false; }
            if (liftTool != null) { liftTool.Unequip(); liftTool.enabled = false; }
            motor.enabled = false;
            if (view != null) view.enabled = false;
            lastInputYaw = input.Yaw;
            lastInputPitch = input.Pitch;
            lookYaw = lookPitch = 0f;
            cameraMode = CameraMode.Follow;
            throttleNow = steerNow = 0f;
            baseFov = cam.fieldOfView;
            shakeAmount = tilt = 0f;
            lastVelocity = v.Body.linearVelocity;
            UpdateCamera(true);
        }

        /// <summary>Get out, beside the vehicle where there's room (or on top of it).</summary>
        public void Exit()
        {
            Vehicle v = Vehicle;
            Vehicle = null;
            Vector3 feet = transform.position;
            float yaw = transform.eulerAngles.y;
            if (v != null)
            {
                v.SetControls(0f, 0f, false);
                v.Driver = null;
                v.Assists = null;
                v.Revs.Manual = false;              // whoever drives it next (an autopilot) shifts automatically
                feet = ExitSpot(v);
                Vector3 f = v.transform.TransformDirection(v.Forward);
                yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
            motor.enabled = true;
            if (view != null) view.enabled = true;
            if (cam != null && baseFov > 0f) cam.fieldOfView = baseFov;
            if (buildTool != null) buildTool.enabled = true;
            if (liftTool != null) liftTool.enabled = true;
            motor.Teleport(feet - Vector3.up * motor.movement.mins.y, yaw, false);
        }

        Vector3 ExitSpot(Vehicle v)
        {
            Bounds b = v.LocalBounds();
            Transform t = v.transform;
            Vector3 right = Vector3.Cross(Vector3.up, v.Forward).normalized;
            Vector3[] candidates =
            {
                b.center - right * (b.extents.x + 0.8f),     // left (the driver's side)
                b.center + right * (b.extents.x + 0.8f),
                b.center - v.Forward * (b.extents.z + 0.8f),
                b.center + v.Forward * (b.extents.z + 0.8f),
            };
            MoveConfig m = motor.movement;
            Vector3 half = new Vector3(m.maxs.x - m.mins.x, m.maxs.y - m.mins.y, m.maxs.z - m.mins.z) / 2f;
            foreach (Vector3 c in candidates)
            {
                Vector3 above = t.TransformPoint(c) + Vector3.up * 2f;
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit ground, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                if (ground.collider.attachedRigidbody == v.Body)
                    continue;
                Vector3 feet = ground.point + Vector3.up * 0.05f;
                if (!Physics.CheckBox(feet + Vector3.up * half.y, half * 0.95f, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return feet;
            }
            // Nowhere beside it: on top.
            return t.TransformPoint(new Vector3(b.center.x, b.max.y + 0.1f, b.center.z));
        }

        Vector3 SeatWorld(out Quaternion rotation)
        {
            Piece seat = Vehicle.System.Get(Vehicle.SeatId);
            Transform t = Vehicle.transform;
            rotation = t.rotation * seat.record.rotation;
            return t.TransformPoint(seat.record.position + seat.record.rotation * seat.def.Seat.eye);
        }

        Vector3 VehicleForward() => Vehicle.transform.TransformDirection(Vehicle.Forward);

        // ------------------------------------------------------------------ camera

        void UpdateCamera(bool snap)
        {
            Transform t = Vehicle.transform;
            Bounds local = Vehicle.LocalBounds();
            Vector3 target = t.TransformPoint(local.center) + Vector3.up * 0.5f;
            float size = local.extents.magnitude;
            if (Vehicle.Towing != null)
            {
                // Towing: stand back far enough to see the trailer too.
                Bounds trailer = Vehicle.Towing.LocalBounds();
                Vector3 back = Vehicle.Towing.transform.TransformPoint(trailer.center);
                size = Mathf.Max(size, (Vector3.Distance(target, back) + trailer.extents.magnitude) * 0.7f);
            }
            Vector3 position;
            Quaternion rotation;

            switch (cameraMode)
            {
                case CameraMode.FirstPerson:
                {
                    position = SeatWorld(out Quaternion seatRotation);
                    rotation = seatRotation * Quaternion.Euler(lookPitch, lookYaw, 0f);
                    cam.transform.SetPositionAndRotation(position, rotation);
                    camPosition = position;
                    return;
                }
                case CameraMode.Orbit:
                {
                    rotation = Quaternion.Euler(lookPitch + 20f, orbitBaseYaw + lookYaw, 0f);
                    position = Unblocked(target, target - rotation * Vector3.forward * Mathf.Max(orbitDistance, size));
                    break;
                }
                default:
                {
                    // Behind the vehicle, looking ahead. The mouse looks around for a moment, then it swings back.
                    if (idle > recenterDelay)
                    {
                        float back = 1f - Mathf.Exp(-3f * Time.deltaTime);
                        lookYaw = Mathf.LerpAngle(lookYaw, 0f, back);
                        lookPitch = Mathf.Lerp(lookPitch, 0f, back);
                    }
                    Vector3 f = VehicleForward();
                    f.y = 0f;
                    if (f.sqrMagnitude < 1e-4f) f = t.up; // pointing straight up or down
                    Quaternion heading = Quaternion.LookRotation(f.normalized) * Quaternion.Euler(lookPitch, lookYaw, 0f);
                    float distance = Mathf.Max(followDistance, size * 1.8f);
                    Vector3 desired = target - heading * Vector3.forward * distance + Vector3.up * followHeight;
                    camPosition = snap ? desired : Vector3.SmoothDamp(camPosition, desired, ref camVelocity, followLag);
                    // Something between the camera and the vehicle (a crest, a wall): ease in front of
                    // it quickly and back out slowly, rather than popping in and out as it comes and goes.
                    Vector3 behind = camPosition - target;
                    float wanted = Vector3.Distance(target, Unblocked(target, camPosition));
                    if (snap || camReach < 0f)
                        camReach = wanted;
                    else if (wanted < camReach)
                        camReach = Mathf.Lerp(camReach, wanted, 1f - Mathf.Exp(-25f * Time.deltaTime));
                    else
                        camReach = Mathf.MoveTowards(camReach, wanted, 4f * Time.deltaTime);
                    position = target + behind.normalized * Mathf.Min(camReach, behind.magnitude);
                    rotation = Quaternion.LookRotation(target - position);
                    break;
                }
            }
            if (cameraMode != CameraMode.Follow)
                camPosition = position;
            cam.transform.SetPositionAndRotation(position, rotation * Effects(Time.deltaTime, out Vector3 shake));
            cam.transform.position += shake;
        }

        /// <summary>
        /// The camera's feel: the field of view widens with speed, hard jolts (landings, impacts,
        /// big bumps: sudden changes in the vehicle's velocity) shake it, and it tilts into a slide.
        /// Returns the tilt; `shake` is the offset to add.
        /// </summary>
        Quaternion Effects(float dt, out Vector3 shake)
        {
            shake = Vector3.zero;
            if (Vehicle == null || dt <= 0f)
                return Quaternion.identity;
            float speed = Mathf.Abs(Vehicle.ForwardSpeed);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov + speedFov * Mathf.Clamp01(speed / fovSpeed), 1f - Mathf.Exp(-3f * dt));

            // The hardest jolt since the last frame, measured per physics step (see FixedUpdate).
            float jolt = joltSinceFrame;
            joltSinceFrame = 0f;
            if (jolt > 25f)
                shakeAmount = Mathf.Max(shakeAmount, Mathf.Min((jolt - 25f) * shakeStrength * 0.02f, 1f));
            shakeAmount = Mathf.MoveTowards(shakeAmount, 0f, shakeDecay * shakeAmount * dt + 0.01f * dt);
            if (shakeAmount > 0f)
            {
                float t = Time.time * 25f;
                shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * shakeAmount;
            }

            // Slow, the slip angle swings about (turning on the spot, rolling backwards): no tilt there.
            float wanted = Mathf.Clamp(-Vehicle.SlipAngle * slideTilt, -maxTilt, maxTilt) * Mathf.Clamp01((speed - 3f) / 6f);
            tilt = Mathf.Lerp(tilt, wanted, 1f - Mathf.Exp(-4f * dt));
            return Quaternion.Euler(0f, 0f, tilt);
        }

        /// <summary>The vehicle itself or its trailer: the camera doesn't dodge these (it jittered behind the semi's trailer).</summary>
        bool Own(Rigidbody body) =>
            body != null && (body == Vehicle.Body || Vehicle.Towing != null && body == Vehicle.Towing.Body);

        /// <summary>Pull the camera in front of walls and terrain between it and the vehicle.</summary>
        Vector3 Unblocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 1e-3f)
                return to;
            RaycastHit[] hits = Physics.SphereCastAll(from, 0.25f, d / length, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = length;
            foreach (RaycastHit h in hits)
                if (!Own(h.collider.attachedRigidbody) && h.distance > 0f)
                    nearest = Mathf.Min(nearest, h.distance);
            return from + d / length * nearest;
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.LowerCenter };
                style.normal.textColor = Color.white;
            }
            if (!Seated)
            {
                if (aimingAtSeat)
                    GUI.Label(new Rect(0, Screen.height / 2f + 20, Screen.width, 40), "<b>F</b> drive", style);
                return;
            }
            if (Vehicle == null)
                return;
            string state = Vehicle.Lift != null ? "  <color=#ffd966>(on a lift: take the lift away to drive)</color>"
                : Vehicle.WheelCount == 0 ? "  <color=#ffd966>(no wheels)</color>" : "";
            string text =
                $"<b>{Vehicle.ForwardSpeed * 3.6f:0} km/h</b>   {Vehicle.Mass:0} kg   wheels on the ground {Vehicle.WheelsOnGround}/{Vehicle.WheelCount}{state}\n" +
                $"<color=#aaa>W/S throttle and brake, A/D steer, Space handbrake, Y assists ({assists.level}), T traction control, G gears ({(manualGears ? "manual: E up, Q down" : "auto")}), V camera ({cameraMode}), F get out</color>";
            GUI.Label(new Rect(0, Screen.height - 120, Screen.width, 60), text, style);
            if (Vehicle.Lift == null && Vehicle.WheelCount > 0)
                VehicleHud.Draw(Vehicle);
            if (Vehicle.Lift == null && Vehicle.WheelCount > 0)
                VehicleTelemetry.Draw(Vehicle);
        }
    }
}
