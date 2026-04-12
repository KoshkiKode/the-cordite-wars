# Stormrend — Air + Ground Offense Hybrid

> *"Hit hard, hit fast, hit everywhere at once, and never stop moving."*

**Campaign:** Storm's Fury | **Commander:** Tempest Kael | **Color:** Purple (#7B1FA2)

---

## Lore

The Stormrend Pact was forged by mercenary companies and breakaway military units who believed the best defense is an overwhelming offense. They don't build empires — they shatter the empires of others. Their doctrine is blitz: hit hard, hit fast, hit everywhere at once, and never stop moving.

Tempest Kael — the player's commander — is a former mercenary captain who rose to lead the entire Pact through sheer ferocity and an almost supernatural sense of when to strike. He has no patience for attrition, no interest in holding ground, and no sympathy for anyone who thinks there's a more civilized way to conduct a war.

---

## Playstyle

Playing Stormrend feels like controlling a thunderstorm. You're always attacking — probing, raiding, flanking, pushing. The moment you stop moving, you're losing, because your base defenses are essentially paper and your economy is fragile.

You win by keeping every opponent off-balance, forcing them to defend three fronts simultaneously while you consolidate on the one that collapsed. Games are short, violent, and decisive. If you haven't won by minute 15, you probably won't.

### Strategic Strengths
- Can attack with air and ground simultaneously — incredibly hard to defend against
- Fastest ground units in the game; fast (if not top-tier) aircraft
- Best economic harassment — light vehicles and helicopters shred harvesters
- Stormrend dictates the pace of the game; opponents are always reacting
- Can concentrate overwhelming firepower on a single point for devastating alpha strikes
- Cheapest unit build times (+20% faster than standard)

### Strategic Weaknesses
- Worst static defenses in the game — practically nonexistent
- Speed comes at the cost of armor — units die fast in sustained fights
- LightVehicle harvesters are fragile and easily raided
- Cannot win long, grinding fights — must win fast or lose
- Combined arms army requires both air and ground infrastructure, splitting the budget
- Reactors cost +25% more, delaying VC-heavy tech

---

## Faction Mechanic: Blitz Doctrine / Momentum

A faction-wide **Momentum Gauge** (0–100) fills when Stormrend units deal damage to enemies:

| Threshold | Effect |
|-----------|--------|
| 25+ Momentum | All Stormrend units gain +10% movement speed |
| 50+ Momentum | **Stormbreak** available — targeted push with massive force multiplier |
| 100 Momentum | **Total Storm** — overwhelming combined assault, maximum force multiplier across the army |

Momentum decays when Stormrend units aren't dealing damage. Taking casualties without dealing damage drains it faster. The **Storm Capacitor** building charges Momentum over time and from unit kills — destroying it resets accumulated Momentum.

---

## Economy

| Modifier | Value |
|----------|-------|
| Harvester type | LightVehicle (fast, fragile) |
| Harvester capacity | 400 Cordite (below base 500) |
| Harvester speed | 1.2× standard (+20%) |
| Reactor cost | +25% more expensive |
| Supply cap | 200 (standard) |
| Unit build speed | +20% faster |
| Building construction | −15% slower |

Stormrend's fast harvesters generate decent income but are easy to raid. The expensive Reactors mean Stormrend should win before VC-heavy units even matter. Their economy is feast-or-famine — great when dominating, collapses when on the back foot.

---

## Tech Tree Unlocks

| Upgrade | Cost | Effect |
|---------|------|--------|
| Momentum Overdrive | 1,500 VC | Momentum builds 25% faster from damage dealt |
| Stormbreak Cascade | 2,000 VC | Stormbreak effect duration increased by 50% |
| Blitz Conditioning | 1,200 VC | All Stormrend units gain +5% movement speed permanently |
| Lightning Payload | 2,500 VC | Razorbeak and Stormbreaker deal +25% damage on first attack run (applies to each aircraft once per sortie) |

---

## Buildings

| Building | Display Name | Prerequisites | HP | Build Time | Description |
|----------|-------------|--------------|-----|-----------|-------------|
| stormrend_command_center | Storm Command | — (pre-placed) | 3,000 | 0s | Produces light harvesters and fast scout bikes. |
| stormrend_refinery | Lightning Refinery | Command Center | 1,500 | 25s | Lower per-trip capacity (400 Cordite) means more frequent harvester visits. |
| stormrend_supply_depot | Storm Depot | Command Center | 600 | 15s | +20 supply. Standard 200 cap. |
| stormrend_barracks | Blitz Barracks | Refinery | 1,000 | 20s | Trains shock infantry, raider squads, and sapper units. |
| stormrend_vehicle_factory | Storm Garage | Barracks | 1,500 | 30s | Produces fast-attack vehicles and armored strike bikes. |
| stormrend_reactor | Storm Reactor | Refinery | 800 | 20s | Standard VC output but 25% more expensive. Delay until needed. |
| stormrend_airfield | Storm Airstrip | Vehicle Factory | 1,200 | 35s | Launches ground-attack jets and raid helicopters. |
| stormrend_tech_lab | Storm Lab | Barracks + Vehicle Factory | 800 | 30s | Unlocks Momentum upgrades and Storm Capacitor. |
| stormrend_storm_capacitor | Storm Capacitor | Tech Lab | 1,000 | 40s | Charges Momentum over time and from kills. Destroying it resets Momentum. |
| stormrend_shipyard | Strike Dock | Vehicle Factory + Tech Lab | 1,600 | 38s | Rapid-build dock. Stormrend ships are lightly armoured but devastating on first strike. |

---

## Unit Roster

### Infantry

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Razorwind** | 75 | Unarmored | 200 | — | 1 | Sprint | Lightning-fast assault infantry hitting and vanishing before retaliation. |
| **Thunderjaw** | 98 | Unarmored | 400 | — | 1 | Momentum bonus | Mobile anti-armor infantry. +20% damage when Momentum > 50. |
| **Sparkrunner** | 75 | Unarmored | 400 | — | 1 | Fast lock | AA infantry keeping pace with Stormrend's relentless advance. |

### Light Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Bolt** | 120 | Light | 400 | — | 1 | Drift | Insanely fast attack buggy that's hard to catch and hard to pin down. |
| **Tempest Raider** | 156 | Light | 700 | — | 2 | Rear fire | Raider vehicle designed to shoot, scoot, and shoot again. |
| **Thunderclap** | 156 | Light | 1,400 | 100 | 3 | Fire on move | Mobile artillery — embodies Stormrend philosophy: shoots, moves, never stops. |
| **Surge Node** | 120 | Light | 700 | — | 2 | Momentum aura | Force multiplier that supercharges Stormrend's aggression. |

### APCs

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Gust APC** | 288 | Medium | 700 | — | 2 | Shock drop | Blitz transport deploying infantry mid-firefight for maximum chaos. |

### Tanks

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Riptide** | 495 | Medium | 1,400 | 100 | 3 | Flanking bonus | A tank that fights best at full speed. +damage when flanking. |
| **Cyclone** | 428 | Medium | 1,800 | 200 | 4 | First shot bonus | Devastating first-strike tank that crumbles in sustained combat. |

### Helicopters

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Flickerhawk** | 195 | Aircraft | 700 | — | 2 | Strafe run | Fast, fragile attack helicopter — chews up infantry and harvesters. |
| **Scrap Hawk** | 195 | Aircraft | 700 | — | 2 | Repair + salvage | Flying salvage helicopter turning enemy losses into Stormrend resources. |
| **Vortex** | 240 | Aircraft | 1,400 | 100 | 3 | Alpha strike | Dumps entire payload then retreats to reload. |

### Jets

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Razorbeak** | 192 | Aircraft | 1,400 | 100 | 3 | Fast rearm | Spends more time in combat than on the ground — reduced rearm time. |

### Special / Superunits

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Stormbreaker** | 228 | Aircraft | 4,500 | 800 | 10 | Carpet bomb | 🏆 Ultimate Stormrend unit. Carpet Bombing Run along 30-cell path. 90-sec rearm. Only 1 may exist. |

### Defenses (Deployable)

| Unit | HP | Armor | Cost | VC | Ability | Description |
|------|-----|-------|------|----|---------|-------------|
| **Chainlink Barrier** | 360 | Building | 200 | — | — | More of a speed bump than a wall — Stormrend doesn't believe in standing still. |
| **Snap Turret** | 292 | Building | 400 | — | Fast acquire | First to shoot and first to die. |
| **Flicker Mine** | 225 | Building | 200 | — | — | 🥷 Stealthed defensive mine for base protection. |

### Naval Units

| Unit | Description |
|------|-------------|
| **Lightning Skiff** | Fastest naval unit in the game — raids supply lines at sea. |
| **Riptide Sub** | 🥷 Stealth submarine. Fast and lethal for a single pass. |
| **Storm Destroyer** | High-speed destroyer embodying Stormrend's naval blitz doctrine. |

---

## 🔍 Detector Units
Stormrend has limited stealth detection — relies on speed and forward presence rather than dedicated detectors.

## 🥷 Stealth Units
**Flicker Mine** (static, always stealthed) | **Riptide Sub** (naval, underwater stealth)
