using System;
using System.Text;
using Godot;
using Godot.Collections;
using TcpClientPeer = Godot.StreamPeerTcp;
using TcpServerPeer = Godot.TcpServer;

public partial class TcpServer : Node
{
    private readonly TcpServerPeer _tcpServer = new();
    private TcpClientPeer _tcpConnection;
    private bool _tcpConnected;
    private string _tcpString = string.Empty;
    private Dictionary _shotData = new();
    private GlobalSettings _globalSettings;
    private Setting _tcpPortSetting;
    private string _currentDeviceId = string.Empty;
    private bool _hasIdentifiedDevice;
    private bool _lastPublishedConnected;
    private string _lastPublishedDeviceId = string.Empty;

    private readonly Dictionary _resp200 = new() { { "Code", 200 } };
    private readonly Dictionary _resp201 = new() { { "Code", 201 }, { "Message", "Player Information" } };
    private readonly Dictionary _resp50x = new() { { "Code", 501 }, { "Message", "Failure Occured" } };

    // C1: Maximum payload size to prevent memory exhaustion (64 KB)
    private const int MAX_PAYLOAD_BYTES = 65536;

    // M1: Minimum interval between accepted shots (milliseconds)
    private const double SHOT_COOLDOWN_MS = 500.0;
    private ulong _lastShotTimeMs;

    // M2: Connection idle timeout (milliseconds)
    private const double CONNECTION_TIMEOUT_MS = 60000.0;
    private ulong _lastActivityTimeMs;

    [Export] public int Port { get; set; } = 55000;

    [Signal]
    public delegate void HitBallEventHandler(Dictionary data);
    [Signal]
    public delegate void ConnectionStatusChangedEventHandler(bool connected, string deviceId);

    public bool HasIdentifiedDevice => _tcpConnected && _hasIdentifiedDevice && !string.IsNullOrWhiteSpace(_currentDeviceId);
    public string ConnectedDeviceId => HasIdentifiedDevice ? _currentDeviceId : string.Empty;

    public override void _Ready()
    {
        BindTcpPortSetting();
        int configuredPort = _tcpPortSetting != null ? (int)_tcpPortSetting.Value : Port;
        ApplyListenPort(configuredPort);
    }

    public override void _ExitTree()
    {
        if (_tcpPortSetting != null)
            _tcpPortSetting.SettingChanged -= OnTcpPortSettingChanged;

        ShutdownServer("TCP server shutting down with scene exit.");
    }

    public override void _Process(double delta)
    {
        if (!_tcpConnected && !_tcpServer.IsListening())
            return;

        if (_tcpConnected && _tcpConnection == null)
        {
            _tcpConnected = false;
            return;
        }

        // Accept new connection
        if (!_tcpConnected)
        {
            _tcpConnection = _tcpServer.TakeConnection();
            if (_tcpConnection != null)
            {
                PhysicsLogger.Info($"We have a tcp connection at {_tcpConnection.GetConnectedHost()}");
                _tcpConnected = true;
                _lastActivityTimeMs = Time.GetTicksMsec();
                ClearIdentifiedDevice();
            }
            return;
        }

        // Poll existing connection
        _tcpConnection.Poll();
        var status = _tcpConnection.GetStatus();

        if (status == StreamPeerTcp.Status.None)
        {
            HandleDisconnected("tcp disconnected");
            return;
        }

        if (status != StreamPeerTcp.Status.Connected)
            return;

        // M2: Check for idle timeout
        if (Time.GetTicksMsec() - _lastActivityTimeMs > CONNECTION_TIMEOUT_MS)
        {
            HandleDisconnected("tcp connection timed out after inactivity");
            return;
        }

        var bytesAvailable = (int)_tcpConnection.GetAvailableBytes();
        if (bytesAvailable <= 0)
            return;

        _lastActivityTimeMs = Time.GetTicksMsec();

        // C1: Reject oversized payloads
        if (bytesAvailable > MAX_PAYLOAD_BYTES)
        {
            PhysicsLogger.Error($"TCP payload too large ({bytesAvailable} bytes > {MAX_PAYLOAD_BYTES}), rejecting");
            // Drain the oversized data to clear the buffer
            _tcpConnection.GetUtf8String(bytesAvailable);
            RespondError(501, "Payload too large");
            return;
        }

        _tcpString = _tcpConnection.GetUtf8String(bytesAvailable);

        var json = new Json();
        var parseResult = json.Parse(_tcpString);
        if (parseResult != Error.Ok)
        {
            RespondError(501, "Bad JSON data");
            return;
        }

        var data = json.GetData();
        if (data.VariantType != Variant.Type.Dictionary)
        {
            RespondError(501, "Invalid payload");
            return;
        }

        var dict = data.AsGodotDictionary();
        _shotData = dict;
        CaptureDeviceIdentity(dict);

        // M3: Log truncated payload after validation
        string logPayload = _tcpString.Length > 200 ? _tcpString[..200] + "..." : _tcpString;
        PhysicsLogger.Info($"Launch monitor payload: {logPayload}");

        TryEmitHitBall(dict);
    }

    private void RespondError(int code, string message)
    {
        if (_tcpConnection == null)
            return;

        _tcpConnection.Poll();
        var status = _tcpConnection.GetStatus();

        if (status == StreamPeerTcp.Status.None)
        {
            HandleDisconnected();
            return;
        }

        if (status != StreamPeerTcp.Status.Connected)
            return;

        _resp50x["Code"] = code;
        _resp50x["Message"] = message;

        var payload = Encoding.ASCII.GetBytes(Json.Stringify(_resp50x));
        _tcpConnection.PutData(payload);
    }

    private void RespondSuccess()
    {
        if (_tcpConnection == null)
            return;

        _tcpConnection.Poll();
        var status = _tcpConnection.GetStatus();

        if (status == StreamPeerTcp.Status.None)
        {
            HandleDisconnected();
            return;
        }

        if (status == StreamPeerTcp.Status.Connected)
        {
            var payload = Encoding.ASCII.GetBytes(Json.Stringify(_resp200));
            _tcpConnection.PutData(payload);
        }
    }

    private void TryEmitHitBall(Dictionary data)
    {
        // M1: Rate limiting — reject shots fired too quickly
        ulong now = Time.GetTicksMsec();
        if (now - _lastShotTimeMs < SHOT_COOLDOWN_MS)
        {
            PhysicsLogger.Info("TCP shot rejected: rate limited");
            RespondError(429, "Too many requests");
            return;
        }

        if (!data.TryGetValue("ShotDataOptions", out Variant optionsVar) || optionsVar.VariantType != Variant.Type.Dictionary)
            return;

        var options = optionsVar.AsGodotDictionary();

        if (!options.TryGetValue("ContainsBallData", out Variant containsVar) || containsVar.VariantType != Variant.Type.Bool)
            return;

        var containsBall = (bool)containsVar;
        if (!containsBall)
            return;

        if (!data.TryGetValue("BallData", out Variant ballVar) || ballVar.VariantType != Variant.Type.Dictionary)
            return;

        var ballData = ballVar.AsGodotDictionary();

        // C2: Validate shot data before emitting
        if (!ShotValidator.ValidateAndClamp(ballData))
        {
            PhysicsLogger.Error("TCP shot rejected: invalid ball data");
            RespondError(501, "Invalid ball data values");
            return;
        }

        _lastShotTimeMs = now;
        EmitSignal(SignalName.HitBall, ballData);

        // C4: Send success response to launch monitor
        RespondSuccess();
    }

    public void SetListenPort(int port)
    {
        ApplyListenPort(port);
    }

    private void ApplyListenPort(int port)
    {
        int sanitizedPort = Mathf.Clamp(port, 1, 65535);
        Port = sanitizedPort;
        ShutdownServer();

        Error listenError = _tcpServer.Listen((ushort)Port);
        if (listenError != Error.Ok)
            PhysicsLogger.Error($"TCP server failed to listen on port {Port}. Error: {listenError}");
    }

    private void ShutdownServer(string logMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(logMessage))
            PhysicsLogger.Info(logMessage);

        if (_tcpConnection != null)
        {
            _tcpConnection.DisconnectFromHost();
            _tcpConnection = null;
        }

        _tcpConnected = false;
        _shotData.Clear();
        _tcpString = string.Empty;
        ClearIdentifiedDevice();
        PublishConnectionStatus(false, string.Empty);

        if (_tcpServer.IsListening())
            _tcpServer.Stop();
    }

    private void HandleDisconnected(string logMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(logMessage))
            PhysicsLogger.Info(logMessage);

        if (_tcpConnection != null)
        {
            _tcpConnection.DisconnectFromHost();
            _tcpConnection = null;
        }

        _tcpConnected = false;
        _shotData.Clear();
        _tcpString = string.Empty;
        ClearIdentifiedDevice();
        PublishConnectionStatus(false, string.Empty);
    }

    private void CaptureDeviceIdentity(Dictionary payload)
    {
        if (!_tcpConnected || payload == null)
            return;

        if (!payload.TryGetValue("DeviceID", out Variant deviceIdValue) || deviceIdValue.VariantType != Variant.Type.String)
            return;

        string deviceId = deviceIdValue.AsString().Trim();
        if (string.IsNullOrWhiteSpace(deviceId))
            return;

        _currentDeviceId = deviceId;
        _hasIdentifiedDevice = true;
        PublishConnectionStatus(true, deviceId);
    }

    private void ClearIdentifiedDevice()
    {
        _currentDeviceId = string.Empty;
        _hasIdentifiedDevice = false;
    }

    private void PublishConnectionStatus(bool connected, string deviceId)
    {
        string safeDeviceId = connected ? (deviceId ?? string.Empty).Trim() : string.Empty;
        bool hasConnectionIdentity = connected && !string.IsNullOrWhiteSpace(safeDeviceId);

        if (_lastPublishedConnected == hasConnectionIdentity
            && string.Equals(_lastPublishedDeviceId, safeDeviceId, StringComparison.Ordinal))
        {
            return;
        }

        _lastPublishedConnected = hasConnectionIdentity;
        _lastPublishedDeviceId = safeDeviceId;
        EmitSignal(SignalName.ConnectionStatusChanged, hasConnectionIdentity, safeDeviceId);
    }

    private void BindTcpPortSetting()
    {
        _globalSettings = GetNodeOrNull<GlobalSettings>("/root/GlobalSettings");
        _tcpPortSetting = _globalSettings?.AppSettings?.TcpPort;
        if (_tcpPortSetting != null)
            _tcpPortSetting.SettingChanged += OnTcpPortSettingChanged;
    }

    private void OnTcpPortSettingChanged(Variant value)
    {
        int nextPort = Port;
        if (value.VariantType == Variant.Type.Int)
            nextPort = (int)value;
        else if (value.VariantType == Variant.Type.Float)
            nextPort = Mathf.RoundToInt((float)value);

        SetListenPort(nextPort);
    }
}
