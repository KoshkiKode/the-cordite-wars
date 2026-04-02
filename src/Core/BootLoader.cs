using Godot;

namespace UnnamedRTS.Core;

/// <summary>
/// Boot scene script. Runs once at application start.
/// Handles initialization order, loading screen, and
/// transitions to the main menu.
/// </summary>
public partial class BootLoader : Node
{
    public override void _Ready()
    {
        GD.Print("╔════════════════════════════════════════╗");
        GD.Print("║   Cordite Wars: Six Fronts — v0.1.0    ║");
        GD.Print("║   Godot 4.4 + C# | Forward Plus       ║");
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

        GD.Print("[Boot] All core systems initialized.");
        GD.Print("[Boot] Transitioning to splash screen...");

        GetTree().ChangeSceneToFile("res://scenes/UI/SplashScreen.tscn");
    }
}
