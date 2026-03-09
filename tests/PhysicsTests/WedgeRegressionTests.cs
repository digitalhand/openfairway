using System.IO;
using System.Text.Json;
using Godot;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    [TestFixture]
    public class WedgeRegressionTests
    {
        private const float TargetCarryYd = 96.0f;
        private const float CarryToleranceYd = 1.5f;

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
        public void WedgeShot_Regression_CarryMatchesTarget()
        {
            Dictionary shot = LoadTestShot("wedge_test_shot.json");
            Dictionary result = _adapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("initial_spin_ratio"), Is.True, "Result missing initial_spin_ratio diagnostic");

            float carry = (float)result["carry_yd"];
            float spinRatio = (float)result["initial_spin_ratio"];

            TestContext.WriteLine("Wedge Shot Diagnostics:");
            TestContext.WriteLine($"  Carry: {carry:F1} yd (target {TargetCarryYd:F1})");
            TestContext.WriteLine($"  Initial Spin Ratio: {spinRatio:F3}");
            TestContext.WriteLine($"  Shared DT: {BallPhysics.SIMULATION_DT:F6} s ({BallPhysics.SIMULATION_HZ:F0} Hz)");

            Assert.That(carry, Is.EqualTo(TargetCarryYd).Within(CarryToleranceYd));
        }

        [Test]
        [Category("RolloutPhysics")]
        public void SharedIntegrator_UsesExpectedDefaultRate()
        {
            Assert.That(BallPhysics.SIMULATION_HZ, Is.EqualTo(120.0f).Within(0.001f));
            Assert.That(BallPhysics.SIMULATION_DT, Is.EqualTo(1.0f / 120.0f).Within(0.000001f));
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
