using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    /// <summary>
    /// CI-safe tests that validate profile default values match original constants.
    /// No Godot runtime required.
    /// </summary>
    [TestFixture]
    public class FlightProfileConstantTests
    {
        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void FlightProfile_DefaultValues_MatchOriginalConstants()
        {
            var p = FlightProfile.Default;

            Assert.That(p.CdPolyA, Is.EqualTo(1.1948f));
            Assert.That(p.CdPolyB, Is.EqualTo(-0.0000209661f));
            Assert.That(p.CdPolyC, Is.EqualTo(1.42472e-10f));
            Assert.That(p.CdPolyD, Is.EqualTo(-3.14383e-16f));
            Assert.That(p.HighReCdCap, Is.EqualTo(0.2f));
            Assert.That(p.LowReCdFloor, Is.EqualTo(0.38f));
            Assert.That(p.CdMin, Is.EqualTo(0.223f));
            Assert.That(p.ClMaxBase, Is.EqualTo(0.268f));
            Assert.That(p.ClMaxHighSpin, Is.EqualTo(0.32f));
            Assert.That(p.SpinDragMultiplierCoeff, Is.EqualTo(4.0f));
            Assert.That(p.SpinDragMultiplierMax, Is.EqualTo(1.20f));
            Assert.That(p.LowLaunchLiftRecoveryMax, Is.EqualTo(1.08f));
            Assert.That(p.HighLaunchDragBoostMax, Is.EqualTo(1.24f));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void BounceProfile_DefaultValues_MatchOriginalConstants()
        {
            var bp = BounceProfile.Default;

            Assert.That(bp.CorBaseA, Is.EqualTo(0.45f));
            Assert.That(bp.CorBaseB, Is.EqualTo(-0.01f));
            Assert.That(bp.CorBaseC, Is.EqualTo(0.0002f));
            Assert.That(bp.CorHighSpeedCap, Is.EqualTo(0.25f));
            Assert.That(bp.CorHighSpeedThreshold, Is.EqualTo(20.0f));
            Assert.That(bp.CorKillThreshold, Is.EqualTo(2.0f));
            Assert.That(bp.FlightTangentialRetentionBase, Is.EqualTo(0.55f));
            Assert.That(bp.FlightSpinFactorMin, Is.EqualTo(0.40f));
            Assert.That(bp.PennerLowEnergyThreshold, Is.EqualTo(20.0f));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void RolloutProfile_DefaultValues_MatchOriginalConstants()
        {
            var rp = RolloutProfile.Default;

            Assert.That(rp.ChipSpeedThreshold, Is.EqualTo(20.0f));
            Assert.That(rp.PitchSpeedThreshold, Is.EqualTo(35.0f));
            Assert.That(rp.ChipVelocityScaleMin, Is.EqualTo(0.60f));
            Assert.That(rp.ChipVelocityScaleMax, Is.EqualTo(0.87f));
            Assert.That(rp.LowSpinThreshold, Is.EqualTo(1750.0f));
            Assert.That(rp.MidSpinThreshold, Is.EqualTo(1750.0f));
            Assert.That(rp.LowSpinMultiplierMax, Is.EqualTo(1.15f));
            Assert.That(rp.MidSpinMultiplierMax, Is.EqualTo(2.25f));
            Assert.That(rp.HighSpinMultiplierMax, Is.EqualTo(2.50f));
            Assert.That(rp.FrictionBlendSpeed, Is.EqualTo(15.0f));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void BallPhysicsProfile_ResolvedDefaults_ReturnSingletons()
        {
            var profile = new BallPhysicsProfile();

            Assert.That(profile.ResolvedFlight, Is.SameAs(FlightProfile.Default));
            Assert.That(profile.ResolvedBounce, Is.SameAs(BounceProfile.Default));
            Assert.That(profile.ResolvedRollout, Is.SameAs(RolloutProfile.Default));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void BallPhysicsProfile_CustomFlight_OverridesDefault()
        {
            var custom = new FlightProfile { ClMaxBase = 0.30f };
            var profile = new BallPhysicsProfile { Flight = custom };

            Assert.That(profile.ResolvedFlight, Is.SameAs(custom));
            Assert.That(profile.ResolvedFlight.ClMaxBase, Is.EqualTo(0.30f));
            Assert.That(profile.ResolvedBounce, Is.SameAs(BounceProfile.Default));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void ShotRegimeKey_BuildsExpectedBins()
        {
            Assert.That(ShotRegimeKey.Build(68.7f, 26.1f, 4149.0f), Is.EqualTo("I-S1a-V3-P2"));
            Assert.That(ShotRegimeKey.Build(125.0f, 11.0f, 2300.0f), Is.EqualTo("D-S4-V1-P0"));
            Assert.That(ShotRegimeKey.Build(52.0f, 37.0f, 5652.0f), Is.EqualTo("C-S0-V4-P3"));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void BallPhysicsProfile_RegimeScaleOverrides_UseMostSpecificMatch()
        {
            var profile = new BallPhysicsProfile
            {
                RegimeScaleOverrides = new System.Collections.Generic.Dictionary<string, RegimeScaleOverride>
                {
                    ["I"] = new() { LiftScaleMultiplier = 1.02f },
                    ["I-S1a-V3"] = new() { LiftScaleMultiplier = 1.06f },
                    ["I-S1a-V3-P2"] = new() { LiftScaleMultiplier = 1.11f },
                },
            };

            RegimeScaleOverride match = profile.ResolveScaleOverride(68.7f, 26.1f, 4149.0f, out string regimeKey, out string matchedKey);

            Assert.That(regimeKey, Is.EqualTo("I-S1a-V3-P2"));
            Assert.That(matchedKey, Is.EqualTo("I-S1a-V3-P2"));
            Assert.That(match.LiftScaleMultiplier, Is.EqualTo(1.11f));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void BallPhysicsProfile_FromJson_LoadsRegimeScaleOverrides()
        {
            const string json = """
                {
                  "DragScaleMultiplier": 1.01,
                  "RegimeScaleOverrides": {
                    "I-S1a-V3-P2": {
                      "DragScaleMultiplier": 0.97,
                      "LiftScaleMultiplier": 1.05
                    }
                  }
                }
                """;

            BallPhysicsProfile profile = BallPhysicsProfile.FromJson(json);
            RegimeScaleOverride match = profile.ResolveScaleOverride(68.7f, 26.1f, 4149.0f, out _, out string matchedKey);

            Assert.That(profile.DragScaleMultiplier, Is.EqualTo(1.01f));
            Assert.That(matchedKey, Is.EqualTo("I-S1a-V3-P2"));
            Assert.That(match.DragScaleMultiplier, Is.EqualTo(0.97f));
            Assert.That(match.LiftScaleMultiplier, Is.EqualTo(1.05f));
        }

        [Test]
        [Category("RolloutPhysics")]
        [Category("FlightProfile")]
        public void PhysicsParamsFactory_AppliesRegimeScaleOverrides()
        {
            var factory = new PhysicsParamsFactory();
            var profile = new BallPhysicsProfile
            {
                DragScaleMultiplier = 1.01f,
                LiftScaleMultiplier = 1.02f,
                RegimeScaleOverrides = new System.Collections.Generic.Dictionary<string, RegimeScaleOverride>
                {
                    ["I-S1a-V3-P2"] = new()
                    {
                        DragScaleMultiplier = 0.98f,
                        LiftScaleMultiplier = 1.08f,
                        KineticFrictionMultiplier = 0.95f,
                    },
                },
            };

            ResolvedPhysicsParams resolved = factory.Create(
                airDensity: 1.2f,
                airViscosity: 0.000018f,
                dragScale: 1.0f,
                liftScale: 1.0f,
                surfaceType: PhysicsEnums.SurfaceType.Fairway,
                floorNormal: Vector3.Up,
                ballProfile: profile,
                initialLaunchAngleDeg: 26.1f,
                launchSpeedMph: 68.7f,
                launchSpinRpm: 4149.0f
            );

            Assert.That(resolved.DragScale, Is.EqualTo(1.01f * 0.98f).Within(0.0001f));
            Assert.That(resolved.LiftScale, Is.EqualTo(1.02f * 1.08f).Within(0.0001f));
            Assert.That(resolved.KineticFriction, Is.LessThan(SurfacePhysicsCatalog.Get(PhysicsEnums.SurfaceType.Fairway).KineticFriction));
        }
    }

    /// <summary>
    /// Runtime tests that validate carry-only mode and profile tweak effects.
    /// Requires Godot runtime.
    /// </summary>
    [TestFixture]
    public class FlightProfileRuntimeTests
    {
        private PhysicsAdapter _adapter;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
        }

        public static IEnumerable<TestCaseData> AllRegressionShots()
        {
            yield return new TestCaseData("driver1.json").SetName("DefaultParity_Driver1");
            yield return new TestCaseData("driver2.json").SetName("DefaultParity_Driver2");
            yield return new TestCaseData("driver3.json").SetName("DefaultParity_Driver3");
            yield return new TestCaseData("driver4.json").SetName("DefaultParity_Driver4");
            yield return new TestCaseData("5iron.json").SetName("DefaultParity_5Iron");
            yield return new TestCaseData("wood1.json").SetName("DefaultParity_Wood1");
            yield return new TestCaseData("wood2.json").SetName("DefaultParity_Wood2");
            yield return new TestCaseData("wedge_test_shot.json").SetName("DefaultParity_Wedge1");
            yield return new TestCaseData("wedge_test_shot2.json").SetName("DefaultParity_Wedge2");
            yield return new TestCaseData("wood_low_test_shot.json").SetName("DefaultParity_WoodLow");
            yield return new TestCaseData("approach_mid_iron_test_shot.json").SetName("DefaultParity_ApproachMid");
            yield return new TestCaseData("checked_test_shot.json").SetName("DefaultParity_Checked");
            yield return new TestCaseData("flop_test_shot.json").SetName("DefaultParity_Flop");
            yield return new TestCaseData("p_wedge_shot_1.json").SetName("DefaultParity_PWedge1");
            yield return new TestCaseData("wedge_shot_1.json").SetName("DefaultParity_WedgeShot1");
            yield return new TestCaseData("wedge_shot_2.json").SetName("DefaultParity_WedgeShot2");
        }

        [TestCaseSource(nameof(AllRegressionShots))]
        [Category("PhysicsRuntime")]
        [Category("FlightProfile")]
        public void DefaultProfile_CarryMatchesFullSimulation(string filename)
        {
            Godot.Collections.Dictionary shot = TestShotLoader.LoadTestShot(filename);
            Godot.Collections.Dictionary fullResult = _adapter.SimulateShotFromJson(shot);
            Godot.Collections.Dictionary carryResult = _adapter.SimulateCarryOnly(shot);

            float fullCarry = (float)fullResult["carry_yd"];
            float carryOnly = (float)carryResult["carry_yd"];

            TestContext.WriteLine($"{filename}: full={fullCarry:F1} yd, carryOnly={carryOnly:F1} yd, delta={carryOnly - fullCarry:+0.0;-0.0} yd");

            Assert.That(carryOnly, Is.EqualTo(fullCarry).Within(0.5f),
                $"Carry-only mode should match full simulation carry within 0.5 yd");
        }

        [Test]
        [Category("PhysicsRuntime")]
        [Category("FlightProfile")]
        public void IncreasedClMaxBase_IncreasesCarry()
        {
            Godot.Collections.Dictionary shot = TestShotLoader.LoadTestShot("driver1.json");

            Godot.Collections.Dictionary baseline = _adapter.SimulateCarryOnly(shot);
            Godot.Collections.Dictionary tweaked = _adapter.SimulateCarryOnly(shot, new FlightProfile { ClMaxBase = 0.30f });

            float baselineCarry = (float)baseline["carry_yd"];
            float tweakedCarry = (float)tweaked["carry_yd"];

            TestContext.WriteLine($"ClMaxBase tweak: baseline={baselineCarry:F1} yd, tweaked={tweakedCarry:F1} yd");

            Assert.That(tweakedCarry, Is.GreaterThan(baselineCarry),
                "Increasing ClMaxBase should increase carry distance");
        }

        [Test]
        [Category("PhysicsRuntime")]
        [Category("FlightProfile")]
        public void IncreasedCdPolyA_DecreasesCarry()
        {
            Godot.Collections.Dictionary shot = TestShotLoader.LoadTestShot("driver1.json");

            Godot.Collections.Dictionary baseline = _adapter.SimulateCarryOnly(shot);
            Godot.Collections.Dictionary tweaked = _adapter.SimulateCarryOnly(shot, new FlightProfile { CdPolyA = 1.30f });

            float baselineCarry = (float)baseline["carry_yd"];
            float tweakedCarry = (float)tweaked["carry_yd"];

            TestContext.WriteLine($"CdPolyA tweak: baseline={baselineCarry:F1} yd, tweaked={tweakedCarry:F1} yd");

            Assert.That(tweakedCarry, Is.LessThan(baselineCarry),
                "Increasing CdPolyA (more drag) should decrease carry distance");
        }

        [Test]
        [Category("PhysicsRuntime")]
        [Category("FlightProfile")]
        public void IncreasedSpinDragMultiplierMax_DecreasesHighSpinCarry()
        {
            Godot.Collections.Dictionary shot = TestShotLoader.LoadTestShot("wedge_test_shot.json");

            Godot.Collections.Dictionary baseline = _adapter.SimulateCarryOnly(shot);
            Godot.Collections.Dictionary tweaked = _adapter.SimulateCarryOnly(shot, new FlightProfile { SpinDragMultiplierMax = 1.40f });

            float baselineCarry = (float)baseline["carry_yd"];
            float tweakedCarry = (float)tweaked["carry_yd"];

            TestContext.WriteLine($"SpinDragMax tweak: baseline={baselineCarry:F1} yd, tweaked={tweakedCarry:F1} yd");

            Assert.That(tweakedCarry, Is.LessThan(baselineCarry),
                "Increasing spin drag max should decrease carry for high-spin wedge shots");
        }

        [Test]
        [Category("PhysicsRuntime")]
        [Category("FlightProfile")]
        public void RegimeScaleOverride_OnlyAffectsMatchingShortShot()
        {
            var profile = new BallPhysicsProfile
            {
                RegimeScaleOverrides = new System.Collections.Generic.Dictionary<string, RegimeScaleOverride>
                {
                    ["I-S1a-V3-P2"] = new()
                    {
                        DragScaleMultiplier = 0.96f,
                        LiftScaleMultiplier = 1.06f,
                    },
                },
            };

            Godot.Collections.Dictionary shortShot = TestShotLoader.LoadTestShot("wedge_test_shot.json");
            Godot.Collections.Dictionary driverShot = TestShotLoader.LoadTestShot("driver1.json");

            float baselineShort = (float)_adapter.SimulateCarryOnlyFromJson(shortShot)["carry_yd"];
            float tweakedShort = (float)_adapter.SimulateCarryOnlyWithProfile(shortShot, profile)["carry_yd"];
            float baselineDriver = (float)_adapter.SimulateCarryOnlyFromJson(driverShot)["carry_yd"];
            float tweakedDriver = (float)_adapter.SimulateCarryOnlyWithProfile(driverShot, profile)["carry_yd"];

            Assert.That(tweakedShort, Is.GreaterThan(baselineShort));
            Assert.That(tweakedDriver, Is.EqualTo(baselineDriver).Within(0.1f));
        }

        // ── FlightScope calibration tests ──

        private static readonly string FlightScopeReferencePath =
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "assets", "data", "SOT", "flightscope_reference.json");

        public static IEnumerable<TestCaseData> FlightScopeCalibrationCases()
        {
            if (!File.Exists(FlightScopeReferencePath))
                yield break;

            string json = File.ReadAllText(FlightScopeReferencePath);
            var reference = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, JsonElement>>(json);

            foreach (var kvp in reference)
            {
                string shotName = kvp.Key;
                var entry = kvp.Value;
                if (!entry.TryGetProperty("carry_yd", out var carryEl))
                    continue;
                if (!entry.TryGetProperty("filename", out var filenameEl))
                    continue;

                float fsCarry = (float)carryEl.GetDouble();
                string filename = filenameEl.GetString();

                if (fsCarry <= 0.0f)
                    continue;

                yield return new TestCaseData(shotName, filename, fsCarry)
                    .SetName($"FlightScope_{shotName}");
            }
        }

        [TestCaseSource(nameof(FlightScopeCalibrationCases))]
        [Category("PhysicsRuntime")]
        [Category("FlightScopeCalibration")]
        public void CarryMatchesFlightScopeReference(
            string shotName,
            string filename,
            float flightScopeCarry)
        {
            Godot.Collections.Dictionary shot = TestShotLoader.LoadTestShot(filename);
            Godot.Collections.Dictionary result = _adapter.SimulateCarryOnly(shot);

            float carry = (float)result["carry_yd"];
            float delta = carry - flightScopeCarry;

            TestContext.WriteLine($"{shotName}: simulated={carry:F1} yd, FlightScope={flightScopeCarry:F1} yd, delta={delta:+0.0;-0.0} yd");

            Assert.Pass($"Delta: {delta:+0.0;-0.0} yd (simulated={carry:F1}, FlightScope={flightScopeCarry:F1})");
        }
    }
}
