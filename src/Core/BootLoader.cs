using Godot;
using CorditeWars.UI;
using CorditeWars.UI.Input;

namespace CorditeWars.Core;

/// <summary>
/// Boot scene script. Runs once at application start.
/// Handles initialization order, loading screen, and
/// transitions to the main menu.
/// </summary>
public partial class BootLoader : Node
{
    public override void _Ready()
    {
        string gameVersion = ProjectSettings.GetSetting("application/config/version", "0.1.0").AsString();
        string versionBuild = ProjectSettings.GetSetting("application/config/version_build", "").AsString();
        string versionDisplay = string.IsNullOrEmpty(versionBuild) ? $"v{gameVersion}" : $"v{gameVersion}-{versionBuild}";
        GD.Print("╔════════════════════════════════════════╗");
        GD.Print($"║   Cordite Wars: Six Fronts — {versionDisplay}");
        GD.Print("║   Godot 4.6 + C# / .NET 9             ║");
        GD.Print("╚════════════════════════════════════════╝");
        GD.Print("");

        // Verify autoloads are available
        var gameManager = GetNode<GameManager>("/root/GameManager");
        var eventBus = GetNode<EventBus>("/root/EventBus");

        if (gameManager == null || eventBus == null)
        {
            GD.PrintErr("[Boot] FATAL: Autoloads not found. Check project.godot.");
            GetTree().Quit(1);
            return;
        }

        // Initialize accessibility & keybind systems (settings loaded from disk)
        _ = new KeybindManager();
        _ = new AccessibilitySettings();
        GD.Print("[Boot] Accessibility and keybind systems initialized.");

        GD.Print("[Boot] All core systems initialized.");
        GD.Print("[Boot] Transitioning to splash screen...");

        GetTree().ChangeSceneToFile("res://scenes/UI/SplashScreen.tscn");
    }
}
