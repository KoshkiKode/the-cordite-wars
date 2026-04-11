#!/usr/bin/env python3
"""Update campaign JSONs: add requires_mission for sequential unlock and equalize to 9 missions."""
import json, pathlib, copy

ROOT = pathlib.Path(__file__).parent.parent
CAMP_DIR = ROOT / "data/campaign"

# New mission 8s for the 5 short factions
new_mission_8 = {
    "arcloft": {
        "id": "arcloft_08",
        "number": 8,
        "name": "Operation Whitestorm",
        "briefing": "A Kragmore stronghold on Iron Ridge is the last obstacle before the final assault. Neutralise their command structure to pave the way for the Six Fronts operation.",
        "map": "iron_ridge",
        "player_faction": "arcloft",
        "enemy_factions": ["kragmore"],
        "objectives": ["Destroy the Kragmore Command Center"],
        "starting_cordite": 5500,
        "ai_difficulty": 2,
        "difficulty_label": "Hard",
        "twist": "Kragmore scouts patrol the highland approaches — Vigilants must clear the detection grid before the main thrust.",
        "win_condition": "destroy_hq",
        "requires_mission": "arcloft_07"
    },
    "bastion": {
        "id": "bastion_08",
        "number": 8,
        "name": "Final Perimeter",
        "briefing": "Ironmarch and Stormrend have launched a coordinated assault. Hold the line at Crossroads until reinforcements arrive.",
        "map": "crossroads",
        "player_faction": "bastion",
        "enemy_factions": ["ironmarch", "stormrend"],
        "objectives": ["Survive the joint assault", "Destroy both enemy Command Centers"],
        "starting_cordite": 6000,
        "ai_difficulty": 2,
        "difficulty_label": "Hard",
        "twist": "Ironmarch heavy armour and Stormrend air strikes attack from opposite flanks — hold the centre at all costs.",
        "win_condition": "destroy_hq",
        "requires_mission": "bastion_07"
    },
    "ironmarch": {
        "id": "ironmarch_08",
        "number": 8,
        "name": "Total Advance",
        "briefing": "Valkyr's defensive line at Highland Pass is the last barrier. Shatter it with overwhelming armoured force.",
        "map": "highland_pass",
        "player_faction": "ironmarch",
        "enemy_factions": ["valkyr"],
        "objectives": ["Breach and destroy the Valkyr Command Center"],
        "starting_cordite": 5500,
        "ai_difficulty": 2,
        "difficulty_label": "Hard",
        "twist": "Valkyr Interceptors will harass your armour columns — deploy anti-air support to protect the advance.",
        "win_condition": "destroy_hq",
        "requires_mission": "ironmarch_07"
    },
    "stormrend": {
        "id": "stormrend_08",
        "number": 8,
        "name": "The Final Storm",
        "briefing": "Arcloft's relay network at Crossroads has been feeding intelligence to all factions. Take it down before the Six Fronts alliance forms.",
        "map": "crossroads",
        "player_faction": "stormrend",
        "enemy_factions": ["arcloft"],
        "objectives": ["Destroy the Arcloft Command Center"],
        "starting_cordite": 5500,
        "ai_difficulty": 2,
        "difficulty_label": "Hard",
        "twist": "Arcloft Vigilants will reveal your Scrap Hawks early — strike fast before their detection grid is fully active.",
        "win_condition": "destroy_hq",
        "requires_mission": "stormrend_07"
    },
    "valkyr": {
        "id": "valkyr_08",
        "number": 8,
        "name": "Sovereign's Gambit",
        "briefing": "The Ironmarch capital at Iron Ridge is within striking range. A decisive aerial assault will end their campaign.",
        "map": "iron_ridge",
        "player_faction": "valkyr",
        "enemy_factions": ["ironmarch"],
        "objectives": ["Destroy the Ironmarch Command Center"],
        "starting_cordite": 5500,
        "ai_difficulty": 2,
        "difficulty_label": "Hard",
        "twist": "Ironmarch AA batteries cover the approaches — use Interceptors to suppress defences before the main strike.",
        "win_condition": "destroy_hq",
        "requires_mission": "valkyr_07"
    },
}

for faction in ["arcloft", "bastion", "ironmarch", "kragmore", "stormrend", "valkyr"]:
    path = CAMP_DIR / f"{faction}.json"
    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    missions = data["missions"]

    # Add requires_mission for sequential unlock (mission 2 onward)
    for i, m in enumerate(missions):
        if i == 0:
            m.pop("requires_mission", None)
            continue
        prev_id = missions[i-1]["id"]
        if not m.get("requires_mission"):
            m["requires_mission"] = prev_id

    # For the 5 short factions, insert new mission 8 and renumber "The Six Fronts" to 9
    if faction != "kragmore":
        last = missions[-1]
        if last.get("number") == 8 and last.get("name") == "The Six Fronts":
            # Renumber last to 9
            last["number"] = 9
            # Give it a new ID ending in _09
            old_id = last["id"]  # e.g. arcloft_08
            new_id = old_id[:-2] + "09"
            last["id"] = new_id
            # requires_mission should be new mission 8
            last["requires_mission"] = f"{faction}_08"
            # Insert new mission 8 before last
            nm8 = copy.deepcopy(new_mission_8[faction])
            missions.insert(len(missions) - 1, nm8)

    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    print(f"Updated {faction}.json: {len(data['missions'])} missions")

print("Done.")
