using System.IO;
using System.Text.Json;
using Godot;
using Godot.Collections;
using NUnit.Framework;

namespace OpenFairway.Tests
{
    internal static class TestShotLoader
    {
        private static readonly string DataPath =
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "assets", "data");

        public static Dictionary LoadTestShot(string filename)
        {
            string path = Path.Combine(DataPath, filename);
            if (!File.Exists(path))
                Assert.Fail($"Test shot file not found: {path}");

            string json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json, options);

            var dict = new Dictionary();
            foreach (var kvp in data)
                dict[kvp.Key] = Variant.From(ConvertJsonValue(kvp.Value));

            return dict;
        }

        private static object ConvertJsonValue(object value)
        {
            if (value is JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (element.TryGetDouble(out double d))
                            return d;
                        break;
                    case JsonValueKind.String:
                        return element.GetString();
                    case JsonValueKind.True:
                        return true;
                    case JsonValueKind.False:
                        return false;
                    case JsonValueKind.Object:
                        var dict = new Dictionary();
                        foreach (var prop in element.EnumerateObject())
                            dict[prop.Name] = Variant.From(ConvertJsonValue(prop.Value));
                        return dict;
                }
            }

            return value;
        }
    }
}
