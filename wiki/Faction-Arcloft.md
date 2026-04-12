# Arcloft — Air + Defense Hybrid

> *"The sky is our blade. The fortress is our shield. And we see everything."*

**Campaign:** Silent Watch | **Commander:** Watcher Prime Idris | **Color:** Cyan (#00ACC1)

---

## Lore

The Arcloft Sovereignty rules from floating citadels ringed with flak batteries and launch bays, elevated above the ground conflicts they regard with a measure of disdain. Their doctrine pairs aerial dominance with impenetrable ground fortifications — the sky is their blade, the fortress is their shield.

Arcloft intelligence is unmatched. Their surveillance network covers vast territory. Their Overwatch Zones make surprise attacks nearly impossible. Watcher Prime Idris — the player's commander — is first and foremost an intelligence officer who happened to become the highest military authority in the Sovereignty. He prefers to know everything before committing to anything, and his campaign name, *Silent Watch*, reflects that philosophy: information as a weapon, precision as a virtue.

---

## Playstyle

Playing Arcloft feels like being a chess player who controls the board. Your base is a no-fly zone backed by the best AA in the game. Your aircraft aren't as individually lethal as Valkyr's, but they operate with the safety net of a fortified home to retreat to. You win by establishing air superiority over the map while making your base untouchable.

Your weakness is the ground between your base and theirs — no heavy armor, no siege capability, and any ground push feels anemic.

### Strategic Strengths
- Best anti-air coverage in the game — both mobile and static
- Safe base: fortified enough to survive raids without pulling units back
- AA turrets at expansions makes enemy air operations very costly
- Flexibility: switch between air offense and base defense fluidly
- Hard counters pure air factions (Valkyr's worst matchup)
- Helicopter harvesters are raid-resistant

### Strategic Weaknesses
- Zero heavy vehicles, no tanks, no artillery — cannot break fortified positions
- Smallest supply cap (180) — fewer total units than any other faction
- Aircraft are 10–15% less effective than Valkyr equivalents
- Defenses are 10–15% weaker than Bastion equivalents
- Ground-based expansions have no native armor to protect them
- Predictable: opponents pre-build AA, negating air advantage

---

## Faction Mechanic: Overwatch Protocol

The player can designate up to **3 circular Overwatch Zones** on the map (radius ~15 cells each):

- Any idle Arcloft aircraft automatically patrols the nearest Overwatch Zone
- Aircraft in Overwatch mode gain **+15% detection range** and **+10% reaction speed**
- Overwatch Zones project a passive radar scan — any unit entering is detected even through fog of war
- The Overwatch Tower building (unique to Arcloft) creates a permanent Overwatch Zone at its location without consuming one of the 3 designation slots

---

## Economy

| Modifier | Value |
|----------|-------|
| Harvester type | Helicopter (bypasses terrain) |
| Harvester capacity | 500 Cordite per trip |
| Harvester speed mod | +10% faster than standard helicopters |
| Reactor VC | 6 VC/sec (+1 vs. standard 5) |
| Supply cap | 180 (−10% vs. standard 200) |
| Defense cost | −10% cheaper |
| Air unit cost | −10% cheaper |
| Ground unit cost | +10% more expensive |

Arcloft's helicopter harvesters are safe from ground raiding and bypass terrain. The smaller supply cap is the primary constraint — it forces careful unit selection and makes supply management critical.

---

## Tech Tree Unlocks

| Upgrade | Cost | Effect |
|---------|------|--------|
| Expanded Overwatch | 1,800 VC | Overwatch Zone radius increases from 15 to 20 cells |
| AWACS Net Integration | 1,500 VC | Vigilant jets in AWACS mode share vision with all Arcloft units on the map |
| Quantum Detection Grid | 2,000 VC | All Arcloft detection range +25%; Rampart Walls detect stealth in a wider area |
| Sky Fortress Protocol | 3,000 VC | Sky Bastion HP +30%, gains ability to project an Overwatch Zone from its current position |

---

## Buildings

| Building | Display Name | Prerequisites | HP | Build Time | Description |
|----------|-------------|--------------|-----|-----------|-------------|
| arcloft_command_center | Sky Citadel | — (pre-placed) | 3,000 | 0s | Elevated HQ. Produces helicopter harvesters and aerial scouts. Max 180 supply. |
| arcloft_refinery | Cloud Refinery | Command Center | 1,500 | 25s | Receives Cordite from helicopter harvesters. High efficiency due to fly-over. |
| arcloft_supply_depot | Arcloft Depot | Command Center | 600 | 15s | +20 supply. Max 9 depots = 180 supply total. |
| arcloft_barracks | Sentinel Barracks | Refinery | 1,000 | 20s | Trains Arcloft sentinel infantry, EMP specialists, and drone operators. |
| arcloft_vehicle_factory | Arcloft Workshop | Barracks | 1,500 | 30s | Precision workshop — light vehicles and anti-air platforms. |
| arcloft_reactor | Arc Reactor | Refinery | 800 | 20s | 6 VC/sec (+20% above standard). |
| arcloft_overwatch_tower | Overwatch Tower | Barracks | 900 | 20s | Creates a permanent Overwatch Zone. Effective chokepoint denial tool. |
| arcloft_airfield | Arcloft Skyport | Vehicle Factory | 1,200 | 35s | Primary air production — interceptors and EMP bombers. |
| arcloft_tech_lab | Arcloft Tech Lab | Barracks + Vehicle Factory | 800 | 30s | Advanced research. Prerequisite for Overwatch Tower network upgrades. |
| arcloft_shipyard | Naval Platform | Vehicle Factory + Tech Lab | 1,800 | 45s | Floating platform using levitation tech. Must be adjacent to water. |

---

## Unit Roster

### Infantry

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Horizon Guard** | 120 | Unarmored | 400 | — | 1 | Overwatch bonus | Versatile line infantry — excels when fighting within Overwatch Zones. Can garrison. |
| **Stormshot** | 98 | Unarmored | 700 | — | 2 | Mode switch | Walking flak platform — Arcloft's answer to shutting down airspace. |
| **Sky Marshal** | 120 | Unarmored | 700 | — | 2 | Target designator | Officer infantry coordinating ground and air operations. |

### Light Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Cirrus Runner** | 156 | Light | 400 | — | 1 | Radar ping | Fast scout with built-in AA — Arcloft never leaves home without air defense. |
| **Nimbus Transport** | 342 | Medium | 700 | — | 2 | Point defense | Armored transport with active missile defense — surprisingly survivable. |

### Helicopters

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Patch Drone** | 150 | Aircraft | 400 | — | 1 | Auto repair | Small repair drone. 🥷 Stealthed for infiltration support. |
| **Aether Relay** | 240 | Aircraft | 1,000 | 50 | 2 | Mobile overwatch | Mobile command relay projecting Arcloft's defensive network forward. |
| **Stratos** | 285 | Aircraft | 1,400 | 100 | 3 | Fire while moving | Versatile attack helicopter. Best within controlled airspace. |
| **Templar** | 240 | Aircraft | 1,400 | 100 | 3 | Overwatch eligible | Dedicated air-to-air helicopter — primary tool for asserting sky dominance. |

### Jets

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Vigilant** | 156 | Aircraft | 1,000 | 50 | 2 | AWACS mode | 🔍 Flying early-warning system — makes sneaking up on Arcloft nearly impossible. Detector. |
| **Apex** | 192 | Aircraft | 1,800 | 200 | 4 | Loadout switch | Flexible fighter adapting to the current threat. |

### Special / Superunits

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Sky Bastion** | 465 | Aircraft | 4,500 | 800 | 10 | Mobile fortress | 🏆 The ultimate Arcloft unit — a flying fortress. Functions as Airstrip + Overwatch Zone projector + combat unit. Only 1 may exist. Incredibly slow. |

### Defenses (Deployable)

| Unit | HP | Armor | Cost | VC | Ability | Description |
|------|-----|-------|------|----|---------|-------------|
| **Rampart Wall** | 562 | Building | 400 | — | Sensor wall | 🔍 Wall that doubles as a detection network. Detects stealthed units. |
| **Arc Turret** | 495 | Building | 1,000 | 50 | Chain damage | Anti-infantry/anti-light-vehicle turret with splash-chain damage. |
| **Interceptor Battery** | 428 | Building | 1,000 | 50 | Point defense | Anti-missile system protecting nearby structures from bombardment. |
| **Flak Citadel** | 562 | Building | 1,800 | 200 | Multi-target | 💥 Devastating AA emplacement — approaching Arcloft by air requires serious commitment. |

### Naval Units

| Unit | Description |
|------|-------------|
| **Sky Skimmer** | Fast surface skimmer using partial anti-grav. |
| **Phantom Sub** | 🥷 Stealth submarine. |
| **Arc Cruiser** | Heavy cruiser with powerful anti-air capability — extends Arcloft's AA umbrella to sea lanes. |

---

## 🔍 Detector Units
**Vigilant** (AWACS mode jet) | **Rampart Wall** (sensor network) | **Overwatch Tower** (Overwatch Zone radar)

## 🥷 Stealth Units
**Patch Drone** (infiltration support, stealthed) | **Phantom Sub** (naval, underwater stealth)
