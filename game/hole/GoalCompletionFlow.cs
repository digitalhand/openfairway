using System;
using System.Threading;
using Godot;

public sealed class GoalCompletionConfig
{
    public Func<bool> IsBallOnGoalProvider { get; set; }
    public Func<int> StrokeProvider { get; set; }
    public Func<int> ParProvider { get; set; }
    public Action<string> ShowOverlay { get; set; }
    public Action HideOverlay { get; set; }
    public Action OnCompleteRound { get; set; }
}

public sealed class GoalCompletionFlow
{
    private GoalCompletionConfig _config;
    private CancellationTokenSource _countdownCts;

    public bool IsRunning { get; private set; }

    public void Initialize(GoalCompletionConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Cancel();
    }

    public bool TryStartIfBallOnGoal(Node timerHost, float overlayDurationSeconds)
    {
        if (timerHost == null || _config == null || IsRunning)
            return false;

        if (_config.IsBallOnGoalProvider == null || !_config.IsBallOnGoalProvider())
            return false;

        StartCountdown(timerHost, overlayDurationSeconds);
        return true;
    }

    public void Cancel()
    {
        _countdownCts?.Cancel();
        _countdownCts = null;

        _config?.HideOverlay?.Invoke();
        IsRunning = false;
    }

    private async void StartCountdown(Node timerHost, float overlayDurationSeconds)
    {
        _countdownCts?.Cancel();
        _countdownCts = new CancellationTokenSource();
        CancellationToken token = _countdownCts.Token;

        IsRunning = true;
        _config.ShowOverlay?.Invoke(BuildOverlayLabel());

        float duration = Mathf.Max(0.0f, overlayDurationSeconds);
        await timerHost.ToSignal(timerHost.GetTree().CreateTimer(duration), SceneTreeTimer.SignalName.Timeout);

        if (token.IsCancellationRequested)
        {
            _config.HideOverlay?.Invoke();
            IsRunning = false;
            return;
        }

        _config.HideOverlay?.Invoke();
        _config.OnCompleteRound?.Invoke();
        IsRunning = false;
    }

    private string BuildOverlayLabel()
    {
        if (_config?.StrokeProvider == null)
            return "Par";

        int strokes = _config.StrokeProvider();
        if (strokes <= 0)
            return "Par";

        int par = _config.ParProvider != null ? Mathf.Max(1, _config.ParProvider()) : 1;
        return ScoreMapper.MapScore(strokes, par).Label;
    }
}
