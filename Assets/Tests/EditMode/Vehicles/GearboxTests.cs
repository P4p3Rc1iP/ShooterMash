using NUnit.Framework;

namespace ShooterMashup.Vehicles.Tests
{
    /// <summary>
    /// The drivetrain (EngineRevs) on its own: drive force through the gears, the limiter,
    /// automatic and manual shifting, and gearbox parts changing the ratios.
    /// </summary>
    public class GearboxTests
    {
        const float Power = 320000f, Pull = 14000f, Dt = 0.02f;

        /// <summary>A V8 (6 gears to 60 m/s) with its own gearbox, or a gearbox part's ratios.</summary>
        static EngineRevs V8(int gears = 6, float length = 1f)
        {
            var revs = new EngineRevs();
            revs.Configure(750f, 6400f, 8, 6, 60f, gears, length, 0.3f);
            return revs;
        }

        /// <summary>Runs the gearbox at a steady speed on full throttle for a while.</summary>
        static void Drive(EngineRevs revs, float speed, float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt)
                revs.Update(Dt, speed, speed, 1f, true, false, true);
        }

        [Test]
        public void FirstGear_PullsWithItsMostAtLowSpeed()
        {
            EngineRevs revs = V8();
            Assert.AreEqual(1, revs.Gear);
            Assert.AreEqual(Pull, revs.DriveForce(1f, Power, Pull), 1f, "pulling away it's limited by the pull");
        }

        [Test]
        public void PastTheRedline_TheLimiterCutsThePower()
        {
            EngineRevs revs = V8();
            revs.Manual = true;
            float redline = revs.GearSpeed(1);
            Assert.Greater(revs.DriveForce(redline * 0.9f, Power, Pull), 0f);
            Assert.AreEqual(0f, revs.DriveForce(redline * 1.01f, Power, Pull));
        }

        [Test]
        public void Automatic_ShiftsUpAtTheRedline_AndDownWhenSlowing()
        {
            EngineRevs revs = V8();
            Drive(revs, revs.GearSpeed(1) * 0.98f, 0.1f);
            Assert.AreEqual(2, revs.Gear, "up at the redline");
            Assert.IsTrue(revs.Shifting);
            Assert.AreEqual(0f, revs.DriveForce(revs.GearSpeed(1), Power, Pull), "no drive while it shifts");
            Drive(revs, revs.GearSpeed(1) * 0.5f, 1f);
            Assert.AreEqual(1, revs.Gear, "down when the revs drop");
            Drive(revs, 0f, 0.1f);
            Assert.AreEqual(1, revs.Gear);
        }

        [Test]
        public void Manual_StaysInGear_UntilTheDriverShifts()
        {
            EngineRevs revs = V8();
            revs.Manual = true;
            Drive(revs, revs.GearSpeed(1) * 1.05f, 1f);
            Assert.AreEqual(1, revs.Gear);
            Assert.IsTrue(revs.Limiter);
            revs.ShiftUp();
            Assert.AreEqual(2, revs.Gear);
            revs.ShiftDown();
            revs.ShiftDown();
            Assert.AreEqual(1, revs.Gear, "no lower than first");
        }

        [Test]
        public void TallGear_LugsFromLowSpeed()
        {
            EngineRevs revs = V8();
            revs.Manual = true;
            for (int i = 0; i < 5; i++)
                revs.ShiftUp();
            Drive(revs, 2f, 1f);                     // let the shifts finish
            Assert.AreEqual(6, revs.Gear);
            Assert.Less(revs.DriveForce(2f, Power, Pull), Pull * 0.2f, "sixth from a crawl has little to give");
        }

        [Test]
        public void GearboxParts_TradeTopSpeedForPull()
        {
            EngineRevs own = V8(), close = V8(6, 0.8f), tall = V8(6, 1.2f);
            Assert.AreEqual(60f, own.TopSpeed, 0.01f);
            Assert.AreEqual(48f, close.TopSpeed, 0.01f);
            Assert.AreEqual(72f, tall.TopSpeed, 0.01f);
            Assert.Greater(close.DriveForce(1f, Power, Pull), own.DriveForce(1f, Power, Pull), "shorter gears pull harder");
            Assert.Less(tall.DriveForce(1f, Power, Pull), own.DriveForce(1f, Power, Pull));
        }

        [Test]
        public void PowerCurve_PeaksNearTheRedline()
        {
            Assert.AreEqual(1f, EngineRevs.PowerCurve(0.9f), 1e-4f);
            Assert.Less(EngineRevs.PowerCurve(1f), 1f);
            Assert.Less(EngineRevs.PowerCurve(0.3f), 0.6f);
        }
    }
}
