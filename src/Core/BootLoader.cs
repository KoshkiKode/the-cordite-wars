using Godot;
using CorditeWars.Core.Licensing;
using CorditeWars.UI;
using CorditeWars.UI.Input;
using CorditeWars.Systems.Graphics;

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
        var qualityManager = GetNode<QualityManager>("/root/QualityManager");

        if (gameManager == null || eventBus == null || qualityManager == null)
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

        // License gate. When LicenseConfig.IsEnabled is false (default for
        // source-tree builds with the placeholder key), this is a no-op.
        if (RunLicenseGateBlocking() == false)
        {
            GD.Print("[Boot] License gate requires user input — switching to activation scene.");
            GetTree().ChangeSceneToFile("res://scenes/UI/ActivateLicense.tscn");
            return;
        }

        GD.Print("[Boot] Transitioning to branding screen...");
        GetTree().ChangeSceneToFile("res://scenes/UI/KoshkiKodeBrandingScreen.tscn");
    }

    /// <summary>
    /// Runs the license gate synchronously. Returns true to proceed to the
    /// main menu, false if the activation UI needs to be shown.
    /// </summary>
    private static bool RunLicenseGateBlocking()
    {
        if (!LicenseConfig.IsEnabled)
        {
            GD.Print("[Boot] License gate disabled (no signing key baked in).");
            return true;
        }

        try
        {
            string installDir = OS.GetExecutablePath() is { } exe && !string.IsNullOrEmpty(exe)
                ? System.IO.Path.GetDirectoryName(exe) ?? OS.GetUserDataDir()
                : OS.GetUserDataDir();
            string userDataDir = OS.GetUserDataDir();

            var store = new EntitlementStore(userDataDir, LicenseConfig.LicenseSigningPublicKey);
            var gate = new LicenseGate(
                store,
                () => new LicenseClient(LicenseConfig.ApiBaseUrl),
                installDir);

            // Boot is single-threaded; block on the gate. The gate's heavy
            // lifting (silent-renewal HTTP) happens on a background task it
            // spawns itself, so this call is fast.
            var result = gate.RunAsync().GetAwaiter().GetResult();
            switch (result.Outcome)
            {
                case LicenseGateOutcome.SkippedForStorefront:
                    GD.Print($"[Boot] {result.Message}");
                    return true;
                case LicenseGateOutcome.AlreadyActivated:
                    GD.Print(
                        $"[Boot] License OK — slot {result.Entitlement?.SlotIndex}/10, " +
                        $"expires {result.Entitlement?.ExpiresAtUtc:yyyy-MM-dd}.");
                    return true;
                case LicenseGateOutcome.NeedsActivation:
                case LicenseGateOutcome.MachineCapReached:
                default:
                    GD.Print($"[Boot] License gate result: {result.Outcome} ({result.Message}).");
                    return false;
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Boot] License gate threw — failing closed: {ex}");
            return false;
        }
    }
}
