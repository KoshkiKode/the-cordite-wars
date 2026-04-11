#!/usr/bin/env python3
"""Generate data/balance/units.json and data/balance/buildings.json from raw JSON data."""
import json, pathlib

ROOT = pathlib.Path(__file__).parent.parent

def load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)

def get_primary_damage(d):
    weapons = d.get("Weapons") or d.get("weapons") or []
    if not weapons:
        return 0
    w = weapons[0]
    return w.get("Damage") or w.get("damage") or 0

def get_speed(d):
    mp = d.get("MovementProfile") or d.get("movementProfile") or {}
    return mp.get("Speed") or mp.get("speed") or 0

def collect_units():
    units = []
    for path in sorted((ROOT / "data/units").rglob("*.json")):
        try:
            d = load_json(path)
        except Exception:
            continue
        if not (d.get("Id") or d.get("id")):
            continue
        first_weapon = (d.get("Weapons") or d.get("weapons") or [{}])[0]
        units.append({
            "id":       d.get("Id") or d.get("id") or "",
            "faction":  d.get("FactionId") or d.get("factionId") or "",
            "category": d.get("Category") or d.get("category") or "",
            "hp":       d.get("MaxHealth") or d.get("maxHealth") or 0,
            "cost":     d.get("Cost") or d.get("cost") or 0,
            "damage":   get_primary_damage(d),
            "speed":    get_speed(d),
            "range":    first_weapon.get("Range", 0),
        })
    units.sort(key=lambda u: (u["faction"], u["id"]))
    return units

def collect_buildings():
    buildings = []
    for path in sorted((ROOT / "data/buildings").rglob("*.json")):
        try:
            d = load_json(path)
        except Exception:
            continue
        if not (d.get("Id") or d.get("id")):
            continue
        buildings.append({
            "id":         d.get("Id") or d.get("id") or "",
            "faction":    d.get("FactionId") or d.get("factionId") or "",
            "cost":       d.get("Cost") or d.get("cost") or 0,
            "hp":         d.get("MaxHealth") or d.get("maxHealth") or 0,
            "build_time": d.get("BuildTime") or d.get("buildTime") or 0,
        })
    buildings.sort(key=lambda b: (b["faction"], b["id"]))
    return buildings

def main():
    out_dir = ROOT / "data/balance"
    out_dir.mkdir(exist_ok=True)

    units = collect_units()
    with open(out_dir / "units.json", "w", encoding="utf-8") as f:
        json.dump(units, f, indent=2)
    print(f"Written {len(units)} units to data/balance/units.json")

    bldgs = collect_buildings()
    with open(out_dir / "buildings.json", "w", encoding="utf-8") as f:
        json.dump(bldgs, f, indent=2)
    print(f"Written {len(bldgs)} buildings to data/balance/buildings.json")

if __name__ == "__main__":
    main()
