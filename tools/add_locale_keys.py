#!/usr/bin/env python3
"""Add MENU_TUTORIAL and MENU_REPLAYS to all locale files."""
import json, pathlib

ROOT = pathlib.Path(__file__).parent.parent
LOCALE_DIR = ROOT / "data/locale"

translations = {
    "en":    ("TUTORIAL",      "REPLAYS"),
    "ar":    ("درس تعليمي",    "مقاطع الإعادة"),
    "cs":    ("VÝUKA",         "REPLAYE"),
    "da":    ("VEJLEDNING",    "GENSPILNING"),
    "de":    ("ANLEITUNG",     "REPLAYS"),
    "es":    ("TUTORIAL",      "REPETICIONES"),
    "fi":    ("OPASTUS",       "TOISTOT"),
    "fr":    ("DIDACTICIEL",   "REPLAYS"),
    "hu":    ("BEMUTATÓ",      "VISSZAJÁTSZÁSOK"),
    "it":    ("TUTORIAL",      "REPLAY"),
    "ja":    ("チュートリアル", "リプレイ"),
    "ko":    ("튜토리얼",       "리플레이"),
    "nb":    ("OPPLÆRING",     "AVSPILLING"),
    "nl":    ("TUTORIAL",      "HERSPELEN"),
    "pl":    ("SAMOUCZEK",     "POWTÓRKI"),
    "pt_BR": ("TUTORIAL",      "REPLAYS"),
    "ro":    ("TUTORIAL",      "REPLAYS"),
    "ru":    ("ОБУЧЕНИЕ",      "ПОВТОРЫ"),
    "sv":    ("HANDLEDNING",   "REPRISER"),
    "th":    ("บทเรียน",        "รีเพลย์"),
    "tr":    ("ÖĞRETİCİ",      "TEKRARLAR"),
    "uk":    ("НАВЧАННЯ",      "ПОВТОРИ"),
    "vi":    ("HƯỚNG DẪN",     "PHÁT LẠI"),
    "zh_CN": ("教程",           "回放"),
    "zh_TW": ("教學",           "回放"),
}

for code, (tutorial, replays) in translations.items():
    path = LOCALE_DIR / f"{code}.json"
    if not path.exists():
        print(f"WARNING: {path} not found")
        continue
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    if "MENU_TUTORIAL" not in data:
        data["MENU_TUTORIAL"] = tutorial
    if "MENU_REPLAYS" not in data:
        data["MENU_REPLAYS"] = replays
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
    print(f"Updated {code}.json")

print("Done.")
