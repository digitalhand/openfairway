using System.IO;
using System.Text.Json;
using Godot;
using Godot.Collections;

public partial class ShotRecordingService : Node
{
    private GlobalSettings _globalSettings;
    private Setting _recordingEnabledSetting;
    private Setting _recordingPathSetting;

    private string _currentSessionPath = string.Empty;
    private int _shotCounter;
    private bool _isRecording;

    public bool IsRecording => _isRecording;
    public string CurrentSessionName => _isRecording ? Path.GetFileName(_currentSessionPath) : string.Empty;
    public int ShotCount => _shotCounter;

    public override void _Ready()
    {
        _globalSettings = GetNodeOrNull<GlobalSettings>("/root/GlobalSettings");

        if (_globalSettings?.AppSettings == null)
            return;

        _recordingEnabledSetting = _globalSettings.AppSettings.ShotRecordingEnabled;
        _recordingPathSetting = _globalSettings.AppSettings.ShotRecordingPath;

        // Always start disabled regardless of persisted value
        _recordingEnabledSetting.SetValue(false);

        _recordingEnabledSetting.SettingChanged += OnRecordingEnabledChanged;
    }

    public override void _ExitTree()
    {
        if (_recordingEnabledSetting != null)
            _recordingEnabledSetting.SettingChanged -= OnRecordingEnabledChanged;
    }

    public void RecordShot(Dictionary ballData)
    {
        if (!_isRecording || ballData == null)
            return;

        if (string.IsNullOrWhiteSpace(_currentSessionPath) || !Directory.Exists(_currentSessionPath))
            return;

        _shotCounter++;

        var shotJson = BuildShotJson(ballData);
        string filePath = Path.Combine(_currentSessionPath, $"shot_{_shotCounter}.json");

        try
        {
            File.WriteAllText(filePath, shotJson);
            PhysicsLogger.Info($"ShotRecordingService: recorded shot {_shotCounter} to {filePath}");
        }
        catch (IOException ex)
        {
            PhysicsLogger.Error($"ShotRecordingService: failed to write {filePath}: {ex.Message}");
        }
    }

    private void OnRecordingEnabledChanged(Variant value)
    {
        bool enabled = (bool)value;
        if (enabled)
            StartNewSession();
        else
            _isRecording = false;
    }

    private void StartNewSession()
    {
        string basePath = _recordingPathSetting?.Value.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
        {
            PhysicsLogger.Error("ShotRecordingService: recording path is empty or does not exist.");
            _isRecording = false;
            return;
        }

        int nextSession = 1;
        while (Directory.Exists(Path.Combine(basePath, $"shot_session_{nextSession}")))
            nextSession++;

        _currentSessionPath = Path.Combine(basePath, $"shot_session_{nextSession}");
        Directory.CreateDirectory(_currentSessionPath);
        _shotCounter = 0;
        _isRecording = true;

        PhysicsLogger.Info($"ShotRecordingService: started session at {_currentSessionPath}");
    }

    private static string BuildShotJson(Dictionary ballData)
    {
        var shot = new System.Collections.Generic.Dictionary<string, object>
        {
            ["BallData"] = ConvertGodotDict(ballData),
            ["ShotDataOptions"] = new System.Collections.Generic.Dictionary<string, object>
            {
                ["ContainsBallData"] = true
            }
        };

        return JsonSerializer.Serialize(shot, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static System.Collections.Generic.Dictionary<string, object> ConvertGodotDict(Dictionary dict)
    {
        var result = new System.Collections.Generic.Dictionary<string, object>();
        foreach (var key in dict.Keys)
        {
            Variant val = dict[key];
            result[key.ToString()] = val.VariantType switch
            {
                Variant.Type.Int => (long)val,
                Variant.Type.Float => (double)val,
                Variant.Type.Bool => (bool)val,
                Variant.Type.String => val.ToString(),
                _ => val.ToString()
            };
        }

        return result;
    }
}
