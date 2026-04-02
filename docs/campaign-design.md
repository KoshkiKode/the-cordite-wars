# Cordite Wars: Six Fronts — Campaign Design

> **Document status:** First draft — v0.1
> **Last updated:** 2026-04-01
> **Purpose:** Complete campaign structure for all 6 factions. Drives mission data files in `data/campaign/`.

---

## Campaign Structure

Each faction has a **7–9 mission campaign** that tells the story of the Cordite Wars from their perspective. The campaigns share one critical property: **Mission Final (the "Six Fronts" battle) is the same map, same war, same moment in time — played from each faction's point of view.**

### Narrative Arc Per Faction

Every campaign follows the same dramatic arc with faction-specific flavor:

1. **Tutorial / Establishment** (Missions 1–2): Learn the faction's core mechanics. Small-scale, controlled encounters against a single enemy faction.
2. **Escalation** (Missions 3–4): Face a second enemy faction. Mechanics deepen. The conflict widens.
3. **Complication** (Missions 5–6): Fight two factions simultaneously. Resources are scarce. Hard choices emerge.
4. **Crisis** (Mission 7, or 7–8 for longer campaigns): A critical turning point. Major setback or desperate gamble.
5. **The Six Fronts** (Final Mission): All six factions converge on the **Cordite Nexus** — the largest deposit of Cordite ever discovered. Same map, same starting positions, same AI behavior for the other 5 factions. Only your perspective and objectives differ.

### The Six Fronts (Shared Final Mission)

**Map: Cordite Nexus**
A massive hexagonal map with 6 starting positions arranged around a central mega-deposit. Between each pair of starting positions lies contested territory with smaller Cordite nodes, chokepoints, and strategic terrain.

**How the shared battle works:**
- All 6 factions are present as AI opponents (except the player's faction)
- The AI behavior is scripted identically regardless of which faction the player controls — Kragmore always pushes from the north with a tank column at minute 8, Valkyr always sorties from the east at minute 5, etc.
- What differs is the **player's objectives**, **briefing narrative**, **camera focus**, and **victory condition**
- Each faction has a unique reason to be at the Nexus, a unique path through the battle, and a unique definition of "victory"

**Starting Positions (clockwise from north):**
1. North: Kragmore
2. Northeast: Stormrend
3. Southeast: Valkyr
4. South: Arcloft
5. Southwest: Bastion
6. Northwest: Ironmarch

**Central Feature:** The Cordite Nexus — an enormous deposit worth 100,000 Cordite, accessible only after destroying the neutral defense turrets guarding it. First faction to establish a Refinery on the Nexus gains a massive economic advantage.

---

## Campaign: Valkyr — "Sovereign Skies"

**Theme:** Proving that air power alone can win a war. The Valkyr Ascendancy must demonstrate their doctrine is not just theory — it works against every conceivable threat.

**Missions: 8**

### Mission 1: "First Sortie"
- **Enemy:** Kragmore (weakened, tutorial-level AI)
- **Map:** Small valley with a Kragmore forward outpost
- **Objective:** Destroy the Kragmore supply depot using only aircraft
- **Teaches:** Basic aircraft control, sortie mechanic (launch→strike→return), Airstrip management
- **Narrative:** A Kragmore mining convoy has encroached on Valkyr territory. Commander Aelara orders a precision strike to send a message.
- **Twist:** None — clean tutorial mission
- **Difficulty:** Easy

### Mission 2: "Eye in the Sky"
- **Enemy:** Ironmarch (tutorial-level AI)
- **Map:** Mountainous terrain with an Ironmarch FOB expanding toward Valkyr borders
- **Objective:** Scout and reveal all Ironmarch positions, then destroy their FOB Truck before it deploys
- **Teaches:** Reconnaissance, fog of war, Kestrel's Recon Pulse, target painting with Windrunners
- **Narrative:** Ironmarch is building a forward base dangerously close to Valkyr's southern aeries. Recon first, strike second.
- **Twist:** The FOB Truck is mobile — you must find and destroy it before it reaches the deployment zone
- **Difficulty:** Easy

### Mission 3: "Storm Chaser"
- **Enemy:** Stormrend (medium AI)
- **Map:** Open plains — Stormrend's preferred terrain
- **Objective:** Defend your Airstrip network while destroying Stormrend's momentum. Survive 15 minutes, then counter-attack.
- **Teaches:** Defensive air patrols, Helicopter vs. ground coordination, dealing with multi-vector pressure
- **Narrative:** The Stormrend Pact has launched a blitz against Valkyr's eastern holdings. Their momentum must be broken.
- **Twist:** Stormrend's Momentum Gauge is visible to the player — you must keep it below 50 to prevent Stormbreak
- **Difficulty:** Medium

### Mission 4: "Fortress Cracker"
- **Enemy:** Bastion (medium AI)
- **Map:** Bastion has a fully fortified base with AA coverage. You start with a large air force but limited economy.
- **Objective:** Destroy Bastion's 3 Command Hubs to collapse their defense network, then eliminate the base
- **Teaches:** Surgical strikes, finding gaps in AA coverage, EMP abilities, prioritizing high-value targets
- **Narrative:** Bastion has blockaded a critical Cordite shipping lane. Diplomacy failed. Aelara authorizes a full assault on their fortress.
- **Twist:** Destroying the first Hub alerts Bastion, who repositions AA to cover the remaining two. Timing and misdirection are key.
- **Difficulty:** Medium-Hard

### Mission 5: "Two Fronts"
- **Enemy:** Kragmore (medium) + Arcloft (medium)
- **Map:** V-shaped valley. Kragmore approaches from the north, Arcloft from the east. Valkyr's base is at the V's point.
- **Objective:** Survive simultaneous assaults from both factions and destroy one faction's base
- **Teaches:** Prioritization, splitting air forces, using speed to fight on interior lines
- **Narrative:** An unlikely alliance between Kragmore and Arcloft threatens Valkyr from two directions. Aelara must choose which front to break first.
- **Twist:** The player can intercept a Kragmore supply convoy to cripple their economy, or strike Arcloft's AA network to gain air superiority on that front — but not both
- **Difficulty:** Hard

### Mission 6: "Sky Siege"
- **Enemy:** Arcloft (hard) + Bastion (medium)
- **Map:** Arcloft controls the airspace over a Bastion fortress. Valkyr must fight through contested skies to reach the ground target.
- **Objective:** Achieve air superiority over Arcloft, then use it to crack Bastion's defenses
- **Teaches:** Air-to-air combat priority, combined operations, the concept of "air superiority enables ground victory"
- **Narrative:** Arcloft's Overwatch Zones protect Bastion's fortress from air assault. Break the umbrella, then break the walls.
- **Twist:** Arcloft sends waves of Templars. Valkyr's Peregrines must win air-to-air before the bombers can operate.
- **Difficulty:** Hard

### Mission 7: "The Aerie Falls"
- **Enemy:** Stormrend (hard) + Kragmore (hard)
- **Map:** Valkyr's home aerie — you start with your main base already built, but under siege from two sides
- **Objective:** Evacuate 5 Carrier-loads of civilians to a safe zone while holding off the assault
- **Teaches:** Defensive operations with an offensive faction, managing retreat, escort missions
- **Narrative:** The Cordite Wars have reached Valkyr's homeland. The Ascendancy must protect its people while buying time for a counter-offensive.
- **Twist:** The safe zone is revealed to be compromised halfway through — you must redirect the evacuation fleet while under fire
- **Difficulty:** Very Hard

### Mission 8: "Six Fronts" (Shared Final)
- **Enemy:** All 5 other factions (hard AI)
- **Map:** Cordite Nexus
- **Valkyr Objective:** Establish air superiority over the Nexus. Destroy all enemy Airstrips/AA installations. Plant Valkyr's banner (build an Airstrip) on the Nexus itself.
- **Valkyr Narrative:** Aelara sees the Nexus as the key to ending the war from the sky. If Valkyr controls the airspace above it, no ground force can hold it.
- **Valkyr Unique Mechanic:** Start with 3 veteran Peregrines (pre-upgraded) representing Aelara's personal squadron
- **Difficulty:** Very Hard

---

## Campaign: Kragmore — "Iron Tide"

**Theme:** The unstoppable march. Kragmore's campaign is about building momentum that nothing can stop — and learning to overcome the one thing that can: the sky.

**Missions: 9** (longest campaign — Kragmore is the "slow build" faction)

### Mission 1: "Dust and Steel"
- **Enemy:** Stormrend (tutorial-level AI)
- **Map:** Flat mining territory with scattered Cordite nodes
- **Objective:** Establish an economy and build 10 tanks. Destroy the Stormrend raider camp.
- **Teaches:** Kragmore economy (slow harvesters, big payloads), tank production, Horde Protocol basics
- **Narrative:** Stormrend raiders are hitting Kragmore mining convoys. Marshal Gorsk orders a decisive response: crush them.
- **Twist:** None — pure power fantasy tutorial
- **Difficulty:** Easy

### Mission 2: "Weight of Numbers"
- **Enemy:** Valkyr (tutorial-level AI)
- **Map:** Valley with limited AA coverage options
- **Objective:** Build a Flak Nest network and escort a Mammoth tank convoy through Valkyr air patrols
- **Teaches:** AA defense, Kragmore's air vulnerability, escort mechanics, Flak Nest placement
- **Narrative:** A Mammoth tank column must reach the front lines, but Valkyr controls the sky above the route.
- **Twist:** The convoy route passes through a narrow gorge where Valkyr can concentrate fire — build Flak Nests to clear the path
- **Difficulty:** Easy-Medium

### Mission 3: "Grinding Advance"
- **Enemy:** Bastion (medium AI)
- **Map:** Bastion fortress blocking a mountain pass
- **Objective:** Use Quake Batteries and Grinders to breach Bastion's walls. Capture the pass.
- **Teaches:** Siege warfare, artillery positioning, wall-breaching, combined arms (infantry clear rubble, tanks exploit gaps)
- **Narrative:** Bastion has fortified the only pass to Kragmore's richest mining territory. The walls must fall.
- **Twist:** Bastion deploys Denial Fields that slow Kragmore's already-slow army. Finding and destroying the field generators is key.
- **Difficulty:** Medium

### Mission 4: "The Horde Assembles"
- **Enemy:** Ironmarch (medium AI)
- **Map:** Open terrain with Ironmarch FOBs creating a defensive line
- **Objective:** Mass 20+ ground units and trigger maximum Horde Protocol bonus. Overrun Ironmarch's FOB line.
- **Teaches:** Horde Protocol mastery, deathball composition, the satisfying CRUNCH of overwhelming force
- **Narrative:** Ironmarch has built a defensive line across the frontier. Gorsk's answer: hit it with everything.
- **Twist:** Ironmarch has mined the obvious approach routes. The Mole Rat underground detection ability would be useful... if you had one. Instead, use Spotter infantry to find safe paths.
- **Difficulty:** Medium

### Mission 5: "Anvil and Hammer"
- **Enemy:** Arcloft (medium) + Stormrend (medium)
- **Map:** Two-front battle with Arcloft attacking from the air and Stormrend raiding from the flanks
- **Objective:** Defend against Arcloft air raids while crushing Stormrend's ground forces
- **Teaches:** Multi-front defense, AA prioritization, Kragmore's weakness to air (Arcloft) vs. strength against ground (Stormrend)
- **Narrative:** Arcloft and Stormrend coordinate an assault. Gorsk must protect his skies and crush the ground threat simultaneously.
- **Twist:** Stormrend's raids are a distraction — Arcloft's real target is the Quake Battery park. Losing it cripples siege capability.
- **Difficulty:** Hard

### Mission 6: "Scorched Earth"
- **Enemy:** Valkyr (hard) + Bastion (medium)
- **Map:** Bastion fortress protected by Valkyr air cover
- **Objective:** Push through Valkyr air harassment to reach Bastion's walls, then siege them down
- **Teaches:** Advancing under air attack, prioritizing AA while maintaining the ground push, patience
- **Narrative:** Valkyr and Bastion have formed a pact: Bastion builds the walls, Valkyr guards the sky. Kragmore must break both.
- **Twist:** Destroying Valkyr's Airstrips (hidden behind Bastion's walls) is the key — but reaching them requires breaching the fortress first
- **Difficulty:** Hard

### Mission 7: "Hold the Line"
- **Enemy:** Stormrend (hard) + Ironmarch (hard)
- **Map:** Kragmore defensive position on a ridge. Must hold for 20 minutes against waves.
- **Objective:** Survive escalating waves from two directions. Horde Protocol keeps your forces strong, but you must manage positioning.
- **Teaches:** Defensive Kragmore play, counterattacking between waves, managing a dwindling resource pool
- **Narrative:** Kragmore's supply lines are cut. Gorsk must hold the ridge until reinforcements arrive.
- **Twist:** At minute 15, Ironmarch deploys a Juggernaut super-tank. You must focus fire it before it reaches your line.
- **Difficulty:** Very Hard

### Mission 8: "The March Begins"
- **Enemy:** Arcloft (hard) + Bastion (hard)
- **Map:** Long map — Kragmore starts at one end and must push to the other, through Arcloft air zones and Bastion defense lines
- **Objective:** March from one end of the map to the other. Establish forward Refineries as you advance. Never stop moving.
- **Teaches:** Kragmore's identity — the unstoppable advance. Economy management during a long push.
- **Narrative:** The path to the Cordite Nexus requires pushing through enemy territory. Gorsk commits his entire army. There is no retreat.
- **Twist:** Resources deplete as you advance. You must capture enemy Refineries to sustain the push.
- **Difficulty:** Very Hard

### Mission 9: "Six Fronts" (Shared Final)
- **Enemy:** All 5 other factions (hard AI)
- **Map:** Cordite Nexus
- **Kragmore Objective:** Reach the Nexus with a Mammoth tank column and build a Refinery on it. Defend the Refinery until 10,000 Cordite is harvested.
- **Kragmore Narrative:** Gorsk sees the Nexus as the ultimate prize — enough Cordite to fuel Kragmore's industry for generations. The march ends here.
- **Kragmore Unique Mechanic:** Start with Horde Protocol already at Commissar Training level. Gorsk's personal Mammoth (hero unit, 2x HP) leads the column.
- **Difficulty:** Very Hard

---

## Campaign: Bastion — "The Last Wall"

**Theme:** Endurance and sacrifice. Bastion's campaign is about building something worth defending and discovering that walls alone cannot protect what matters.

**Missions: 7** (shortest campaign — Bastion's gameplay is methodical and missions run longer)

### Mission 1: "Perimeter"
- **Enemy:** Stormrend (tutorial-level AI)
- **Map:** Small base with Cordite nodes nearby
- **Objective:** Build a complete defense network (walls, turrets, Command Hub) and survive 3 Stormrend raids
- **Teaches:** Wall placement, turret positioning, kill zones, Command Hub network bonus, repair mechanics
- **Narrative:** Engineer-Commander Thane receives reports of Stormrend scouts near the outer settlements. Time to fortify.
- **Twist:** None — pure defense tutorial. The satisfaction of watching raiders break on your walls.
- **Difficulty:** Easy

### Mission 2: "The Shield Test"
- **Enemy:** Valkyr (tutorial-level AI)
- **Map:** Base with exposed flanks and limited AA coverage
- **Objective:** Build AA turrets (Spire Arrays) and deploy the Aegis Generator to survive Valkyr sorties
- **Teaches:** Anti-air defense, Aegis Generator placement, Spire Array coverage, Guardian Drone patrols
- **Narrative:** Valkyr probes Bastion's defenses from above. Thane must prove the walls protect against all threats — even from the sky.
- **Twist:** Valkyr targets the Command Hub with EMP strikes. Losing it weakens an entire sector.
- **Difficulty:** Easy-Medium

### Mission 3: "Expansion Protocol"
- **Enemy:** Kragmore (medium AI)
- **Map:** Your base is secure, but the nearest Cordite nodes are outside your walls
- **Objective:** Expand to 3 Cordite nodes and defend all 3 expansion bases against Kragmore's ground push
- **Teaches:** The cost and challenge of Bastion expansion, building satellite defense networks, economy management
- **Narrative:** Kragmore's advance threatens Bastion's supply lines. Thane must extend the perimeter — every new node needs new walls.
- **Twist:** Kragmore's Quake Batteries outrange your turrets. You must build Patrol Rovers to scout and Bulwark Mortars to counter-battery.
- **Difficulty:** Medium

### Mission 4: "Siege of the Citadel"
- **Enemy:** Ironmarch (medium) + Kragmore (medium)
- **Map:** Large Bastion fortress under siege from two directions
- **Objective:** Survive a coordinated ground siege. Repair walls faster than they can be destroyed. Hold for 25 minutes.
- **Teaches:** Repair prioritization, rotating defenders between fronts, Denial Fields, the limits of passive defense
- **Narrative:** Ironmarch's FOBs approach from the west while Kragmore's tanks roll from the east. The citadel must hold.
- **Twist:** At minute 15, Ironmarch deploys a Juggernaut that can outrange your turrets. You must sortie your Patrol Rovers to destroy it.
- **Difficulty:** Hard

### Mission 5: "Beyond the Walls"
- **Enemy:** Stormrend (hard) + Arcloft (medium)
- **Map:** Open terrain between your base and a critical allied settlement you must protect
- **Objective:** Build a forward defense line to protect an allied settlement 40 cells from your base. Fight outside your comfort zone.
- **Teaches:** Bastion on offense/forward defense, the vulnerability of operating away from home, Warden escort tactics
- **Narrative:** An allied settlement is under attack. Thane can't just protect his own walls — he must extend protection to others. This is what Bastion was built for.
- **Twist:** Arcloft bombs the supply line between your base and the forward position. You must build redundant supply routes.
- **Difficulty:** Hard

### Mission 6: "The Crumbling Wall"
- **Enemy:** Valkyr (hard) + Stormrend (hard) + Kragmore (hard)
- **Map:** Bastion's capital fortress — massive, well-supplied, but surrounded
- **Objective:** Survive a 3-faction siege. Resources are finite. Every decision matters.
- **Teaches:** Ultimate defensive play, resource management under siege, knowing when to sacrifice a sector
- **Narrative:** The Cordite Wars have turned against Bastion. Three factions converge on the capital. Thane must hold until the ceasefire talks conclude.
- **Twist:** At minute 20, a section of wall collapses due to a Mole Rat tunnel breach. Plugging the gap becomes the crisis.
- **Difficulty:** Very Hard

### Mission 7: "Six Fronts" (Shared Final)
- **Enemy:** All 5 other factions (hard AI)
- **Map:** Cordite Nexus
- **Bastion Objective:** Fortify the Nexus itself. Build walls and turrets around the central deposit and hold it against all comers for 30 minutes.
- **Bastion Narrative:** Thane sees the Nexus not as a prize to seize but as a position to hold. If Bastion controls the Nexus, no one else gets it — and that's enough.
- **Bastion Unique Mechanic:** Start with 2 pre-built Command Hubs and enough resources for a quick wall ring. Thane's presence (hero unit) boosts nearby repair speed by 50%.
- **Difficulty:** Very Hard

---

## Campaign: Arcloft — "Overwatch"

**Theme:** Control and precision. Arcloft's campaign is about maintaining control from above while managing the limitations of a small army with hybrid strengths.

**Missions: 8**

### Mission 1: "Eyes Above"
- **Enemy:** Ironmarch (tutorial-level AI)
- **Map:** Mountainous terrain with an Ironmarch patrol
- **Objective:** Establish 2 Overwatch Zones and use patrol aircraft to detect and destroy an Ironmarch scout column
- **Teaches:** Overwatch Zones, aircraft patrol behavior, AA turret placement, hybrid doctrine basics
- **Narrative:** Sovereign-Commander Lyris deploys her first Overwatch network over contested territory. Ironmarch probes will be met with steel from above.
- **Twist:** None — introduction to Arcloft's unique mechanics
- **Difficulty:** Easy

### Mission 2: "Fortified Airspace"
- **Enemy:** Valkyr (tutorial-level AI)
- **Map:** Open sky with Valkyr sorties against your airfield
- **Objective:** Build AA turrets around your Airstrips, then send Templars to win air-to-air combat
- **Teaches:** Air-to-air combat, the safety net of AA-backed airbases, Templar helicopter strengths
- **Narrative:** Valkyr doesn't recognize Arcloft's right to this airspace. Lyris intends to teach them.
- **Twist:** Valkyr sends Peregrines (their best fighter) — the Templar must use its AA turret support to win
- **Difficulty:** Easy-Medium

### Mission 3: "Ground Truth"
- **Enemy:** Kragmore (medium AI)
- **Map:** Plains with a Kragmore tank column approaching
- **Objective:** Use air strikes to slow and attrit a Kragmore column, then hold with ground defenses
- **Teaches:** Arcloft's ground weakness, using air power to compensate, the hybrid tax
- **Narrative:** Kragmore's armored division advances on an Arcloft outpost. Lyris has no tanks to meet them — only aircraft and turrets.
- **Twist:** If you lose all aircraft, you lose — Arcloft's ground forces alone cannot stop Kragmore
- **Difficulty:** Medium

### Mission 4: "Denied Skies"
- **Enemy:** Bastion (medium) + Stormrend (medium)
- **Map:** Bastion fortress with Stormrend attacking both of you
- **Objective:** Establish air superiority over the battlefield while Bastion's AA complicates your operations. Destroy Stormrend first, then deal with Bastion.
- **Teaches:** Operating in contested airspace, temporary alliances (Bastion's AA helps against Stormrend too), switching targets
- **Narrative:** The enemy of my enemy is still my enemy — but Stormrend is the immediate threat.
- **Twist:** Mid-mission, Bastion breaks the informal truce and targets your aircraft. You must pivot.
- **Difficulty:** Hard

### Mission 5: "The Citadel Above"
- **Enemy:** Ironmarch (hard) + Kragmore (medium)
- **Map:** Arcloft's floating citadel overlooking a ground battle between Ironmarch and Kragmore
- **Objective:** Use your air force to kingmake — weaken whichever faction is winning to prevent either from threatening you, then destroy both
- **Teaches:** Strategic decision-making, air power as a balancing force, Overwatch Zone mastery
- **Narrative:** Two ground armies clash below. Lyris controls the sky. She must ensure neither wins.
- **Twist:** If either faction captures the valley's Cordite, they become strong enough to threaten the citadel
- **Difficulty:** Hard

### Mission 6: "Sky Marshal"
- **Enemy:** Valkyr (hard) + Stormrend (hard)
- **Map:** Contested airspace over a resource-rich island chain
- **Objective:** Win a three-way air war. Establish Overwatch over 5 islands.
- **Teaches:** Full-scale air operations, managing a small army across multiple fronts, Overwatch Zone rotation
- **Narrative:** Three air-capable factions fight for the richest Cordite archipelago. Only one can control it.
- **Twist:** Stormrend builds makeshift AA on captured islands. You must bomb ground targets AND fight air-to-air.
- **Difficulty:** Very Hard

### Mission 7: "The Long Watch"
- **Enemy:** Kragmore (hard) + Ironmarch (hard) + Bastion (medium)
- **Map:** Arcloft's home citadel under siege from all ground factions
- **Objective:** Defend for 25 minutes using only air power and AA. No reinforcements.
- **Teaches:** Ultimate hybrid defense, attrition management with 180 supply cap, every unit matters
- **Narrative:** Every ground faction has united against Arcloft's aerial dominance. The citadel must endure.
- **Twist:** Kragmore's Strix Gunship (their only aircraft) starts hunting your Templars. Air-to-air dogfights over your own base.
- **Difficulty:** Very Hard

### Mission 8: "Six Fronts" (Shared Final)
- **Enemy:** All 5 other factions (hard AI)
- **Map:** Cordite Nexus
- **Arcloft Objective:** Establish 5 Overwatch Zones covering the entire Nexus. Maintain air superiority for 20 minutes while denying all factions access to the central deposit.
- **Arcloft Narrative:** Lyris sees the Nexus as the ultimate Overwatch challenge. Control the sky above it, and the ground becomes irrelevant.
- **Arcloft Unique Mechanic:** Start with Expanded Overwatch upgrade already researched (5 zones, 20-cell radius). Lyris's personal Sky Bastion (hero unit, mobile AA platform) patrols the center.
- **Difficulty:** Very Hard

---

## Campaign: Ironmarch — "The Long Road"

**Theme:** Methodical, inevitable progress. Ironmarch's campaign is about patience, fortification, and the agonizing vulnerability to things that fly.

**Missions: 8**

### Mission 1: "Forward Operating"
- **Enemy:** Stormrend (tutorial-level AI)
- **Map:** Flat terrain with a Stormrend camp 40 cells from your start
- **Objective:** Deploy an FOB truck, build a forward base, then push to destroy the Stormrend camp
- **Teaches:** FOB Truck deployment, forward base construction, Wire Fields, Watchtower placement
- **Narrative:** Commander Hask receives orders: establish a forward position. The Stormrend camp won't clear itself.
- **Twist:** None — satisfying FOB tutorial
- **Difficulty:** Easy

### Mission 2: "Supply Road"
- **Enemy:** Kragmore (tutorial-level AI)
- **Map:** Long road between two outposts with Kragmore ambush positions
- **Objective:** Build FOBs along the road to create a safe supply corridor. Escort harvesters to the far Cordite node.
- **Teaches:** Multiple FOBs, supply line protection, Ironmarch's fortified economy style
- **Narrative:** The main base needs Cordite from the far deposits. The road goes through Kragmore territory. Fortify every meter.
- **Twist:** Kragmore sends a Mole Rat to tunnel under an FOB — introduces the concept of threats from unexpected directions
- **Difficulty:** Easy-Medium

### Mission 3: "No Sky"
- **Enemy:** Valkyr (medium AI)
- **Map:** Open terrain with zero natural cover
- **Objective:** Advance your armor column while under constant Valkyr air attack. Build Reinforced FOBs with flak for AA coverage.
- **Teaches:** Ironmarch's critical weakness (air), the Reinforced FOB upgrade, advancing under air harassment
- **Narrative:** Valkyr controls the sky above the advance route. Hask's nightmare begins. Every step forward costs blood.
- **Twist:** This mission is deliberately painful — the player should FEEL Ironmarch's AA weakness. Victory is surviving, not dominating.
- **Difficulty:** Medium-Hard

### Mission 4: "Trench War"
- **Enemy:** Bastion (medium) + Ironmarch AI ally (weak)
- **Map:** Two Ironmarch armies (player + allied AI) pushing against a Bastion fortress from opposite sides
- **Objective:** Coordinate your push with the allied AI. Build FOBs to create a strangling perimeter around Bastion.
- **Teaches:** Combined arms siege, FOB creep strategy, patience as a weapon
- **Narrative:** Two Ironmarch corps converge on a Bastion stronghold. Hask commands the southern front while an ally takes the north.
- **Twist:** The allied AI is terrible and gets bogged down. You must compensate by pushing harder on your side.
- **Difficulty:** Medium

### Mission 5: "The Gauntlet"
- **Enemy:** Arcloft (hard) + Valkyr (medium)
- **Map:** Canyon map with both air factions controlling different sections
- **Objective:** Push through a canyon defended by Arcloft Overwatch Zones and Valkyr sorties. Build FOBs in each canyon section for AA cover.
- **Teaches:** Advancing against air superiority, FOB leapfrogging, the Bullfrog helicopter as limited AA support
- **Narrative:** The only route to the Nexus passes through enemy-controlled airspace. Hask must build a tunnel of steel through the sky.
- **Twist:** The canyon narrows, making FOB placement critical. One wrong position means overlapping enemy air zones.
- **Difficulty:** Hard

### Mission 6: "Steel Curtain"
- **Enemy:** Stormrend (hard) + Kragmore (hard)
- **Map:** Ironmarch defensive line across a wide front
- **Objective:** Build and maintain an FOB line across 60 cells of frontage. Survive 20 minutes of coordinated assaults.
- **Teaches:** Large-scale FOB defense, Wire Field placement, managing two FOBs simultaneously
- **Narrative:** Stormrend probes for gaps while Kragmore prepares a concentrated push. Hask's line must hold everywhere.
- **Twist:** At minute 12, Kragmore's Mammoth arrives. At minute 16, Stormrend hits Stormbreak. Surviving both peaks is the challenge.
- **Difficulty:** Very Hard

### Mission 7: "One Mile at a Time"
- **Enemy:** Bastion (hard) + Arcloft (hard) + Stormrend (medium)
- **Map:** Extremely long map. You start at one end. The objective is at the other. Three factions block the path.
- **Objective:** Advance your FOB line from one end of the map to the other. Each section has a different enemy. Time limit: 35 minutes.
- **Teaches:** The full Ironmarch fantasy — methodical, unstoppable advance through all opposition
- **Narrative:** Hask commits to the longest march. Every position taken becomes permanent. There is no retreat.
- **Twist:** Arcloft air strikes can destroy FOBs — you must choose between speed (fewer FOBs) and safety (full coverage)
- **Difficulty:** Very Hard

### Mission 8: "Six Fronts" (Shared Final)
- **Enemy:** All 5 other factions (hard AI)
- **Map:** Cordite Nexus
- **Ironmarch Objective:** Build a continuous FOB line from your starting position to the Nexus center. Establish a fortified Refinery on the Nexus. Hold for 20 minutes.
- **Ironmarch Narrative:** Hask sees the Nexus as the final position. Once Ironmarch digs in, no force in the world will uproot them.
- **Ironmarch Unique Mechanic:** Start with 2 FOB Trucks (normally max 2 total) and Reinforced FOB pre-researched. Hask's personal Juggernaut (hero unit, extended range) leads the advance.
- **Difficulty:** Very Hard

---

## Campaign: Stormrend — "Lightning War"

**Theme:** Speed, aggression, and the consequences of a doctrine that only knows how to attack. Stormrend's campaign is fast and brutal — games are short, stakes are high.

**Missions: 8**

### Mission 1: "First Blood"
- **Enemy:** Ironmarch (tutorial-level AI)
- **Map:** Small map with Ironmarch building an FOB
- **Objective:** Destroy the FOB before it finishes deploying. Maintain momentum above 50.
- **Teaches:** Blitz Doctrine, Momentum Gauge, the "always attacking" playstyle, Bolt scout car usage
- **Narrative:** Warlord Kael spots an Ironmarch FOB truck in no-man's-land. Strike now, before it digs in.
- **Twist:** None — pure aggression tutorial. Hitting the FOB before deployment is deeply satisfying.
- **Difficulty:** Easy

### Mission 2: "Raid and Ruin"
- **Enemy:** Bastion (tutorial-level AI)
- **Map:** Bastion's incomplete fortress with gaps in the wall
- **Objective:** Raid Bastion's harvesters and Refineries through wall gaps. Wreck their economy before defenses finish.
- **Teaches:** Economic raiding, harvester hunting, exploiting defensive gaps, Sparkrunner infantry stealth
- **Narrative:** Bastion's fortress isn't finished. Kael sees opportunity — hit them before the walls close.
- **Twist:** A timer shows how long until Bastion completes their walls. If you haven't wrecked the economy by then, you lose.
- **Difficulty:** Easy-Medium

### Mission 3: "Momentum"
- **Enemy:** Kragmore (medium AI)
- **Map:** Open plains — Kragmore's territory
- **Objective:** Build Momentum to 100 and trigger Stormbreak. Use Stormbreak to destroy Kragmore's tank depot before they mass.
- **Teaches:** Momentum management, the power of Stormbreak, combined air+ground assault
- **Narrative:** Kragmore is mustering their armor. Kael must strike before the horde assembles.
- **Twist:** Kragmore's Horde Protocol makes their grouped tanks terrifying. Stormbreak is the equalizer — but it's temporary.
- **Difficulty:** Medium

### Mission 4: "Three Strikes"
- **Enemy:** Valkyr (medium) + Arcloft (medium)
- **Map:** Three enemy installations spread across the map
- **Objective:** Destroy all 3 installations in 15 minutes. You must split your army and attack all three simultaneously.
- **Teaches:** Multi-vector pressure, army splitting, Stormrend's combined arms flexibility
- **Narrative:** Three targets. Fifteen minutes. Kael doesn't have enough forces for all three — but the enemy doesn't know that.
- **Twist:** One of the three is a trap — heavily defended. The player must identify which one and bypass or commit.
- **Difficulty:** Medium-Hard

### Mission 5: "Hit and Fade"
- **Enemy:** Bastion (hard) + Ironmarch (medium)
- **Map:** Two fortified positions with open ground between them
- **Objective:** Defeat both factions without ever engaging their defenses head-on. Use raids, flanking, and economic destruction.
- **Teaches:** Stormrend cannot fight fortifications — must find other win conditions. Raiding as primary strategy.
- **Narrative:** Two fortresses. Zero siege weapons. Kael must be creative or be destroyed.
- **Twist:** Destroying both factions' Reactors (cutting VC) prevents them from building advanced defenses. Surgical > brute force.
- **Difficulty:** Hard

### Mission 6: "The Blitz"
- **Enemy:** Kragmore (hard) + Valkyr (hard)
- **Map:** Kragmore from the north, Valkyr from the sky, Stormrend in the middle
- **Objective:** Use Momentum to play both factions against each other. Maintain 75+ Momentum for 10 minutes to win.
- **Teaches:** Sustained aggression against multiple threats, the art of never stopping
- **Narrative:** Surrounded, outnumbered, outgunned — but never outpaced. This is where Stormrend doctrine is forged.
- **Twist:** If Momentum drops below 25 for more than 30 seconds, you lose. Constant aggression is mandatory.
- **Difficulty:** Very Hard

### Mission 7: "Storm's End"
- **Enemy:** Arcloft (hard) + Bastion (hard) + Ironmarch (hard)
- **Map:** Stormrend's last base, surrounded by three defensive factions
- **Objective:** Break out through ONE of the three defense lines and escape to a safe zone. The other two lines are impenetrable.
- **Teaches:** Reconnaissance, choosing your battles, all-in commitment
- **Narrative:** The defensive factions have cornered Stormrend. Kael must find the weakest point and commit everything to the breakout.
- **Twist:** Each defense line has a hidden weakness — but finding it requires scouting (Sparkrunners), and scouting costs precious time.
- **Difficulty:** Very Hard

### Mission 8: "Six Fronts" (Shared Final)
- **Enemy:** All 5 other factions (hard AI)
- **Map:** Cordite Nexus
- **Stormrend Objective:** Trigger Stormbreak 3 times during the battle. Each Stormbreak damages all enemy factions simultaneously (map-wide lightning storm event). After the 3rd Stormbreak, capture the Nexus.
- **Stormrend Narrative:** Kael doesn't care about holding the Nexus. He wants to break every other faction in a single battle. The Nexus is just the arena.
- **Stormrend Unique Mechanic:** Start with Momentum at 50. Kael's personal Stormbreaker (hero unit, massive AoE damage) is present. Each Stormbreak triggers a unique visual — the "Six Fronts" storm that names the game.
- **Difficulty:** Very Hard

---

## Mission Count Summary

| Faction | Missions | Campaign Name | Final Mission |
|---------|----------|---------------|---------------|
| Valkyr | 8 | Sovereign Skies | Six Fronts |
| Kragmore | 9 | Iron Tide | Six Fronts |
| Bastion | 7 | The Last Wall | Six Fronts |
| Arcloft | 8 | Overwatch | Six Fronts |
| Ironmarch | 8 | The Long Road | Six Fronts |
| Stormrend | 8 | Lightning War | Six Fronts |
| **Total** | **48** | | |

---

## Mission Flow — Faction Matchup Coverage

Each campaign includes all 5 opposing factions at least once. Below tracks which matchup each mission teaches:

### Valkyr (8 missions)
| Mission | Enemy 1 | Enemy 2 | Enemy 3+ |
|---------|---------|---------|----------|
| 1 | Kragmore | | |
| 2 | Ironmarch | | |
| 3 | Stormrend | | |
| 4 | Bastion | | |
| 5 | Kragmore | Arcloft | |
| 6 | Arcloft | Bastion | |
| 7 | Stormrend | Kragmore | |
| 8 | All 5 | | |

### Kragmore (9 missions)
| Mission | Enemy 1 | Enemy 2 | Enemy 3+ |
|---------|---------|---------|----------|
| 1 | Stormrend | | |
| 2 | Valkyr | | |
| 3 | Bastion | | |
| 4 | Ironmarch | | |
| 5 | Arcloft | Stormrend | |
| 6 | Valkyr | Bastion | |
| 7 | Stormrend | Ironmarch | |
| 8 | Arcloft | Bastion | |
| 9 | All 5 | | |

### Bastion (7 missions)
| Mission | Enemy 1 | Enemy 2 | Enemy 3+ |
|---------|---------|---------|----------|
| 1 | Stormrend | | |
| 2 | Valkyr | | |
| 3 | Kragmore | | |
| 4 | Ironmarch | Kragmore | |
| 5 | Stormrend | Arcloft | |
| 6 | Valkyr | Stormrend | Kragmore |
| 7 | All 5 | | |

### Arcloft (8 missions)
| Mission | Enemy 1 | Enemy 2 | Enemy 3+ |
|---------|---------|---------|----------|
| 1 | Ironmarch | | |
| 2 | Valkyr | | |
| 3 | Kragmore | | |
| 4 | Bastion | Stormrend | |
| 5 | Ironmarch | Kragmore | |
| 6 | Valkyr | Stormrend | |
| 7 | Kragmore | Ironmarch | Bastion |
| 8 | All 5 | | |

### Ironmarch (8 missions)
| Mission | Enemy 1 | Enemy 2 | Enemy 3+ |
|---------|---------|---------|----------|
| 1 | Stormrend | | |
| 2 | Kragmore | | |
| 3 | Valkyr | | |
| 4 | Bastion | (ally) | |
| 5 | Arcloft | Valkyr | |
| 6 | Stormrend | Kragmore | |
| 7 | Bastion | Arcloft | Stormrend |
| 8 | All 5 | | |

### Stormrend (8 missions)
| Mission | Enemy 1 | Enemy 2 | Enemy 3+ |
|---------|---------|---------|----------|
| 1 | Ironmarch | | |
| 2 | Bastion | | |
| 3 | Kragmore | | |
| 4 | Valkyr | Arcloft | |
| 5 | Bastion | Ironmarch | |
| 6 | Kragmore | Valkyr | |
| 7 | Arcloft | Bastion | Ironmarch |
| 8 | All 5 | | |

---

## Campaign Narrative Framework

### The Cordite Wars (Background)

The six factions coexisted in uneasy tension for decades, each controlling their own Cordite deposits. The balance shattered when geological surveys revealed the **Cordite Nexus** — a deposit larger than all known reserves combined. Located at the intersection of all six territories, it became the flashpoint for total war.

The war progresses through phases:
1. **Border Skirmishes** (Missions 1-2): Probing attacks, reconnaissance, limited engagements
2. **Escalation** (Missions 3-4): Full mobilization, alliances of convenience, combined operations
3. **Total War** (Missions 5-7/8): No holds barred. Every faction for themselves. Alliances fracture.
4. **The Six Fronts** (Final Mission): All six factions converge on the Nexus in the war's decisive battle.

### Commander Characters

Each faction has a named commander who serves as the player's avatar and narrator:

| Faction | Commander | Rank/Title | Personality |
|---------|-----------|------------|-------------|
| Valkyr | Aelara | Wing Commander | Cool, calculating, views ground warfare as beneath her |
| Kragmore | Gorsk | Marshal | Blunt, patient, believes in overwhelming force |
| Bastion | Thane | Engineer-Commander | Methodical, protective, values every life |
| Arcloft | Lyris | Sovereign-Commander | Strategic, aloof, sees the battlefield from above |
| Ironmarch | Hask | Field Commander | Pragmatic, steady, "one mile at a time" mentality |
| Stormrend | Kael | Warlord | Aggressive, instinctive, "strike first, ask never" |

### Shared Final Battle — Faction-Specific Perspectives

The Six Fronts mission is the same war — but each faction experiences it differently:

| Faction | What they see | What they feel |
|---------|---------------|----------------|
| Valkyr | The chaos below from the sky. Ground armies clashing while they strike from above. | Superiority — this proves the air doctrine works |
| Kragmore | A target-rich environment. Enemies everywhere, Cordite in the center. | Determination — the march ends here, one way or another |
| Bastion | Chaos all around, a defensible position in the center. | Resolve — build the last wall, and hold it |
| Arcloft | The battlefield as a game board. Air zones, ground threats, optimal patrol routes. | Control — manage the chaos from above |
| Ironmarch | A long road to the center, enemies on all sides. | Grit — one position at a time, like always |
| Stormrend | Targets. Momentum. The storm building. | Ecstasy — this is what Stormrend was made for |

---

*End of document. Next steps: mission scripting system, campaign progression save data, cutscene/briefing framework.*
