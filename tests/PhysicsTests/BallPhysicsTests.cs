using NUnit.Framework;
using Godot;

namespace PhysicsTests;

[TestFixture]
public class BallPhysicsTests
{
    private const float TOLERANCE = 1e-6f;

    [Test]
    public void Constants_HaveCorrectValues()
    {
        // Verify physical constants match GDScript exactly
        Assert.That(BallPhysics.MASS, Is.EqualTo(0.04592623f).Within(TOLERANCE));
        Assert.That(BallPhysics.RADIUS, Is.EqualTo(0.021335f).Within(TOLERANCE));
        Assert.That(BallPhysics.SPIN_DECAY_TAU, Is.EqualTo(3.0f).Within(TOLERANCE));
    }

    [Test]
    public void CrossSection_CalculatedCorrectly()
    {
        float expected = Mathf.Pi * BallPhysics.RADIUS * BallPhysics.RADIUS;
        Assert.That(BallPhysics.CROSS_SECTION, Is.EqualTo(expected).Within(TOLERANCE));
    }

    [Test]
    public void MomentOfInertia_CalculatedCorrectly()
    {
        float expected = 0.4f * BallPhysics.MASS * BallPhysics.RADIUS * BallPhysics.RADIUS;
        Assert.That(BallPhysics.MOMENT_OF_INERTIA, Is.EqualTo(expected).Within(TOLERANCE));
    }

    [Test]
    public void PhysicsParams_ClassExists()
    {
        // PhysicsParams derives from RefCounted (Godot runtime), can't instantiate in NUnit
        var type = typeof(BallPhysics).GetNestedType("PhysicsParams");
        Assert.That(type, Is.Not.Null);
        Assert.That(type.IsClass, Is.True);
    }

    [Test]
    public void BounceResult_ClassExists()
    {
        // BounceResult derives from RefCounted (Godot runtime), can't instantiate in NUnit
        var type = typeof(BallPhysics).GetNestedType("BounceResult");
        Assert.That(type, Is.Not.Null);
        Assert.That(type.IsClass, Is.True);
    }

    [Test]
    public void GetCoefficientOfRestitution_HighSpeed_Returns0Point25()
    {
        float cor = BallPhysics.GetCoefficientOfRestitution(25.0f);
        Assert.That(cor, Is.EqualTo(0.25f));
    }

    [Test]
    public void GetCoefficientOfRestitution_LowSpeed_Returns0()
    {
        float cor = BallPhysics.GetCoefficientOfRestitution(1.0f);
        Assert.That(cor, Is.EqualTo(0.0f));
    }

    [Test]
    public void GetCoefficientOfRestitution_MidSpeed_ReturnsPolynomial()
    {
        float cor = BallPhysics.GetCoefficientOfRestitution(10.0f);
        float expected = 0.45f - 0.0100f * 10.0f + 0.0002f * 10.0f * 10.0f;
        Assert.That(cor, Is.EqualTo(expected).Within(TOLERANCE));
    }

    [Test]
    public void CalculateForces_MethodExists()
    {
        var method = typeof(BallPhysics).GetMethod("CalculateForces");
        Assert.That(method, Is.Not.Null);
        Assert.That(method.IsStatic, Is.True);
    }

    [Test]
    public void CalculateTorques_MethodExists()
    {
        var method = typeof(BallPhysics).GetMethod("CalculateTorques");
        Assert.That(method, Is.Not.Null);
        Assert.That(method.IsStatic, Is.True);
    }

    [Test]
    public void CalculateBounce_MethodExists()
    {
        var method = typeof(BallPhysics).GetMethod("CalculateBounce");
        Assert.That(method, Is.Not.Null);
        Assert.That(method.IsStatic, Is.True);
    }

    // NOTE: Full physics validation (forces, torques, bounces) requires Godot runtime
    // and will be tested in-game where we can compare C# vs GDScript results
}
