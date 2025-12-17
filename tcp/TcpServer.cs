using Godot;
using Godot.Collections;
using System.Text;
using TcpClientPeer = Godot.StreamPeerTcp;
using TcpServerPeer = Godot.TcpServer;

public partial class TcpServer : Node
{
    private readonly TcpServerPeer _tcpServer = new();
    private TcpClientPeer _tcpConnection;
    private bool _tcpConnected;
    private string _tcpString = string.Empty;
    private Dictionary _shotData = new();

    private readonly Dictionary _resp200 = new() { { "Code", 200 } };
    private readonly Dictionary _resp201 = new() { { "Code", 201 }, { "Message", "Player Information" } };
    private readonly Dictionary _resp50x = new() { { "Code", 501 }, { "Message", "Failure Occured" } };

    [Signal]
    public delegate void HitBallEventHandler(Dictionary data);

    public override void _Ready()
    {
        _tcpServer.Listen(49152);
    }

    public override void _Process(double delta)
    {
        // Accept new connection
        if (!_tcpConnected)
        {
            _tcpConnection = _tcpServer.TakeConnection();
            if (_tcpConnection != null)
            {
                GD.Print($"We have a tcp connection at {_tcpConnection.GetConnectedHost()}");
                _tcpConnected = true;
            }
            return;
        }

        // Poll existing connection
        _tcpConnection.Poll();
        var status = _tcpConnection.GetStatus();

        if (status == StreamPeerTcp.Status.None)
        {
            _tcpConnected = false;
            _tcpConnection = null;
            _shotData.Clear();
            GD.Print("tcp disconnected");
            return;
        }

        if (status != StreamPeerTcp.Status.Connected)
            return;

        var bytesAvailable = (int)_tcpConnection.GetAvailableBytes();
        if (bytesAvailable <= 0)
            return;

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
        GD.Print($"Launch monitor payload: {_tcpString}");
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
            _tcpConnected = false;
            _tcpConnection = null;
            return;
        }

        if (status != StreamPeerTcp.Status.Connected)
            return;

        _resp50x["Code"] = code;
        _resp50x["Message"] = message;

        var payload = Encoding.ASCII.GetBytes(Json.Stringify(_resp50x));
        _tcpConnection.PutData(payload);
    }

    private void TryEmitHitBall(Dictionary data)
    {
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
        EmitSignal(SignalName.HitBall, ballData);
    }

    private void _on_golf_ball_good_data()
    {
        if (_tcpConnection == null)
            return;

        _tcpConnection.Poll();
        var status = _tcpConnection.GetStatus();

        if (status == StreamPeerTcp.Status.None)
        {
            _tcpConnected = false;
            _tcpConnection = null;
            return;
        }

        if (status == StreamPeerTcp.Status.Connected)
        {
            var payload = Encoding.ASCII.GetBytes(Json.Stringify(_resp200));
            _tcpConnection.PutData(payload);
        }
    }

    private void _on_player_bad_data()
    {
        RespondError(501, "Bad Ball Data");
    }
}
