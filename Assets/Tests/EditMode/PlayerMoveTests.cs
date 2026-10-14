using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShooterMashup.Player.Tests
{
    /// <summary>
    /// Runs the movement simulation against real Unity colliders and checks it against
    /// Enemy Territory's numbers (converted to metres). Geometry is built far from the origin
    /// and removed afterwards.
    /// </summary>
    public class PlayerMoveTests
    {
        static readonly Vector3 Base = new Vector3(500f, 0f, 500f); // away from any scene content
        const int Tick = 8;
        const float StandOriginY = 0.6096f; // origin height when standing on the floor
        const float SpeedTolerance = 0.03f;

        MoveConfig cfg;
        PlayerMove pmove;
        PlayerState st;
        int time;
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<PmEvent> events = new List<PmEvent>();

        [SetUp]
        public void SetUp()
        {
            cfg = new MoveConfig();
            pmove = new PlayerMove(cfg, new UnityPmoveWorld(Physics.DefaultRaycastLayers));
            time = 1000;
            events.Clear();
            Box(new Vector3(0, -0.4f, 0), new Vector3(100, 0.8f, 100)); // floor, top at y = 0
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects)
                Object.DestroyImmediate(go);
            objects.Clear();
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ harness

        GameObject Box(Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var go = new GameObject("TestBox");
            go.transform.SetPositionAndRotation(Base + center, rotation ?? Quaternion.identity);
            go.AddComponent<BoxCollider>().size = size;
            objects.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        void Spawn(Vector3 origin, float yaw = 0f)
        {
            st = PlayerState.Spawn(Base + origin, cfg, time);
            st.viewAngles = new Vector2(0f, yaw);
            st.deltaAngles = new Vector2(0f, yaw);
        }

        void SpawnOnFloor(float x = 0f, float z = 0f, float yaw = 0f) => Spawn(new Vector3(x, StandOriginY, z), yaw);

        Vector3 Pos => st.origin - Base;
        float HorizontalSpeed => new Vector2(st.velocity.x, st.velocity.z).magnitude;

        void Run(int ms, int forward = 0, int right = 0, int up = 0, CmdButtons buttons = CmdButtons.None,
            float pitch = 0f, DoubleTap doubleTap = DoubleTap.None)
        {
            for (int t = 0; t < ms; t += Tick)
            {
                time += Tick;
                var cmd = new PlayerCmd
                {
                    serverTime = time,
                    forwardMove = (sbyte)forward,
                    rightMove = (sbyte)right,
                    upMove = (sbyte)up,
                    pitch = pitch,
                    buttons = buttons,
                    doubleTap = t == 0 ? doubleTap : DoubleTap.None,
                };
                pmove.Move(ref st, cmd);
                events.AddRange(pmove.Events);
            }
        }

        void Settle() => Run(200);

        // ------------------------------------------------------------------ ground speeds

        [Test]
        public void StandingOnFloor_IsGroundedAndStill()
        {
            SpawnOnFloor();
            Run(500);
            Assert.IsTrue(st.onGround);
            Assert.AreEqual(StandOriginY, Pos.y, 0.01f);
            Assert.AreEqual(0f, st.velocity.magnitude, 0.001f);
        }

        [Test]
        public void RunSpeed_Is6Point5()
        {
            SpawnOnFloor();
            Run(1500, forward: 127);
            Assert.AreEqual(6.5024f, HorizontalSpeed, SpeedTolerance);
            Assert.Greater(Pos.z, 5f);
        }

        [Test]
        public void DiagonalRun_IsNotFaster()
        {
            SpawnOnFloor();
            Run(1500, forward: 127, right: 127);
            Assert.AreEqual(6.5024f, HorizontalSpeed, SpeedTolerance);
        }

        [Test]
        public void SprintSpeed_Is8Point9()
        {
            SpawnOnFloor();
            Run(1000, forward: 127, buttons: CmdButtons.Sprint);
            Assert.AreEqual(8.9408f, HorizontalSpeed, SpeedTolerance);
        }

        [Test]
        public void WalkSpeed_IsHalfOfRun()
        {
            SpawnOnFloor();
            Run(1500, forward: 64, buttons: CmdButtons.Walk);
            Assert.AreEqual(8.128f * 64f / 127f * 0.8f, HorizontalSpeed, SpeedTolerance);
        }

        [Test]
        public void CrouchSpeed_Is2()
        {
            SpawnOnFloor();
            Run(1500, forward: 127, up: -127);
            Assert.IsTrue(st.IsDucked);
            Assert.AreEqual(2.032f, HorizontalSpeed, SpeedTolerance);
        }

        [Test]
        public void ProneSpeed_Is1Point7()
        {
            SpawnOnFloor();
            Run(Tick, buttons: CmdButtons.Prone);
            Assert.IsTrue(st.IsProne);
            Run(1500, forward: 127);
            Assert.AreEqual(8.128f * 0.21f, HorizontalSpeed, SpeedTolerance);
        }

        [Test]
        public void Friction_StopsQuickly()
        {
            SpawnOnFloor();
            Run(1000, forward: 127);
            Run(400);
            Assert.AreEqual(0f, HorizontalSpeed, 0.001f);
        }

        // ------------------------------------------------------------------ jumping

        [Test]
        public void JumpApex_IsOnePointOneSixMetres()
        {
            SpawnOnFloor();
            Settle();
            float start = Pos.y, apex = start;
            Run(Tick, up: 127);
            for (int i = 0; i < 100; i++)
            {
                Run(Tick);
                apex = Mathf.Max(apex, Pos.y);
            }
            // v^2 / 2g = 6.858^2 / 40.64
            Assert.AreEqual(1.1573f, apex - start, 0.02f);
            Assert.IsTrue(st.onGround, $"y {Pos.y} v {st.velocity}");
        }

        [Test]
        public void Jump_HasCooldownAndCostsStamina()
        {
            SpawnOnFloor();
            Settle();
            var jumpTimes = new List<int>();
            for (int i = 0; i < 300; i++)
            {
                // Tap jump every other tick.
                Run(Tick, up: i % 2 == 0 ? 127 : 0);
                if (pmove.Events.Any(e => e.type == PmEventType.Jump))
                    jumpTimes.Add(time);
            }
            Assert.GreaterOrEqual(jumpTimes.Count, 2);
            for (int i = 1; i < jumpTimes.Count; i++)
                Assert.GreaterOrEqual(jumpTimes[i] - jumpTimes[i - 1], cfg.jumpCooldownMs);
            Assert.Less(st.stamina, 1f);
        }

        [TestCase(MovePreset.ShooterMash, true)]
        [TestCase(MovePreset.EtExact, false)]
        public void LandingEarlyOnANewPiece_JumpAgain(MovePreset preset, bool canJump)
        {
            // Jump, have a 0.5 m slab appear under you on the way up (building under yourself),
            // land on it early, and press jump again straight away. ET's cooldown counts 850 ms
            // from the jump, so it refuses; ShooterMash has none.
            MovePresets.Apply(cfg, preset);
            Spawn(new Vector3(0f, -cfg.mins.y, 0f));
            Settle();
            Run(Tick, up: 127);
            Run(240);
            Box(new Vector3(0f, 0.25f, 0f), new Vector3(3f, 0.5f, 3f));
            for (int t = 0; t < 1000 && !st.onGround; t += Tick)
                Run(Tick);
            Assert.IsTrue(st.onGround, "should have landed on the slab");
            Assert.AreEqual(0.5f, Pos.y + cfg.mins.y, 0.01f, "on top of it");

            events.Clear();
            Run(2 * Tick, up: 127);
            Assert.AreEqual(canJump, events.Any(e => e.type == PmEventType.Jump));
        }

        [Test]
        public void HoldingJump_OnlyJumpsOnce()
        {
            SpawnOnFloor();
            Settle();
            events.Clear();
            Run(3000, up: 127);
            Assert.AreEqual(1, events.Count(e => e.type == PmEventType.Jump));
        }

        // ------------------------------------------------------------------ moving platforms

        /// <summary>A 4 x 4 m platform with its top at 1 m, centred above x = 0, z = 5, with a rigidbody.</summary>
        GameObject PlatformUnderfoot()
        {
            GameObject platform = Box(new Vector3(0f, 0.75f, 5f), new Vector3(4f, 0.5f, 4f));
            var body = platform.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return platform;
        }

        /// <summary>Stand on the platform, 1 m off its middle, and let the rider see it.</summary>
        PlatformRider StandOn(GameObject platform, Vector3 platformVelocity = default)
        {
            Spawn(new Vector3(1f, 1f + StandOriginY, 5f));
            Settle();
            // Physics doesn't run in edit-mode tests, so the platform says how fast it moves.
            var rider = new PlatformRider((t, p) => platformVelocity);
            rider.UpdateGround(ref st, pmove.GroundObject);
            st.velocity = Vector3.zero; // standing still on it (it doesn't really move here)
            Assert.AreEqual(platform.transform, rider.Platform, "riding the platform");
            return rider;
        }

        void Carry(PlatformRider rider)
        {
            Physics.SyncTransforms();
            PlayerState previous = st;
            rider.Carry(ref st, ref previous, pmove.World, cfg.mins, cfg.maxs);
        }

        [Test]
        public void Platform_CarriesThePlayer()
        {
            GameObject platform = PlatformUnderfoot();
            PlatformRider rider = StandOn(platform);
            Vector3 before = Pos;
            platform.transform.position += new Vector3(2f, 0.5f, -1f);
            Carry(rider);
            AssertNear(before + new Vector3(2f, 0.5f, -1f), Pos);
        }

        [Test]
        public void TurningPlatform_TurnsThePlayerAndTheView()
        {
            GameObject platform = PlatformUnderfoot();
            PlatformRider rider = StandOn(platform);
            float yaw = st.deltaAngles.y;
            platform.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Carry(rider);
            // 1 m east of the middle, turned 90° right about it: 1 m south of the middle.
            AssertNear(new Vector3(0f, 1f + StandOriginY, 4f), Pos);
            Assert.AreEqual(yaw + 90f, st.deltaAngles.y, 0.01f);
        }

        [Test]
        public void Jumping_StaysWithThePlatformWhileAboveIt_ThenKeepsItsSpeed()
        {
            GameObject platform = PlatformUnderfoot();
            PlatformRider rider = StandOn(platform, new Vector3(5f, 0f, 0f));

            Run(Tick, up: 127);
            Assert.IsFalse(st.onGround);
            rider.UpdateGround(ref st, pmove.GroundObject);
            Assert.AreEqual(platform.transform, rider.Platform, "still in its frame, above it");
            Assert.AreEqual(0f, st.velocity.x, 0.01f, "moving with it, so no extra speed yet");

            // Out past its edge in the air: let go of it, keeping its momentum.
            st.origin += new Vector3(4f, 0f, 0f);
            Carry(rider);
            Assert.IsNull(rider.Platform);
            Assert.AreEqual(5f, st.velocity.x, 0.01f, "keeps the platform's speed");
        }

        /// <summary>A rider on the platform with a fake physics clock: each Carry is one 20 ms step.</summary>
        PlatformRider StandOnWithClock(GameObject platform, System.Func<Vector3> platformVelocity, System.Func<double> clock)
        {
            Spawn(new Vector3(1f, 1f + StandOriginY, 5f));
            Settle();
            var rider = new PlatformRider((t, p) => platformVelocity(), clock);
            rider.UpdateGround(ref st, pmove.GroundObject);
            st.velocity = Vector3.zero;
            return rider;
        }

        [Test]
        public void HardJolt_ThrowsTheRider_GentleOnesDoNot()
        {
            GameObject platform = PlatformUnderfoot();
            Vector3 velocity = new Vector3(30f, 0f, 0f);
            double time = 0;
            PlatformRider rider = StandOnWithClock(platform, () => velocity, () => time);

            // Braking at 10 m/s² (under the 20 m/s² threshold): carried along, no slipping.
            time += 0.02;
            velocity.x -= 0.2f;
            Carry(rider);
            Assert.AreEqual(0f, st.velocity.x, 1e-4f, "gentle braking: feet don't slip");

            // A crash: 29.8 to 0 m/s in one 20 ms step (1490 m/s²). All but 20 m/s² of it isn't
            // passed on: the rider keeps going at almost the full speed, relative to the truck.
            time += 0.02;
            float before = velocity.x;
            velocity.x = 0f;
            Carry(rider);
            Assert.AreEqual(before * (1f - 20f / (before / 0.02f)), st.velocity.x, 1e-3f, "thrown forwards");
        }

        [Test]
        public void PlatformDroppingAway_FloatsTheRiderUp_ButPushingUpDoesNot()
        {
            GameObject platform = PlatformUnderfoot();
            Vector3 velocity = Vector3.zero;
            double time = 0;
            PlatformRider rider = StandOnWithClock(platform, () => velocity, () => time);

            time += 0.02;
            velocity.y = 3f;          // shoved up hard: the floor carries the rider
            Carry(rider);
            Assert.AreEqual(0f, st.velocity.y, 1e-4f);

            time += 0.02;
            velocity.y = -3f;         // over a crest, falling away at 300 m/s²: the rider floats
            Carry(rider);
            Assert.Greater(st.velocity.y, 5f);
        }

        /// <summary>Come down onto the platform (said to move at 5 m/s) from above, at `velocity`.</summary>
        void LandOn(GameObject platform, PlatformRider rider, Vector3 velocity)
        {
            Spawn(new Vector3(-1f, 3f, 5f));
            st.velocity = velocity;
            for (int t = 0; t < 1500 && !st.onGround; t += Tick)
                Run(Tick);
            Assert.IsTrue(st.onGround, "landed");
            Assert.AreEqual(platform.GetComponent<Collider>(), pmove.GroundObject, "on the platform");
        }

        [Test]
        public void LandingOnAMovingPlatform_Sticks()
        {
            // Jumping onto the truck from the ground: you stand still on it, no sliding.
            GameObject platform = PlatformUnderfoot();
            var rider = new PlatformRider((t, p) => new Vector3(5f, 0f, 0f));
            LandOn(platform, rider, new Vector3(2f, 0f, 0f));
            rider.UpdateGround(ref st, pmove.GroundObject);
            Assert.AreEqual(Vector2.zero, new Vector2(st.velocity.x, st.velocity.z), "standing still on it");
            Assert.AreEqual(platform.transform, rider.Platform);
        }

        [Test]
        public void LandingWithoutSticking_TakesThePlatformsSpeedOff()
        {
            // Physically right: what's left is your speed relative to it, and friction stops you sliding.
            GameObject platform = PlatformUnderfoot();
            var rider = new PlatformRider((t, p) => new Vector3(5f, 0f, 0f)) { StickyLandings = false };
            LandOn(platform, rider, new Vector3(2f, 0f, 0f));
            float before = st.velocity.x;
            rider.UpdateGround(ref st, pmove.GroundObject);
            Assert.AreEqual(before - 5f, st.velocity.x, 0.01f, "moving relative to the platform");
        }

        [Test]
        public void JumpingOnATurningPlatform_TurnsWithIt()
        {
            // Walking on the platform at 0.5 m/s, jump; it turns 90° while we're in the air. We turn
            // with it, still walking the same way relative to it, and land back on it.
            GameObject platform = PlatformUnderfoot();
            PlatformRider rider = StandOn(platform);
            st.velocity = new Vector3(0.5f, 0f, 0f);
            Run(Tick, up: 127);
            var takeoff = new Vector3(st.velocity.x, 0f, st.velocity.z);
            Vector3 relative = Pos - new Vector3(0f, Pos.y, 5f);
            rider.UpdateGround(ref st, pmove.GroundObject);

            platform.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Carry(rider);
            Quaternion turn = Quaternion.Euler(0f, 90f, 0f);
            AssertNear(turn * relative, Pos - new Vector3(0f, Pos.y, 5f), 0.01f);
            AssertNear(turn * takeoff, new Vector3(st.velocity.x, 0f, st.velocity.z));

            for (int t = 0; t < 1500 && !st.onGround; t += Tick)
                Run(Tick);
            Assert.IsTrue(st.onGround, "landed");
            rider.UpdateGround(ref st, pmove.GroundObject);
            Assert.AreEqual(platform.transform, rider.Platform, "back on it");
        }

        [Test]
        public void LandingWithoutHittingTheFloor_DoesNotTurnTheFallSideways()
        {
            // A floor tilted 2° (a vehicle's deck rocking on its suspension), and the player falling
            // at 6.8 m/s with their feet already within the ground check's reach: they land on it
            // without the move hitting it. The fall must not become a run along the floor.
            Box(new Vector3(0f, -0.15f, 10f), new Vector3(6f, 0.5f, 6f), Quaternion.Euler(0f, 0f, 2f)); // top 0.1 m above the floor
            Physics.SyncTransforms();
            // 3 mm above the floor's highest point under the hull (its uphill edge, 0.46 m out at 2°).
            Spawn(new Vector3(0f, StandOriginY + 0.1f + 0.4572f * Mathf.Tan(2f * Mathf.Deg2Rad) + 0.003f, 10f));
            st.velocity = new Vector3(0f, -6.8f, 0f);
            Run(Tick);
            Assert.IsTrue(st.onGround, "landed");
            Run(Tick);
            Assert.Less(HorizontalSpeed, 0.5f, "no sideways fling");
        }

        [Test]
        public void StaticGround_IsNotAPlatform()
        {
            SpawnOnFloor();
            Settle();
            var rider = new PlatformRider();
            rider.UpdateGround(ref st, pmove.GroundObject);
            Assert.IsNull(rider.Platform);
        }

        static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 0.01f) =>
            Assert.Less(Vector3.Distance(expected, actual), tolerance, $"expected {expected}, got {actual}");

        // ------------------------------------------------------------------ stamina

        [Test]
        public void Sprint_DrainsInFourSeconds_ThenRuns()
        {
            SpawnOnFloor();
            Run(3800, forward: 127, buttons: CmdButtons.Sprint);
            Assert.Greater(st.stamina, 0f);
            Run(400, forward: 127, buttons: CmdButtons.Sprint);
            Assert.AreEqual(0f, st.stamina, 1e-4f);
            Run(500, forward: 127, buttons: CmdButtons.Sprint);
            Assert.AreEqual(6.5024f, HorizontalSpeed, SpeedTolerance);
        }

        [Test]
        public void Stamina_RegeneratesFasterAboveAQuarter()
        {
            SpawnOnFloor();
            st.stamina = 0f;
            Run(1000);
            Assert.AreEqual(0.025f, st.stamina, 0.001f);
            st.stamina = 0.5f;
            Run(1000);
            Assert.AreEqual(0.55f, st.stamina, 0.001f);
        }

        // ------------------------------------------------------------------ modifiers

        [Test]
        public void SpeedModifier_ScalesRunSpeed()
        {
            SpawnOnFloor();
            st.modifiers.speed = 0.5f;
            Run(1500, forward: 127);
            Assert.AreEqual(6.5024f * 0.5f, HorizontalSpeed, SpeedTolerance);
        }

        [Test]
        public void StaminaMax_ExtendsSprint()
        {
            SpawnOnFloor();
            st.modifiers.staminaMax = 2f;
            st.stamina = 2f;
            Run(7800, forward: 127, buttons: CmdButtons.Sprint);
            Assert.Greater(st.stamina, 0f);
            Assert.AreEqual(8.9408f, HorizontalSpeed, SpeedTolerance);
            Run(400, forward: 127, buttons: CmdButtons.Sprint);
            Assert.AreEqual(0f, st.stamina, 1e-4f);
        }

        [Test]
        public void StaminaCost_ScalesSprintAndJumpCost()
        {
            SpawnOnFloor();
            st.modifiers.staminaCost = 0.5f;
            Run(1000, forward: 127, buttons: CmdButtons.Sprint);
            Assert.AreEqual(1f - 0.125f, st.stamina, 0.001f);

            st.stamina = 1f;
            Run(Tick, up: 127);
            Assert.AreEqual(1f - 0.0625f, st.stamina, 0.001f);
        }

        [Test]
        public void StaminaRegen_ScalesRegeneration()
        {
            SpawnOnFloor();
            st.modifiers.staminaRegen = 2f;
            st.stamina = 0f;
            Run(1000);
            Assert.AreEqual(0.05f, st.stamina, 0.001f);
        }

        [Test]
        public void LoweringStaminaMax_ClampsStamina()
        {
            SpawnOnFloor();
            st.modifiers.staminaMax = 0.5f;
            Run(Tick);
            Assert.AreEqual(0.5f, st.stamina, 1e-4f);
            Assert.AreEqual(1f, st.StaminaFraction, 1e-4f);
        }

        // ------------------------------------------------------------------ steps and slopes

        [TestCase(0.2f, true)]
        [TestCase(0.4f, true)]
        [TestCase(0.45f, true)]
        [TestCase(0.5f, false)]
        public void Steps_UpToStepSize(float riser, bool climbable)
        {
            const float tread = 0.8f;
            for (int k = 0; k < 6; k++)
            {
                float h = (k + 1) * riser;
                Box(new Vector3(0, h / 2, 2.5f + k * tread + tread / 2), new Vector3(3.2f, h, tread));
            }
            float top = 6 * riser;
            float stairsEnd = 2.5f + 6 * tread;
            Box(new Vector3(0, top / 2, stairsEnd + 13f), new Vector3(3.2f, top, 26f)); // landing

            SpawnOnFloor();
            Run(2000, forward: 127);
            if (climbable)
                Assert.AreEqual(StandOriginY + top, Pos.y, 0.03f, "should have climbed the stairs");
            else
                Assert.AreEqual(StandOriginY, Pos.y, 0.03f, "should be blocked at the bottom");
        }

        [Test]
        public void StepUp_RaisesEventWithHeight()
        {
            Box(new Vector3(0, 0.2f, 2.5f), new Vector3(3.2f, 0.4f, 0.8f));
            SpawnOnFloor();
            Run(1000, forward: 127);
            Assert.IsTrue(events.Any(e => e.type == PmEventType.StepUp && Mathf.Abs(e.value - 0.4f) < 0.02f),
                string.Join(", ", events));
        }

        [TestCase(30f, true)]
        [TestCase(44f, true)]
        [TestCase(50f, false)]
        public void Slopes_WalkableBelow45Point6Degrees(float angle, bool walkable)
        {
            float rad = angle * Mathf.Deg2Rad;
            const float length = 13f, thick = 0.4f;
            var dir = new Vector3(0, Mathf.Sin(rad), Mathf.Cos(rad));
            var normal = new Vector3(0, Mathf.Cos(rad), -Mathf.Sin(rad));
            Vector3 start = new Vector3(0, 0, 1.6f);
            Box(start + dir * (length / 2) - normal * (thick / 2), new Vector3(6.4f, thick, length), Quaternion.Euler(-angle, 0, 0));

            SpawnOnFloor();
            Run(1500, forward: 127);
            if (walkable)
                Assert.Greater(Pos.y, StandOriginY + 2.5f, "should have walked up the ramp");
            else
                Assert.Less(Pos.y, StandOriginY + 1.5f, "should not get far up a too-steep ramp");
        }

        // ------------------------------------------------------------------ falling

        [TestCase(5f, PmEventType.FallShort, 0)]
        [TestCase(7f, PmEventType.FallDamage, 10)]
        [TestCase(8.5f, PmEventType.FallDamage, 15)]
        [TestCase(10f, PmEventType.FallDamage, 25)]
        [TestCase(11.5f, PmEventType.FallDamage, 50)]
        [TestCase(13f, PmEventType.FallDamage, 500)]
        public void FallDamage_MatchesEtThresholds(float drop, PmEventType expected, int damage)
        {
            Spawn(new Vector3(0, StandOriginY + drop, 0));
            Run(3000);
            Assert.IsTrue(st.onGround);
            PmEvent[] landings = events.Where(e => e.type == PmEventType.FallShort || e.type == PmEventType.FallDamage).ToArray();
            Assert.AreEqual(1, landings.Length, $"events: {string.Join(", ", events)}");
            Assert.AreEqual(expected, landings[0].type);
            Assert.AreEqual(damage, landings[0].param);
        }

        [Test]
        public void Landing_OnHugeFloor_DoesNotBounceOrDrift()
        {
            // PhysX sweep normals against very large boxes are tilted; this once caused a
            // small sideways shove and a hop on every landing.
            Box(new Vector3(0, 4.6f, 0), new Vector3(200f, 0.8f, 200f)); // top at y = 5
            Spawn(new Vector3(0, 5f + StandOriginY + 0.05f, 0));
            float maxUp = 0f;
            for (int i = 0; i < 100; i++)
            {
                Run(Tick);
                if (st.onGround)
                    maxUp = Mathf.Max(maxUp, st.velocity.y);
            }
            Assert.IsTrue(st.onGround);
            Assert.AreEqual(0f, Pos.x, 1e-4f);
            Assert.AreEqual(0f, Pos.z, 1e-4f);
            Assert.Less(maxUp, 0.01f);
        }

        [Test]
        public void FallDamage_CanBeDisabled()
        {
            cfg.enableFallDamage = false;
            Spawn(new Vector3(0, StandOriginY + 13f, 0));
            Run(3000);
            Assert.IsFalse(events.Any(e => e.type == PmEventType.FallDamage));
            Assert.IsTrue(events.Any(e => e.type == PmEventType.FallShort));
        }

        // ------------------------------------------------------------------ stances

        [Test]
        public void CrouchTunnel_BlocksStanding_AllowsCrouching()
        {
            BuildTunnel(1.4f);
            SpawnOnFloor();
            Run(2000, forward: 127);
            Assert.Less(Pos.z, 3.6f, "standing player should be blocked");

            SpawnOnFloor();
            Run(7000, forward: 127, up: -127);
            Assert.Greater(Pos.z, 10f, "crouched player should get through");
        }

        [Test]
        public void ProneTunnel_BlocksCrouching_AllowsProne()
        {
            BuildTunnel(1.1f);
            SpawnOnFloor();
            Run(2000, forward: 127, up: -127);
            Assert.Less(Pos.z, 3.6f, "crouched player should be blocked");

            SpawnOnFloor();
            Run(Tick, buttons: CmdButtons.Prone);
            Assert.IsTrue(st.IsProne);
            Run(9000, forward: 127);
            Assert.Greater(Pos.z, 10f, "prone player should get through");
        }

        [Test]
        public void CantStandUp_UnderLowCeiling()
        {
            BuildTunnel(1.4f);
            SpawnOnFloor(z: 7.5f);
            st.pmFlags |= PmFlags.Ducked; // spawn crouched, the roof is too low to stand
            Run(500, up: -127);
            Assert.IsTrue(st.IsDucked);
            Run(500);
            Assert.IsTrue(st.IsDucked, "should stay crouched under the roof");
        }

        [Test]
        public void Prone_DoubleTapForwardWhileCrouched_AndDelayBeforeStanding()
        {
            SpawnOnFloor();
            Run(200, up: -127);
            Run(Tick, up: -127, doubleTap: DoubleTap.Forward);
            Assert.IsTrue(st.IsProne);

            // Too soon to get up.
            Run(Tick, up: 127);
            Assert.IsTrue(st.IsProne);

            Run(800);
            Run(Tick, up: 127);
            Assert.IsFalse(st.IsProne);
        }

        void BuildTunnel(float innerHeight)
        {
            const float length = 13f, inner = 2.4f, wall = 0.8f, total = 3.2f;
            float zc = 3.8f + length / 2;
            Box(new Vector3(-inner / 2 - wall / 2, total / 2, zc), new Vector3(wall, total, length));
            Box(new Vector3(inner / 2 + wall / 2, total / 2, zc), new Vector3(wall, total, length));
            Box(new Vector3(0, (innerHeight + total) / 2, zc), new Vector3(inner, total - innerHeight, length));
        }

        // ------------------------------------------------------------------ lean

        [Test]
        public void Lean_ReachesMax_AndIsBlockedByWalls()
        {
            SpawnOnFloor();
            Run(400, buttons: CmdButtons.LeanLeft);
            Assert.AreEqual(-cfg.leanMax, st.leanf, 1e-4f);

            Run(400);
            Assert.AreEqual(0f, st.leanf, 1e-4f);

            Box(new Vector3(0.7112f, 1.6f, 0), new Vector3(0.4064f, 3.2f, 6.4f)); // wall 5 cm from the hull, on the right
            Run(400, buttons: CmdButtons.LeanRight);
            Assert.Greater(st.leanf, 0.127f);
            Assert.Less(st.leanf, 0.356f);
        }

        [Test]
        public void Lean_NotWhileMovingForward()
        {
            SpawnOnFloor();
            Run(400, forward: 127, buttons: CmdButtons.LeanLeft);
            Assert.AreEqual(0f, st.leanf, 1e-4f);
        }

        // ------------------------------------------------------------------ ladders and water

        [Test]
        public void LongTrace_DoesNotSkipAThinObstacle()
        {
            // A 5 cm plank with nothing behind it: a 1.2 m hull trace ends past it, in free space,
            // but must still stop at it (the ladder check traces this far ahead).
            Box(new Vector3(0, 1f, 1f), new Vector3(2f, 2f, 0.05f));
            var world = new UnityPmoveWorld(Physics.DefaultRaycastLayers);
            Vector3 start = Base + new Vector3(0, StandOriginY, 0);
            TraceResult tr = world.Trace(start, start + new Vector3(0, 0, 1.2f), cfg.mins, cfg.maxs);
            Assert.Less(tr.fraction, 1f, "should hit the plank");
            Assert.AreEqual(1f - 0.025f - cfg.maxs.z, tr.endPos.z - Base.z, 0.02f, "and stop against it");
        }

        [Test]
        public void Ladder_ClimbsWhenPushingForward()
        {
            Box(new Vector3(0, 6.4f, 3.2f), new Vector3(6.4f, 12.8f, 1.6f));
            var ladder = Box(new Vector3(0, 6.4f, 2.3f), new Vector3(1.6f, 12.8f, 0.2f));
            ladder.AddComponent<PmSurface>().flags = SurfaceFlags.Ladder;

            SpawnOnFloor();
            Run(2000, forward: 127);
            Assert.Greater(Pos.y, 5f);
            Assert.IsTrue(st.IsOnLadder);
        }

        [Test]
        public void Water_SwimsAndSinks()
        {
            var water = Box(new Vector3(0, 6.4f, 0), new Vector3(26f, 12.8f, 26f));
            water.GetComponent<BoxCollider>().isTrigger = true;
            water.AddComponent<LiquidVolume>();
            Physics.SyncTransforms();

            Spawn(new Vector3(0, 5f, 0));
            Run(100);
            Assert.AreEqual(3, st.waterLevel);

            float y = Pos.y;
            Run(1000);
            Assert.Less(Pos.y, y, "should sink slowly with no input");

            Run(2000, forward: 127);
            Assert.LessOrEqual(HorizontalSpeed, 8.128f * cfg.waterSwimScale + SpeedTolerance);
            Assert.Greater(HorizontalSpeed, 2.5f);
        }

        // ------------------------------------------------------------------ presets

        [Test]
        public void EtExactPreset_MatchesDefaults()
        {
            var config = new MoveConfig();
            MovePresets.Apply(config, MovePreset.ShooterMash);
            Assert.AreNotEqual(JsonUtility.ToJson(new MoveConfig()), JsonUtility.ToJson(config));
            MovePresets.Apply(config, MovePreset.EtExact);
            Assert.AreEqual(JsonUtility.ToJson(new MoveConfig()), JsonUtility.ToJson(config));
        }

        [Test]
        public void ShooterMashPreset_SpeedsAndJump()
        {
            MovePresets.Apply(cfg, MovePreset.ShooterMash);
            Spawn(new Vector3(0, 0.6f, 0));
            Run(1500, forward: 127);
            Assert.AreEqual(6.4f, HorizontalSpeed, SpeedTolerance);
            Run(1000, forward: 127, buttons: CmdButtons.Sprint);
            Assert.AreEqual(8.8f, HorizontalSpeed, SpeedTolerance);

            Run(1000);
            float start = Pos.y, apex = start;
            Run(Tick, up: 127);
            for (int i = 0; i < 100; i++)
            {
                Run(Tick);
                apex = Mathf.Max(apex, Pos.y);
            }
            Assert.AreEqual(6.8f * 6.8f / 40f, apex - start, 0.02f);
        }

        [TestCase(0.45f, true)]
        [TestCase(0.5f, true)]
        [TestCase(0.56f, false)]
        public void ShooterMashPreset_StepSize(float riser, bool climbable)
        {
            MovePresets.Apply(cfg, MovePreset.ShooterMash);
            Box(new Vector3(0, riser / 2, 2.5f + 6.5f), new Vector3(3.2f, riser, 13f));
            Spawn(new Vector3(0, 0.6f, 0));
            Run(1500, forward: 127);
            Assert.AreEqual(climbable ? 0.6f + riser : 0.6f, Pos.y, 0.03f);
        }

        [Test]
        public void Prone_CrawlsOntoAFloorUnderAnotherFloor()
        {
            // A floor (top at 0.2 m) with a second floor on 1 m pillars above it (underside at 1.0 m),
            // reaching out past the first. Crawling in from the ground means a 0.2 m step with no room
            // for a full step height.
            MovePresets.Apply(cfg, MovePreset.ShooterMash);
            Box(new Vector3(0, 0.1f, 10f), new Vector3(3f, 0.2f, 10f));   // z 5..15
            Box(new Vector3(0, 1.1f, 9f), new Vector3(3f, 0.2f, 12f));    // z 3..15
            Spawn(new Vector3(0, 0.6f, 0));
            Run(Tick, buttons: CmdButtons.Prone);
            Assert.IsTrue(st.IsProne);
            Run(9000, forward: 127);
            Assert.Greater(Pos.z, 10f, "prone player should crawl in");
            Assert.AreEqual(0.8f, Pos.y, 0.02f, "and be lying on the lower floor");
        }

        [Test]
        public void Prone_CrawlsOntoAFloorWithAnotherFloorStraightAbove()
        {
            // As built: a floor, 1 m pillars on it, a floor on those. Both floors have the same
            // footprint, so the ceiling starts right above the step and a full step hits its edge.
            MovePresets.Apply(cfg, MovePreset.ShooterMash);
            Box(new Vector3(0, 0.1f, 10f), new Vector3(3f, 0.2f, 10f));   // z 5..15, top at 0.2
            Box(new Vector3(0, 1.1f, 10f), new Vector3(3f, 0.2f, 10f));   // z 5..15, underside at 1.0
            Spawn(new Vector3(0, 0.6f, 0));
            Run(Tick, buttons: CmdButtons.Prone);
            Assert.IsTrue(st.IsProne);
            Run(9000, forward: 127);
            Assert.Greater(Pos.z, 10f, "prone player should crawl in");
            Assert.AreEqual(0.8f, Pos.y, 0.02f, "and be lying on the lower floor");
        }

        [Test]
        public void ShooterMashPreset_ProneFitsUnderAFloorOnOneMetrePillars()
        {
            // A floor on 1 m pillars has its top at 1 m and its underside at 0.8 m.
            MovePresets.Apply(cfg, MovePreset.ShooterMash);
            BuildTunnel(0.8f);
            Spawn(new Vector3(0, 0.6f, 0));
            Run(Tick, buttons: CmdButtons.Prone);
            Assert.IsTrue(st.IsProne);
            Run(9000, forward: 127);
            Assert.Greater(Pos.z, 10f, "prone player should get through");
        }

        [Test]
        public void EtProne_DoesNotFitUnderAFloorOnOneMetrePillars()
        {
            BuildTunnel(0.8f);
            SpawnOnFloor();
            Run(Tick, buttons: CmdButtons.Prone);
            Run(9000, forward: 127);
            Assert.Less(Pos.z, 3.6f);
        }

        [TestCase(5.9f, PmEventType.FallShort, 0)]
        [TestCase(6.2f, PmEventType.FallDamage, 10)]
        [TestCase(12.2f, PmEventType.FallDamage, 500)]
        public void ShooterMashPreset_FallDamage(float drop, PmEventType expected, int damage)
        {
            MovePresets.Apply(cfg, MovePreset.ShooterMash);
            Spawn(new Vector3(0, 0.6f + drop, 0));
            Run(3000);
            PmEvent landing = events.Single(e => e.type == PmEventType.FallShort || e.type == PmEventType.FallDamage);
            Assert.AreEqual(expected, landing.type);
            Assert.AreEqual(damage, landing.param);
        }

        // ------------------------------------------------------------------ determinism

        [Test]
        public void SameCommands_GiveIdenticalResults()
        {
            Box(new Vector3(0, 0.2f, 7.5f), new Vector3(6.4f, 0.4f, 1.6f));

            PlayerState RunScript()
            {
                time = 1000;
                SpawnOnFloor();
                Run(500, forward: 127, buttons: CmdButtons.Sprint);
                Run(Tick, forward: 127, up: 127);
                Run(800, forward: 127, right: 127);
                Run(600, forward: -127, up: -127);
                return st;
            }

            PlayerState a = RunScript();
            PlayerState b = RunScript();
            Assert.AreEqual(a.origin, b.origin);
            Assert.AreEqual(a.velocity, b.velocity);
            Assert.AreEqual(a.pmFlags, b.pmFlags);
        }
    }
}
