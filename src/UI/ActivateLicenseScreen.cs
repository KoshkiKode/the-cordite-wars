using Godot;
using CorditeWars.Core.Licensing;
using System.Threading.Tasks;

namespace CorditeWars.UI;

/// <summary>
/// License activation screen. Shown by <c>BootLoader</c> when the
/// in-game licensing gate determines the player has no valid entitlement
/// for this machine.
///
/// Builds its UI programmatically so the scene file stays minimal — the
/// screen itself has only an enter-key text field, an Activate button, an
/// "Enter offline activation blob" toggle, and a status label. On success
/// it transitions to the branding screen; on failure it shows the error.
/// </summary>
public partial class ActivateLicenseScreen : Control
{
    private LineEdit _keyEdit = null!;
    private Button _activateBtn = null!;
    private Label _statusLabel = null!;
    private TextEdit _offlineBlobEdit = null!;
    private Button _applyOfflineBtn = null!;
    private CheckButton _offlineToggle = null!;
    private Container _onlineContainer = null!;
    private Container _offlineContainer = null!;

    private LicenseGate _gate = null!;

    public override void _Ready()
    {
        BuildUi();
        _gate = BuildGate();
    }

    private static LicenseGate BuildGate()
    {
        string installDir = OS.GetExecutablePath() is { } exe && !string.IsNullOrEmpty(exe)
            ? System.IO.Path.GetDirectoryName(exe) ?? OS.GetUserDataDir()
            : OS.GetUserDataDir();
        string userDataDir = OS.GetUserDataDir();
        var store = new EntitlementStore(userDataDir, LicenseConfig.LicenseSigningPublicKey);
        return new LicenseGate(
            store,
            () => new LicenseClient(LicenseConfig.ApiBaseUrl),
            installDir);
    }

    private void BuildUi()
    {
        var bg = new ColorRect
        {
            Color = new Color(0.07f, 0.08f, 0.10f, 1),
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(bg);

        var center = new CenterContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(center);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
        center.AddChild(col);

        col.AddChild(new Label
        {
            Text = "Cordite Wars — License Activation",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        col.AddChild(new Label
        {
            Text = "Enter the 25-character license key from your purchase email.\n"
                 + "Up to 10 machines per license. Inactive machines free up after 30 days.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        col.AddChild(new HSeparator());

        _offlineToggle = new CheckButton { Text = "Offline activation (paste blob from website)" };
        _offlineToggle.Toggled += OnOfflineToggled;
        col.AddChild(_offlineToggle);

        // --- Online tab ---
        _onlineContainer = new VBoxContainer();
        col.AddChild(_onlineContainer);

        _keyEdit = new LineEdit
        {
            PlaceholderText = "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX",
            CustomMinimumSize = new Vector2(440, 0),
        };
        _keyEdit.TextSubmitted += text => { _ = ActivateClicked(); };
        _onlineContainer.AddChild(_keyEdit);

        _activateBtn = new Button { Text = "Activate" };
        _activateBtn.Pressed += () => _ = ActivateClicked();
        _onlineContainer.AddChild(_activateBtn);

        // --- Offline tab ---
        _offlineContainer = new VBoxContainer { Visible = false };
        col.AddChild(_offlineContainer);

        _offlineContainer.AddChild(new Label
        {
            Text =
                "1. On any device with internet, visit:\n"
              + $"     {LicenseConfig.ApiBaseUrl}/manage.html\n"
              + "2. Sign in with your purchase email + license key.\n"
              + "3. Click 'Get offline activation blob for this machine' and paste\n"
              + "   the resulting text below.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _offlineBlobEdit = new TextEdit
        {
            PlaceholderText = "Paste base64 entitlement blob here...",
            CustomMinimumSize = new Vector2(440, 120),
        };
        _offlineContainer.AddChild(_offlineBlobEdit);

        _applyOfflineBtn = new Button { Text = "Apply offline entitlement" };
        _applyOfflineBtn.Pressed += OnApplyOfflineClicked;
        _offlineContainer.AddChild(_applyOfflineBtn);

        // --- Status ---
        col.AddChild(new HSeparator());
        _statusLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        col.AddChild(_statusLabel);

        var quitBtn = new Button { Text = "Quit" };
        quitBtn.Pressed += () => GetTree().Quit();
        col.AddChild(quitBtn);
    }

    private void OnOfflineToggled(bool pressed)
    {
        _onlineContainer.Visible = !pressed;
        _offlineContainer.Visible = pressed;
        _statusLabel.Text = "";
    }

    private async Task ActivateClicked()
    {
        _activateBtn.Disabled = true;
        _statusLabel.Text = "Contacting server…";
        try
        {
            var result = await _gate.ActivateAsync(_keyEdit.Text);
            HandleResult(result);
        }
        finally
        {
            _activateBtn.Disabled = false;
        }
    }

    private void OnApplyOfflineClicked()
    {
        _statusLabel.Text = "Verifying entitlement…";
        var result = _gate.ApplyOfflineEntitlement(_offlineBlobEdit.Text);
        HandleResult(result);
    }

    private void HandleResult(LicenseGateResult result)
    {
        switch (result.Outcome)
        {
            case LicenseGateOutcome.AlreadyActivated:
                _statusLabel.Text =
                    $"Activated — slot {result.Entitlement?.SlotIndex}/10. Launching game…";
                GetTree().ChangeSceneToFile("res://scenes/UI/KoshkiKodeBrandingScreen.tscn");
                break;
            case LicenseGateOutcome.MachineCapReached:
                _statusLabel.Text =
                    $"This license is already active on 10 machines. "
                  + $"Visit {LicenseConfig.ApiBaseUrl}/manage.html to free a slot, then try again.";
                break;
            case LicenseGateOutcome.NeedsActivation:
            default:
                _statusLabel.Text = result.Message ?? "Activation failed.";
                break;
        }
    }
}
