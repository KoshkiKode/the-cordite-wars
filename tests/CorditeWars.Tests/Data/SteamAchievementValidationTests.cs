using System.Text.Json;
using System.Text.Json.Serialization;

namespace CorditeWars.Tests.Data;

/// <summary>
/// Validates the Steam achievements data file (<c>data/achievements.json</c>).
/// These tests prevent broken achievement definitions from shipping.
/// </summary>
public class SteamAchievementValidationTests
{
    private static readonly string DataRoot = FindDataRoot();

    private static string FindDataRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            string candidate = Path.Combine(dir, "data");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the 'data' directory by walking up from the test assembly. " +
            "Ensure you are running tests from within the repository checkout.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true
    };

    // ── Helper ───────────────────────────────────────────────────────────────

    private List<AchievementEntry> LoadAchievements()
    {
        string path = Path.Combine(DataRoot, "achievements.json");
        Assert.True(File.Exists(path), $"achievements.json not found at: {path}");

        string json = File.ReadAllText(path);
        var root = JsonSerializer.Deserialize<AchievementFile>(json, JsonOptions);
        Assert.NotNull(root);
        Assert.NotNull(root.Achievements);
        return root.Achievements;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void AchievementsFile_Exists()
    {
        string path = Path.Combine(DataRoot, "achievements.json");
        Assert.True(File.Exists(path), "data/achievements.json must exist for Steam integration.");
    }

    [Fact]
    public void AchievementsFile_IsValidJson()
    {
        string path = Path.Combine(DataRoot, "achievements.json");
        string json = File.ReadAllText(path);
        var exc = Record.Exception(() => JsonDocument.Parse(json));
        Assert.Null(exc);
    }

    [Fact]
    public void AllAchievements_HaveNonEmptyId()
    {
        var achievements = LoadAchievements();
        foreach (var a in achievements)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(a.Id),
                $"Achievement with name '{a.Name}' has an empty or missing 'id' field.");
        }
    }

    [Fact]
    public void AllAchievements_HaveNonEmptyName()
    {
        var achievements = LoadAchievements();
        foreach (var a in achievements)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(a.Name),
                $"Achievement '{a.Id}' has an empty or missing 'name' field.");
        }
    }

    [Fact]
    public void AllAchievements_HaveNonEmptyDescription()
    {
        var achievements = LoadAchievements();
        foreach (var a in achievements)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(a.Description),
                $"Achievement '{a.Id}' has an empty or missing 'description' field.");
        }
    }

    [Fact]
    public void AllAchievementIds_AreUnique()
    {
        var achievements = LoadAchievements();
        var ids = achievements.Select(a => a.Id).ToList();
        var duplicates = ids.GroupBy(id => id)
                            .Where(g => g.Count() > 1)
                            .Select(g => g.Key)
                            .ToList();
        Assert.True(
            duplicates.Count == 0,
            $"Duplicate achievement IDs found: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void AtLeastOneAchievement_IsDefined()
    {
        var achievements = LoadAchievements();
        Assert.True(achievements.Count > 0, "achievements.json must define at least one achievement.");
    }

    [Fact]
    public void PerFactionAchievements_CoverAllSixFactions()
    {
        // Each faction must have a per-faction win achievement so every player
        // has something to unlock regardless of their favourite faction.
        var achievements = LoadAchievements();
        var ids = new HashSet<string>(achievements.Select(a => a.Id));

        string[] required =
        {
            "WIN_AS_ARCLOFT",
            "WIN_AS_VALKYR",
            "WIN_AS_KRAGMORE",
            "WIN_AS_BASTION",
            "WIN_AS_IRONMARCH",
            "WIN_AS_STORMREND"
        };

        foreach (string id in required)
        {
            Assert.True(ids.Contains(id),
                $"Missing per-faction achievement: '{id}'. All six factions need a win achievement.");
        }
    }

    [Fact]
    public void RequiredSteamAchievements_AllPresent()
    {
        // These IDs are referenced from SteamManager.cs; if they're removed
        // from the data file the integration will silently fail.
        var achievements = LoadAchievements();
        var ids = new HashSet<string>(achievements.Select(a => a.Id));

        string[] codeReferences =
        {
            "FIRST_VICTORY",
            "WIN_MULTIPLAYER",
            "FIRST_NAVAL_VICTORY",
            "MATCH_UNDER_10_MIN",
            "DESTROY_100_UNITS",
            "LOSE_A_MATCH",
            "DEFEAT_HARD_AI",
            "PLAY_10_MATCHES"
        };

        foreach (string id in codeReferences)
        {
            Assert.True(ids.Contains(id),
                $"Achievement '{id}' is referenced in SteamManager.cs but is missing from achievements.json.");
        }
    }

    // ── Local data model (mirrors SteamManager's AchievementDefinition) ──────

    private sealed class AchievementEntry
    {
        [JsonPropertyName("id")]          public string Id          { get; set; } = string.Empty;
        [JsonPropertyName("name")]        public string Name        { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("hidden")]      public bool   Hidden      { get; set; }
        [JsonPropertyName("icon")]        public string Icon        { get; set; } = string.Empty;
    }

    private sealed class AchievementFile
    {
        [JsonPropertyName("achievements")]
        public List<AchievementEntry> Achievements { get; set; } = new();
    }
}
