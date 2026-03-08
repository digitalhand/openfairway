using System.IO;
using System.Text.Json;
using Godot;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class MidIronRegressionTests
    {
        private const float TargetCarryYd = 126.0f;
        private const float TargetApexFt = 70.0f;
        private const float CarryToleranceYd = 3.0f;
        private const float ApexToleranceFt = 4.0f;
        private const float DriveCarryMinYd = 220.0f;

        private PhysicsAdapter _adapter;
        private string _dataPath;

        [SetUp]
        public void Setup()
        {
            _adapter = new PhysicsAdapter();
            _dataPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "assets", "data");
        }

        [Test]
        [Category("RolloutPhysics")]
        public void ApproachMidIron_Regression_CarryAndApex()
        {
            Dictionary shot = LoadTestShot("approach_mid_iron_test_shot.json");
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("apex_ft"), Is.True, "Result missing apex_ft diagnostic");
            Assert.That(result.ContainsKey("initial_cd"), Is.True, "Result missing initial_cd diagnostic");
            Assert.That(result.ContainsKey("initial_cl"), Is.True, "Result missing initial_cl diagnostic");

            float carry = (float)result["carry_yd"];
            float apex = (float)result["apex_ft"];

            TestContext.WriteLine("Approach Mid Iron Diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (target {TargetCarryYd:F1})");
            TestContext.WriteLine($"  Apex: {apex:F1} ft (target {TargetApexFt:F1})");
            TestContext.WriteLine($"  Hang Time: {(float)result["hang_time_s"]:F2} s");
            TestContext.WriteLine($"  Initial Re: {(float)result["initial_re"]:F0}");
            TestContext.WriteLine($"  Initial Spin Ratio: {(float)result["initial_spin_ratio"]:F3}");
            TestContext.WriteLine($"  Initial Cd: {(float)result["initial_cd"]:F3}");
            TestContext.WriteLine($"  Initial Cl: {(float)result["initial_cl"]:F3}");
            TestContext.WriteLine($"  Peak Cl: {(float)result["peak_cl"]:F3}");

            Assert.That(carry, Is.EqualTo(TargetCarryYd).Within(CarryToleranceYd));
            Assert.That(apex, Is.EqualTo(TargetApexFt).Within(ApexToleranceFt));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void DriveShot_Regression_CarryAboveThreshold()
        {
            Dictionary shot = LoadTestShot("drive_test_shot.json");
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("initial_cl"), Is.True, "Result missing initial_cl diagnostic");

            float carry = (float)result["carry_yd"];
            float initialCl = (float)result["initial_cl"];

            TestContext.WriteLine("Drive Shot Diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (min {DriveCarryMinYd:F1})");
            TestContext.WriteLine($"  Initial Cl: {initialCl:F3}");

            Assert.That(carry, Is.GreaterThan(DriveCarryMinYd));
            Assert.That(initialCl, Is.GreaterThan(0.10f), "Driver low-spin lift collapsed unexpectedly");
        }

        private Dictionary LoadTestShot(string filename)
        {
            string path = Path.Combine(_dataPath, filename);
            if (!File.Exists(path))
                Assert.Fail($"Test shot file not found: {path}");

            string json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json, options);

            var dict = new Dictionary();
            foreach (var kvp in data)
                dict[kvp.Key] = Variant.From(ConvertJsonValue(kvp.Value));

            return dict;
        }

        private object ConvertJsonValue(object value)
        {
            if (value is JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (element.TryGetDouble(out double d))
                            return d;
                        break;
                    case JsonValueKind.String:
                        return element.GetString();
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.False:
                        return false;
                    case JsonValueKind.Object:
                        var dict = new Dictionary();
                        foreach (var prop in element.EnumerateObject())
                            dict[prop.Name] = Variant.From(ConvertJsonValue(prop.Value));
                        return dict;
                }
            }

            return value;
        }
    }
}
