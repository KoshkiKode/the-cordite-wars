# Valkyr — Air Primary

> *"The sky is sovereign territory. Everything beneath it is just a target."*

**Campaign:** Sovereign Skies | **Commander:** Wing Commander Aelara | **Color:** Blue (#2196F3)

---

## Lore

The Valkyr Ascendancy abandoned the scarred earth generations ago, constructing their civilization on airborne carrier platforms and mountain-top aeries. They view ground warfare as barbaric and wasteful — a relic of lesser civilizations. Their technology revolves around anti-gravity systems, precision optics, and ultra-lightweight alloys engineered for altitude.

Valkyr culture prizes elegance, precision, and the view from above. Ground-dwellers are referred to, not unkindly, as "those who haven't left yet." Wing Commander Aelara — the player's commander — is a doctrine theorist who spent years arguing that air supremacy alone could win any war, then found herself in command during the Cordite Wars and was forced to prove it.

---

## Playstyle

Playing Valkyr feels like controlling a swarm of wasps. You're everywhere and nowhere — hitting supply lines, sniping key buildings, dodging AA fire. Your ground forces exist only to hold captured points long enough for air reinforcements to arrive. You never fight fair; if the enemy is ready for you, you're already gone.

**High APM required.** Idle aircraft are wasted aircraft.

### Strategic Strengths
- Best aircraft in the game — widest variety, highest individual quality
- Fastest map presence: can strike anywhere within seconds
- Fastest scouts and extended radar via flying units
- Precision bombers can delete key structures without committing ground forces
- Army largely ignores terrain restrictions

### Strategic Weaknesses
- Worst ground infantry and vehicles in the game
- Concentrated AA fire devastates the entire army composition
- Individual aircraft cost 30–50% more than equivalent ground units
- Cannot hold ground against a determined ground push
- Jets must return to airfields to rearm — destroy the fields, strand the jets

---

## Faction Mechanic: Sortie System

Valkyr jets operate on a **sortie model**. After launching from an Airstrip, a jet performs its attack run, then must return to rearm. Jets have a limited loiter time (15–25 seconds depending on type). Helicopters are exempt from sorties.

- Destroying Airstrips strands jets — they can make one more pass, then auto-land and become vulnerable ground targets
- Skilled players chain sortie timing to maintain constant pressure
- The Carrier Pad produces the Valkyrie Carrier, a flying super-airstrip that extends operational range across the entire map

---

## Economy

| Modifier | Value |
|----------|-------|
| Harvester type | Helicopter (bypasses terrain) |
| Harvester capacity | 350 Cordite per trip |
| Reactor cost | −20% cheaper than standard |
| VC generation | 5 VC/sec (standard) |
| Supply cap | 200 (standard) |

Valkyr's helicopter harvesters bypass terrain entirely, making every node effectively "closer." However, they carry less per trip. Cheap Reactors allow early tech access, but expensive aircraft drain resources fast. Valkyr games tend to be short — they win before the opponent can max out.

---

## Tech Tree Unlocks

| Upgrade | Cost | Effect |
|---------|------|--------|
| Afterburner Array | 1,500 VC | Jets gain +15% speed and +10% turning rate |
| Air-to-Ground Munitions | 2,000 VC | Bombers deal +25% damage vs. buildings and heavy vehicles |
| Extended Fuel Tanks | 1,200 VC | Jets gain +50% loiter time before needing to rearm |
| EMP Hardening | 2,500 VC | All Valkyr aircraft become immune to EMP effects |

---

## Buildings

| Building | Display Name | Prerequisites | HP | Build Time | Description |
|----------|-------------|--------------|-----|-----------|-------------|
| valkyr_command_center | Aerie Command | — (pre-placed) | 3,000 | 0s | HQ. Produces harvesters and scouts. |
| valkyr_refinery | Sky Refinery | Command Center | 1,500 | 25s | Processes Cordite from helicopter harvesters. |
| valkyr_supply_depot | Aerie Depot | Command Center | 600 | 15s | +20 supply. Max 10 depots. |
| valkyr_barracks | Wing Barracks | Refinery | 1,000 | 20s | Trains infantry including Skyguard and Gale Troopers. |
| valkyr_vehicle_factory | Valkyr Motor Pool | Barracks | 1,500 | 30s | Produces ground vehicles. |
| valkyr_reactor | Voltaic Spire | Refinery | 800 | 20s | 5 VC/sec. 20% cheaper than standard. |
| valkyr_airfield | Valkyr Airstrip | Vehicle Factory | 1,200 | 35s | Launches and recovers fixed-wing aircraft and gunships. |
| valkyr_tech_lab | Aerie Research | Barracks + Vehicle Factory | 800 | 30s | Unlocks advanced upgrades. Required for Carrier Pad. |
| valkyr_carrier_pad | Carrier Pad | Tech Lab + Reactor | 2,000 | 90s | Constructs the Valkyrie Carrier superunit. |
| valkyr_shipyard | Valkyr Carrier Berth | Vehicle Factory + Tech Lab | 1,750 | 40s | Naval vessels serving as mobile airbases. |

---

## Unit Roster

### Infantry

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Windrunner** | 75 | Unarmored | 200 | — | 1 | Stealth when stationary | Lightly armed recon infantry that paints targets. Stealthed while still. |
| **Gale Trooper** | 98 | Unarmored | 700 | — | 2 | Grapple/reposition | Jump-jet infantry reaching elevated positions others can't. |
| **Skyguard** | 98 | Unarmored | 700 | — | 2 | SAM lock-on | Valkyr's only ground-based AA unit — essential but fragile. |

### Light Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Zephyr Buggy** | 120 | Light | 400 | — | 1 | Afterburner | Extremely fast scout that runs circles around slow armies. |
| **Mistral APC** | 288 | Medium | 700 | — | 2 | Instant deploy | Fast-deploying transport for keeping infantry relevant. |

### Helicopters

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Overwatch Drone** | 150 | Aircraft | 400 | — | 1 | Stealth detection | Tiny autonomous drone — continuous vision and stealth detection. 🔍 |
| **Kestrel** | 240 | Aircraft | 1,000 | 50 | 2 | Recon pulse | Fast attack helicopter and primary scouting platform. 🔍 |
| **Updraft** | 240 | Aircraft | 1,000 | 50 | 2 | Airborne repair | Flying support craft keeps the air fleet operational far from base. |
| **Harrier** | 285 | Aircraft | 1,800 | 200 | 4 | Hover lock | Dedicated tank-hunter helicopter. Backbone of Valkyr's anti-armor. |

### Jets

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Shriek** | 156 | Aircraft | 1,400 | 100 | 3 | Afterburner intercept | Fast interceptor jet that patrols airspace and shreds anything entering. |
| **Peregrine** | 192 | Aircraft | 1,800 | 200 | 4 | Evasive maneuver | Best dogfighter in the game — nothing outruns or outfights it in the sky. |
| **Tempest** | 228 | Aircraft | 2,800 | 400 | 6 | Target designator | Heavy precision bomber that deletes buildings in a single pass. |

### Special / Superunits

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Stormcaller** | 264 | Aircraft | 4,500 | 800 | 10 | EMP strike | Strategic bomber that paralyzes rather than kills — EMP blast disables all electronics in an area. |
| **Valkyrie Carrier** | 420 | Aircraft | 3,500 | 600 | 10 | Mobile airstrip | Massive flying aircraft carrier extending operational range across the map. |

### Defenses

| Unit | HP | Armor | Cost | VC | Ability | Description |
|------|-----|-------|------|----|---------|-------------|
| **Wind Wall** | 428 | Building | 200 | — | Slow aura | Lightweight energy barrier. Stops infantry, crumbles under tank fire. |
| **Downburst Turret** | 360 | Building | 700 | — | — | Mediocre ground defense — Valkyr doesn't rely on turrets. |
| **Skypiercer Turret** | 428 | Building | 1,000 | 50 | — | Dedicated AA emplacement. Valkyr protects their airfields fiercely. |

### Naval Units

| Unit | Description |
|------|-------------|
| **Seabird Cutter** | Fast naval scout and light patrol vessel. |
| **Tempest Frigate** | Multi-role frigate serving as a naval air defense platform. |
| **Depth Hunter** | Anti-submarine hunter, detects stealth submarines. |

---

## 🔍 Detector Units
Valkyr's detector units can reveal stealthed enemies: **Overwatch Drone**, **Kestrel**

## 🥷 Stealth Units
Valkyr's stealthed units: **Windrunner** (stealth while stationary)
