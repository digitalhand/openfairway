using System;
using Godot;

internal readonly struct FlightAerodynamicsSample
{
    public float Speed { get; }
    public float SpinRatio { get; }
    public float Reynolds { get; }
    public float SpinDragMultiplier { get; }
    public float LowLaunchLiftScale { get; }
    public float DragCoefficient { get; }
    public float LiftCoefficient { get; }

    public bool HasAerodynamics => Speed >= FlightAerodynamicsModel.MinAerodynamicSpeed;

    public FlightAerodynamicsSample(
        float speed,
        float spinRatio,
        float reynolds,
        float spinDragMultiplier,
        float lowLaunchLiftScale,
        float dragCoefficient,
        float liftCoefficient)
    {
        Speed = speed;
        SpinRatio = spinRatio;
        Reynolds = reynolds;
        SpinDragMultiplier = spinDragMultiplier;
        LowLaunchLiftScale = lowLaunchLiftScale;
        DragCoefficient = dragCoefficient;
        LiftCoefficient = liftCoefficient;
    }
}

internal static class FlightAerodynamicsModel
{
    internal const float MinAerodynamicSpeed = 0.5f;
    internal const float ClMax = 0.268f;
    internal const float CdMin = 0.22f;
    internal const float SpinDragMultiplierCoeff = 4.0f;
    internal const float SpinDragMultiplierMax = 1.20f;
    internal const float SpinDragMultiplierHighSpinMax = 1.06f;
    internal const float SpinDragMultiplierUltraHighSpinMax = 1.21f;
    internal const float LowLaunchLiftRecoveryMax = 1.08f;

    private const float HighReStart = 75000.0f;
    private const float HighReMidSpinGain = 12.5f;
    private const float HighReSpinGain = 16.0f;
    private const float HighReGainReductionStart = 0.10f;
    private const float HighReGainReductionEnd = 0.18f;
    private const float HighReGainRecoveryStart = 0.30f;
    private const float HighReGainRecoveryEnd = 0.45f;
    private const float HighSpinClAttenuationStart = 0.50f;
    private const float HighSpinClAttenuationEnd = 0.80f;
    private const float HighSpinClAttenuationMax = 0.09f;
    private const float UltraHighSpinClAttenuationStart = 0.58f;
    private const float UltraHighSpinClAttenuationEnd = 0.86f;
    private const float UltraHighSpinClAttenuationMax = 0.10f;

    private const float HighSpinDragSrStart = 0.30f;
    private const float HighSpinDragSrEnd = 0.48f;
    private const float HighSpinDragReliefReFullMax = 90000.0f;
    private const float HighSpinDragReliefReZero = 105000.0f;
    private const float UltraHighSpinDragSrStart = 0.60f;
    private const float UltraHighSpinDragSrEnd = 0.80f;

    private const float LowLaunchVlaFullDeg = 6.5f;
    private const float LowLaunchVlaZeroDeg = 9.5f;
    private const float LowLaunchReStart = 125000.0f;
    private const float LowLaunchReEnd = 135000.0f;
    private const float LowLaunchSpinRatioFull = 0.18f;
    private const float LowLaunchSpinRatioMax = 0.22f;

    internal static FlightAerodynamicsSample Sample(
        Vector3 velocity,
        Vector3 omega,
        float airDensity,
        float airViscosity,
        float dragScale,
        float liftScale,
        float initialLaunchAngleDeg)
    {
        float speed = velocity.Length();
        if (speed < MinAerodynamicSpeed)
        {
            return new FlightAerodynamicsSample(
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                1.0f,
                0.0f,
                0.0f
            );
        }

        float spinRatio = omega.Length() * BallPhysics.RADIUS / speed;
        float reynolds = airDensity * speed * BallPhysics.RADIUS * 2.0f / airViscosity;
        float spinDragMultiplier = GetSpinDragMultiplier(spinRatio, reynolds);
        float lowLaunchLiftScale = GetLowLaunchLiftScale(initialLaunchAngleDeg, spinRatio, reynolds);
        float dragCoefficient = GetCd(reynolds) * spinDragMultiplier * dragScale;
        float liftCoefficient = GetCl(reynolds, spinRatio) * liftScale * lowLaunchLiftScale;

        return new FlightAerodynamicsSample(
            speed,
            spinRatio,
            reynolds,
            spinDragMultiplier,
            lowLaunchLiftScale,
            dragCoefficient,
            liftCoefficient
        );
    }

    internal static float GetCd(float reynolds)
    {
        if (reynolds > 200000.0f)
            return 0.2f;

        if (reynolds >= 50000.0f)
        {
            return 1.1948f - 0.0000209661f * reynolds +
                1.42472e-10f * reynolds * reynolds -
                3.14383e-16f * reynolds * reynolds * reynolds;
        }

        const float LowReCdFloor = 0.38f;
        const float LowReBlendStart = 30000.0f;
        const float CdAt50k = 0.4632f;

        if (reynolds <= LowReBlendStart)
            return LowReCdFloor;

        float t = SmoothStep01((reynolds - LowReBlendStart) / (50000.0f - LowReBlendStart));
        return Mathf.Lerp(LowReCdFloor, CdAt50k, t);
    }

    internal static float GetCl(float reynolds, float spinRatio)
    {
        float spin = Mathf.Max(0.0f, spinRatio);
        if (spin <= 0.0f)
            return 0.0f;

        if (reynolds < 50000.0f)
        {
            if (reynolds <= 30000.0f)
                return 0.0f;

            float lowReT = SmoothStep01((reynolds - 30000.0f) / 20000.0f);
            float clAt50k = Mathf.Clamp(ClRe50k(spin), 0.0f, ClMax);
            return clAt50k * lowReT;
        }

        if (reynolds >= HighReStart)
            return ApplyHighSpinLiftAttenuation(spin, Mathf.Clamp(ClHighRe(spin), 0.0f, ClMax));

        int[] reValues = { 50000, 60000, 65000, 70000, 75000 };
        int reHighIndex = reValues.Length - 1;

        for (int i = 0; i < reValues.Length; i++)
        {
            if (reynolds <= reValues[i])
            {
                reHighIndex = i;
                break;
            }
        }

        int reLowIndex = Mathf.Max(reHighIndex - 1, 0);
        Func<float, float>[] clFunctions =
        {
            ClRe50k,
            ClRe60k,
            ClRe65k,
            ClRe70k,
            ClHighRe
        };

        float clLow = Mathf.Max(0.0f, clFunctions[reLowIndex](spin));
        float clHigh = Mathf.Max(0.0f, clFunctions[reHighIndex](spin));
        float reLow = reValues[reLowIndex];
        float reHigh = reValues[reHighIndex];
        float weight = reHigh != reLow ? (reynolds - reLow) / (reHigh - reLow) : 0.0f;

        float clInterpolated = Mathf.Lerp(clLow, clHigh, weight);
        return Mathf.Clamp(clInterpolated, 0.0f, ClMax);
    }

    internal static float GetSpinDragMultiplier(float spinRatio)
    {
        return GetSpinDragMultiplier(spinRatio, HighSpinDragReliefReFullMax);
    }

    internal static float GetSpinDragMultiplier(float spinRatio, float reynolds)
    {
        if (spinRatio <= 0.0f)
            return 1.0f;

        float highSpinWeight = SmoothStep01(
            (spinRatio - HighSpinDragSrStart) / (HighSpinDragSrEnd - HighSpinDragSrStart)
        );
        float reReliefWeight = 1.0f - SmoothStep01(
            (reynolds - HighSpinDragReliefReFullMax) / (HighSpinDragReliefReZero - HighSpinDragReliefReFullMax)
        );
        float reliefWeight = highSpinWeight * reReliefWeight;
        float effectiveCap = Mathf.Lerp(SpinDragMultiplierMax, SpinDragMultiplierHighSpinMax, reliefWeight);

        float ultraHighSpinWeight = SmoothStep01(
            (spinRatio - UltraHighSpinDragSrStart) / (UltraHighSpinDragSrEnd - UltraHighSpinDragSrStart)
        );
        effectiveCap = Mathf.Lerp(effectiveCap, SpinDragMultiplierUltraHighSpinMax, ultraHighSpinWeight);

        float spinDragMultiplier = 1.0f + SpinDragMultiplierCoeff * spinRatio * spinRatio;
        return Mathf.Min(spinDragMultiplier, effectiveCap);
    }

    internal static float GetLowLaunchLiftScale(float initialLaunchAngleDeg, float spinRatio, float reynolds)
    {
        float launchFactor = SmoothStep01(
            (LowLaunchVlaZeroDeg - initialLaunchAngleDeg) / (LowLaunchVlaZeroDeg - LowLaunchVlaFullDeg)
        );
        if (launchFactor <= 0.0f)
            return 1.0f;

        float reFactor = SmoothStep01((reynolds - LowLaunchReStart) / (LowLaunchReEnd - LowLaunchReStart));
        if (reFactor <= 0.0f)
            return 1.0f;

        float spinFactor = 1.0f - SmoothStep01(
            (spinRatio - LowLaunchSpinRatioFull) / (LowLaunchSpinRatioMax - LowLaunchSpinRatioFull)
        );
        if (spinFactor <= 0.0f)
            return 1.0f;

        float recoveryWeight = launchFactor * reFactor * spinFactor;
        return Mathf.Lerp(1.0f, LowLaunchLiftRecoveryMax, recoveryWeight);
    }

    private static float ClRe50k(float spinRatio)
    {
        return 0.0472121f + 2.84795f * spinRatio - 23.4342f * spinRatio * spinRatio +
            45.4849f * spinRatio * spinRatio * spinRatio;
    }

    private static float ClRe60k(float spinRatio)
    {
        return 0.320524f - 4.7032f * spinRatio + 14.0613f * spinRatio * spinRatio;
    }

    private static float ClRe65k(float spinRatio)
    {
        return 0.266667f - 4.0f * spinRatio + 13.3333f * spinRatio * spinRatio;
    }

    private static float ClRe70k(float spinRatio)
    {
        return 0.0496189f + 0.00211396f * spinRatio + 2.34201f * spinRatio * spinRatio;
    }

    private static float ClHighRe(float spinRatio)
    {
        float effectiveGain = GetHighReSpinGain(spinRatio);
        return ClMax * spinRatio * effectiveGain / (1.0f + spinRatio * effectiveGain);
    }

    private static float ApplyHighSpinLiftAttenuation(float spinRatio, float cl)
    {
        float attenuationT = SmoothStep01(
            (spinRatio - HighSpinClAttenuationStart) / (HighSpinClAttenuationEnd - HighSpinClAttenuationStart)
        );
        float attenuation = 1.0f - HighSpinClAttenuationMax * attenuationT;

        float ultraHighSpinAttenuationT = SmoothStep01(
            (spinRatio - UltraHighSpinClAttenuationStart) /
            (UltraHighSpinClAttenuationEnd - UltraHighSpinClAttenuationStart)
        );
        float ultraHighSpinAttenuation = 1.0f - UltraHighSpinClAttenuationMax * ultraHighSpinAttenuationT;

        return cl * attenuation * ultraHighSpinAttenuation;
    }

    private static float GetHighReSpinGain(float spinRatio)
    {
        if (spinRatio <= HighReGainReductionStart)
            return HighReSpinGain;

        if (spinRatio < HighReGainReductionEnd)
        {
            float reductionT = SmoothStep01(
                (spinRatio - HighReGainReductionStart) / (HighReGainReductionEnd - HighReGainReductionStart)
            );
            return Mathf.Lerp(HighReSpinGain, HighReMidSpinGain, reductionT);
        }

        if (spinRatio <= HighReGainRecoveryStart)
            return HighReMidSpinGain;

        if (spinRatio < HighReGainRecoveryEnd)
        {
            float recoveryT = SmoothStep01(
                (spinRatio - HighReGainRecoveryStart) / (HighReGainRecoveryEnd - HighReGainRecoveryStart)
            );
            return Mathf.Lerp(HighReMidSpinGain, HighReSpinGain, recoveryT);
        }

        return HighReSpinGain;
    }

    private static float SmoothStep01(float t)
    {
        float clampedT = Mathf.Clamp(t, 0.0f, 1.0f);
        return clampedT * clampedT * (3.0f - 2.0f * clampedT);
    }
}
