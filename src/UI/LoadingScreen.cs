using Godot;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CorditeWars.Game.Assets;
using CorditeWars.Game.Factions;
using CorditeWars.Game.Tech;
using CorditeWars.Game.World;
using CorditeWars.Systems.Audio;
using CorditeWars.Systems.Graphics;
using CorditeWars.Systems.Localization;

namespace CorditeWars.UI;

/// <summary>
/// Progress bar loading screen. Loads all registries in sequence,
/// updating progress bar and status text. Transitions to MainMenu when done.
/// </summary>
public partial class LoadingScreen : Control
{
    private const string NextScene = "res://scenes/UI/MainMenu.tscn";
    private const float TipRotationIntervalSeconds = 5f;

    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private Label _tipLabel = null!;
    private int _currentStep;
    private bool _loadingComplete;
    private int _currentTipIndex;
    private string[] _tipRotationList = null!;
    private CancellationTokenSource? _tipRotationCts;

    private static readonly string[] LoadingTips =
    {
        "Queue commands with Shift so units keep moving while you manage other fronts.",
        "Scout early and often; vision wins battles before weapons do.",
        "Spend Cordite steadily instead of stockpiling it for too long.",
        "Build production before you need it so reinforcements arrive on time.",
        "Control groups make multi-front fights much easier to manage.",
        "Flank static defenses instead of charging straight into them.",
        "Capture resource points quickly to create long-term economic pressure.",
        "Pull damaged units back; preserving veterancy improves your army over time.",
        "Use terrain choke points to force favorable engagements.",
        "Expand to a second base before your first one is fully saturated.",
        "Keep anti-air with your army even when skies look clear.",
        "A balanced army is safer than overcommitting to one unit type.",
        "Use quick raids to interrupt enemy economy and tech timings.",
        "Hotkeys save seconds, and seconds win close battles.",
        "Position artillery behind line-of-sight support for maximum impact.",
        "Pressure multiple fronts to split enemy attention.",
        "Reactors and tech structures are high-value strategic targets.",
        "Keep production queues active so your army never stalls.",
        "Defend supply lines and harvesters to protect your momentum.",
        "Retreating early can be better than losing an entire force.",
        "Always have a plan for detection against stealth threats.",
        "Air units are strongest when paired with ground spotting.",
        "Drop map markers and pings to keep team coordination tight.",
        "Reinforce from forward positions to reduce travel downtime.",
        "When ahead, secure objectives and deny comeback opportunities.",
    };

    private struct LoadStep
    {
        public string StatusKey;
        public float TargetPercent;
    }

    private static readonly LoadStep[] Steps =
    {
        new() { StatusKey = "LOADING_DETECTING_HARDWARE",  TargetPercent = 5 },
        new() { StatusKey = "LOADING_UNIT_DATA",           TargetPercent = 15 },
        new() { StatusKey = "LOADING_BUILDING_DATA",       TargetPercent = 25 },
        new() { StatusKey = "LOADING_ASSET_MANIFEST",      TargetPercent = 35 },
        new() { StatusKey = "LOADING_BUILDING_MANIFEST",   TargetPercent = 45 },
        new() { StatusKey = "LOADING_TERRAIN_MANIFEST",    TargetPercent = 55 },
        new() { StatusKey = "LOADING_UPGRADE_DATA",        TargetPercent = 65 },
        new() { StatusKey = "LOADING_MAPS",                TargetPercent = 75 },
        new() { StatusKey = "LOADING_SOUND_MANIFEST",      TargetPercent = 85 },
        new() { StatusKey = "LOADING_FACTION_DATA",        TargetPercent = 95 },
        new() { StatusKey = "LOADING_READY",               TargetPercent = 100 },
    };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Background
        var bg = new ColorRect();
        bg.Color = UITheme.Background;
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Main layout
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 200);
        margin.AddThemeConstantOverride("margin_right", 200);
        margin.AddThemeConstantOverride("margin_top", 0);
        margin.AddThemeConstantOverride("margin_bottom", 80);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 20);
        margin.AddChild(vbox);

        // LOADING text
        var loadingLabel = new Label();
        loadingLabel.Text = Tr("LOADING_TITLE");
        loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(loadingLabel, UITheme.FontSizeHeading, UITheme.TextPrimary);
        vbox.AddChild(loadingLabel);

        // Progress bar
        _progressBar = new ProgressBar();
        _progressBar.MinValue = 0;
        _progressBar.MaxValue = 100;
        _progressBar.Value = 0;
        _progressBar.CustomMinimumSize = new Vector2(0, 24);
        _progressBar.ShowPercentage = false;
        UITheme.StyleProgressBar(_progressBar);
        vbox.AddChild(_progressBar);

        // Status text
        _statusLabel = new Label();
        _statusLabel.Text = "";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        UITheme.StyleLabel(_statusLabel, UITheme.FontSizeNormal, UITheme.TextSecondary);
        vbox.AddChild(_statusLabel);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 60);
        vbox.AddChild(spacer);

        // Tip text at bottom
        _tipLabel = new Label();
        _tipLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _tipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UITheme.StyleLabel(_tipLabel, UITheme.FontSizeSmall, UITheme.TextMuted);
        _tipLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide, LayoutPresetMode.KeepSize);
        _tipLabel.OffsetTop = -60;
        _tipLabel.OffsetLeft = 200;
        _tipLabel.OffsetRight = -200;
        AddChild(_tipLabel);

        InitializeTips();
        _tipRotationCts = new CancellationTokenSource();
        _ = RotateTipsOverTime(_tipRotationCts.Token);

        // Start loading on next frame to let UI render first
        CallDeferred(MethodName.StartLoading);
    }

    public override void _ExitTree()
    {
        _tipRotationCts?.Cancel();
        _tipRotationCts?.Dispose();
        _tipRotationCts = null;
    }

    private async void StartLoading()
    {
        for (_currentStep = 0; _currentStep < Steps.Length; _currentStep++)
        {
            _statusLabel.Text = Tr(Steps[_currentStep].StatusKey);

            // Animate progress bar
            var tween = CreateTween();
            tween.TweenProperty(_progressBar, "value", (double)Steps[_currentStep].TargetPercent, 0.15f);
            await ToSignal(tween, Tween.SignalName.Finished);

            // Execute the actual loading step
            ExecuteStep(_currentStep);
            AdvanceTip();

            // Yield a frame between steps so the UI stays responsive
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        _loadingComplete = true;

        // Wait a moment at 100% then transition
        await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
        SceneTransition.TransitionTo(GetTree(), NextScene);
    }

    private void ExecuteStep(int step)
    {
        switch (step)
        {
            case 0: // Detect hardware + load translations
                LocalizationManager.Instance?.LoadAllTranslations();
                var qm = QualityManager.Instance;
                if (qm != null)
                    qm.AutoDetect();
                else
                    GD.Print("[LoadingScreen] QualityManager not available, skipping auto-detect.");
                break;

            case 1: // Unit data — FactionRegistry handles this in LoadAll
                GD.Print("[LoadingScreen] Unit data will be loaded with faction registry.");
                break;

            case 2: // Building data — FactionRegistry handles this in LoadAll
                GD.Print("[LoadingScreen] Building data will be loaded with faction registry.");
                break;

            case 3: // Asset manifest
                try
                {
                    var assetReg = new AssetRegistry();
                    assetReg.Load("res://data/asset_manifest.json");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Asset manifest not found, skipping.");
                }
                break;

            case 4: // Building manifest
                try
                {
                    var buildingManifest = new BuildingManifest();
                    buildingManifest.Load("res://data/building_manifest.json");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Building manifest not found, skipping.");
                }
                break;

            case 5: // Terrain manifest
                try
                {
                    var terrainManifest = new TerrainManifest();
                    terrainManifest.Load("res://data/terrain_manifest.json");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Terrain manifest not found, skipping.");
                }
                break;

            case 6: // Upgrade data
                try
                {
                    var upgradeReg = new UpgradeRegistry();
                    upgradeReg.Load("res://data/upgrades");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Upgrade data not found, skipping.");
                }
                break;

            case 7: // Maps
                try
                {
                    var mapLoader = new MapLoader();
                    mapLoader.LoadAllMaps("res://data/maps");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Map data not found, skipping.");
                }
                break;

            case 8: // Sound manifest
                try
                {
                    SoundRegistry.Instance.Load("res://data/sound_manifest.json");
                    // Start loading music now that the registry is available
                    GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlayMusicById("music_loading");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Sound manifest not found, skipping.");
                }
                break;

            case 9: // Faction data (loads factions + units + buildings)
                try
                {
                    var factionReg = new FactionRegistry();
                    factionReg.LoadAll("res://data/factions", "res://data/units", "res://data/buildings");
                }
                catch
                {
                    GD.PushWarning("[LoadingScreen] Faction data not found, skipping.");
                }
                break;

            case 10: // Ready!
                GD.Print("[LoadingScreen] All registries loaded.");
                break;
        }
    }

    private void InitializeTips()
    {
        _tipRotationList = (string[])LoadingTips.Clone();
        ShuffleTips(_tipRotationList);
        _currentTipIndex = 0;
        RefreshTipLabel();
    }

    private void AdvanceTip()
    {
        if (_tipRotationList.Length == 0)
            return;

        _currentTipIndex++;
        if (_currentTipIndex >= _tipRotationList.Length)
        {
            ShuffleTips(_tipRotationList);
            _currentTipIndex = 0;
        }

        RefreshTipLabel();
    }

    private async Task RotateTipsOverTime(CancellationToken cancellationToken)
    {
        while (!_loadingComplete && !cancellationToken.IsCancellationRequested)
        {
            if (!IsInsideTree())
                break;

            SceneTree? tree = GetTree();
            if (tree == null)
                break;

            await ToSignal(tree.CreateTimer(TipRotationIntervalSeconds), SceneTreeTimer.SignalName.Timeout);

            if (_loadingComplete || cancellationToken.IsCancellationRequested || !IsInsideTree())
                break;

            AdvanceTip();
        }
    }

    private void RefreshTipLabel()
    {
        if (!IsInsideTree() || !GodotObject.IsInstanceValid(_tipLabel))
            return;

        _tipLabel.Text = Tr("LOADING_TIP_PREFIX") + " " + _tipRotationList[_currentTipIndex];
    }

    private static void ShuffleTips(IList<string> tips)
    {
        for (int i = tips.Count - 1; i > 0; i--)
        {
            int j = GD.RandRange(0, i);
            (tips[i], tips[j]) = (tips[j], tips[i]);
        }
    }
}
