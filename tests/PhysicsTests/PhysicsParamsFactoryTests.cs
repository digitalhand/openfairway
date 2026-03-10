using Godot;
using NUnit.Framework;

namespace PhysicsTests;

[TestFixture]
public class PhysicsParamsFactoryTests
{
    [Test]
    public void Create_UsesCatalogValuesWithDefaultProfile()
    {
        var factory = new PhysicsParamsFactory();

        ResolvedPhysicsParams resolved = factory.Create(
            airDensity: 1.225f,
            airViscosity: 0.0000181f,
            dragScale: 1.0f,
            liftScale: 1.0f,
            surfaceType: PhysicsEnums.SurfaceType.Green,
            floorNormal: Vector3.Up,
            rolloutImpactSpin: 3200.0f
        );

        Assert.That(resolved.SurfaceType, Is.EqualTo(PhysicsEnums.SurfaceType.Green));
        Assert.That(resolved.KineticFriction, Is.EqualTo(0.58f).Within(0.0001f));
        Assert.That(resolved.RollingFriction, Is.EqualTo(0.028f).Within(0.0001f));
        Assert.That(resolved.GrassViscosity, Is.EqualTo(0.0009f).Within(0.0001f));
        Assert.That(resolved.CriticalAngle, Is.EqualTo(0.36f).Within(0.0001f));
        Assert.That(resolved.SpinbackResponseScale, Is.EqualTo(1.12f).Within(0.0001f));
        Assert.That(resolved.SpinbackThetaBoostMax, Is.EqualTo(0.12f).Within(0.0001f));
        Assert.That(resolved.RolloutImpactSpin, Is.EqualTo(3200.0f).Within(0.0001f));
    }

    [Test]
    public void Create_AppliesBallProfileMultipliers()
    {
        var factory = new PhysicsParamsFactory();
        var profile = new BallPhysicsProfile
        {
            DragScaleMultiplier = 0.95f,
            LiftScaleMultiplier = 1.10f,
            KineticFrictionMultiplier = 1.20f,
            RollingFrictionMultiplier = 0.80f,
            GrassViscosityMultiplier = 1.50f,
            CriticalAngleOffsetRadians = 0.02f,
            SpinbackThetaBoostMultiplier = 1.25f
        };

        ResolvedPhysicsParams resolved = factory.Create(
            airDensity: 1.225f,
            airViscosity: 0.0000181f,
            dragScale: 1.10f,
            liftScale: 0.90f,
            surfaceType: PhysicsEnums.SurfaceType.Green,
            floorNormal: Vector3.Up,
            rolloutImpactSpin: 0.0f,
            ballProfile: profile
        );

        Assert.That(resolved.DragScale, Is.EqualTo(1.045f).Within(0.0001f));
        Assert.That(resolved.LiftScale, Is.EqualTo(0.99f).Within(0.0001f));
        Assert.That(resolved.KineticFriction, Is.EqualTo(0.696f).Within(0.0001f));
        Assert.That(resolved.RollingFriction, Is.EqualTo(0.0224f).Within(0.0001f));
        Assert.That(resolved.GrassViscosity, Is.EqualTo(0.00135f).Within(0.0001f));
        Assert.That(resolved.CriticalAngle, Is.EqualTo(0.38f).Within(0.0001f));
        Assert.That(resolved.SpinbackResponseScale, Is.EqualTo(1.12f).Within(0.0001f));
        Assert.That(resolved.SpinbackThetaBoostMax, Is.EqualTo(0.15f).Within(0.0001f));
    }
}
