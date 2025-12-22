using Godot;
using Godot.Collections;
using System.Linq;

/// <summary>
/// Tracks shot statistics and manages ball tracers.
/// Monitors the golf ball during flight and rollout to record
/// apex height, carry distance, side distance, and total distance.
/// </summary>
public partial class ShotTracker : Node3D
{
    // Signals
    [Signal]
    public delegate void GoodDataEventHandler();
    [Signal]
    public delegate void BadDataEventHandler();
    [Signal]
    public delegate void ShotCompleteEventHandler(Dictionary data);

    // Tracer settings
    [Export] public int MaxTracers { get; set; } = 4;
    [Export] public float TrailResolution { get; set; } = 0.01f;

    // Shot statistics
    public float Apex { get; set; } = 0.0f;
    public float Carry { get; set; } = 0.0f;
    public float SideDistance { get; set; } = 0.0f;
    public Dictionary ShotData { get; set; } = new();

    // Internal state
    private bool _trackPoints = false;
    private float _trailTimer = 0.0f;
    private System.Collections.Generic.List<Node3D> _tracers = new();
    private Node3D _currentTracer = null;

    private GolfBall _ball;

    public override void _Ready()
    {
        _ball = GetNode<GolfBall>("Ball");
        _ball.BallAtRest += OnBallRest;
        MaxTracers = (int)GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.ShotTracerCount.Value;
        GetNode<GlobalSettings>("/root/GlobalSettings").RangeSettings.ShotTracerCount.SettingChanged += OnTracerCountChanged;
    }

    private void OnTracerCountChanged(Variant value)
    {
        MaxTracers = (int)value;
        // Remove excess tracers if limit lowered
        while (_tracers.Count > MaxTracers)
        {
            var oldest = _tracers[0];
            _tracers.RemoveAt(0);
            oldest.QueueFree();
        }
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("hit"))
        {
            StartShot();
            _ball.CallDeferred("Hit");
        }

        if (Input.IsActionJustPressed("reset"))
        {
            ResetBall();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_trackPoints)
            return;

        Apex = Mathf.Max(Apex, _ball.Position.Y);
        SideDistance = _ball.Position.Z;

        if (_ball.State == PhysicsEnums.BallState.Flight)
        {
            float newCarry = _ball.GetDownrangeYards() / 1.09361f;  // Convert to meters
            if (newCarry > Carry)
                Carry = newCarry;
        }

        // Update tracer visual if enabled
        if (_currentTracer != null)
        {
            _trailTimer += (float)delta;
            if (_trailTimer >= TrailResolution)
            {
                ((BallTrail)_currentTracer).AddPoint(_ball.Position);
                _trailTimer = 0.0f;
            }
        }
    }

    private void StartShot()
    {
        _trackPoints = false;
        Apex = 0.0f;
        Carry = 0.0f;
        SideDistance = 0.0f;
        CreateNewTracer();

        if (_currentTracer != null)
        {
            ((BallTrail)_currentTracer).AddPoint(_ball.Position);
        }

        _trackPoints = true;
        _trailTimer = 0.0f;
    }

    private Node3D CreateNewTracer()
    {
        if (MaxTracers == 0)
        {
            _currentTracer = null;
            return null;
        }

        // Remove oldest if at limit
        if (_tracers.Count >= MaxTracers)
        {
            var oldest = _tracers[0];
            _tracers.RemoveAt(0);
            oldest.QueueFree();
        }

        // Create new tracer
        var newTracer = new BallTrail();
        AddChild(newTracer);

        _tracers.Add(newTracer);
        _currentTracer = newTracer;
        return newTracer;
    }

    /// <summary>
    /// Reset the ball and clear all tracers
    /// </summary>
    public void ResetBall()
    {
        _ball.CallDeferred("Reset");
        ClearAllTracers();
        Apex = 0.0f;
        Carry = 0.0f;
        SideDistance = 0.0f;
        ResetShotData();
    }

    private void ClearAllTracers()
    {
        foreach (var tracer in _tracers)
        {
            tracer.QueueFree();
        }
        _tracers.Clear();
        _currentTracer = null;
    }

    private void ResetShotData()
    {
        var keys = ShotData.Keys.ToList();
        foreach (var key in keys)
        {
            ShotData[key] = 0.0f;
        }
    }

    /// <summary>
    /// Get current total distance in meters
    /// </summary>
    public int GetDistance()
    {
        return (int)(_ball.GetDownrangeYards() / 1.09361f);
    }

    /// <summary>
    /// Get current side distance in meters
    /// </summary>
    public int GetSideDistance()
    {
        return (int)_ball.Position.Z;
    }

    /// <summary>
    /// Get current ball state
    /// </summary>
    public PhysicsEnums.BallState GetBallState()
    {
        return _ball.State;
    }

    /// <summary>
    /// Validate incoming shot data
    /// </summary>
    public bool ValidateData(Dictionary data)
    {
        // TODO: Implement proper validation
        return data != null && data.Count > 0;
    }

    private void OnBallRest()
    {
        _trackPoints = false;
        ShotData["TotalDistance"] = (int)(_ball.GetDownrangeYards() / 1.09361f);
        ShotData["CarryDistance"] = (int)Carry;
        ShotData["Apex"] = (int)Apex;
        ShotData["SideDistance"] = (int)SideDistance;
        EmitSignal(SignalName.ShotComplete, ShotData);
    }

    /// <summary>
    /// Handle incoming shot from TCP (launch monitor)
    /// </summary>
    public void OnTcpClientHitBall(Dictionary data)
    {
        if (!ValidateData(data))
        {
            EmitSignal(SignalName.BadData);
            return;
        }

        EmitSignal(SignalName.GoodData);
        ShotData = data.Duplicate();
        StartShot();
        _ball.CallDeferred(GolfBall.MethodName.HitFromData, data);
    }

    /// <summary>
    /// Handle locally injected shot from UI
    /// </summary>
    public void OnRangeUiHitShot(Variant data)
    {
        ShotData = ((Dictionary)data).Duplicate();
        GD.Print($"Local shot injection payload: {Json.Stringify(ShotData)}");
        StartShot();
        _ball.CallDeferred(GolfBall.MethodName.HitFromData, data);
    }

    private void OnRangeUiSetEnv(Variant data)
    {
        _ball.CallDeferred("SetEnv", data);
    }
}
