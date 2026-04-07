using Godot;

namespace UnnamedRTS.UI;

/// <summary>
/// Campaign faction selection: 6 faction cards in 3x2 grid.
/// Selecting a faction shows description panel below with Start/Continue buttons.
/// </summary>
public partial class CampaignSelect : Control
{
    private static readonly string[] FactionDescriptionKeys =
    {
        "FACTION_DESC_VALKYR",
        "FACTION_DESC_KRAGMORE",
        "FACTION_DESC_BASTION",
        "FACTION_DESC_ARCLOFT",
        "FACTION_DESC_IRONMARCH",
        "FACTION_DESC_STORMREND"
    };

    private static readonly string[] CampaignNameKeys =
    {
        "CAMPAIGN_SOVEREIGN_SKIES", "CAMPAIGN_CRIMSON_TIDE", "CAMPAIGN_IRON_BASTION",
        "CAMPAIGN_SILENT_WATCH", "CAMPAIGN_STEEL_MARCH", "CAMPAIGN_STORMS_FURY"
    };

    private static readonly string[] CommanderNames =
    {
        "Wing Commander Aelara", "Warlord Grok", "Castellan Mira",
        "Watcher Prime Idris", "Marshal Volkov", "Tempest Kael"
    };

    private static readonly int[] MissionCounts = { 8, 9, 7, 8, 8, 8 };

    private int _selectedFaction = -1;
    private Panel[] _factionCards = new Panel[6];
    private Label _campaignTitle = null!;
    private Label _campaignDesc = null!;
    private Label _campaignCommander = null!;
    private Label _campaignMissions = null!;
    private Button _startBtn = null!;
    private Button _continueBtn = null!;
    private VBoxContainer _detailPanel = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Background
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Outer margin
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 80);
        margin.AddThemeConstantOverride("margin_right", 80);
        margin.AddThemeConstantOverride("margin_top", 40);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        AddChild(margin);

        var outerVBox = new VBoxContainer();
        outerVBox.AddThemeConstantOverride("separation", 24);
        margin.AddChild(outerVBox);

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 20);
        outerVBox.AddChild(header);

        var backBtn = new Button();
        backBtn.Text = Tr("OPTIONS_BACK");
        UITheme.StyleButton(backBtn);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/UI/MainMenu.tscn");
        header.AddChild(backBtn);

        var headerSpacer = new Control();
        headerSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(headerSpacer);

        var title = new Label();
        title.Text = Tr("CAMPAIGN_SELECT_TITLE");
        UITheme.StyleLabel(title, UITheme.FontSizeHeading, UITheme.Accent);
        header.AddChild(title);

        // Faction grid: 3x2
        var gridContainer = new GridContainer();
        gridContainer.Columns = 3;
        gridContainer.AddThemeConstantOverride("h_separation", 16);
        gridContainer.AddThemeConstantOverride("v_separation", 16);
        outerVBox.AddChild(gridContainer);

        for (int i = 0; i < 6; i++)
        {
            var card = BuildFactionCard(i);
            _factionCards[i] = card;
            gridContainer.AddChild(card);
        }

        // Detail panel (hidden until selection)
        _detailPanel = new VBoxContainer();
        _detailPanel.AddThemeConstantOverride("separation", 8);
        _detailPanel.Visible = false;
        outerVBox.AddChild(_detailPanel);

        // Separator
        var sep = new HSeparator();
        sep.AddThemeColorOverride("separator", UITheme.Border);
        _detailPanel.AddChild(sep);

        // Campaign info row
        var infoRow = new HBoxContainer();
        infoRow.AddThemeConstantOverride("separation", 24);
        _detailPanel.AddChild(infoRow);

        var infoLeft = new VBoxContainer();
        infoLeft.AddThemeConstantOverride("separation", 4);
        infoLeft.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        infoRow.AddChild(infoLeft);

        _campaignTitle = new Label();
        UITheme.StyleLabel(_campaignTitle, UITheme.FontSizeLarge, UITheme.Accent);
        infoLeft.AddChild(_campaignTitle);

        _campaignMissions = new Label();
        UITheme.StyleLabel(_campaignMissions, UITheme.FontSizeNormal, UITheme.TextSecondary);
        infoLeft.AddChild(_campaignMissions);

        _campaignDesc = new Label();
        _campaignDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(_campaignDesc, UITheme.FontSizeNormal, UITheme.TextPrimary);
        infoLeft.AddChild(_campaignDesc);

        _campaignCommander = new Label();
        UITheme.StyleLabel(_campaignCommander, UITheme.FontSizeSmall, UITheme.TextMuted);
        infoLeft.AddChild(_campaignCommander);

        // Buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 16);
        _detailPanel.AddChild(btnRow);

        _startBtn = new Button();
        _startBtn.Text = Tr("CAMPAIGN_START");
        _startBtn.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleAccentButton(_startBtn);
        _startBtn.Pressed += OnStartPressed;
        btnRow.AddChild(_startBtn);

        _continueBtn = new Button();
        _continueBtn.Text = string.Format(Tr("CAMPAIGN_CONTINUE"), 1);
        _continueBtn.CustomMinimumSize = new Vector2(200, 0);
        UITheme.StyleButton(_continueBtn);
        _continueBtn.Pressed += OnContinuePressed;
        btnRow.AddChild(_continueBtn);

        // Select first faction by default
        SelectFaction(0);
    }

    private Panel BuildFactionCard(int index)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(200, 120);
        panel.AddThemeStyleboxOverride("panel", UITheme.MakeFactionCard(UITheme.GetFactionColor(index), false));

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 4);
        panel.AddChild(vbox);

        // Margin inside
        var innerMargin = new MarginContainer();
        innerMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        innerMargin.AddThemeConstantOverride("margin_left", 12);
        innerMargin.AddThemeConstantOverride("margin_right", 12);
        innerMargin.AddThemeConstantOverride("margin_top", 12);
        innerMargin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(innerMargin);

        var innerVBox = new VBoxContainer();
        innerVBox.AddThemeConstantOverride("separation", 4);
        innerMargin.AddChild(innerVBox);

        var nameLabel = new Label();
        nameLabel.Text = UITheme.FactionNames[index];
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(nameLabel, UITheme.FontSizeLarge, UITheme.GetFactionColor(index));
        innerVBox.AddChild(nameLabel);

        var missionLabel = new Label();
        missionLabel.Text = $"({MissionCounts[index]} missions)";
        missionLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(missionLabel, UITheme.FontSizeSmall, UITheme.TextSecondary);
        innerVBox.AddChild(missionLabel);

        // Progress stars (placeholder — all empty for now)
        var starsLabel = new Label();
        starsLabel.Text = "\u2606\u2606\u2606";
        starsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(starsLabel, UITheme.FontSizeNormal, UITheme.TextMuted);
        innerVBox.AddChild(starsLabel);

        // Make it clickable with a transparent button overlay
        var clickBtn = new Button();
        clickBtn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        clickBtn.Flat = true;
        clickBtn.MouseDefaultCursorShape = CursorShape.PointingHand;
        int idx = index;
        clickBtn.Pressed += () => SelectFaction(idx);
        panel.AddChild(clickBtn);

        return panel;
    }

    private void SelectFaction(int index)
    {
        _selectedFaction = index;

        // Update card borders
        for (int i = 0; i < 6; i++)
        {
            _factionCards[i].AddThemeStyleboxOverride("panel",
                UITheme.MakeFactionCard(UITheme.GetFactionColor(i), i == index));
        }

        // Update detail panel
        _detailPanel.Visible = true;
        _campaignTitle.Text = string.Format(Tr("CAMPAIGN_TITLE_FMT"), Tr(CampaignNameKeys[index]));
        _campaignMissions.Text = string.Format(Tr("CAMPAIGN_MISSIONS_FMT"), MissionCounts[index]);
        _campaignDesc.Text = $"\"{Tr(FactionDescriptionKeys[index])}\"";
        _campaignCommander.Text = string.Format(Tr("CAMPAIGN_COMMANDER_FMT"), CommanderNames[index]);
        _continueBtn.Text = string.Format(Tr("CAMPAIGN_CONTINUE"), 1); // Would track real progress
    }

    private void OnStartPressed()
    {
        if (_selectedFaction < 0) return;
        GD.Print($"[CampaignSelect] Starting campaign: {UITheme.FactionNames[_selectedFaction]}");
        // TODO: Transition to first campaign mission
    }

    private void OnContinuePressed()
    {
        if (_selectedFaction < 0) return;
        GD.Print($"[CampaignSelect] Continuing campaign: {UITheme.FactionNames[_selectedFaction]}");
        // TODO: Transition to saved mission
    }
}
