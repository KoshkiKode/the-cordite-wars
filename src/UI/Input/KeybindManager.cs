using Godot;
using System.Collections.Generic;

namespace UnnamedRTS.UI.Input;

/// <summary>
/// Manages remappable keybinds. Stores action→key mappings, handles conflict
/// detection, and persists to user://settings.cfg under [Keybinds].
/// Intended to be created once and accessed via <see cref="Instance"/>.
/// </summary>
public sealed class KeybindManager
{
    private const string SettingsPath = "user://settings.cfg";
    private const string Section = "Keybinds";

    public static KeybindManager? Instance { get; private set; }

    // ── Game Actions ─────────────────────────────────────────────────

    /// <summary>All remappable game actions.</summary>
    public enum GameAction
    {
        AttackMove,
        Stop,
        HoldPosition,
        Patrol,
        CancelMode,
        ControlGroup1,
        ControlGroup2,
        ControlGroup3,
        ControlGroup4,
        ControlGroup5,
        ControlGroup6,
        ControlGroup7,
        ControlGroup8,
        ControlGroup9,
        ControlGroup0,
    }

    /// <summary>Human-readable labels shown in the keybind UI.</summary>
    public static string GetActionLabel(GameAction action) => action switch
    {
        GameAction.AttackMove => "Attack-Move",
        GameAction.Stop => "Stop",
        GameAction.HoldPosition => "Hold Position",
        GameAction.Patrol => "Patrol",
        GameAction.CancelMode => "Cancel / Deselect Mode",
        GameAction.ControlGroup1 => "Control Group 1",
        GameAction.ControlGroup2 => "Control Group 2",
        GameAction.ControlGroup3 => "Control Group 3",
        GameAction.ControlGroup4 => "Control Group 4",
        GameAction.ControlGroup5 => "Control Group 5",
        GameAction.ControlGroup6 => "Control Group 6",
        GameAction.ControlGroup7 => "Control Group 7",
        GameAction.ControlGroup8 => "Control Group 8",
        GameAction.ControlGroup9 => "Control Group 9",
        GameAction.ControlGroup0 => "Control Group 0",
        _ => action.ToString()
    };

    // ── Defaults ─────────────────────────────────────────────────────

    private static readonly Dictionary<GameAction, Key> DefaultBindings = new()
    {
        { GameAction.AttackMove,     Key.A },
        { GameAction.Stop,           Key.S },
        { GameAction.HoldPosition,   Key.H },
        { GameAction.Patrol,         Key.P },
        { GameAction.CancelMode,     Key.Escape },
        { GameAction.ControlGroup1,  Key.Key1 },
        { GameAction.ControlGroup2,  Key.Key2 },
        { GameAction.ControlGroup3,  Key.Key3 },
        { GameAction.ControlGroup4,  Key.Key4 },
        { GameAction.ControlGroup5,  Key.Key5 },
        { GameAction.ControlGroup6,  Key.Key6 },
        { GameAction.ControlGroup7,  Key.Key7 },
        { GameAction.ControlGroup8,  Key.Key8 },
        { GameAction.ControlGroup9,  Key.Key9 },
        { GameAction.ControlGroup0,  Key.Key0 },
    };

    // ── State ────────────────────────────────────────────────────────

    private readonly Dictionary<GameAction, Key> _bindings = new();

    // ── Lifecycle ────────────────────────────────────────────────────

    public KeybindManager()
    {
        Instance = this;
        ResetToDefaults();
        Load();
    }

    // ── Public API ───────────────────────────────────────────────────

    /// <summary>Get the currently-bound key for <paramref name="action"/>.</summary>
    public Key GetKey(GameAction action)
    {
        return _bindings.TryGetValue(action, out Key key) ? key : Key.None;
    }

    /// <summary>
    /// Returns the <see cref="GameAction"/> bound to <paramref name="key"/>,
    /// or <c>null</c> if no action uses that key.
    /// </summary>
    public GameAction? GetActionForKey(Key key)
    {
        foreach (var kvp in _bindings)
        {
            if (kvp.Value == key)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>
    /// Set a new key for <paramref name="action"/>.
    /// If another action already uses the key, the conflicting action is unbound (set to None).
    /// </summary>
    /// <returns>The <see cref="GameAction"/> that was displaced, or <c>null</c> if no conflict.</returns>
    public GameAction? SetKey(GameAction action, Key key)
    {
        GameAction? displaced = null;

        // Find conflict
        if (key != Key.None)
        {
            foreach (var kvp in _bindings)
            {
                if (kvp.Key != action && kvp.Value == key)
                {
                    displaced = kvp.Key;
                    _bindings[kvp.Key] = Key.None;
                    break;
                }
            }
        }

        _bindings[action] = key;
        return displaced;
    }

    /// <summary>Reset all bindings to factory defaults.</summary>
    public void ResetToDefaults()
    {
        _bindings.Clear();
        foreach (var kvp in DefaultBindings)
            _bindings[kvp.Key] = kvp.Value;
    }

    /// <summary>Check if <paramref name="key"/> matches the binding for <paramref name="action"/>.</summary>
    public bool IsAction(Key key, GameAction action)
    {
        return _bindings.TryGetValue(action, out Key bound) && bound == key;
    }

    /// <summary>Check if <paramref name="key"/> is any of the control group keys (0-9).</summary>
    public int GetControlGroupIndex(Key key)
    {
        for (int i = 0; i < 10; i++)
        {
            var groupAction = GameAction.ControlGroup1 + i;
            if (_bindings.TryGetValue(groupAction, out Key bound) && bound == key)
                return i;
        }
        return -1;
    }

    /// <summary>Get the default key for an action.</summary>
    public static Key GetDefaultKey(GameAction action)
    {
        return DefaultBindings.TryGetValue(action, out Key key) ? key : Key.None;
    }

    /// <summary>Human-readable name for a key.</summary>
    public static string GetKeyName(Key key)
    {
        if (key == Key.None) return "—";
        return OS.GetKeycodeString(key);
    }

    // ── Persistence ──────────────────────────────────────────────────

    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);

        foreach (var kvp in _bindings)
            cfg.SetValue(Section, kvp.Key.ToString(), (int)kvp.Value);

        var err = cfg.Save(SettingsPath);
        if (err != Error.Ok)
            GD.PrintErr($"[KeybindManager] Failed to save: {err}");
        else
            GD.Print("[KeybindManager] Keybinds saved.");
    }

    public void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok)
            return;

        if (!cfg.HasSection(Section))
            return;

        foreach (var action in DefaultBindings.Keys)
        {
            string actionName = action.ToString();
            if (cfg.HasSectionKey(Section, actionName))
            {
                int keyVal = (int)cfg.GetValue(Section, actionName, (int)DefaultBindings[action]);
                _bindings[action] = (Key)keyVal;
            }
        }

        GD.Print("[KeybindManager] Keybinds loaded.");
    }
}
