using System.Text.Json;

namespace CorditeWars.Tests.Data;

/// <summary>
/// Validates all JSON game data files for structural integrity.
/// These tests catch data issues (typos, missing fields, broken references)
/// that would otherwise only surface at runtime.
/// </summary>
public class GameDataValidationTests
{
    // Path to the data directory relative to the test assembly
    private static readonly string DataRoot = FindDataRoot();

    private static string FindDataRoot()
    {
        // Walk up from the test assembly directory to find the repo root
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            string candidate = Path.Combine(dir, "data");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        // Fallback to absolute path (CI environment)
        return "/home/runner/work/the-cordite-wars/the-cordite-wars/data";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string[] ExpectedFactions =
        { "arcloft", "bastion", "ironmarch", "kragmore", "stormrend", "valkyr" };

    // ═══════════════════════════════════════════════════════════════════
    // Faction data validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AllSixFactionFiles_Exist()
    {
        foreach (string faction in ExpectedFactions)
        {
            string path = Path.Combine(DataRoot, "factions", $"{faction}.json");
            Assert.True(File.Exists(path), $"Missing faction file: {path}");
        }
    }

    [Fact]
    public void FactionFiles_AreValidJson()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "factions"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("Id", out var id),
                $"{Path.GetFileName(file)}: Missing 'Id' property");
            Assert.True(root.TryGetProperty("DisplayName", out _),
                $"{Path.GetFileName(file)}: Missing 'DisplayName' property");

            Assert.False(string.IsNullOrWhiteSpace(id.GetString()),
                $"{Path.GetFileName(file)}: 'Id' is empty");
        }
    }

    [Fact]
    public void FactionFiles_IdsMatchFilenames()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "factions"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("Id").GetString()!;
            string expectedId = Path.GetFileNameWithoutExtension(file);
            Assert.True(expectedId == id,
                $"{Path.GetFileName(file)}: Id '{id}' doesn't match filename '{expectedId}'");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Unit data validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UnitFiles_AreValidJson()
    {
        string unitDir = Path.Combine(DataRoot, "units");
        Assert.True(Directory.Exists(unitDir), "Missing units directory");

        foreach (string file in Directory.GetFiles(unitDir, "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string fileName = Path.GetFileName(file);

            Assert.True(root.TryGetProperty("Id", out _), $"{fileName}: Missing 'Id'");
            Assert.True(root.TryGetProperty("FactionId", out _), $"{fileName}: Missing 'FactionId'");
            Assert.True(root.TryGetProperty("MaxHealth", out _), $"{fileName}: Missing 'MaxHealth'");
            Assert.True(root.TryGetProperty("Cost", out _), $"{fileName}: Missing 'Cost'");
        }
    }

    [Fact]
    public void UnitFiles_IdsMatchFilenames()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "units"), "*.json", SearchOption.AllDirectories))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("Id").GetString()!;
            string expectedId = Path.GetFileNameWithoutExtension(file);
            Assert.True(expectedId == id,
                $"{Path.GetFileName(file)}: Id '{id}' doesn't match filename '{expectedId}'");
        }
    }

    [Fact]
    public void UnitFiles_HavePositiveHealth()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "units"), "*.json", SearchOption.AllDirectories))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            double health = doc.RootElement.GetProperty("MaxHealth").GetDouble();
            Assert.True(health > 0,
                $"{Path.GetFileName(file)}: MaxHealth should be positive, got {health}");
        }
    }

    [Fact]
    public void UnitFiles_HavePositiveCost()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "units"), "*.json", SearchOption.AllDirectories))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            int cost = doc.RootElement.GetProperty("Cost").GetInt32();
            Assert.True(cost > 0,
                $"{Path.GetFileName(file)}: Cost should be positive, got {cost}");
        }
    }

    [Fact]
    public void UnitFiles_FactionIdMatchesPrefix()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "units"), "*.json", SearchOption.AllDirectories))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("Id").GetString()!;
            string factionId = doc.RootElement.GetProperty("FactionId").GetString()!;
            Assert.True(id.StartsWith(factionId + "_"),
                $"{Path.GetFileName(file)}: Unit Id '{id}' should start with FactionId prefix '{factionId}_'");
        }
    }

    [Fact]
    public void UnitFiles_BelongToKnownFaction()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "units"), "*.json", SearchOption.AllDirectories))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string factionId = doc.RootElement.GetProperty("FactionId").GetString()!;
            Assert.True(ExpectedFactions.Contains(factionId),
                $"{Path.GetFileName(file)}: Unknown faction '{factionId}'");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Building data validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildingFiles_AreValidJson()
    {
        string buildingDir = Path.Combine(DataRoot, "buildings");
        Assert.True(Directory.Exists(buildingDir), "Missing buildings directory");

        foreach (string file in Directory.GetFiles(buildingDir, "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string fileName = Path.GetFileName(file);

            Assert.True(root.TryGetProperty("Id", out _), $"{fileName}: Missing 'Id'");
            Assert.True(root.TryGetProperty("FactionId", out _), $"{fileName}: Missing 'FactionId'");
            Assert.True(root.TryGetProperty("MaxHealth", out _), $"{fileName}: Missing 'MaxHealth'");
            Assert.True(root.TryGetProperty("Cost", out _), $"{fileName}: Missing 'Cost'");
        }
    }

    [Fact]
    public void BuildingFiles_IdsMatchFilenames()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "buildings"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("Id").GetString()!;
            string expectedId = Path.GetFileNameWithoutExtension(file);
            Assert.True(expectedId == id,
                $"{Path.GetFileName(file)}: Id '{id}' doesn't match filename '{expectedId}'");
        }
    }

    [Fact]
    public void BuildingFiles_HavePositiveHealth()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "buildings"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            double health = doc.RootElement.GetProperty("MaxHealth").GetDouble();
            Assert.True(health > 0,
                $"{Path.GetFileName(file)}: MaxHealth should be positive, got {health}");
        }
    }

    [Fact]
    public void BuildingFiles_BelongToKnownFaction()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "buildings"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string factionId = doc.RootElement.GetProperty("FactionId").GetString()!;
            Assert.True(ExpectedFactions.Contains(factionId),
                $"{Path.GetFileName(file)}: Unknown faction '{factionId}'");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Upgrade data validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void UpgradeFiles_AreValidJson()
    {
        string upgradeDir = Path.Combine(DataRoot, "upgrades");
        Assert.True(Directory.Exists(upgradeDir), "Missing upgrades directory");

        foreach (string file in Directory.GetFiles(upgradeDir, "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string fileName = Path.GetFileName(file);

            Assert.True(root.TryGetProperty("Id", out _), $"{fileName}: Missing 'Id'");
            Assert.True(root.TryGetProperty("FactionId", out _), $"{fileName}: Missing 'FactionId'");
            Assert.True(root.TryGetProperty("Cost", out _), $"{fileName}: Missing 'Cost'");
        }
    }

    [Fact]
    public void UpgradeFiles_IdsMatchFilenames()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "upgrades"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string id = doc.RootElement.GetProperty("Id").GetString()!;
            string expectedId = Path.GetFileNameWithoutExtension(file);
            Assert.True(expectedId == id,
                $"{Path.GetFileName(file)}: Id '{id}' doesn't match filename '{expectedId}'");
        }
    }

    [Fact]
    public void UpgradeFiles_BelongToKnownFaction()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "upgrades"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string factionId = doc.RootElement.GetProperty("FactionId").GetString()!;
            Assert.True(ExpectedFactions.Contains(factionId),
                $"{Path.GetFileName(file)}: Unknown faction '{factionId}'");
        }
    }

    [Fact]
    public void UpgradeFiles_HavePositiveCost()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "upgrades"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            int cost = doc.RootElement.GetProperty("Cost").GetInt32();
            Assert.True(cost > 0,
                $"{Path.GetFileName(file)}: Cost should be positive, got {cost}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Cross-reference validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FactionUnits_ReferenceExistingUnitFiles()
    {
        var unitIds = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "units"), "*.json", SearchOption.AllDirectories))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            unitIds.Add(doc.RootElement.GetProperty("Id").GetString()!);
        }

        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "factions"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string factionId = doc.RootElement.GetProperty("Id").GetString()!;

            if (doc.RootElement.TryGetProperty("AvailableUnitIds", out var units))
            {
                foreach (var unitRef in units.EnumerateArray())
                {
                    string unitId = unitRef.GetString()!;
                    Assert.True(unitIds.Contains(unitId),
                        $"Faction '{factionId}' references non-existent unit '{unitId}'");
                }
            }
        }
    }

    [Fact]
    public void FactionBuildings_ReferenceExistingBuildingFiles()
    {
        var buildingIds = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "buildings"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            buildingIds.Add(doc.RootElement.GetProperty("Id").GetString()!);
        }

        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "factions"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string factionId = doc.RootElement.GetProperty("Id").GetString()!;

            if (doc.RootElement.TryGetProperty("AvailableBuildingIds", out var buildings))
            {
                foreach (var buildingRef in buildings.EnumerateArray())
                {
                    string buildingId = buildingRef.GetString()!;
                    Assert.True(buildingIds.Contains(buildingId),
                        $"Faction '{factionId}' references non-existent building '{buildingId}'");
                }
            }
        }
    }

    [Fact]
    public void FactionTechTree_ReferencesExistingUpgrades()
    {
        var upgradeIds = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "upgrades"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            upgradeIds.Add(doc.RootElement.GetProperty("Id").GetString()!);
        }

        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "factions"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string factionId = doc.RootElement.GetProperty("Id").GetString()!;

            if (doc.RootElement.TryGetProperty("TechTreeUnlocks", out var techTree))
            {
                foreach (var entry in techTree.EnumerateObject())
                {
                    Assert.True(upgradeIds.Contains(entry.Name),
                        $"Faction '{factionId}' tech tree references non-existent upgrade '{entry.Name}'");
                }
            }
        }
    }

    [Fact]
    public void UpgradePrerequisiteBuildings_ReferenceExistingBuildings()
    {
        var buildingIds = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "buildings"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            buildingIds.Add(doc.RootElement.GetProperty("Id").GetString()!);
        }

        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "upgrades"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string upgradeId = doc.RootElement.GetProperty("Id").GetString()!;

            if (doc.RootElement.TryGetProperty("PrerequisiteBuilding", out var prereqProp))
            {
                string prereq = prereqProp.GetString()!;
                if (!string.IsNullOrEmpty(prereq))
                {
                    Assert.True(buildingIds.Contains(prereq),
                        $"Upgrade '{upgradeId}' requires non-existent building '{prereq}'");
                }
            }
        }
    }

    [Fact]
    public void BuildingPrerequisites_ReferenceExistingBuildings()
    {
        var buildingIds = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "buildings"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            buildingIds.Add(doc.RootElement.GetProperty("Id").GetString()!);
        }

        foreach (string file in Directory.GetFiles(Path.Combine(DataRoot, "buildings"), "*.json"))
        {
            string json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            string buildingId = doc.RootElement.GetProperty("Id").GetString()!;

            if (doc.RootElement.TryGetProperty("Prerequisites", out var prereqs))
            {
                foreach (var prereq in prereqs.EnumerateArray())
                {
                    string prereqId = prereq.GetString()!;
                    Assert.True(buildingIds.Contains(prereqId),
                        $"Building '{buildingId}' requires non-existent prerequisite building '{prereqId}'");
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Manifest validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildingManifest_IsValidJson()
    {
        string path = Path.Combine(DataRoot, "building_manifest.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            Assert.NotNull(JsonDocument.Parse(json));
        }
    }

    [Fact]
    public void SoundManifest_IsValidJson()
    {
        string path = Path.Combine(DataRoot, "sound_manifest.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            Assert.NotNull(JsonDocument.Parse(json));
        }
    }

    [Fact]
    public void AssetManifest_IsValidJson()
    {
        string path = Path.Combine(DataRoot, "asset_manifest.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            Assert.NotNull(JsonDocument.Parse(json));
        }
    }
}
