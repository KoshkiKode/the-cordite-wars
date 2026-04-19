# The Cordite Wars: Six Fronts

A cross-platform real-time strategy game inspired by Command & Conquer and Tempest Rising.

## Project Details

- **Engine:** Godot 4.6 with C# (.NET 9)
- **Platforms:** Windows, Linux, macOS, Android, iOS
- **Multiplayer:** LAN/Local deterministic lockstep (no backend servers)
- **License:** Proprietary — all rights reserved
- **Assets:** Human-made, royalty-free (CC0/CC-BY)

## Project Structure

```
├── project.godot          # Godot project configuration
├── CorditeWars.sln        # C# solution
├── CorditeWars.csproj     # C# project
├── src/                   # All C# source code
│   ├── Core/              # Game manager, event bus, deterministic RNG, fixed-point math
│   ├── Game/              # Gameplay logic (units, buildings, factions, resources, AI)
│   ├── Systems/           # Engine systems (pathfinding, networking, fog of war, etc.)
│   └── UI/                # User interface code (HUD, menus, minimap)
├── scenes/                # Godot scene files (.tscn)
├── assets/                # Art, audio, fonts, shaders
│   ├── models/            # 3D models (units, buildings, terrain, props)
│   ├── textures/          # Textures and materials
│   ├── audio/             # Sound effects, music, voice
│   ├── fonts/             # Typography
│   ├── icons/             # Game and UI icons
│   └── shaders/           # Custom shaders
├── data/                  # Game data (JSON/resource definitions)
│   ├── units/             # Unit stat definitions
│   ├── buildings/         # Building definitions
│   ├── factions/          # Faction configurations
│   ├── maps/              # Map templates and presets
│   ├── campaign/          # Campaign mission data
│   └── balance/           # Balance tuning parameters
├── addons/                # Godot plugins/addons
├── export_presets/        # Platform export configurations
└── docs/                  # Design documents and technical specs
```

## Core Architecture

- **Deterministic Simulation:** All gameplay uses fixed-point math (`FixedPoint`, `FixedVector2`) and a seeded xoshiro256** RNG to guarantee identical results across all platforms.
- **Event Bus:** Decoupled communication between systems via Godot signals.
- **Physics Tick:** Simulation runs at 30 ticks/sec via `_PhysicsProcess`, independent of render framerate.

## Building

1. Install [Godot 4.6 .NET edition](https://godotengine.org/download)
2. Install [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
3. Open `project.godot` in Godot
4. Build via Godot editor or `dotnet build`

## Legal

All rights reserved. This source code is proprietary and confidential.
Unauthorized copying, distribution, or modification is strictly prohibited.
The End User License Agreement (EULA) is the primary governing license for
installation and use: `versions/windows/EULA.txt`.
