using Godot;
using NUnit.Framework;

namespace PhysicsTests;

[TestFixture]
public class AerodynamicsTests
{
    private const float TOLERANCE = 1e-6f;

    private readonly Aerodynamics _aero = new();

    [Test]
    public void GetAirDensity_AtSeaLevel_Imperial()
    {
        // Sea level, 59°F (standard conditions)
        float density = _aero.GetAirDensity(0.0f, 59.0f, PhysicsEnums.Units.Imperial);

        // Should be close to 1.225 kg/m³ at sea level, 15°C (59°F)
        Assert.That(density, Is.InRange(1.2f, 1.25f));
    }

    [Test]
    public void GetAirDensity_AtSeaLevel_Metric()
    {
        // Sea level, 15°C (standard conditions)
        float density = _aero.GetAirDensity(0.0f, 15.0f, PhysicsEnums.Units.Metric);

        // Should be close to 1.225 kg/m³
        Assert.That(density, Is.InRange(1.2f, 1.25f));
    }

    [Test]
    public void GetAirDensity_AtAltitude_LowerDensity()
    {
        float seaLevelDensity = _aero.GetAirDensity(0.0f, 59.0f, PhysicsEnums.Units.Imperial);
        float altitudeDensity = _aero.GetAirDensity(5000.0f, 59.0f, PhysicsEnums.Units.Imperial);

        // Density decreases with altitude
        Assert.That(altitudeDensity, Is.LessThan(seaLevelDensity));
    }

    [Test]
    public void GetDynamicViscosity_StandardConditions()
    {
        // At standard temperature (15°C / 59°F)
        float viscosity = _aero.GetDynamicViscosity(59.0f, PhysicsEnums.Units.Imperial);

        // Should be close to 1.81e-5 kg/(m*s)
        Assert.That(viscosity, Is.InRange(1.7e-5f, 1.9e-5f));
    }

    [Test]
    public void GetCd_LowReynolds_Returns0Point5()
    {
        float cd = _aero.GetCd(30000.0f);
        Assert.That(cd, Is.EqualTo(0.5f));
    }

    [Test]
    public void GetCd_HighReynolds_Returns0Point2()
    {
        float cd = _aero.GetCd(250000.0f);
        Assert.That(cd, Is.EqualTo(0.2f));
    }

    [Test]
    public void GetCd_MidRange_ReturnsPolynomialValue()
    {
        float cd = _aero.GetCd(100000.0f);

        // Should be between 0.2 and 0.5
        Assert.That(cd, Is.InRange(0.2f, 0.5f));
    }

    [Test]
    public void GetCd_TransitionPoint_50000()
    {
        float cd1 = _aero.GetCd(49999.0f);
        float cd2 = _aero.GetCd(50001.0f);

        // cd1 should be 0.5 (low Re), cd2 should be polynomial
        Assert.That(cd1, Is.EqualTo(0.5f));
        Assert.That(cd2, Is.LessThan(0.5f));
    }

    [Test]
    public void GetCl_LowReynolds_ReturnsZero()
    {
        float cl = _aero.GetCl(30000.0f, 0.3f);
        Assert.That(cl, Is.EqualTo(0.0f).Within(TOLERANCE));
    }

    [Test]
    public void GetCl_HighReynolds_ConstrainedModel()
    {
        float cl = _aero.GetCl(100000.0f, 0.2f);

        Assert.That(cl, Is.InRange(0.10f, 0.25f));
    }

    [Test]
    public void GetCl_ClampedToMax()
    {
        // Very high spin ratio should be clamped to CL_MAX
        float cl = _aero.GetCl(100000.0f, 1.0f);
        Assert.That(cl, Is.LessThanOrEqualTo(Aerodynamics.CL_MAX));
    }

    [Test]
    public void GetCl_InterpolationRange()
    {
        // Test in interpolation range (50k to 75k Re)
        float cl1 = _aero.GetCl(60000.0f, 0.2f);
        float cl2 = _aero.GetCl(70000.0f, 0.2f);

        // Both should be reasonable lift coefficients
        Assert.That(cl1, Is.InRange(0.0f, Aerodynamics.CL_MAX));
        Assert.That(cl2, Is.InRange(0.0f, Aerodynamics.CL_MAX));
    }

    [Test]
    public void GetCl_ZeroSpinRatio()
    {
        float cl = _aero.GetCl(100000.0f, 0.0f);

        Assert.That(cl, Is.EqualTo(0.0f).Within(TOLERANCE));
    }

    [Test]
    public void GetCl_IncreasingWithSpinRatio()
    {
        // For same Reynolds, higher spin ratio should give more lift
        float cl1 = _aero.GetCl(100000.0f, 0.1f);
        float cl2 = _aero.GetCl(100000.0f, 0.2f);

        Assert.That(cl2, Is.GreaterThan(cl1));
    }

    [Test]
    public void GetCl_NearSeventyFiveK_IsContinuous()
    {
        float clLow = _aero.GetCl(74999.0f, 0.25f);
        float clHigh = _aero.GetCl(75001.0f, 0.25f);

        Assert.That(Mathf.Abs(clHigh - clLow), Is.LessThan(0.03f));
    }

    [Test]
    public void CdMin_PhysicalFloor_IsReasonable()
    {
        Assert.That(Aerodynamics.CD_MIN, Is.InRange(0.20f, 0.25f));
    }

    [Test]
    public void GetCd_ExactPolynomialValue()
    {
        // Test exact polynomial calculation at Re = 100000
        float Re = 100000.0f;
        float expected = 1.1948f - 0.0000209661f * Re + 1.42472e-10f * Re * Re - 3.14383e-16f * Re * Re * Re;
        float actual = _aero.GetCd(Re);

        Assert.That(actual, Is.EqualTo(expected).Within(TOLERANCE));
    }

    [Test]
    public void GetAirDensity_MetricImperialConsistency()
    {
        // 59°F = 15°C, should give same density
        float densityF = _aero.GetAirDensity(0.0f, 59.0f, PhysicsEnums.Units.Imperial);
        float densityC = _aero.GetAirDensity(0.0f, 15.0f, PhysicsEnums.Units.Metric);

        Assert.That(densityC, Is.EqualTo(densityF).Within(0.001f));
    }

    [Test]
    public void GetAirDensity_AltitudeConversion()
    {
        // 3280.84 feet = 1000 meters
        float densityFeet = _aero.GetAirDensity(3280.84f, 59.0f, PhysicsEnums.Units.Imperial);
        float densityMeters = _aero.GetAirDensity(1000.0f, 15.0f, PhysicsEnums.Units.Metric);

        // Should be very close (within 1% due to rounding)
        Assert.That(densityMeters, Is.EqualTo(densityFeet).Within(densityFeet * 0.01f));
    }
}
