# Bastion — Defense Primary

> *"Every inch of ground we hold has been paid for in steel and sweat. We do not yield it lightly."*

**Campaign:** Iron Bastion | **Commander:** Castellan Mira | **Color:** Gold/Amber (#F9A825)

---

## Lore

The Bastion Protectorate believes the world is fundamentally hostile and that survival demands layers of protection. Their engineers are the finest fortification architects in existence. Their economy is built on efficiency — extracting maximum value from minimal territory, generating passive income that compounds over time.

They don't conquer. They endure.

Castellan Mira — the player's commander — built her reputation defending positions that every strategic doctrine called indefensible. She became a Castellan (Bastion's highest military rank) not by winning offensive campaigns, but by never losing a defensive one. She views the Cordite Nexus not as a prize to seize, but as a territory to occupy and hold until everyone else runs out of army.

---

## Playstyle

Playing Bastion feels like building a puzzle. Every turret placement matters, every wall angle creates a kill zone, every mine cluster channels the enemy into your firing lanes. You're not trying to kill the enemy — you're trying to make them kill themselves against your defenses.

The satisfaction is watching a massive assault dissolve into wreckage at your gates.

Offense is your weakness. Every push outside your defense perimeter is a calculated risk that probably isn't worth it.

### Strategic Strengths
- Best turrets, walls, mines, and bunkers in the game
- Passive income from Refineries — earns Cordite even without active harvesting
- Superior Reactors (7 VC/sec vs. standard 5) for faster tech
- Fastest structure repair speed; buildings self-heal slowly
- More defensive upgrades than any other faction
- Fortification Network multiplies all defensive structure effectiveness

### Strategic Weaknesses
- Worst offensive unit roster — mediocre outside the base
- Every expansion requires rebuilding the entire defense network
- Virtually no fast units; army is painfully slow
- Long-range artillery outranges turrets and demolishes walls
- Ceding map control means ceding resource nodes and slowly starving

---

## Faction Mechanic: Fortification Network

Bastion structures grow stronger when connected to a Command Hub:

- Turrets and walls within 8 cells of a **Command Hub** gain **+15% HP, +10% damage, +20% repair speed**
- Multiple Command Hubs can overlap, but there is no stacking — largest bonus wins
- The Command Hub itself requires the Tech Lab to build
- Destroying Command Hubs collapses their network bonus in that zone

The Aegis Generator — Bastion's superstructure — projects a dome shield over a base radius, blocking all incoming projectiles for a duration. Requires a Command Hub network to power.

---

## Economy

| Modifier | Value |
|----------|-------|
| Harvester type | LightVehicle |
| Harvester capacity | 500 Cordite |
| Refinery passive income | +15 Cordite/sec (even without active harvesters) |
| Reactor VC | 7 VC/sec (+40% vs. standard 5) |
| Supply cap | 200 (standard) |

Bastion's Refinery generates passive income on top of normal harvesting. Over time, this compounds significantly — a fully built Bastion economy generates more VC than any other faction. The tradeoff is that building and maintaining defenses is expensive, so passive gains are often immediately reinvested.

---

## Tech Tree Unlocks

| Upgrade | Cost | Effect |
|---------|------|--------|
| Redundant Systems | 2,000 VC | Structures regenerate HP 50% faster even without a Restoration Rig |
| Reinforced Turret Mounts | 1,500 VC | All turrets gain +20% damage and +1 range |
| Aegis Overcharge | 1,800 VC | Aegis Generator dome has +50% duration and shorter recharge |
| Network Amplifier | 2,500 VC | Fortification Network range increased from 8 to 12 cells |

---

## Buildings

| Building | Display Name | Prerequisites | HP | Build Time | Description |
|----------|-------------|--------------|-----|-----------|-------------|
| bastion_command_center | Citadel Core | — (pre-placed) | 3,000 | 0s | Fortified HQ. Produces harvesters and scouts. |
| bastion_refinery | Shield Refinery | Command Center | 1,500 | 25s | +15 Cordite/sec passive income regardless of harvester activity. |
| bastion_supply_depot | Bastion Depot | Command Center | 600 | 15s | +20 supply. Max 10. Standard 200 cap. |
| bastion_barracks | Guard Barracks | Refinery | 1,000 | 20s | Trains guard infantry, shield bearers, and defensive specialists. |
| bastion_vehicle_factory | Bastion Armory | Barracks | 1,500 | 30s | Produces armored vehicles including siege platforms and heavy APCs. |
| bastion_reactor | Bastion Reactor | Refinery | 800 | 20s | 7 VC/sec — 40% above standard. Highest VC rate in game. |
| bastion_airfield | Bastion Airfield | Vehicle Factory | 1,200 | 35s | Fortified airfield for interceptors and defensive gunships. |
| bastion_tech_lab | Bastion Tech Lab | Barracks + Vehicle Factory | 800 | 30s | Unlocks shield upgrades and Command Hub construction. |
| bastion_command_hub | Command Hub | Tech Lab | 1,800 | 45s | Extends the Fortification Network and provides passive sensor scan. Required for Aegis Generator. |
| bastion_shipyard | Bastion Drydock | Vehicle Factory + Tech Lab | 2,200 | 50s | Most heavily armoured shipyard. Slow build but ships are fortresses. |

---

## Unit Roster

### Infantry

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Warden** | 120 | Unarmored | 400 | — | 1 | Garrison bonus | Defensive infantry — excels inside structures, mediocre in the open. |
| **Sentinel** | 120 | Unarmored | 700 | — | 2 | Deploy prone | Long-range missile infantry — melts vehicles from behind walls. |
| **Keeper** | 98 | Unarmored | 400 | — | 1 | Engineer repair | Bastion's engineer — the faction's most important non-combat unit. |

### Light Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Patrol Rover** | 156 | Light | 400 | — | 1 | Motion sensor | 🔍 Fast patrol vehicle with advanced sensors — eyes beyond the walls. |
| **Restoration Rig** | 156 | Light | 700 | — | 2 | Dual repair | Fast repair vehicle keeping the fortress in fighting shape. |

### APCs & Support

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Shield Bearer** | 396 | Medium | 1,000 | 50 | 2 | Energy shield | Mobile shield generator creating temporary cover for advancing infantry. |

### Tanks & Heavy Vehicles

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Rampart** | 698 | Medium | 1,400 | 100 | 3 | Entrench mode | A tank that wants to stop moving — best used as a mobile hardpoint. |
| **Phalanx** | 1,110 | Heavy | 1,800 | 200 | 4 | Taunt aura | 🛡️ A rolling fortress that absorbs punishment meant for everything behind it. |
| **Constructor** | 570 | Heavy | 1,000 | 50 | 2 | Build anywhere | Bastion's expansion vehicle — where the Constructor goes, the fortress follows. |

### Artillery

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Bulwark Mortar** | 240 | Heavy | 1,400 | 100 | 3 | Deploy mode | Defensive mortar that creates kill zones around the base. |

### Helicopters

| Unit | HP | Armor | Cost | VC | Supply | Ability | Description |
|------|-----|-------|------|----|--------|---------|-------------|
| **Watcher** | 150 | Aircraft | 400 | — | 1 | Extended vision | 🔍 Unarmed observation helicopter — keeps Bastion informed of incoming threats. |
| **Guardian Drone** | 195 | Aircraft | 700 | — | 2 | Autonomous patrol | Set-and-forget AA drone that patrols the base perimeter. |

### Defenses (Deployable)

| Unit | HP | Armor | Cost | VC | Ability | Description |
|------|-----|-------|------|----|---------|-------------|
| **Citadel Wall** | 765 | Building | 400 | — | Self repair | Massive fortified wall — requires concentrated siege or a Grinder to break. |
| **Denial Field** | 292 | Building | 400 | — | Reusable mine | 🥷 Stealthed energy mine. Slows and damages — forces enemies into kill zones. |
| **Bastion Turret** | 562 | Building | 1,400 | 100 | Upgradeable | The gold-standard turret — high damage, high range, built to last. |
| **Spire Array** | 495 | Building | 1,400 | 100 | Multi-target | AA emplacement — says "this airspace is closed." |
| **Command Hub** | 765 | Building | 2,200 | 300 | Fortification network | Heart of Bastion's defense network. Protect at all costs. |
| **Aegis Generator** | 630 | Building | 3,500 | 600 | Dome shield | 🏆 Energy dome over base — blocks all projectiles for a duration. |

### Naval Units

| Unit | Description |
|------|-------------|
| **Shield Skiff** | Fast patrol vessel with active defense bubble. |
| **Corvette** | 🔍 Multi-role corvette with sonar — detects submarine stealth. |
| **Depth Ward** | 🔍 Submarine minelayer with sonar detection capability. |
| **Citadel Ship** | Massive heavily armored naval fortress. |

---

## 🔍 Detector Units
**Patrol Rover** (motion sensor) | **Watcher** (extended vision) | **Corvette** (naval sonar) | **Depth Ward** (naval sonar)

## 🥷 Stealth Units
**Denial Field** (static energy mine, always stealthed) | **Depth Ward** (naval, underwater stealth)
