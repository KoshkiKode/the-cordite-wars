using Godot;

namespace CorditeWars.Systems.Audio;

/// <summary>
/// Global audio manager autoload. Handles music playback, sound effects,
/// and volume control. Provides pooled AudioStreamPlayer instances
/// to avoid allocation during gameplay.
/// </summary>
public partial class AudioManager : Node
{
    private const int SfxPoolSize = 32;

    private AudioStreamPlayer? _musicPlayer;
    private readonly AudioStreamPlayer3D[] _sfxPool = new AudioStreamPlayer3D[SfxPoolSize];
    private int _nextSfxIndex;

    // Volume levels (linear, 0.0 to 1.0)
    private float _masterVolume = 1.0f;
    private float _musicVolume = 0.7f;
    private float _sfxVolume = 1.0f;

    public override void _Ready()
    {
        // Create the music player
        _musicPlayer = new AudioStreamPlayer
        {
            Bus = "Music"
        };
        AddChild(_musicPlayer);

        // Pre-allocate SFX pool
        for (int i = 0; i < SfxPoolSize; i++)
        {
            _sfxPool[i] = new AudioStreamPlayer3D
            {
                Bus = "SFX",
                MaxPolyphony = 2
            };
            AddChild(_sfxPool[i]);
        }

        GD.Print($"[AudioManager] Initialized with {SfxPoolSize} SFX pool slots.");
    }

    /// <summary>
    /// Plays a sound effect at a 3D position using the next available pool slot.
    /// </summary>
    public void PlaySfx(AudioStream stream, Vector3 position)
    {
        var player = _sfxPool[_nextSfxIndex];
        player.Stream = stream;
        player.GlobalPosition = position;
        player.VolumeDb = Mathf.LinearToDb(_sfxVolume * _masterVolume);
        player.Play();

        _nextSfxIndex = (_nextSfxIndex + 1) % SfxPoolSize;
    }

    /// <summary>
    /// Plays background music. Crossfades if music is already playing.
    /// </summary>
    public void PlayMusic(AudioStream stream, float fadeTime = 1.0f)
    {
        if (_musicPlayer == null) return;

        _musicPlayer.Stream = stream;
        _musicPlayer.VolumeDb = Mathf.LinearToDb(_musicVolume * _masterVolume);
        _musicPlayer.Play();
    }

    /// <summary>
    /// Stops all music playback.
    /// </summary>
    public void StopMusic()
    {
        _musicPlayer?.Stop();
    }

    public void SetMasterVolume(float volume)
    {
        _masterVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
    }

    public void SetMusicVolume(float volume)
    {
        _musicVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
        if (_musicPlayer != null)
            _musicPlayer.VolumeDb = Mathf.LinearToDb(_musicVolume * _masterVolume);
    }

    public void SetSfxVolume(float volume)
    {
        _sfxVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
    }
}
