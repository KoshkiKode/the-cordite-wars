using CorditeWars.Game.Campaign;

namespace CorditeWars.Tests.Game.Campaign;

/// <summary>
/// Tests for FactionCampaignProgress and AllCampaignProgress —
/// the pure-data campaign progress model (no Godot runtime required).
/// </summary>
public class CampaignProgressTests
{
    // ═══════════════════════════════════════════════════════════════════
    // FactionCampaignProgress.IsCompleted
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsCompleted_ReturnsFalse_WhenMissionNotDone()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        Assert.False(progress.IsCompleted("arcloft_01"));
    }

    [Fact]
    public void IsCompleted_ReturnsTrue_AfterRecordCompletion()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        progress.RecordCompletion("arcloft_01", 2);
        Assert.True(progress.IsCompleted("arcloft_01"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // FactionCampaignProgress.GetStars
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetStars_ReturnsZero_ForUncompletedMission()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        Assert.Equal(0, progress.GetStars("arcloft_01"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GetStars_ReturnsRecordedStars(int stars)
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        progress.RecordCompletion("arcloft_01", stars);
        Assert.Equal(stars, progress.GetStars("arcloft_01"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // FactionCampaignProgress.RecordCompletion — first-time flag
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RecordCompletion_ReturnsTrue_OnFirstCompletion()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        bool firstTime = progress.RecordCompletion("arcloft_01", 1);
        Assert.True(firstTime);
    }

    [Fact]
    public void RecordCompletion_ReturnsFalse_OnSubsequentCompletion()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        progress.RecordCompletion("arcloft_01", 1);
        bool firstTime = progress.RecordCompletion("arcloft_01", 2);
        Assert.False(firstTime);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FactionCampaignProgress.RecordCompletion — star improvement logic
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RecordCompletion_ImprovesStars_WhenNewResultIsBetter()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        progress.RecordCompletion("arcloft_01", 1);
        progress.RecordCompletion("arcloft_01", 3);
        Assert.Equal(3, progress.GetStars("arcloft_01"));
    }

    [Fact]
    public void RecordCompletion_DoesNotDowngradeStars()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        progress.RecordCompletion("arcloft_01", 3);
        progress.RecordCompletion("arcloft_01", 1);
        Assert.Equal(3, progress.GetStars("arcloft_01"));
    }

    [Fact]
    public void RecordCompletion_SameStar_KeepsCurrent()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        progress.RecordCompletion("arcloft_01", 2);
        progress.RecordCompletion("arcloft_01", 2);
        Assert.Equal(2, progress.GetStars("arcloft_01"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // FactionCampaignProgress — multiple missions
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleMissions_TrackedIndependently()
    {
        var progress = new FactionCampaignProgress { FactionId = "arcloft" };
        progress.RecordCompletion("arcloft_01", 3);
        progress.RecordCompletion("arcloft_02", 1);

        Assert.True(progress.IsCompleted("arcloft_01"));
        Assert.True(progress.IsCompleted("arcloft_02"));
        Assert.False(progress.IsCompleted("arcloft_03"));

        Assert.Equal(3, progress.GetStars("arcloft_01"));
        Assert.Equal(1, progress.GetStars("arcloft_02"));
        Assert.Equal(0, progress.GetStars("arcloft_03"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // AllCampaignProgress.GetOrCreate
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetOrCreate_ReturnsEmptyProgress_ForNewFaction()
    {
        var all = new AllCampaignProgress();
        var factionProgress = all.GetOrCreate("arcloft");

        Assert.NotNull(factionProgress);
        Assert.Equal("arcloft", factionProgress.FactionId);
        Assert.Empty(factionProgress.CompletedMissions);
    }

    [Fact]
    public void GetOrCreate_ReturnsSameInstance_OnSecondCall()
    {
        var all = new AllCampaignProgress();
        var first = all.GetOrCreate("arcloft");
        first.RecordCompletion("arcloft_01", 2);

        var second = all.GetOrCreate("arcloft");
        Assert.True(second.IsCompleted("arcloft_01"),
            "GetOrCreate should return the same faction progress object");
    }

    [Fact]
    public void GetOrCreate_DifferentFactions_AreIndependent()
    {
        var all = new AllCampaignProgress();
        all.GetOrCreate("arcloft").RecordCompletion("arcloft_01", 3);
        var bastion = all.GetOrCreate("bastion");

        Assert.False(bastion.IsCompleted("arcloft_01"),
            "Bastion progress should not be polluted by Arcloft missions");
    }

    [Fact]
    public void AllCampaignProgress_StartsWithVersion1()
    {
        var all = new AllCampaignProgress();
        Assert.Equal(1, all.Version);
    }

    [Fact]
    public void GetOrCreate_AddsToFactionsDictionary()
    {
        var all = new AllCampaignProgress();
        all.GetOrCreate("arcloft");
        all.GetOrCreate("bastion");
        Assert.Equal(2, all.Factions.Count);
        Assert.True(all.Factions.ContainsKey("arcloft"));
        Assert.True(all.Factions.ContainsKey("bastion"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // CampaignMission.WinCondition mapping
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WinCondition_DefaultsToDestroyHQ_ForUnknownId()
    {
        var mission = new CampaignMission { WinConditionId = "unknown" };
        Assert.Equal(WinCondition.DestroyHQ, mission.WinCondition);
    }

    [Fact]
    public void WinCondition_DefaultsToDestroyHQ_WhenIdIsDestroyHq()
    {
        var mission = new CampaignMission { WinConditionId = "destroy_hq" };
        Assert.Equal(WinCondition.DestroyHQ, mission.WinCondition);
    }

    [Fact]
    public void WinCondition_MapsToKillAllUnits()
    {
        var mission = new CampaignMission { WinConditionId = "kill_all_units" };
        Assert.Equal(WinCondition.KillAllUnits, mission.WinCondition);
    }
}
