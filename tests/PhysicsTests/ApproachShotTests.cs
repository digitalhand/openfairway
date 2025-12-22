using NUnit.Framework;
using Godot;
using Godot.Collections;

namespace OpenShotGolf.Tests
{
    [TestFixture]
    public class ApproachShotTests
    {
        private const string SHOT_PATH = "res://assets/data/approach_test_shot.json";

        private Dictionary LoadJson(string path)
        {
            Assert.That(FileAccess.FileExists(path), Is.True, $"Missing JSON file: {path}");

            string text = FileAccess.GetFileAsString(path);
            var json = new Json();
            var error = json.Parse(text);

            Assert.That(error, Is.EqualTo(Error.Ok), "JSON parsing failed");

            var data = json.Data;
            Assert.That(data.VariantType, Is.EqualTo(Variant.Type.Dictionary), "JSON must parse to a Dictionary");

            return (Dictionary)data;
        }

        [Test]
        public void TestApproachShotCarryAndTotal()
        {
            var shot = LoadJson(SHOT_PATH);

            var result = PhysicsAdapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("total_yd"), Is.True, "Result missing total_yd");

            float carry = (float)result["carry_yd"];
            float total = (float)result["total_yd"];

            // Replace these with your expected ranges once you see real output.
            // Start wide, then tighten.
            Assert.That(carry, Is.InRange(10.0f, 200.0f), "Carry out of expected range");
            Assert.That(total, Is.InRange(carry, 260.0f), "Total out of expected range (should be >= carry)");
        }

        [Test]
        public void TestWoodLowShotBounce()
        {
            const string woodLowPath = "res://assets/data/wood_low_test_shot.json";
            var shot = LoadJson(woodLowPath);

            var result = PhysicsAdapter.SimulateShotFromJson(shot);

            Assert.That(result.ContainsKey("carry_yd"), Is.True, "Result missing carry_yd");
            Assert.That(result.ContainsKey("total_yd"), Is.True, "Result missing total_yd");

            float carry = (float)result["carry_yd"];
            float total = (float)result["total_yd"];

            // Print results for debugging
            TestContext.WriteLine($"Wood Low Shot Results:");
            TestContext.WriteLine($"  Carry: {carry:F1} yards");
            TestContext.WriteLine($"  Total: {total:F1} yards");
            TestContext.WriteLine($"  Rollout: {total - carry:F1} yards");

            // GSPro expected: carry ~113 yards, total ~175 yards
            // With bounce fix, we should see significant improvement from previous 95.3/113.7
            Assert.That(carry, Is.InRange(90.0f, 120.0f), "Carry should be ~95-115 yards");
            Assert.That(total, Is.GreaterThan(carry + 10.0f), "Total should have at least 10 yards rollout");
        }
    }
}
