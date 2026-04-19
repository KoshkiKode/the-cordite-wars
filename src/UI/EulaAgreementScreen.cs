using Godot;

namespace CorditeWars.UI;

/// <summary>
/// First-run EULA acceptance gate shown during setup before entering the game flow.
/// </summary>
public partial class EulaAgreementScreen : Control
{
    private const string SettingsPath = "user://settings.cfg";
    private const string SettingsSection = "Legal";
    private const string SettingsKeyAccepted = "eula_accepted";
    private const string SettingsKeyAcceptedVersion = "eula_accepted_version";
    private const string RequiredEulaVersion = "2026-04-18";

    private const string EulaPath = "res://versions/windows/EULA.txt";
    private const string WebsiteUrl = "https://koshkikode.com";
    private const string NextScene = "res://scenes/UI/LoadingScreen.tscn";

    private Button _acceptButton = null!;
    private CheckBox _agreeCheck = null!;

    public static bool IsAcceptanceRequired()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok)
            return true;

        bool accepted = cfg.GetValue(SettingsSection, SettingsKeyAccepted, false).AsBool();
        string acceptedVersion = cfg.GetValue(SettingsSection, SettingsKeyAcceptedVersion, "").AsString();

        return !accepted || !string.Equals(acceptedVersion, RequiredEulaVersion, System.StringComparison.Ordinal);
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 80);
        margin.AddThemeConstantOverride("margin_right", 80);
        margin.AddThemeConstantOverride("margin_top", 40);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        var title = new Label();
        title.Text = "END USER LICENSE AGREEMENT";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        root.AddChild(title);

        var subtitle = new Label();
        subtitle.Text = "You must accept this agreement to continue setup. For issues, visit https://koshkikode.com.";
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(subtitle, UITheme.FontSizeSmall, UITheme.TextSecondary);
        root.AddChild(subtitle);

        var textPanel = new PanelContainer();
        textPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        UITheme.StylePanel(textPanel);
        root.AddChild(textPanel);

        var eulaText = new RichTextLabel();
        eulaText.BbcodeEnabled = false;
        eulaText.ScrollActive = true;
        eulaText.FitContent = false;
        eulaText.SelectionEnabled = true;
        eulaText.Text = LoadEulaText();
        textPanel.AddChild(eulaText);

        _agreeCheck = new CheckBox();
        _agreeCheck.Text = "I have read and agree to the End User License Agreement.";
        _agreeCheck.Toggled += OnAgreeToggled;
        root.AddChild(_agreeCheck);

        var buttonRow = new HBoxContainer();
        buttonRow.Alignment = BoxContainer.AlignmentMode.End;
        buttonRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(buttonRow);

        var websiteButton = new Button();
        websiteButton.Text = "Open koshkikode.com";
        UITheme.StyleMenuButton(websiteButton);
        websiteButton.Pressed += () => OS.ShellOpen(WebsiteUrl);
        buttonRow.AddChild(websiteButton);

        var declineButton = new Button();
        declineButton.Text = "Decline";
        UITheme.StyleMenuButton(declineButton);
        declineButton.Pressed += OnDeclinePressed;
        buttonRow.AddChild(declineButton);

        _acceptButton = new Button();
        _acceptButton.Text = "Accept and Continue";
        UITheme.StyleMenuButton(_acceptButton);
        _acceptButton.Disabled = true;
        _acceptButton.Pressed += OnAcceptPressed;
        buttonRow.AddChild(_acceptButton);
    }

    private static string LoadEulaText()
    {
        using var file = FileAccess.Open(EulaPath, FileAccess.ModeFlags.Read);
        if (file != null)
            return file.GetAsText();

        return
            "Unable to load full EULA text from the packaged file.\n\n" +
            "Please review the latest agreement at https://koshkikode.com before continuing.\n" +
            "If you do not agree, choose Decline.";
    }

    private void OnAgreeToggled(bool isChecked)
    {
        _acceptButton.Disabled = !isChecked;
    }

    private void OnAcceptPressed()
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue(SettingsSection, SettingsKeyAccepted, true);
        cfg.SetValue(SettingsSection, SettingsKeyAcceptedVersion, RequiredEulaVersion);
        cfg.Save(SettingsPath);
        GetTree().ChangeSceneToFile(NextScene);
    }

    private void OnDeclinePressed()
    {
        GetTree().Quit();
    }
}
