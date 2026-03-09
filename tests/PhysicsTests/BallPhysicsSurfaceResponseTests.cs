using Godot;
using NUnit.Framework;

namespace PhysicsTests;

[TestFixture]
public class BallPhysicsSurfaceResponseTests
{
    [Test]
    public void LoggedFlopImpact_GreenProducesMoreReverseTangentialSpeedThanFairway()
    {
        var physics = new BallPhysics();
        var factory = new PhysicsParamsFactory();
        Vector3 impactVelocity = new(9.794386f, -18.043974f, -1.2941796f);
        Vector3 impactOmega = new(74.5761f, -7.7262826f, 520.71954f);
        Vector3 normal = Vector3.Up;

        PhysicsParams fairwayParams = factory.Create(
            airDensity: 1.1883f,
            airViscosity: 0.00001852198f,
            dragScale: 1.0f,
            liftScale: 1.0f,
            surfaceType: PhysicsEnums.SurfaceType.Fairway,
            floorNormal: normal,
            rolloutImpactSpin: 5024.0f
        ).ToPhysicsParams();

        PhysicsParams greenParams = factory.Create(
            airDensity: 1.1883f,
            airViscosity: 0.00001852198f,
            dragScale: 1.0f,
            liftScale: 1.0f,
            surfaceType: PhysicsEnums.SurfaceType.Green,
            floorNormal: normal,
            rolloutImpactSpin: 5024.0f
        ).ToPhysicsParams();

        BounceResult fairwayBounce = physics.CalculateBounce(
            impactVelocity,
            impactOmega,
            normal,
            PhysicsEnums.BallState.Flight,
            fairwayParams
        );

        BounceResult greenBounce = physics.CalculateBounce(
            impactVelocity,
            impactOmega,
            normal,
            PhysicsEnums.BallState.Flight,
            greenParams
        );

        float fairwayTangentOut = GetSignedTangentialSpeed(impactVelocity, fairwayBounce.NewVelocity, normal);
        float greenTangentOut = GetSignedTangentialSpeed(impactVelocity, greenBounce.NewVelocity, normal);

        Assert.That(greenParams.SpinbackResponseScale, Is.GreaterThan(fairwayParams.SpinbackResponseScale));
        Assert.That(greenTangentOut, Is.LessThan(-0.25f), "Green should retain meaningful reverse tangential speed for the logged flop impact.");
        Assert.That(greenTangentOut, Is.LessThan(fairwayTangentOut - 0.25f), "Green should respond more aggressively than fairway for the same landing.");
    }

    private static float GetSignedTangentialSpeed(Vector3 incomingVelocity, Vector3 outgoingVelocity, Vector3 normal)
    {
        Vector3 incomingTangent = incomingVelocity - normal * incomingVelocity.Dot(normal);
        Vector3 outgoingTangent = outgoingVelocity - normal * outgoingVelocity.Dot(normal);
        float outgoingMagnitude = outgoingTangent.Length();

        if (incomingTangent.Length() < 0.01f || outgoingMagnitude < 0.01f)
            return 0.0f;

        float directionDot = incomingTangent.Normalized().Dot(outgoingTangent.Normalized());
        return directionDot < 0.0f ? -outgoingMagnitude : outgoingMagnitude;
    }
}
