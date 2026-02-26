using Godot;
using Godot.Collections;

public readonly struct ShotDisplaySnapshot
{
    private const string UnknownValue = "---";

    public ShotDisplaySnapshot(
        string distance,
        string carry,
        string offline,
        string apex,
        float vla,
        float hla,
        string speed,
        string backSpin,
        string sideSpin,
        string totalSpin,
        string spinAxis)
    {
        Distance = NormalizeText(distance);
        Carry = NormalizeText(carry);
        Offline = NormalizeText(offline);
        Apex = NormalizeText(apex);
        VLA = vla;
        HLA = hla;
        Speed = NormalizeText(speed);
        BackSpin = NormalizeText(backSpin);
        SideSpin = NormalizeText(sideSpin);
        TotalSpin = NormalizeText(totalSpin);
        SpinAxis = NormalizeText(spinAxis);
    }

    public string Distance { get; }
    public string Carry { get; }
    public string Offline { get; }
    public string Apex { get; }
    public float VLA { get; }
    public float HLA { get; }
    public string Speed { get; }
    public string BackSpin { get; }
    public string SideSpin { get; }
    public string TotalSpin { get; }
    public string SpinAxis { get; }

    public static ShotDisplaySnapshot Empty => new(
        distance: UnknownValue,
        carry: UnknownValue,
        offline: UnknownValue,
        apex: UnknownValue,
        vla: 0.0f,
        hla: 0.0f,
        speed: UnknownValue,
        backSpin: UnknownValue,
        sideSpin: UnknownValue,
        totalSpin: UnknownValue,
        spinAxis: UnknownValue
    );

    public Dictionary ToDictionary()
    {
        return new Dictionary
        {
            { "Distance", Distance },
            { "Carry", Carry },
            { "Offline", Offline },
            { "Apex", Apex },
            { "VLA", VLA },
            { "HLA", HLA },
            { "Speed", Speed },
            { "BackSpin", BackSpin },
            { "SideSpin", SideSpin },
            { "TotalSpin", TotalSpin },
            { "SpinAxis", SpinAxis }
        };
    }

    public static ShotDisplaySnapshot FromDictionary(Dictionary data)
    {
        if (data == null || data.Count == 0)
            return Empty;

        return new ShotDisplaySnapshot(
            distance: GetString(data, "Distance", UnknownValue),
            carry: GetString(data, "Carry", UnknownValue),
            offline: GetString(data, "Offline", UnknownValue),
            apex: GetString(data, "Apex", UnknownValue),
            vla: GetFloat(data, "VLA", 0.0f),
            hla: GetFloat(data, "HLA", 0.0f),
            speed: GetString(data, "Speed", UnknownValue),
            backSpin: GetString(data, "BackSpin", UnknownValue),
            sideSpin: GetString(data, "SideSpin", UnknownValue),
            totalSpin: GetString(data, "TotalSpin", UnknownValue),
            spinAxis: GetString(data, "SpinAxis", UnknownValue)
        );
    }

    private static string NormalizeText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? UnknownValue : value;
    }

    private static string GetString(Dictionary data, string key, string fallback)
    {
        if (!data.ContainsKey(key))
            return fallback;

        Variant value = data[key];
        if (value.VariantType == Variant.Type.String)
            return NormalizeText((string)value);

        string fromVariant = value.ToString();
        return NormalizeText(fromVariant);
    }

    private static float GetFloat(Dictionary data, string key, float fallback)
    {
        if (!data.ContainsKey(key))
            return fallback;

        Variant value = data[key];
        return value.VariantType switch
        {
            Variant.Type.Float => (float)value,
            Variant.Type.Int => (int)value,
            Variant.Type.String => float.TryParse((string)value, out float parsed) ? parsed : fallback,
            _ => fallback
        };
    }
}
