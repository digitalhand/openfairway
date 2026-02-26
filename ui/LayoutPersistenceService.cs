using System.Collections.Generic;
using Godot;

public static class LayoutPersistenceService
{
    public const int CurrentLayoutVersion = 3;
    private const string MetaSection = "meta";
    private const string LayoutVersionKey = "layout_version";

    public static bool TryLoad(string path, out ConfigFile config)
    {
        config = new ConfigFile();
        return config.Load(path) == Error.Ok;
    }

    public static void Save(string path, IEnumerable<Control> panels)
    {
        var config = new ConfigFile();
        foreach (Control panel in panels)
        {
            config.SetValue("positions", panel.Name, panel.Position);
            config.SetValue("visibility", panel.Name, panel.Visible);
        }

        config.SetValue(MetaSection, LayoutVersionKey, CurrentLayoutVersion);
        config.Save(path);
    }

    public static int GetLayoutVersion(ConfigFile config)
    {
        if (!config.HasSectionKey(MetaSection, LayoutVersionKey))
            return 1;

        Variant versionValue = config.GetValue(MetaSection, LayoutVersionKey);
        if (versionValue.VariantType == Variant.Type.Int)
            return (int)versionValue;

        if (versionValue.VariantType == Variant.Type.Float)
            return Mathf.RoundToInt((float)versionValue);

        PhysicsLogger.Error("LayoutPersistenceService: invalid layout_version type in metadata");
        return 1;
    }

    public static void Apply(ConfigFile config, IEnumerable<Control> panels, Vector2 viewportSize)
    {
        foreach (Control panel in panels)
        {
            if (config.HasSectionKey("positions", panel.Name))
            {
                Variant posValue = config.GetValue("positions", panel.Name);
                if (posValue.VariantType == Variant.Type.Vector2)
                {
                    var pos = (Vector2)posValue;
                    pos.X = Mathf.Clamp(pos.X, -panel.Size.X, viewportSize.X);
                    pos.Y = Mathf.Clamp(pos.Y, -panel.Size.Y, viewportSize.Y);
                    panel.Position = pos;
                }
                else
                {
                    PhysicsLogger.Error($"LayoutPersistenceService: invalid position type for panel '{panel.Name}'");
                }
            }

            if (config.HasSectionKey("visibility", panel.Name))
            {
                Variant visValue = config.GetValue("visibility", panel.Name);
                if (visValue.VariantType == Variant.Type.Bool)
                {
                    panel.Visible = (bool)visValue;
                }
                else
                {
                    PhysicsLogger.Error($"LayoutPersistenceService: invalid visibility type for panel '{panel.Name}'");
                }
            }
        }
    }
}
