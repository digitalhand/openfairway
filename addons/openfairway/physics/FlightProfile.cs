/// <summary>
/// Centralizes all flight aerodynamics constants into a single swappable profile.
/// Use init properties for calibration: new FlightProfile { ClMaxBase = 0.28f }
/// </summary>
public sealed class FlightProfile
{
    // --- Drag curve (Cd polynomial) ---
    public float CdPolyA { get; init; } = 1.1948f;
    public float CdPolyB { get; init; } = -0.0000209661f;
    public float CdPolyC { get; init; } = 1.42472e-10f;
    public float CdPolyD { get; init; } = -3.14383e-16f;
    public float HighReCdCap { get; init; } = 0.2f;
    public float LowReCdFloor { get; init; } = 0.38f;
    public float LowReBlendStart { get; init; } = 30000.0f;
    public float CdAt50k { get; init; } = 0.4632f;
    public float CdMin { get; init; } = 0.22f;

    // --- Lift caps ---
    public float ClMaxBase { get; init; } = 0.268f;
    public float ClMaxHighSpin { get; init; } = 0.32f;
    public float ClMaxSrTransitionStart { get; init; } = 0.35f;
    public float ClMaxSrTransitionEnd { get; init; } = 0.50f;

    // --- Spin drag ---
    public float SpinDragMultiplierCoeff { get; init; } = 4.0f;
    public float SpinDragMultiplierMax { get; init; } = 1.20f;
    public float SpinDragMultiplierHighSpinMax { get; init; } = 1.03f;
    public float SpinDragMultiplierUltraHighSpinMax { get; init; } = 1.21f;

    // Spin drag relief window thresholds
    public float HighSpinDragSrStart { get; init; } = 0.30f;
    public float HighSpinDragSrEnd { get; init; } = 0.48f;
    public float HighSpinDragReliefReFullMax { get; init; } = 90000.0f;
    public float HighSpinDragReliefReZero { get; init; } = 105000.0f;
    public float UltraHighSpinDragSrStart { get; init; } = 0.57f;
    public float UltraHighSpinDragSrEnd { get; init; } = 0.77f;

    // --- High-Re lift ---
    public float HighReStart { get; init; } = 75000.0f;
    public float HighReMidSpinGain { get; init; } = 13.8f;
    public float HighReSpinGain { get; init; } = 16.0f;
    public float HighReGainReductionStart { get; init; } = 0.10f;
    public float HighReGainReductionEnd { get; init; } = 0.18f;
    public float HighReGainRecoveryStart { get; init; } = 0.26f;
    public float HighReGainRecoveryEnd { get; init; } = 0.40f;

    // --- High-Re lift attenuation ---
    public float HighSpinClAttenuationStart { get; init; } = 0.45f;
    public float HighSpinClAttenuationEnd { get; init; } = 0.55f;
    public float HighSpinClAttenuationMax { get; init; } = 0.09f;
    public float UltraHighSpinClAttenuationStart { get; init; } = 0.58f;
    public float UltraHighSpinClAttenuationEnd { get; init; } = 0.85f;
    public float UltraHighSpinClAttenuationMax { get; init; } = 0.10f;

    // --- Low-Re lift attenuation ---
    public float LowReHighSpinClAttenuationMax { get; init; } = 0.10f;
    public float LowReUltraHighSpinClAttenuationMax { get; init; } = 0.06f;

    // --- Low-launch lift recovery ---
    public float LowLaunchLiftRecoveryMax { get; init; } = 1.08f;
    public float LowLaunchVlaFullDeg { get; init; } = 6.5f;
    public float LowLaunchVlaZeroDeg { get; init; } = 9.5f;
    public float LowLaunchReStart { get; init; } = 85000.0f;
    public float LowLaunchReEnd { get; init; } = 110000.0f;
    public float LowLaunchSpinRatioFull { get; init; } = 0.18f;
    public float LowLaunchSpinRatioMax { get; init; } = 0.22f;

    // --- High-launch drag boost ---
    public float HighLaunchDragBoostMax { get; init; } = 1.18f;
    public float HighLaunchDragVlaStartDeg { get; init; } = 33.0f;
    public float HighLaunchDragVlaFullDeg { get; init; } = 40.0f;
    public float HighLaunchDragSrStart { get; init; } = 0.50f;
    public float HighLaunchDragSrEnd { get; init; } = 0.70f;

    // --- Metadata ---
    public string Name { get; init; } = "Default";
    public string Version { get; init; } = "1.0";

    public static FlightProfile Default { get; } = new();
}
