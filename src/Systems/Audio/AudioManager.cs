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
    private const int UiPoolSize  = 8;

    private const string SettingsPath    = "user://settings.cfg";
    private const string SettingsSection = "Audio";

    private AudioStreamPlayer? _musicPlayer;
    private readonly AudioStreamPlayer3D[] _sfxPool = new AudioStreamPlayer3D[SfxPoolSize];
    private readonly AudioStreamPlayer[]   _uiPool  = new AudioStreamPlayer[UiPoolSize];
    private int _nextSfxIndex;
    private int _nextUiIndex;

    // Volume levels (linear, 0.0 to 1.0)
    private float _masterVolume = 1.0f;
    private float _musicVolume  = 0.7f;
    private float _sfxVolume    = 0.8f;
    private float _uiVolume     = 0.8f;

    // Track the current music stream so we can loop it when it finishes.
    private AudioStream? _currentMusicStream;
    // Entry volume for the current music track (applied on loop restart).
    private float _currentMusicEntryVolume = 1.0f;

    public override void _Ready()
    {
        // Create the music player
        _musicPlayer = new AudioStreamPlayer { Bus = "Music" };
        _musicPlayer.Finished += OnMusicFinished;
        AddChild(_musicPlayer);

        // Pre-allocate 3D SFX pool (combat, world events)
        for (int i = 0; i < SfxPoolSize; i++)
        {
            _sfxPool[i] = new AudioStreamPlayer3D { Bus = "SFX", MaxPolyphony = 2 };
            AddChild(_sfxPool[i]);
        }

        // Pre-allocate 2D UI pool (buttons, notifications — non-positional)
        for (int i = 0; i < UiPoolSize; i++)
        {
            _uiPool[i] = new AudioStreamPlayer { Bus = "UI" };
            AddChild(_uiPool[i]);
        }

        // Apply persisted user preferences immediately so volume is correct
        // from the very first sound played, without needing the Options menu.
        LoadSettings();

        GD.Print($"[AudioManager] Initialized — {SfxPoolSize} SFX slots, {UiPoolSize} UI slots.");
    }

    // ── Playback ─────────────────────────────────────────────────────

    /// <summary>
    /// Plays a 3D positional sound effect using the next available pool slot.
    /// </summary>
    /// <param name="stream">The audio stream to play.</param>
    /// <param name="position">World-space position of the sound source.</param>
    /// <param name="entryVolume">
    /// Per-entry volume multiplier from the sound manifest (0–1). Defaults to 1.
    /// </param>
    /// <param name="pitchScale">
    /// Pitch multiplier (1 = normal). Use <see cref="SoundRegistry.RandomisedPitch"/> to derive this.
    /// </param>
    public void PlaySfx(AudioStream stream, Vector3 position, float entryVolume = 1.0f, float pitchScale = 1.0f)
    {
        var player = _sfxPool[_nextSfxIndex];
        player.Stream         = stream;
        player.GlobalPosition = position;
        player.VolumeDb       = Mathf.LinearToDb(entryVolume * _sfxVolume * _masterVolume);
        player.PitchScale     = pitchScale;
        player.Play();

        _nextSfxIndex = (_nextSfxIndex + 1) % SfxPoolSize;
    }

    /// <summary>
    /// Plays a non-positional UI sound (button click, alert, notification).
    /// </summary>
    /// <param name="stream">The audio stream to play.</param>
    /// <param name="entryVolume">
    /// Per-entry volume multiplier from the sound manifest (0–1). Defaults to 1.
    /// </param>
    /// <param name="pitchScale">
    /// Pitch multiplier (1 = normal). Use <see cref="SoundRegistry.RandomisedPitch"/> to derive this.
    /// </param>
    public void PlayUiSound(AudioStream stream, float entryVolume = 1.0f, float pitchScale = 1.0f)
    {
        var player = _uiPool[_nextUiIndex];
        player.Stream      = stream;
        player.VolumeDb    = Mathf.LinearToDb(entryVolume * _uiVolume * _masterVolume);
        player.PitchScale  = pitchScale;
        player.Play();

        _nextUiIndex = (_nextUiIndex + 1) % UiPoolSize;
    }

    /// <summary>
    /// Convenience: resolve a sound by category + ID from the SoundRegistry
    /// and play it as a non-positional UI sound.
    /// Applies the manifest entry's Volume and PitchVariation automatically.
    /// </summary>
    public void PlayUiSoundById(string soundId)
    {
        if (!SoundRegistry.Instance.IsLoaded) return;

        SoundEntry? entry = SoundRegistry.Instance.GetSound("ui", soundId);
        if (entry == null) return;

        string? file = SoundRegistry.PickVariant(entry);
        if (file == null) return;

        string resPath = file.StartsWith("res://") ? file : "res://" + file;
        AudioStream? stream = GD.Load<AudioStream>(resPath);
        if (stream == null)
        {
            GD.PushWarning($"[AudioManager] Cannot load UI audio: {resPath}");
            return;
        }

        PlayUiSound(stream, entry.Volume, SoundRegistry.RandomisedPitch(entry));
    }

    /// <summary>
    /// Starts background music, looping it when it ends.
    /// If the same stream is already playing nothing changes.
    /// Pass <c>null</c> to stop music without starting new music.
    /// </summary>
    public void PlayMusic(AudioStream? stream, float entryVolume = 1.0f)
    {
        if (_musicPlayer == null) return;
        if (stream == null) { StopMusic(); return; }

        // Avoid restarting the exact same track.
        if (_musicPlayer.Playing && _currentMusicStream == stream) return;

        _currentMusicStream      = stream;
        _currentMusicEntryVolume = entryVolume;
        _musicPlayer.Stream      = stream;
        _musicPlayer.VolumeDb    = Mathf.LinearToDb(entryVolume * _musicVolume * _masterVolume);
        _musicPlayer.Play();
    }

    /// <summary>
    /// Convenience: resolve a music entry by sound ID from the "music" category
    /// and start it (looping). Pass <c>null</c> soundId to stop music.
    /// Applies the manifest entry's Volume automatically.
    /// </summary>
    public void PlayMusicById(string? soundId)
    {
        if (soundId == null) { StopMusic(); return; }

        if (!SoundRegistry.Instance.IsLoaded)
        {
            GD.PushWarning("[AudioManager] SoundRegistry not loaded; cannot play music.");
            return;
        }

        SoundEntry? entry = SoundRegistry.Instance.GetSound("music", soundId);
        if (entry == null) return;

        string? file = SoundRegistry.PickVariant(entry);
        if (file == null) return;

        string resPath = file.StartsWith("res://") ? file : "res://" + file;
        AudioStream? stream = GD.Load<AudioStream>(resPath);
        if (stream == null)
        {
            GD.PushWarning($"[AudioManager] Cannot load music file: {resPath}");
            return;
        }

        PlayMusic(stream, entry.Volume);
    }

    /// <summary>
    /// Stops all music playback.
    /// </summary>
    public void StopMusic()
    {
        _currentMusicStream      = null;
        _currentMusicEntryVolume = 1.0f;
        _musicPlayer?.Stop();
    }

    // ── Volume control ───────────────────────────────────────────────

    public void SetMasterVolume(float volume)
    {
        _masterVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
    }

    public void SetMusicVolume(float volume)
    {
        _musicVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
        if (_musicPlayer != null)
            _musicPlayer.VolumeDb = Mathf.LinearToDb(_currentMusicEntryVolume * _musicVolume * _masterVolume);
    }

    public void SetSfxVolume(float volume)
    {
        _sfxVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
    }

    public void SetUiVolume(float volume)
    {
        _uiVolume = Mathf.Clamp(volume, 0.0f, 1.0f);
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Called when the music track finishes. Restarts the same track to loop it.
    /// </summary>
    private void OnMusicFinished()
    {
        if (_currentMusicStream == null || _musicPlayer == null) return;
        _musicPlayer.Stream   = _currentMusicStream;
        _musicPlayer.VolumeDb = Mathf.LinearToDb(_currentMusicEntryVolume * _musicVolume * _masterVolume);
        _musicPlayer.Play();
    }

    // ── Settings persistence ─────────────────────────────────────────

    /// <summary>
    /// Loads volume settings saved by <see cref="CorditeWars.UI.OptionsMenu"/> from
    /// <c>user://settings.cfg</c> and applies them immediately.
    /// Called on <see cref="_Ready"/> so the correct volumes are active from
    /// the very first sound, without requiring the user to open the Options menu.
    /// </summary>
    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok)
            return; // No saved settings — keep defaults.

        _masterVolume = Mathf.Clamp((float)cfg.GetValue(SettingsSection, "master_volume", _masterVolume), 0f, 1f);
        _musicVolume  = Mathf.Clamp((float)cfg.GetValue(SettingsSection, "music_volume",  _musicVolume),  0f, 1f);
        _sfxVolume    = Mathf.Clamp((float)cfg.GetValue(SettingsSection, "sfx_volume",    _sfxVolume),    0f, 1f);
        _uiVolume     = Mathf.Clamp((float)cfg.GetValue(SettingsSection, "ui_volume",     _uiVolume),     0f, 1f);

        GD.Print($"[AudioManager] Loaded settings — master={_masterVolume:F2} music={_musicVolume:F2} sfx={_sfxVolume:F2} ui={_uiVolume:F2}");
    }
}
