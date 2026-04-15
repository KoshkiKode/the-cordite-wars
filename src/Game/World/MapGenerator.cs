using System;
using System.Collections.Generic;
using CorditeWars.Core;

namespace CorditeWars.Game.World;

/// <summary>
/// Deterministic procedural map generator that produces <see cref="MapData"/>
/// instances compatible with the existing map pipeline (TerrainRenderer,
/// PropPlacer, GameSession, etc.).
///
/// Uses <see cref="DeterministicRng"/> so the same seed + config always
/// produces the identical map — safe for lockstep multiplayer.
/// </summary>
public sealed class MapGenerator
{
    // ── Biome → prop palette mapping ────────────────────────────────

    private static readonly string[] TemperateTrees =
    {
        "tree_default", "tree_simple", "tree_tall", "tree_fat",
        "tree_oak", "tree_detailed", "tree_thin", "tree_small"
    };

    private static readonly string[] DesertProps =
    {
        "rock_smallA", "rock_smallB", "rock_smallC",
        "rock_largeA", "rock_largeB"
    };

    private static readonly string[] RockyProps =
    {
        "rock_smallA", "rock_smallB", "rock_smallC",
        "rock_largeA", "rock_largeB",
        "tree_cone", "tree_cone_dark"
    };

    private static readonly string[] CoastalTrees =
    {
        "tree_default", "tree_simple", "tree_small",
        "tree_fat", "tree_thin"
    };

    private static readonly string[] VolcanicProps =
    {
        "rock_largeA", "rock_largeB",
        "rock_smallA", "rock_smallB", "rock_smallC"
    };

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Generates a complete <see cref="MapData"/> from the given configuration.
    /// Deterministic: same config → same output.
    /// </summary>
    public MapData Generate(MapGenConfig config)
    {
        ValidateConfig(config);

        var rng = new DeterministicRng(config.Seed);

        int w = config.Width;
        int h = config.Height;
        int players = config.PlayerCount;

        // 1. Starting positions — radially symmetric around map center
        var startingPositions = GenerateStartingPositions(w, h, players);

        // 2. Cordite nodes — per-player base nodes + contested center nodes
        var corditeNodes = GenerateCorditeNodes(
            rng, w, h, players, startingPositions, config.CorditeNodesPerPlayer);

        // 3. Elevation zones
        var elevationZones = GenerateElevationZones(
            rng, w, h, config.ElevationZoneCount, config.Biome, startingPositions);

        // 4. Terrain features (rivers, paths)
        var terrainFeatures = GenerateTerrainFeatures(
            rng, w, h, config.Biome, config.GenerateRivers, config.GeneratePaths,
            startingPositions);

        // 5. Props
        var props = GenerateProps(
            rng, w, h, config.Biome, config.PropDensity, startingPositions);

        // 6. Structures — light scattering of ruins in contested zones
        var structures = GenerateStructures(rng, w, h, startingPositions);

        string id = $"generated_{config.Seed}";

        return new MapData
        {
            Id = id,
            DisplayName = $"Random ({config.Biome})",
            Description = $"Procedurally generated {config.Biome} map for " +
                          $"{players} players (seed {config.Seed}).",
            Author = "MapGenerator",
            MaxPlayers = players,
            Width = w,
            Height = h,
            Biome = config.Biome,
            StartingPositions = startingPositions,
            CorditeNodes = corditeNodes,
            TerrainFeatures = terrainFeatures,
            Props = props,
            Structures = structures,
            ElevationZones = elevationZones,
        };
    }

    // ── Validation ──────────────────────────────────────────────────

    private static void ValidateConfig(MapGenConfig config)
    {
        if (config.Width < 64)
            throw new ArgumentOutOfRangeException(nameof(config), "Width must be ≥ 64.");
        if (config.Height < 64)
            throw new ArgumentOutOfRangeException(nameof(config), "Height must be ≥ 64.");
        if (config.PlayerCount < 2 || config.PlayerCount > 6)
            throw new ArgumentOutOfRangeException(nameof(config), "PlayerCount must be 2–6.");
    }

    // ── Starting Positions ──────────────────────────────────────────

    /// <summary>
    /// Places players radially around the center with equal angular spacing.
    /// Each player is pushed toward the map edge with a configurable margin.
    /// Facing angles point toward the center.
    /// </summary>
    private static StartingPosition[] GenerateStartingPositions(
        int width, int height, int playerCount)
    {
        var positions = new StartingPosition[playerCount];
        double cx = width / 2.0;
        double cy = height / 2.0;

        // Players placed at ~80 % of the radius from center to the nearest edge.
        double radius = Math.Min(cx, cy) * 0.78;

        // Angular offset so 2-player maps don't align on a cardinal axis
        const double baseAngle = Math.PI / 4.0; // 45 °

        for (int i = 0; i < playerCount; i++)
        {
            double angle = baseAngle + 2.0 * Math.PI * i / playerCount;
            int px = (int)Math.Round(cx + radius * Math.Cos(angle));
            int py = (int)Math.Round(cy + radius * Math.Sin(angle));

            // Clamp inside map bounds with margin
            const int margin = 12;
            px = Math.Clamp(px, margin, width - margin);
            py = Math.Clamp(py, margin, height - margin);

            // Facing toward center
            double facing = Math.Atan2(cy - py, cx - px);
            if (facing < 0) facing += 2.0 * Math.PI;

            positions[i] = new StartingPosition
            {
                PlayerId = i,
                X = px,
                Y = py,
                Facing = FixedPoint.FromFloat((float)facing),
            };
        }

        return positions;
    }

    // ── Cordite Nodes ───────────────────────────────────────────────

    private static CorditeNodeData[] GenerateCorditeNodes(
        DeterministicRng rng, int width, int height, int playerCount,
        StartingPosition[] starts, int nodesPerPlayer)
    {
        var nodes = new List<CorditeNodeData>();
        int nextId = 0;
        double cx = width / 2.0;
        double cy = height / 2.0;

        // Per-player base nodes — near each starting position
        for (int p = 0; p < playerCount; p++)
        {
            double sx = starts[p].X;
            double sy = starts[p].Y;

            // Direction from player toward center
            double dx = cx - sx;
            double dy = cy - sy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1) dist = 1;
            dx /= dist;
            dy /= dist;

            for (int n = 0; n < nodesPerPlayer; n++)
            {
                // Spread nodes in a small arc near the base
                double spreadAngle = (n - (nodesPerPlayer - 1) / 2.0) * 0.4;
                double cos = Math.Cos(spreadAngle);
                double sin = Math.Sin(spreadAngle);
                double ndx = dx * cos - dy * sin;
                double ndy = dx * sin + dy * cos;

                // Distance from start: first node close, further ones a bit more
                double nodeDist = 8.0 + n * 5.0 + rng.NextInt(4);

                double nx = sx + ndx * nodeDist;
                double ny = sy + ndy * nodeDist;
                nx = Math.Clamp(nx, 2, width - 2);
                ny = Math.Clamp(ny, 2, height - 2);

                // First node is richest, subsequent are smaller
                int amount = n == 0 ? 10000 : 8000 - n * 1000;
                if (amount < 4000) amount = 4000;

                nodes.Add(new CorditeNodeData
                {
                    NodeId = nextId,
                    X = (float)nx,
                    Y = (float)ny,
                    Amount = amount,
                });
                nextId++;
            }
        }

        // Contested center nodes — one or two near map center
        int centerNodes = playerCount <= 3 ? 1 : 2;
        for (int c = 0; c < centerNodes; c++)
        {
            double offset = c == 0 ? -6 : 6;
            nodes.Add(new CorditeNodeData
            {
                NodeId = nextId,
                X = (float)(cx + offset),
                Y = (float)(cy + (rng.NextInt(10) - 5)),
                Amount = 12000,
            });
            nextId++;
        }

        return nodes.ToArray();
    }

    // ── Elevation Zones ─────────────────────────────────────────────

    private static ElevationZone[] GenerateElevationZones(
        DeterministicRng rng, int width, int height, int count, string biome,
        StartingPosition[] starts)
    {
        var zones = new List<ElevationZone>();
        int minDim = Math.Min(width, height);

        for (int i = 0; i < count; i++)
        {
            int zcx = rng.NextInt(width / 6, width * 5 / 6);
            int zcy = rng.NextInt(height / 6, height * 5 / 6);
            int radius = rng.NextInt(minDim / 8, minDim / 3);

            // Height varies by biome
            double maxHeight = biome switch
            {
                "desert" => 1.2,
                "rocky" or "volcanic" => 3.5,
                "coastal" or "archipelago" => 2.0,
                _ => 2.5, // temperate
            };

            double h = 0.5 + rng.NextDouble() * (maxHeight - 0.5);
            // ~30 % chance of a depression
            if (rng.NextBool(0.3))
                h = -h * 0.5;

            string zoneType = h >= 0 ? "hill" : "valley";

            zones.Add(new ElevationZone
            {
                Type = zoneType,
                CenterX = zcx,
                CenterY = zcy,
                Radius = radius,
                Height = FixedPoint.FromFloat((float)h),
            });
        }

        // Archipelago biome: add island elevation zones at each starting position
        if (biome == "archipelago")
        {
            foreach (var start in starts)
            {
                zones.Add(new ElevationZone
                {
                    Type = "island",
                    CenterX = start.X,
                    CenterY = start.Y,
                    Radius = minDim / 6,
                    Height = FixedPoint.FromFloat(2.0f),
                });
            }
        }

        return zones.ToArray();
    }

    // ── Terrain Features ────────────────────────────────────────────

    private static TerrainFeature[] GenerateTerrainFeatures(
        DeterministicRng rng, int width, int height, string biome,
        bool generateRivers, bool generatePaths, StartingPosition[] starts)
    {
        var features = new List<TerrainFeature>();

        // Rivers — skip for desert / volcanic unless explicitly enabled
        bool wantRiver = generateRivers &&
                         biome is not ("desert" or "volcanic");

        if (wantRiver)
        {
            features.Add(GenerateRiver(rng, width, height));
        }

        // Paths — connect adjacent starting positions through the map
        if (generatePaths && starts.Length >= 2)
        {
            features.Add(GeneratePath(starts, width, height));
        }

        return features.ToArray();
    }

    /// <summary>
    /// Generates a river that meanders across the map from one edge to the opposite.
    /// </summary>
    private static TerrainFeature GenerateRiver(
        DeterministicRng rng, int width, int height)
    {
        var points = new List<int[]>();

        // Decide orientation: horizontal or vertical
        bool horizontal = rng.NextBool();

        int segments = 8 + rng.NextInt(5);

        if (horizontal)
        {
            int y = height / 2 + rng.NextInt(-height / 6, height / 6);
            for (int i = 0; i <= segments; i++)
            {
                int x = width * i / segments;
                int drift = rng.NextInt(-height / 12, height / 12);
                int py = Math.Clamp(y + drift, 4, height - 4);
                points.Add(new[] { x, py });
                y = py; // carry drift forward for natural winding
            }
        }
        else
        {
            int x = width / 2 + rng.NextInt(-width / 6, width / 6);
            for (int i = 0; i <= segments; i++)
            {
                int y2 = height * i / segments;
                int drift = rng.NextInt(-width / 12, width / 12);
                int px = Math.Clamp(x + drift, 4, width - 4);
                points.Add(new[] { px, y2 });
                x = px;
            }
        }

        return new TerrainFeature
        {
            Type = "river",
            Points = points.ToArray(),
        };
    }

    /// <summary>
    /// Generates a connecting path that links adjacent starting positions
    /// through a rough ring, giving units natural avenues of approach.
    /// </summary>
    private static TerrainFeature GeneratePath(
        StartingPosition[] starts, int width, int height)
    {
        var points = new List<int[]>();

        // Build a ring: p0 → p1 → … → pN → p0
        for (int i = 0; i < starts.Length; i++)
        {
            int next = (i + 1) % starts.Length;
            points.Add(new[] { starts[i].X, starts[i].Y });

            // Add a midpoint with slight offset toward map center for natural feel
            int mx = (starts[i].X + starts[next].X) / 2;
            int my = (starts[i].Y + starts[next].Y) / 2;
            int offsetX = (width / 2 - mx) / 4;
            int offsetY = (height / 2 - my) / 4;
            points.Add(new[] {
                Math.Clamp(mx + offsetX, 2, width - 2),
                Math.Clamp(my + offsetY, 2, height - 2)
            });
        }

        // Close the ring
        points.Add(new[] { starts[0].X, starts[0].Y });

        return new TerrainFeature
        {
            Type = "path",
            Points = points.ToArray(),
        };
    }

    // ── Props ───────────────────────────────────────────────────────

    private static PropPlacement[] GenerateProps(
        DeterministicRng rng, int width, int height, string biome,
        double density, StartingPosition[] starts)
    {
        var props = new List<PropPlacement>();

        string[] palette = biome switch
        {
            "desert" => DesertProps,
            "rocky" => RockyProps,
            "volcanic" => VolcanicProps,
            "coastal" or "archipelago" => CoastalTrees,
            _ => TemperateTrees,
        };

        if (palette.Length == 0) return props.ToArray();

        // Determine number of props based on map area and density
        int area = width * height;
        int maxProps = (int)(area * density * 0.006); // ~0.6% of cells at full density
        int propCount = Math.Clamp(maxProps, 10, 2000);

        // Keep-out radius around starting positions (no props blocking the base)
        const double keepOutRadius = 15.0;

        for (int i = 0; i < propCount; i++)
        {
            double px = 2.0 + rng.NextDouble() * (width - 4.0);
            double py = 2.0 + rng.NextDouble() * (height - 4.0);

            // Check keep-out zones
            bool tooClose = false;
            for (int s = 0; s < starts.Length; s++)
            {
                double dx = px - starts[s].X;
                double dy = py - starts[s].Y;
                if (dx * dx + dy * dy < keepOutRadius * keepOutRadius)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            string modelId = palette[rng.NextInt(palette.Length)];
            double rotation = rng.NextDouble() * 360.0;
            double scale = 0.8 + rng.NextDouble() * 0.4; // 0.8–1.2

            props.Add(new PropPlacement
            {
                ModelId = modelId,
                X = FixedPoint.FromFloat((float)px),
                Y = FixedPoint.FromFloat((float)py),
                Rotation = FixedPoint.FromFloat((float)rotation),
                Scale = FixedPoint.FromFloat((float)scale),
            });
        }

        return props.ToArray();
    }

    // ── Structures ──────────────────────────────────────────────────

    /// <summary>
    /// Places a small number of ruin structures near the center contested zone
    /// to provide cover and strategic interest.
    /// </summary>
    private static StructurePlacement[] GenerateStructures(
        DeterministicRng rng, int width, int height, StartingPosition[] starts)
    {
        var structures = new List<StructurePlacement>();

        // A handful of ruins near the map center
        int cx = width / 2;
        int cy = height / 2;
        int count = 2 + rng.NextInt(3); // 2–4 ruins

        string[] centerStructureIds = { "rock_largeA", "rock_largeB" };
        const double keepOutRadius = 20.0;

        for (int i = 0; i < count; i++)
        {
            double sx = cx + rng.NextInt(-width / 8, width / 8);
            double sy = cy + rng.NextInt(-height / 8, height / 8);

            // Keep away from starting positions
            bool tooClose = false;
            for (int s = 0; s < starts.Length; s++)
            {
                double dx = sx - starts[s].X;
                double dy = sy - starts[s].Y;
                if (dx * dx + dy * dy < keepOutRadius * keepOutRadius)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            structures.Add(new StructurePlacement
            {
                ModelId = centerStructureIds[rng.NextInt(centerStructureIds.Length)],
                X = FixedPoint.FromFloat((float)sx),
                Y = FixedPoint.FromFloat((float)sy),
                Rotation = FixedPoint.FromFloat((float)(rng.NextDouble() * 360.0)),
                Scale = FixedPoint.FromFloat(1.0f),
            });
        }

        return structures.ToArray();
    }
}
