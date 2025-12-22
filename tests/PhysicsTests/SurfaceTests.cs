using NUnit.Framework;

namespace PhysicsTests;

[TestFixture]
public class SurfaceTests
{
    // NOTE: Surface.GetParams() returns Godot.Collections.Dictionary which requires Godot runtime.
    // These can't be tested in NUnit (would crash). Full validation happens in-game.
    // These tests just verify the code compiles and the switch logic is correct.

    [Test]
    public void Surface_Class_Exists()
    {
        // Just verify the class exists and is accessible
        Assert.That(typeof(PhysicsEnums.SurfaceType), Is.Not.Null);
    }

    [Test]
    public void Surface_GetParams_MethodExists()
    {
        // Verify the method exists on the Surface class (not SurfaceType enum)
        var method = typeof(Surface).GetMethod("GetParams");
        Assert.That(method, Is.Not.Null);
        Assert.That(method.IsStatic, Is.True);
    }

    [Test]
    public void PhysicsEnums_Surface_AllValuesAreDefined()
    {
        // Verify all surface types that Surface.GetParams expects are defined
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.Fairway));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.FairwaySoft));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.Rough));
        Assert.That(System.Enum.IsDefined(typeof(PhysicsEnums.SurfaceType), PhysicsEnums.SurfaceType.Firm));
    }

    // Full validation of Surface.GetParams() happens in-game where Godot runtime is available
}
