using Godot;

/// <summary>
/// Manages hole scene audio: non-attenuated 3D audio configuration, driver hit, and ball landing sounds.
/// </summary>
public sealed class HoleAudioManager
{
    private readonly AudioStreamPlayer3D _driverHit;
    private readonly AudioStreamPlayer3D _ambientBirds;
    private readonly AudioStreamPlayer3D _ballLanding;

    public HoleAudioManager(
        AudioStreamPlayer3D driverHit,
        AudioStreamPlayer3D ambientBirds,
        AudioStreamPlayer3D ballLanding)
    {
        _driverHit = driverHit;
        _ambientBirds = ambientBirds;
        _ballLanding = ballLanding;
    }

    public void ConfigureAll(bool startAmbient)
    {
        ConfigureNonAttenuated(_ambientBirds, ensurePlaying: startAmbient);
        ConfigureNonAttenuated(_driverHit, ensurePlaying: false);
        ConfigureNonAttenuated(_ballLanding, ensurePlaying: false);
    }

    public void PlayDriverHit()
    {
        if (_driverHit == null)
            return;

        if (_driverHit.Playing)
            _driverHit.Stop();

        _driverHit.Play();
    }

    public void PlayBallLanding(Vector3 ballPosition)
    {
        if (_ballLanding == null)
            return;

        _ballLanding.GlobalPosition = ballPosition;
        if (_ballLanding.Playing)
            _ballLanding.Stop();

        _ballLanding.Play();
    }

    private static void ConfigureNonAttenuated(AudioStreamPlayer3D player, bool ensurePlaying)
    {
        if (player == null)
            return;

        player.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
        player.DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled;

        if (ensurePlaying && !player.Playing)
            player.Play();
    }
}
