using System.Collections.Generic;
using CorditeWars.UI.Minimap;

namespace CorditeWars.Tests.UI.Minimap;

/// <summary>
/// Tests for <see cref="MinimapPing"/> and <see cref="MinimapPingSystem"/>.
/// All tests are Godot-free — pure tick-math and collection management.
/// </summary>
public class MinimapPingSystemTests
{
    // ══════════════════════════════════════════════════════════════════
    // MinimapPing — constructor and field storage
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MinimapPing_Constructor_StoresAllFields()
    {
        var ping = new MinimapPing(12, 34, PingType.Attack, playerIndex: 1,
                                   startTick: 100, durationTicks: 60);

        Assert.Equal(12, ping.GridX);
        Assert.Equal(34, ping.GridY);
        Assert.Equal(PingType.Attack, ping.Type);
        Assert.Equal(1, ping.PlayerIndex);
        Assert.Equal(100UL, ping.StartTick);
        Assert.Equal(60UL, ping.DurationTicks);
    }

    [Fact]
    public void MinimapPing_DefaultDuration_Is90Ticks()
    {
        var ping = new MinimapPing(0, 0, PingType.Beacon, 0, startTick: 0);
        Assert.Equal(90UL, ping.DurationTicks);
    }

    // ══════════════════════════════════════════════════════════════════
    // MinimapPing.IsExpired
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsExpired_ReturnsFalse_BeforeExpiry()
    {
        var ping = new MinimapPing(0, 0, PingType.Alert, 0, startTick: 0, durationTicks: 90);
        Assert.False(ping.IsExpired(currentTick: 89));
    }

    [Fact]
    public void IsExpired_ReturnsTrue_AtExactExpiry()
    {
        var ping = new MinimapPing(0, 0, PingType.Alert, 0, startTick: 0, durationTicks: 90);
        Assert.True(ping.IsExpired(currentTick: 90));
    }

    [Fact]
    public void IsExpired_ReturnsTrue_AfterExpiry()
    {
        var ping = new MinimapPing(0, 0, PingType.Alert, 0, startTick: 10, durationTicks: 30);
        Assert.True(ping.IsExpired(currentTick: 100));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenCurrentTickBeforeStart()
    {
        var ping = new MinimapPing(0, 0, PingType.Attack, 0, startTick: 50, durationTicks: 30);
        Assert.False(ping.IsExpired(currentTick: 30));
    }

    // ══════════════════════════════════════════════════════════════════
    // MinimapPing.ElapsedTicks
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ElapsedTicks_ReturnsZero_AtCreationTick()
    {
        var ping = new MinimapPing(0, 0, PingType.Beacon, 0, startTick: 100);
        Assert.Equal(0UL, ping.ElapsedTicks(100));
    }

    [Fact]
    public void ElapsedTicks_ReturnsCorrectElapsed_AfterSomeTicks()
    {
        var ping = new MinimapPing(0, 0, PingType.Beacon, 0, startTick: 100);
        Assert.Equal(45UL, ping.ElapsedTicks(145));
    }

    [Fact]
    public void ElapsedTicks_ReturnsZero_WhenCurrentTickBeforeStart()
    {
        var ping = new MinimapPing(0, 0, PingType.Beacon, 0, startTick: 100);
        Assert.Equal(0UL, ping.ElapsedTicks(50));
    }

    // ══════════════════════════════════════════════════════════════════
    // MinimapPingSystem.AddPing
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void AddPing_AddsSinglePing()
    {
        var sys = new MinimapPingSystem();
        sys.AddPing(5, 10, PingType.Attack, playerIndex: 0, currentTick: 0);

        Assert.Single(sys.ActivePings);
        Assert.Equal(5, sys.ActivePings[0].GridX);
        Assert.Equal(10, sys.ActivePings[0].GridY);
        Assert.Equal(PingType.Attack, sys.ActivePings[0].Type);
    }

    [Fact]
    public void AddPing_MultiplePings_AllAdded()
    {
        var sys = new MinimapPingSystem();
        sys.AddPing(1, 1, PingType.Attack, 0, 0);
        sys.AddPing(2, 2, PingType.Beacon, 1, 0);
        sys.AddPing(3, 3, PingType.Alert, 2, 0);

        Assert.Equal(3, sys.ActivePings.Count);
    }

    [Fact]
    public void AddPing_CustomDuration_StoredCorrectly()
    {
        var sys = new MinimapPingSystem();
        sys.AddPing(0, 0, PingType.Alert, 0, currentTick: 0, duration: 150);

        Assert.Equal(150UL, sys.ActivePings[0].DurationTicks);
    }

    // ══════════════════════════════════════════════════════════════════
    // MinimapPingSystem.Update — expiry removal
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Update_RemovesExpiredPings()
    {
        var sys = new MinimapPingSystem();
        sys.AddPing(0, 0, PingType.Attack, 0, currentTick: 0, duration: 30);
        sys.AddPing(0, 0, PingType.Beacon, 0, currentTick: 0, duration: 120);

        sys.Update(currentTick: 30);

        Assert.Single(sys.ActivePings);
        Assert.Equal(PingType.Beacon, sys.ActivePings[0].Type);
    }

    [Fact]
    public void Update_KeepsUnexpiredPings()
    {
        var sys = new MinimapPingSystem();
        sys.AddPing(0, 0, PingType.Attack, 0, currentTick: 0, duration: 90);

        sys.Update(currentTick: 50);

        Assert.Single(sys.ActivePings);
    }

    [Fact]
    public void Update_AllExpired_ClearsActivePings()
    {
        var sys = new MinimapPingSystem();
        sys.AddPing(0, 0, PingType.Alert, 0, currentTick: 0, duration: 10);
        sys.AddPing(1, 1, PingType.Alert, 0, currentTick: 0, duration: 20);

        sys.Update(currentTick: 100);

        Assert.Empty(sys.ActivePings);
    }

    [Fact]
    public void Update_EmptyList_DoesNotThrow()
    {
        var sys = new MinimapPingSystem();
        var ex = Record.Exception(() => sys.Update(currentTick: 1000));
        Assert.Null(ex);
    }

    // ══════════════════════════════════════════════════════════════════
    // GetActivePings
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetActivePings_ReturnsReferenceToActivePings()
    {
        var sys = new MinimapPingSystem();
        sys.AddPing(0, 0, PingType.Beacon, 0, 0);

        List<MinimapPing> pings = sys.GetActivePings();

        Assert.Same(sys.ActivePings, pings);
    }

    [Fact]
    public void GetActivePings_EmptySystem_ReturnsEmptyList()
    {
        var sys = new MinimapPingSystem();
        Assert.Empty(sys.GetActivePings());
    }

    // ══════════════════════════════════════════════════════════════════
    // DefaultDurationTicks constant
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void DefaultDurationTicks_Is90()
    {
        Assert.Equal(90UL, MinimapPingSystem.DefaultDurationTicks);
    }

    // ══════════════════════════════════════════════════════════════════
    // PingType enum values
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void PingType_AllValues_CanBeCreated()
    {
        var ping1 = new MinimapPing(0, 0, PingType.Attack, 0, 0);
        var ping2 = new MinimapPing(0, 0, PingType.Beacon, 0, 0);
        var ping3 = new MinimapPing(0, 0, PingType.Alert, 0, 0);

        Assert.Equal(PingType.Attack, ping1.Type);
        Assert.Equal(PingType.Beacon, ping2.Type);
        Assert.Equal(PingType.Alert, ping3.Type);
    }
}
