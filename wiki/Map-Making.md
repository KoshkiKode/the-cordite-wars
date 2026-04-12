# Map Making — Editor Guide & Procedural Generation

Cordite Wars: Six Fronts includes a full in-engine **Map Editor** and a **procedural map generator** accessible from the Skirmish Lobby. This guide covers both.

---

## Table of Contents

1. [Map Editor Overview](#map-editor-overview)
2. [Creating a New Map](#creating-a-new-map)
3. [Terrain Brushes](#terrain-brushes)
4. [Biome Painting](#biome-painting)
5. [Water Bodies & Rivers](#water-bodies--rivers)
6. [Elevation Zones](#elevation-zones)
7. [Cordite Nodes](#cordite-nodes)
8. [Starting Positions](#starting-positions)
9. [Props & Structures](#props--structures)
10. [Saving & Loading Maps](#saving--loading-maps)
11. [Procedural Map Generation](#procedural-map-generation)
12. [Custom Map JSON Format](#custom-map-json-format)
13. [Map Design Guidelines](#map-design-guidelines)

---

## Map Editor Overview

The Map Editor is accessed in-engine and provides:

- **Terrain sculpting** via brush-based height editing
- **Biome painting** to assign terrain visual themes to regions
- **Water body and river placement**
- **Elevation zone creation** for high-ground and valley features
- **Cordite Node placement** to define resource locations
- **Starting Position assignment** for each player
- **Prop/decoration placement** (trees, rocks, ruins)
- **Undo/redo** support for all operations
- **Save/load** in JSON format to `data/maps/custom/`

Maps must define: dimensions, at least 2 starting positions, at least 1 Cordite node, and a biome.

---

## Creating a New Map

When creating a new map, you must specify:

| Parameter | Description | Minimum |
|-----------|-------------|---------|
| Width | Map width in grid cells | 64 |
| Height | Map height in grid cells | 64 |
| Biome | Base terrain visual theme | Required |

The new map is created as a blank grid with the selected biome applied uniformly. All cells default to passable flat terrain.

**Recommended sizes:**
- 1v1 maps: 80×80 to 120×120 cells
- 2v2 maps: 120×120 to 160×160 cells
- 6-player maps: 180×200 cells

---

## Terrain Brushes

The terrain brush edits the height and passability of individual grid cells. Brush operations:

| Operation | Effect |
|-----------|--------|
| **Raise** | Increases terrain height at brush location |
| **Lower** | Decreases terrain height (creates valleys/water zones) |
| **Smooth** | Averages height between neighboring cells |
| **Flatten** | Sets all cells in brush radius to current height |

**Slope limits by unit type** (max traversable slope angle):
| Unit Class | Max Slope |
|-----------|-----------|
| Jet/Helicopter | N/A (ignores terrain) |
| Infantry | 50° |
| LightVehicle | 35° |
| APC | 30° |
| Tank | 28° |
| HeavyVehicle | 20° |
| Artillery | 15° |

Terrain that exceeds a unit class's slope limit is impassable to that class. This is how mountains and cliffs are created without explicit blocking tiles — simply raise the terrain steeply enough.

---

## Biome Painting

Biomes define the visual appearance of the terrain and affect ambient audio, prop types, and some gameplay elements (e.g., lava channels in volcanic biomes).

### Available Biomes

| Biome | Visual Theme | Special Features |
|-------|------------|-----------------|
| **temperate** | Green fields, deciduous trees | Rivers enabled, moderate prop density |
| **desert** | Sandy dunes, rock formations | Rivers disabled, sparse props, sandstorm effects |
| **rocky** | Stone outcrops, mountain terrain | Rivers optional, rock-type props |
| **coastal** | Beach, tidal zones, tropical plants | Water bodies common, coral and palm props |
| **volcanic** | Lava flows, obsidian rock | Rivers replaced by lava channels (impassable) |

Biome is painted per-cell using the biome brush. A single map can contain multiple biome regions — transition smoothly for best visual results.

---

## Water Bodies & Rivers

### Water Bodies

Place a rectangular water body by clicking two corner points:
1. Click the **first corner** of the water rectangle
2. Click the **opposite corner**
3. The editor commits the water body as a `TerrainFeature` of type `water_body`

Water bodies require a minimum of **2×2 cells**. Units with `MovementClassId = Naval` can traverse water bodies. All other units (except jets and helicopters) cannot.

**Shipyards** must be placed adjacent to a navigable water body.

### Rivers

Draw a river by clicking a sequence of waypoints:
1. Click to begin the river path
2. Continue clicking to add bends and curves
3. Double-click or press **Enter** to finalize the river

Rivers are stored as a `TerrainFeature` of type `river` with a series of polygon points. River width is set in the editor settings (default: 3 cells).

**Bridges** are separate prop placements that overlay rivers at crossing points, creating passable tiles over the river for ground units.

---

## Elevation Zones

Elevation zones apply height modification to a circular area:

| Property | Description |
|----------|-------------|
| Type | `hill`, `mountain`, `plateau`, `crater`, `valley` |
| Center X/Y | Grid coordinates of the zone center |
| Radius | Zone radius in grid cells |
| Height | Height modifier (positive = raised, negative = lowered) |

Multiple zones can overlap — heights are additive. Create dramatic mountain ranges by stacking overlapping zones of decreasing radius.

---

## Cordite Nodes

Cordite Nodes define where harvesters can gather resources.

| Property | Description | Default |
|----------|-------------|---------|
| Node ID | Unique integer ID | Auto-assigned |
| X / Y | Grid position | Required |
| Amount | Cordite capacity | 10,000 |

**Placement Guidelines:**
- Place 2 nodes near each starting position — one "safe" node close to the base, one "contested" node farther out
- Place 2–4 contested nodes in the map center to force mid-game expansion
- Standard node capacity is 10,000. Reduce for faster-paced games, increase for longer ones
- Minimum spacing between nodes: 15 cells (to prevent trivial harvester paths)

---

## Starting Positions

Starting positions define where each player's HQ is pre-placed at game start.

| Property | Description |
|----------|-------------|
| Player ID | 1-indexed player number |
| X / Y | Grid coordinates |
| Facing | Direction the HQ faces (in fixed-point radians) |

**Rules:**
- Maximum **6 starting positions** per map
- Starting positions must be on passable, flat terrain
- Minimum spacing between starting positions: 40 cells
- Each starting position should have at least 1 Cordite node within 20 cells

**Symmetric vs. Asymmetric Layouts:**
- 2-player maps should be rotationally symmetric (180° rotation)
- 4-player maps can be 2-vs-2 symmetric (two halves) or fully symmetric (90° rotation)
- 6-player maps typically use hexagonal radial symmetry

---

## Props & Structures

Props are decorative elements (trees, rocks, ruins) placed via the prop brush. They affect aesthetics but not gameplay directly.

**Prop density options:**
- `0.0` — Barren (no props)
- `0.5` — Normal density
- `1.0` — Dense coverage

Pre-built **neutral structures** (bridges, lighthouse, Nexus turrets) are placed as `StructurePlacement` entries with specific model IDs. These are included in the JSON data file.

---

## Saving & Loading Maps

Maps are saved to JSON format at:
```
data/maps/custom/{map_id}.json
```

### Save Process
1. Open **File → Save Map**
2. Enter a unique Map ID (lowercase, underscore-separated, e.g. `my_custom_map`)
3. Enter a Display Name and Description
4. The map is saved and immediately available in Skirmish Lobby

### Load Process
1. Open **File → Load Map**
2. Select from the list of saved maps
3. The editor restores all terrain, features, nodes, and starting positions

### Sharing Custom Maps
- Custom map files in `data/maps/custom/` can be shared directly
- Place shared map files in the same directory on the recipient machine
- Maps appear in Skirmish Lobby automatically on next launch

---

## Procedural Map Generation

The Random Map system in Skirmish Lobby generates maps using the `MapGenConfig` parameters. All generation is **deterministic** — the same seed and configuration always produces the same map.

### Configuration Parameters

| Parameter | Default | Range/Options | Description |
|-----------|---------|--------------|-------------|
| `Width` | 200 | 64–200+ | Map width in grid cells |
| `Height` | 200 | 64–200+ | Map height in grid cells |
| `PlayerCount` | 2 | 2–6 | Number of starting positions |
| `Biome` | temperate | temperate, desert, rocky, coastal, volcanic, archipelago | Terrain theme |
| `Seed` | 42 | Any integer | Deterministic RNG seed |
| `PropDensity` | 0.5 | 0.0–1.0 | Decorative prop density |
| `CorditeNodesPerPlayer` | 3 | 1–5 | Nodes allocated per player's area |
| `ElevationZoneCount` | 6 | 0–20 | Number of elevation variation zones |
| `GenerateRivers` | true | true/false | Forced off for desert/volcanic biomes |
| `GeneratePaths` | true | true/false | Auto-generates roads between starting positions |

### Generation Algorithm

1. **Starting Positions** are placed at regular angular intervals around a center point, offset outward
2. **Cordite Nodes** are placed near each starting position (using `CorditeNodesPerPlayer`) plus contested nodes in the center
3. **Elevation Zones** are placed using the deterministic RNG with the given count
4. **Water/Rivers** are generated based on biome and `GenerateRivers` setting
5. **Paths** connect starting positions if `GeneratePaths` is enabled, ensuring no starting position is unreachable
6. **Biome** is painted uniformly, with variation zones at terrain transitions
7. **Props** are scattered using the `PropDensity` parameter

### Biome-Specific Behaviors

| Biome | River behavior | Typical terrain | Naval zones |
|-------|---------------|----------------|-------------|
| temperate | Rivers enabled | Rolling hills, moderate elevation | Rivers, small lakes |
| desert | No rivers | Flat dunes, sandstone mesas | None |
| rocky | Optional rivers | High peaks, passes | None (mountain streams) |
| coastal | Water bodies common | Low coastal flats, inlets | Extensive water |
| volcanic | Lava channels (impassable) | Plateaus separated by lava | None |
| archipelago | Sea surrounding islands | Multiple distinct islands | Surrounding sea |

---

## Custom Map JSON Format

Maps are stored as JSON. The key sections are:

```json
{
  "Id": "my_map",
  "DisplayName": "My Map",
  "Description": "A custom map.",
  "Biome": "temperate",
  "Width": 120,
  "Height": 120,
  "StartingPositions": [
    { "PlayerId": 1, "X": 15, "Y": 60, "Facing": 0 },
    { "PlayerId": 2, "X": 105, "Y": 60, "Facing": 180 }
  ],
  "CorditeNodes": [
    { "NodeId": 1, "X": 20, "Y": 55, "Amount": 10000 },
    { "NodeId": 2, "X": 30, "Y": 65, "Amount": 10000 },
    { "NodeId": 3, "X": 60, "Y": 60, "Amount": 10000 },
    { "NodeId": 4, "X": 90, "Y": 55, "Amount": 10000 },
    { "NodeId": 5, "X": 100, "Y": 65, "Amount": 10000 }
  ],
  "TerrainFeatures": [
    {
      "Type": "water_body",
      "Points": [[55, 50], [65, 70]]
    },
    {
      "Type": "river",
      "Points": [[0, 60], [20, 58], [40, 62], [60, 60]]
    }
  ],
  "ElevationZones": [
    { "Type": "hill", "CenterX": 60, "CenterY": 30, "Radius": 15, "Height": 5 }
  ],
  "SunConfig": {
    "Enabled": true,
    "RotationX": -55,
    "RotationY": 30,
    "Color": "#FFF4E0",
    "Energy": 1.2,
    "AmbientColor": "#8899BB",
    "AmbientEnergy": 0.45,
    "SkyColor": "#1A2040"
  }
}
```

**Key rules:**
- `NodeId` must be an **integer**, not a string
- `TerrainFeature` points are `[x, y]` integer pairs in grid coordinates
- `water_body` features use exactly 2 points (rectangle corners)
- `river` features use 2+ points (path waypoints)
- Starting positions are 1-indexed
- Maximum 6 starting positions

---

## Map Design Guidelines

### Balance Principles

1. **Equal opportunity** — Starting positions should have equal nearby node count and terrain quality
2. **Contested center** — Place the most valuable Cordite node(s) where all players must contest them
3. **Multiple paths** — Give players at least 2 routes to the enemy base
4. **Chokepoints exist but aren't mandatory** — Avoid maps where one chokepoint determines everything unless that's the map's intended design

### Faction Considerations

- **Valkyr/Arcloft** — Terrain barely matters; ensure AA positions are accessible
- **Kragmore** — Heavy Vehicles struggle with steep terrain; provide flat routes
- **Ironmarch** — Enjoys chokepoints; don't make ALL paths wide open
- **Bastion** — Needs defensible expansion zones; avoid maps where you can't build walls
- **Stormrend** — Loves open terrain for raiding; ensure some open space

### Node Placement Tips

- Safe node (10–15 cells from HQ): Guaranteed early income
- Forward node (25–35 cells): Requires army presence to hold
- Contested node (map center): Worth fighting over — make it the focus

### Tested Map Size Guide

| Player Count | Recommended Width×Height | Notes |
|-------------|--------------------------|-------|
| 1v1 | 80×80 to 120×120 | Too large → boring; too small → no room for expansion |
| 2v2 | 120×160 | Two "sides" with a center contested zone |
| 3-way FFA | 140×140 | Equidistant triangle layout |
| 4 player | 160×160 | Four corners, contested center |
| 6 player | 180×200 | Hexagonal radial layout |
