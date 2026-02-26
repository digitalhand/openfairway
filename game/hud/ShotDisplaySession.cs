using Godot.Collections;

public sealed class ShotDisplaySession
{
    private Dictionary _rawBallData = new();

    public ShotDisplaySnapshot Current { get; private set; } = ShotDisplaySnapshot.Empty;
    public ShotDisplaySnapshot LastPublished { get; private set; } = ShotDisplaySnapshot.Empty;

    public void SetRawPayload(Dictionary payload)
    {
        _rawBallData = payload?.Duplicate() ?? new Dictionary();
    }

    public ShotDisplaySnapshot Refresh(ShotTracker shotTracker, PhysicsEnums.Units units, bool showDistance = true)
    {
        Current = ShotFormatter.FormatBallDisplaySnapshot(
            rawBallData: _rawBallData,
            shotTracker: shotTracker,
            units: units,
            showDistance: showDistance,
            prevSnapshot: Current
        );

        LastPublished = Current;
        return Current;
    }

    public void Reset()
    {
        _rawBallData.Clear();
        Current = ShotDisplaySnapshot.Empty;
        LastPublished = ShotDisplaySnapshot.Empty;
    }
}
