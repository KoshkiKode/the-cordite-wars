# Ironmarch — Ground + Defense Hybrid

> *"We do not rush. We arrive."*

**Campaign:** Steel March | **Commander:** Marshal Volkov | **Color:** Green (#558B2F)

---

## Lore

The Ironmarch Compact fights wars like they build roads — one mile at a time, paved in steel. Their doctrine is methodical advance: establish a position, fortify it, then push forward to the next. They have never lost a war of attrition and have never won one quickly.

Ironmarch culture values reliability, the weight of steel, and the satisfaction of watching the map slowly fill with your color. Marshal Volkov — the player's commander — is a logistics genius who transformed Ironmarch from a purely defensive faction into a slow but inexorable offensive force. His campaign *Steel March* is nine missions of grinding, inevitable advance.

---

## Playstyle

Playing Ironmarch feels like an unstoppable glacier. You don't rush — you *arrive*. You build a forward base, throw up turrets and walls, then inch your armor column to the next ridgeline. Every position you take becomes permanent.

Your nightmare is aircraft. Watching helicopters pick apart your beautiful armored column is genuinely painful — and there is almost nothing you can do about it.

### Strategic Strengths
- Can build turrets and walls *anywhere on the map*, not just the home base
- Strong tanks backed by mobile turret deployment — hard to dislodge from a position
- Excels in chokepoints, narrow passes, and urban terrain
- Fortified positions mean losing ground units doesn't mean losing territory
- Strongest faction in the 10–20 minute window when ground armies peak
- Fortified Refineries (extra HP + built-in point-defense turret) make economic raids extremely difficult
- Higher supply cap (210) supports combined arms of ground army + forward structures

### Strategic Weaknesses
- Catastrophically vulnerable to air — worst anti-air in the game
- Slowest repositioning — if you push the wrong lane, you're committed for minutes
- Forward bases are expensive; overextension leads to bankruptcy
- Everything moves in a straight line, slowly — easy to predict and kite
- Bastion out-turtles them; Valkyr and Stormrend outmaneuver them

---

## Faction Mechanic: Forward Operations

The **FOB Truck** (unique HeavyVehicle) deploys into a **Forward Operating Base**:

- A deployed FOB allows constructing turrets, walls, wire fields, and basic infantry/vehicle production *anywhere on the map*
- Ironmarch essentially has two bases at once — the home HQ and the forward FOB
- The FOB can be packed up and redeployed to a new location, though this takes significant time
- Destroying the FOB Truck before it deploys prevents the forward push; destroying it after requires dismantling the entire FOB network

The **Forward Assembly** building (built at home base) provides an additional forward production facility — it can be placed anywhere on the map, enabling Ironmarch's push-and-fortify strategy without needing the FOB Truck.

---

## Economy

| Modifier | Value |
|----------|-------|
| Harvester type | LightVehicle |
| Harvester capacity | 500 Cordite |
| Refinery HP | 2,250 (50% more than standard 1,500) |
| Refinery passive defense | Built-in point-defense turret |
| Supply cap | 210 (+5% over standard 200) |
| Defense cost | −15% cheaper |
| Air unit cost | +30% more expensive |

Ironmarch's fortified economy is very hard to raid. The refinery's built-in turret deters harasser helicopters and light vehicles. The higher supply cap supports more forward-based structures without cutting into the army.

---

## Tech Tree Unlocks

| Upgrade | Cost | Effect |
|---------|------|--------|
| Reinforced FOB | 2,000 VC | FOB buildings gain +25% HP and repair 2x faster |
| FOB Rapid Deployment | 1,500 VC | FOB Truck deploy/undeploy time reduced by 50% |
| Tactical Entrenching Kits | 1,800 VC | All Infantry gain the ability to dig a foxhole, gaining +30% defense |
| Siege Tank Munitions | 2,500 VC | Siegebreaker and Stonehail deal +30% damage vs. structures |

---

## Buildings

| Building | Display Name | Prerequisites | HP | Build Time | Description |
|----------|-------------|--------------|-----|-----------|-------------|
| ironmarch_command_center | Field HQ | — (pre-placed) | 3,000 | 0s | Mobile command structure. Produces ground harvesters and scouts. |
| ironmarch_refinery | March Refinery | Command Center | 2,250 | 25s | 50% more HP than standard + built-in point-defense turret. Extremely raid-resistant. |
| ironmarch_supply_depot | March Depot | Command Center | 600 | 15s | +20 supply. Max 10 depots = 210 cap (with faction +10 bonus). |
| ironmarch_barracks | Ironmarch Barracks | Refinery | 1,000 | 20s | Trains assault infantry, combat engineers, and suppression units. |
| ironmarch_vehicle_factory | Iron Foundry | Barracks | 1,500 | 30s | Produces armored vehicles and fortified APCs. |
| ironmarch_reactor | March Reactor | Refinery | 800 | 20s | Standard 5 VC/sec, capped at 500 VC. |
| ironmarch_forward_assembly | Forward Assembly | Command Center | 1,200 | 20s | Deployable FOB. Can be placed anywhere. Enables forward infantry/vehicle production. |
| ironmarch_airfield | Ironmarch Airfield | Vehicle Factory | 1,200 | 35s | Ground-support aircraft and anti-armor helicopters. |
| ironmarch_tech_lab | Ironmarch Lab | Barracks + Vehicle Factory | 800 | 30s | Research for armor, fortification, and assembly upgrades. |
| ironmarch_shipyard | Ironmarch War Dock | Vehicle Factory + Tech Lab | 1,900 | 42s | Modular rapid-assembly dock. One mile at a time, paved in steel. |

---

## Unit Roster

### Infantry

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Holdfast** | 142 | Unarmored | 400 | — | 1 | Foxhole dig | Tough defensive infantry that literally digs in wherever they stop. |
| **Breacher** | 120 | Unarmored | 700 | — | 2 | Breach garrison | Close-quarters specialist that clears buildings and punches through walls. |
| **Field Engineer** | 98 | Unarmored | 400 | — | 1 | FOB builder | Backbone of Ironmarch's forward-basing strategy. |

### Light Vehicles & Scouts

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Pathcutter** | 156 | Light | 400 | — | 1 | Road builder | Scout that literally paves the way for the main army. |
| **Signal Truck** | 156 | Light | 700 | — | 2 | Radar jam | 🔍 Electronic warfare vehicle controlling the information battlefield. Detector. |

### APCs & Transport

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Trenchline APC** | 396 | Medium | 1,000 | 50 | 2 | Deploy bunker | APC that transforms into a forward bunker — ultimate mobile defense. |

### Tanks

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Basalt** | 630 | Medium | 1,400 | 100 | 3 | Hull down | Reliable medium tank designed to fight from prepared positions. |
| **Siegebreaker** | 698 | Medium | 2,200 | 300 | 4 | Structure bonus | Purpose-built wall-breaker leading assaults on enemy fortifications. |

### Heavy Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Dozer** | 750 | Heavy | 1,000 | 50 | 2 | Terrain modify | Engineering vehicle that reshapes the battlefield. |
| **Field Hospital** | 570 | Heavy | 1,000 | 50 | 2 | Heal + revive | Mobile medical unit keeping Ironmarch's infantry fighting longer. |
| **FOB Truck** | 660 | Heavy | 1,800 | 200 | 4 | Deploy FOB | 🏗️ Deploys into a full Forward Operating Base anywhere on the map. |
| **Juggernaut** | 1,020 | Heavy | 3,500 | 600 | 8 | Deploy turret | A turret with an engine — moves to position, deploys, nothing survives its line of fire. |

### Artillery

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Stonehail** | 240 | Heavy | 1,800 | 200 | 4 | Deploy mode | Rocket artillery blanketing an area in explosions. |

### Helicopters

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Bullfrog** | 285 | Aircraft | 1,000 | 50 | 2 | Paradrop | Armored transport helicopter — Ironmarch's sole concession to aerial mobility. |

### Defenses (Deployable)

| Unit | HP | Armor | Cost | VC | Ability | Description |
|------|-----|-------|------|----|---------|-------------|
| **Wire Field** | 225 | Building | 200 | — | Slow field | Cheap area denial channeling enemy movement into kill zones. |
| **Fieldwork Wall** | 562 | Building | 200 | — | Garrison wall | Rapidly-deployed field fortification — quantity has its own quality. |
| **Watchtower** | 495 | Building | 1,000 | 50 | Garrison tower | Multi-purpose defense tower providing both firepower and intelligence. |

### Naval Units

| Unit | Description |
|------|-------------|
| **March Barge** | Armored transport barge for Ironmarch seaborne assaults. |
| **Ironclad Frigate** | Heavy frigate applying Ironmarch ground doctrine to the sea. |
| **Trench Diver** | Submarine with forward basing capability. |
| **Battleship** | Ironmarch's ultimate naval unit — the FOB Truck of the sea. |

---

## 🔍 Detector Units
**Signal Truck** (radar jam + detection)

## 🥷 Stealth Units
Ironmarch has no stealthed units — they believe in the opposite of subtlety.
