# Unit 3D Models

This directory will contain the final game-ready unit 3D models for all six factions (Valkyr, Kragmore, Bastion, Arcloft, Ironmarch, Stormrend) and their naval equivalents.

## Format
- **Format**: `.glb` (GLTF Binary) — Godot's preferred import format
- **Polycount target**: 500–2000 triangles per unit (LOD0)
- **Rigging**: Not required for non-infantry units; infantry will use a simple bone rig for idle/move/death animations
- **Naming convention**: `{faction}_{unit_id}.glb` (e.g. `valkyr_windrunner.glb`)

## Status
Currently using placeholder proxy models from the Quaternius Space pack. See `data/asset_manifest.json` for the current model assignments.

## Factions
- **Valkyr Command** — Aerial/hover aesthetic, sleek and futuristic
- **Kragmore Clans** — Heavy industrial, rugged and angular
- **Bastion Republic** — Fortified, heavily armoured, defensive look
- **Arcloft Syndicate** — Stealth/drone aesthetic, angular and angular
- **Ironmarch Union** — Trench warfare / engineering corps aesthetic
- **Stormrend Accord** — Lightning/energy themed, asymmetric design

## Naval Models
Naval unit models go in `assets/models/naval/` once created.
