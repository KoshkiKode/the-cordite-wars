# Kragmore — Ground Primary

> *"What cannot be outfought can be outweighed."*

**Campaign:** Crimson Tide | **Commander:** Warlord Grok | **Color:** Red (#D32F2F)

---

## Lore

The Kragmore Collective emerged from deep mining colonies, where survival demanded heavy machinery and an unshakable collective discipline. Centuries of living underground forged a culture that prizes mass over elegance, endurance over agility, and the collective over the individual.

Their doctrine is simple: build the biggest army possible, march it forward, and dare anyone to stop it. Their units are purpose-built for ground dominance — thick plates, powerful engines, and enough firepower to reduce any fortification to rubble. What Kragmore cannot do is look up. Their air capacity is effectively an afterthought, and any commander who leads a Kragmore force against a sky full of aircraft is going to have a bad day.

Warlord Grok is a veteran armored commander who has never lost a war of attrition. He is not subtle, he is not patient with clever strategies, and he has exactly zero interest in diplomacy. He wants the Nexus because the Collective needs it, and that's the end of the discussion.

---

## Playstyle

Playing Kragmore feels like driving a freight train. Early game is agonizingly slow — your tanks lumber, your economy crawls, and aggressive opponents will probe your edges relentlessly. But once the Kragmore engine reaches critical mass, it is nearly unstoppable. You build an army that fills the screen, march it forward, and dare anyone to stand in the way.

The satisfaction is the *crunch* — the moment your wall of armor rolls over the enemy base and there is simply nothing they can do.

### Strategic Strengths
- Most durable tanks and heaviest armor in the game
- Best and most varied infantry roster — specialists for every situation
- Crush mechanic: heavy units physically destroy enemy infantry underfoot
- Artillery denies entire map zones; tanks push through defenses
- Army value scales harder than any faction past mid-game
- Highest income potential per trip (enormous harvester capacity)
- Highest supply cap (220) — supports the largest armies

### Strategic Weaknesses
- Near-zero anti-air — limited to a mediocre flak turret and infantry missiles
- Slowest expansion, slowest army, slowest tech progression
- Heavy vehicles need roads and flat ground — hills and rivers cripple columns
- Cannot surgically remove targets — must brute-force through everything
- Vulnerable to hit-and-run harassment that the slow army can't catch

---

## Faction Mechanic: Horde Protocol

Kragmore units gain power through massed formation:

| Group Size | Damage Bonus | Armor Bonus |
|-----------|-------------|------------|
| 5+ ground units within 10 cells | +10% | +10% |
| 10+ ground units within 10 cells | +20% | +15% |
| 20+ ground units within 10 cells | +25% | +20% |

This incentivizes deathball composition and makes splitting forces a costly mistake. Every unit with the **Horde Protocol** ability tag contributes to and benefits from this mechanic.

---

## Economy

| Modifier | Value |
|----------|-------|
| Harvester type | HeavyVehicle (slow, enormous capacity) |
| Harvester capacity | 1,000 Cordite per trip (2x base) |
| Harvester speed | 0.10 cells/sec (very slow) |
| Reactor VC | 5 VC/sec (standard) |
| Supply cap | 220 (highest in game, +10% over standard) |

Place refineries close to nodes. Long-haul harvesting is brutal — at 35 cells a Kragmore rig earns only ~30 C/min. At 10 cells it earns ~92 C/min. Economy placement is a strategic priority.

---

## Tech Tree Unlocks

| Upgrade | Cost | Effect |
|---------|------|--------|
| Heavy Plate Kits | 1,500 VC | All ground vehicles gain +15% HP |
| Commissar Training | 2,000 VC | Infantry units deal +20% damage when near a tank or APC |
| Fortified Advance | 1,800 VC | Bunkers and Ironwalls gain +25% HP and self-repair |
| Crush Override | 1,200 VC | HeavyVehicles can crush Medium-armor units (not just infantry) |

---

## Buildings

| Building | Display Name | Prerequisites | HP | Build Time | Description |
|----------|-------------|--------------|-----|-----------|-------------|
| kragmore_command_center | War Command | — (pre-placed) | 3,000 | 0s | HQ. Produces heavy harvesters. Provides 20 starting supply (free Depot). |
| kragmore_refinery | Ore Refinery | Command Center | 1,500 | 25s | Processes Cordite from heavy rigs. Place close to nodes. |
| kragmore_supply_depot | Supply Bunker | Command Center | 600 | 15s | +20 supply. Max 10 bunkers = 220 total. |
| kragmore_barracks | Drill Barracks | Refinery | 1,000 | 20s | Trains infantry including heavy troopers and demolition crews. |
| kragmore_vehicle_factory | Kragmore Foundry | Barracks | 1,500 | 30s | Forges armored vehicles. Required for War Forge. |
| kragmore_reactor | Kragmore Reactor | Refinery | 800 | 20s | 5 VC/sec, capped at 500 VC. |
| kragmore_airfield | Kragmore Airfield | Vehicle Factory | 1,200 | 35s | Heavy-lift airfield for gunship and bomber production. |
| kragmore_tech_lab | Kragmore Lab | Barracks + Vehicle Factory | 800 | 30s | Research. Required before War Forge. |
| kragmore_war_forge | War Forge | Tech Lab + Vehicle Factory | 2,000 | 60s | Unlocks the Mammoth and advanced armor upgrades. |
| kragmore_shipyard | Industrial Dock | Vehicle Factory + Tech Lab | 2,000 | 48s | Maximum tonnage naval doctrine. Must be adjacent to water. |

---

## Unit Roster

### Infantry

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Vanguard** | 120 | Unarmored | 200 | — | 1 | Horde Protocol | Bread-and-butter line infantry — cheap, tough in groups. |
| **Ironclad** | 142 | Unarmored | 700 | — | 2 | Sandbag cover | Heavy weapons team — digs in and punishes vehicles at range. |
| **Mole Rat** | 98 | Unarmored | 700 | — | 2 | Tunnel attack | 🥷 Suicidal sapper that burrows under defenses. Stealthed. |
| **Spotter** | 75 | Unarmored | 200 | — | 1 | Binoculars | Cheap forward observer — makes artillery terrifying. |

### Light Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Dust Runner** | 156 | Light | 400 | — | 1 | Smoke screen | Fast recon vehicle — scouting, not fighting. |
| **Minelayer** | 156 | Light | 700 | — | 2 | Deploy mines | Deploys hidden mines across movement corridors. |

### Tanks & APCs

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Bulwark** | 630 | Medium | 1,400 | 100 | 3 | Ammo switch | Workhorse tank — nothing fancy, just a lot of gun and armor. |
| **Anvil** | 765 | Medium | 2,200 | 300 | 5 | Horde Protocol | Dual-cannon tank — slow, expensive, absolutely devastating. |
| **Hauler APC** | 396 | Medium | 700 | — | 2 | Large transport | Massive troop carrier delivering infantry to the front. |

### Heavy Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Grinder** | 750 | Heavy | 2,200 | 300 | 5 | Melee siege | A mobile building-eater. Walks up to a wall, creates a hole. |
| **Wrecker** | 660 | Heavy | 700 | — | 2 | Repair + salvage | Mobile repair rig keeping the column rolling. Recycles enemy wrecks. |
| **Mammoth** | 1,110 | Heavy | 3,500 | 600 | 10 | Regeneration | 🏆 The king of the battlefield — a mobile fortress. Demands an answer or wins the game. Requires War Forge. |

### Artillery

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Quake Battery** | 240 | Heavy | 1,800 | 200 | 4 | Deploy mode | Classic artillery — sets up, rains shells, turns areas into craters. |

### Helicopters

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Strix Gunship** | 330 | Aircraft | 1,400 | 100 | 3 | — | Kragmore's only aircraft — a flying tank trading speed for survivability. |

### Defenses

| Unit | HP | Armor | Cost | VC | Ability | Description |
|------|-----|-------|------|----|---------|-------------|
| **Ironwall** | 630 | Building | 400 | — | Garrison wall | Massive concrete-and-steel wall segment — armies break against it. |
| **Bunker** | 698 | Building | 1,000 | 50 | Garrison bunker | Heavily fortified infantry fighting position. Hard to crack without siege. |
| **Flak Nest** | 428 | Building | 700 | — | — | Reluctant AA defense — a concession that aircraft exist and must be addressed. |
| **Tremor Mine** | 225 | Building | 200 | — | — | 🥷 Buried anti-vehicle mine — cheap, lethal, paranoia-inducing. Stealthed. |

### Naval Units

| Unit | Description |
|------|-------------|
| **Iron Trawler** | Heavy assault vessel embodying Kragmore ground doctrine at sea. |
| **Deep Bore** | Stealth submarine; 🥷 stealthed underwater. |
| **Leviathan** | Super-heavy warship — a floating Mammoth. |
| **Dreadnought** | Ultimate Kragmore naval unit: maximum firepower, maximum armor. |

---

## 🔍 Detector Units
Kragmore has limited stealth detection — primarily via forward observers and visual range.

## 🥷 Stealth Units
**Mole Rat** (infantry, always stealthed) | **Tremor Mine** (static, always stealthed) | **Deep Bore** (naval, underwater stealth)
