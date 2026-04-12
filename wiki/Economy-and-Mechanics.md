# Economy & Mechanics

A reference guide to the core game systems in Cordite Wars: Six Fronts.

---

## Table of Contents

1. [Resources](#resources)
2. [Cordite Economy](#cordite-economy)
3. [Voltaic Charge (VC)](#voltaic-charge-vc)
4. [Supply System](#supply-system)
5. [Tech Tree Architecture](#tech-tree-architecture)
6. [Stealth System](#stealth-system)
7. [Fog of War](#fog-of-war)
8. [Combat Mechanics](#combat-mechanics)
9. [Armor Classes](#armor-classes)
10. [Movement Classes](#movement-classes)
11. [Faction Mechanics Summary](#faction-mechanics-summary)

---

## Resources

Cordite Wars uses three layered resources:

| Resource | Source | Purpose |
|----------|--------|---------|
| **Cordite** | Harvesters → Refinery | Builds all units and structures |
| **Voltaic Charge (VC)** | Reactor buildings | Unlocks advanced units and upgrades |
| **Supply** | HQ + Supply Depots | Population cap — limits total army size |

---

## Cordite Economy

### Cordite Nodes

- Scattered across each map — typically 4–8 nodes per map, with 2 near each starting position
- Each node contains a finite amount of Cordite (default: **10,000 units**)
- Nodes deplete as they are harvested and do **not** regenerate
- Depleted nodes drive expansion — players must contest new nodes mid-game
- A single node at medium range supports ~4–6 minutes of active harvesting before depletion

### Harvesting

1. A **Harvester** unit travels from a **Refinery** to a Cordite Node
2. Loads Cordite at the node (3.0 seconds load time)
3. Returns to the Refinery and unloads (3.0 seconds unload time)
4. The Refinery converts Cordite into usable economy

**Income formula:**
```
Trip time = (2 × distance / speed) + 3 + 3   (in seconds)
Income (C/min) = (carry_capacity / trip_time) × 60
```

### Harvester Reference (Cordite per minute)

| Faction | Type | Speed | Capacity | Close (10c) | Medium (20c) | Far (35c) |
|---------|------|-------|----------|------------|-------------|---------|
| Valkyr | Helicopter | 0.30 c/s | 350 | 97 C/min | 72 C/min | 50 C/min |
| Kragmore | HeavyVehicle | 0.10 c/s | 1,000 | 92 C/min | 50 C/min | 30 C/min |
| Bastion | LightVehicle | 0.35 c/s | 500 | 126 C/min | 83 C/min | 56 C/min |
| Arcloft | Helicopter | 0.30 c/s | 500 | 138 C/min | 102 C/min | 71 C/min |
| Ironmarch | LightVehicle | 0.35 c/s | 500 | 126 C/min | 83 C/min | 56 C/min |
| Stormrend | LightVehicle | 0.42 c/s | 400 | 101 C/min | 66 C/min | 45 C/min |

> Helicopter-class harvesters bypass terrain obstacles, making all effective distances shorter on complex maps.

### Refinery Passive Income

Bastion's refineries generate **+15 Cordite/sec** passively, regardless of harvester activity. No other faction has this trait.

---

## Voltaic Charge (VC)

- Generated passively by **Reactor** buildings (base: 5 VC/sec per Reactor)
- Caps at **500 VC per Reactor** — "use it or lose it" encourages spending on tech
- Multiple Reactors can be built, each with its own cap
- Required for advanced units (50–800 VC), upgrades, and tech buildings

### VC Rates by Faction

| Faction | VC/sec | Notes |
|---------|--------|-------|
| Valkyr | 5 | Reactors cost 20% less |
| Kragmore | 5 | Standard |
| Bastion | 7 | Best VC rate (+40%) |
| Arcloft | 6 | Slightly above standard |
| Ironmarch | 5 | Standard |
| Stormrend | 5 | Reactors cost 25% more |

**Destroying enemy Reactors** cripples their tech production without affecting basic unit income — a valid strategic target.

---

## Supply System

- Each faction has a **supply cap** (default: 200)
- Units consume supply proportional to their power level
- Supply increased by building **Supply Depots** (each +20 supply, max 10 Depots)
- Exceeding the supply cap prevents training new units until existing ones die

### Supply Costs by Unit Category

| Category | Supply Cost |
|---------|------------|
| Infantry | 1–2 |
| Light Vehicle | 1–2 |
| APC / Tank | 2–4 |
| Helicopter | 1–3 |
| Jet | 2–4 |
| Heavy Vehicle | 2–5 |
| Artillery | 3–4 |
| Superunits | 8–10 |
| Static Defenses | 0 |

### Faction Supply Caps

| Faction | Base Cap | Max (10 Depots) |
|---------|---------|----------------|
| Arcloft | 10 (HQ) | 180 (max 9 depots) |
| Valkyr | 10 (HQ) | 200 |
| Kragmore | 20 (HQ — free Depot equivalent) | 220 |
| Bastion | 10 (HQ) | 200 |
| Ironmarch | 10 (HQ) | 210 (+10 faction bonus) |
| Stormrend | 10 (HQ) | 200 |

---

## Tech Tree Architecture

The tech tree uses a **building-prerequisite gate** system. Players advance by constructing specific buildings.

### Universal Tier Structure

```
Tier 0 ─── HQ (pre-placed, free)
            ├── Refinery
            └── Supply Depot

Tier 1 ─── Barracks
           Vehicle Factory
           Reactor (VC begins)

Tier 2 ─── Tech Lab (req. Barracks + Vehicle Factory + 200 VC)
           Airfield (req. Vehicle Factory)
           Faction-unique building (varies)

Tier 3 ─── Tech Lab Lv.2 upgrade (req. Tech Lab + 300 VC)
           Advanced jets and heavy vehicles unlock

Tier 4 ─── Faction Superweapon Building (req. Tech Lab Lv.2 + 600 VC)
           Superunit unlocked
```

### Tier Cost Summary

| Tier | Gate | Approx. Cordite | VC Required |
|------|------|----------------|-------------|
| 0 | Game start | 0 | 0 |
| 1 | Barracks + Vehicle Factory | ~2,300 C | 0 |
| 2 | Tech Lab | +1,500 C | 200 VC |
| 3 | Tech Lab Lv.2 | +1,500 C | 300 VC |
| 4 | Superweapon Building | +3,500 C | 600 VC |

### Faction Superweapon Buildings

| Faction | Superweapon Building | Unlocks |
|---------|---------------------|---------|
| Valkyr | Carrier Pad | Valkyrie Carrier |
| Kragmore | War Forge | Mammoth |
| Bastion | Command Hub → Aegis Generator | Aegis Generator |
| Arcloft | Tech Lab upgrade | Sky Bastion |
| Ironmarch | (FOB Truck + Tech Lab) | Juggernaut |
| Stormrend | Storm Capacitor → Stormbreaker | Stormbreaker |

---

## Stealth System

Units marked as **stealthed** are invisible to enemies unless a **detector** unit is within sight range.

### How Stealth Works

- Stealthed units appear invisible in the enemy's view
- If a **detector** unit is within its own sight range of the stealthed unit, the stealth is broken and the unit becomes visible
- Stealthed units that **fire a weapon** are revealed for **15 ticks** (half a second at 30 ticks/sec) before re-entering stealth

### Always-Stealthed Units (by faction)

| Faction | Unit | Type |
|---------|------|------|
| Kragmore | Mole Rat | Infantry |
| Kragmore | Tremor Mine | Static Defense |
| Stormrend | Flicker Mine | Static Defense |
| Valkyr | Windrunner | Infantry (stationary only) |
| Arcloft | Patch Drone | Helicopter (infiltration) |
| Bastion | Denial Field | Static Defense |
| Kragmore | Deep Bore | Naval Submarine |
| Stormrend | Riptide Sub | Naval Submarine |
| Bastion | Depth Ward | Naval Submarine |
| Arcloft | Phantom Sub | Naval Submarine |

### Detector Units (by faction)

| Faction | Unit | Type |
|---------|------|------|
| Arcloft | Vigilant | Jet (AWACS mode) |
| Arcloft | Rampart Wall | Static (sensor network) |
| Bastion | Patrol Rover | Light Vehicle |
| Bastion | Watcher | Helicopter |
| Bastion | Corvette | Naval (sonar) |
| Bastion | Depth Ward | Naval submarine (sonar) |
| Ironmarch | Signal Truck | Light Vehicle |
| Valkyr | Overwatch Drone | Helicopter |
| Valkyr | Kestrel | Helicopter |

---

## Fog of War

- The map begins obscured ("fog of war") — only areas within unit sight range are visible
- Once explored, terrain remains visible but units disappear when they leave sight range ("shroud")
- Sight range varies by unit (infantry ~8 cells, helicopters/jets ~12–15 cells)
- Buildings retain vision in their immediate area while alive
- Flying units have extended sight range and ignore terrain-based sight-blocking

---

## Combat Mechanics

### Damage Formula

```
Final damage = Base damage × ArmorModifier[target armor class] × AccuracyRoll
```

- `AccuracyPercent` (default 90%) determines the chance of landing a full-damage hit vs. reduced-damage graze
- `ArmorModifier` scales damage up or down based on the weapon's effectiveness vs. the target's armor class

### Armor Modifiers Example (Machine Gun)

| Target Armor | Modifier |
|-------------|----------|
| Aircraft | 0.8× |
| Light | 1.0× |
| Medium | 0.5× |
| Heavy | 0.2× |
| Unarmored | 1.5× |
| Building | 0.2× |

Each weapon type has its own modifier table — this is what defines unit roles (anti-infantry vs. anti-armor vs. anti-air).

### Weapon Types

| Type | Primary Use |
|------|------------|
| MachineGun | Anti-infantry, light AA |
| Cannon | Anti-vehicle, anti-building |
| Missile | Anti-air, precision anti-vehicle |
| Flak | Dedicated anti-air (area) |
| Artillery | Long-range area bombardment |

### Garrison

Units with `CanGarrison: true` can enter compatible structures:
- Garrisoned infantry benefit from structure protection while firing from inside
- Structures marked `garrison_wall` or `garrison_tower` can hold garrisoning infantry

### Crush Mechanic

Heavy units (`CanCrush: true`) physically destroy infantry and light vehicles when they move over them. Kragmore's Grinder can be upgraded to crush medium-armor units as well.

---

## Armor Classes

| Class | Description | Examples |
|-------|-------------|---------|
| **Unarmored** | No plating — takes full damage from everything | Infantry |
| **Light** | Minimal armor, fast units | Scout vehicles, Zephyr Buggy |
| **Medium** | Standard tank armor | APCs, most tanks |
| **Heavy** | Maximum ground armor | Heavy vehicles, HeavyVehicles |
| **Aircraft** | Special class for all flying units | Jets, helicopters |
| **Building** | Structures, turrets, walls | All buildings |

---

## Movement Classes

| Class | Domain | Speed (cells/sec) | Slope Limit | Notes |
|-------|--------|------------------|-------------|-------|
| Infantry | Ground | 0.12–0.15 | 50° | Goes anywhere, 1×1, crushable |
| LightVehicle | Ground | 0.35–0.42 | 35° | Road bonus, bouncy terrain |
| APC | Ground | 0.20 | 30° | Crushes infantry |
| Tank | Ground | 0.18 | 28° | Crushes infantry, tracked |
| HeavyVehicle | Ground | 0.10 | 20° | Needs flat terrain, crushes |
| Artillery | Ground | 0.06 | 15° | Road-dependent, 3×3 |
| Helicopter | Air (Low) | 0.27–0.36 | N/A | Ignores terrain, can hover |
| Jet | Air (High) | 0.60–0.70 | N/A | Fastest, wide turning radius |
| Naval | Water | Varies | N/A | Water domain only |
| Static | None | 0 | N/A | Buildings, turrets, walls |

---

## Faction Mechanics Summary

| Faction | Mechanic | Effect |
|---------|----------|--------|
| Valkyr | Sortie System | Jets must return to airfields to rearm after attack runs |
| Kragmore | Horde Protocol | 5/10/20+ units grouped = +10/20/25% damage and +10/15/20% armor |
| Bastion | Fortification Network | Structures near Command Hub: +15% HP, +10% damage, +20% repair |
| Arcloft | Overwatch Protocol | Designate 3 patrol zones; idle aircraft auto-patrol with +15% detection |
| Ironmarch | Forward Operations | FOB Truck deploys into a forward base anywhere on the map |
| Stormrend | Blitz Doctrine | Momentum Gauge: 25=speed+, 50=Stormbreak, 100=Total Storm |

---

## Win Conditions

| Condition | Description | Campaign Usage |
|-----------|-------------|---------------|
| **Destroy HQ** | Eliminate the enemy's Command Center | Most campaign missions from M3+ |
| **Kill All Units** | Destroy every enemy unit | Early tutorial missions (M1–M2) |

Both conditions can be configured per mission. Skirmish matches default to **Destroy HQ**.
