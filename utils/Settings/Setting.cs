using Godot;

/// <summary>
/// Represents a single setting with value, default, and optional min/max constraints.
/// </summary>
public partial class Setting : RefCounted
{
    [Signal]
    public delegate void SettingChangedEventHandler(Variant val);

    public Variant Value { get; private set; }
    public Variant Default { get; private set; }
    public Variant MinValue { get; private set; } = default;
    public Variant MaxValue { get; private set; } = default;

    public Setting(Variant def, Variant minimum = default, Variant maximum = default)
    {
        MinValue = minimum;
        MaxValue = maximum;
        Value = def;
        Default = def;
    }

    public void ResetDefault()
    {
        Value = Default;
        EmitSignal(SignalName.SettingChanged, Value);
    }

    public void SetValue(Variant val)
    {
        Variant newValue = val;

        if (MinValue.VariantType != Variant.Type.Nil && newValue.AsDouble() < MinValue.AsDouble())
        {
            newValue = MinValue;
        }
        else if (MaxValue.VariantType != Variant.Type.Nil && newValue.AsDouble() > MaxValue.AsDouble())
        {
            newValue = MaxValue;
        }

        Value = newValue;
        EmitSignal(SignalName.SettingChanged, Value);
    }

    public void SetDefault(Variant def)
    {
        Default = def;
        EmitSignal(SignalName.SettingChanged, Value);
    }
}
