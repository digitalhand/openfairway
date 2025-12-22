using NUnit.Framework;

namespace PhysicsTests;

[TestFixture]
public class EnumsTests
{
    [Test]
    public void BallState_HasAllValues()
    {
        // Verify all ball states are defined
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.BallState), PhysicsEnums.BallState.Rest));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.BallState), PhysicsEnums.BallState.Flight));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.BallState), PhysicsEnums.BallState.Rollout));
    }

    [Test]
    public void BallState_HasCorrectValues()
    {
        // Verify enum numeric values match GDScript (0, 1, 2)
        Assert.That((int)PhysicsEnums.BallState.Rest, Is.EqualTo(0));
        Assert.That((int)PhysicsEnums.BallState.Flight, Is.EqualTo(1));
        Assert.That((int)PhysicsEnums.BallState.Rollout, Is.EqualTo(2));
    }

    [Test]
    public void Units_HasAllValues()
    {
        // Verify all unit types are defined
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.Units), PhysicsEnums.Units.Metric));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.Units), PhysicsEnums.Units.Imperial));
    }

    [Test]
    public void Units_HasCorrectValues()
    {
        // Verify enum numeric values match GDScript (0, 1)
        Assert.That((int)PhysicsEnums.Units.Metric, Is.EqualTo(0));
        Assert.That((int)PhysicsEnums.Units.Imperial, Is.EqualTo(1));
    }

    [Test]
    public void Surface_HasAllValues()
    {
        // Verify all surface types are defined
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.Fairway));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.FairwaySoft));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.Rough));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.Firm));
    }

    [Test]
    public void Surface_HasCorrectValues()
    {
        // Verify enum numeric values match GDScript (0, 1, 2, 3)
        Assert.That((int)PhysicsEnums.SurfaceType.Fairway, Is.EqualTo(0));
        Assert.That((int)PhysicsEnums.SurfaceType.FairwaySoft, Is.EqualTo(1));
        Assert.That((int)PhysicsEnums.SurfaceType.Rough, Is.EqualTo(2));
        Assert.That((int)PhysicsEnums.SurfaceType.Firm, Is.EqualTo(3));
    }
}
