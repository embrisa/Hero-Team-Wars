"""Refresh the reviewed field catalog from pinned metadata; runtime stays offline.

Only this explicit maintenance command uses the network. The generated catalog
contains selected metadata facts, not game assets or the full upstream tables.
"""
import hashlib
import json
import re
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[1]
COMMIT = "66b637e38df68023dc94c7ed46210a28b52e0b12"
BASE = f"https://raw.githubusercontent.com/inwc3/wc3libs/{COMMIT}/src/test/resources/wc3data/"

# Readable names and explanations are our authoring vocabulary. Storage types,
# applicability and scope metadata below are extracted, never guessed here.
FIELDS = {
    "unit": """
unam unitName Unit or hero name
utip unitTooltip Unit tooltip
ustr startingStrength Starting strength
uagi startingAgility Starting agility
uint startingIntelligence Starting intelligence
uhpm maximumHitPoints Maximum hit points
ugol goldCost Gold cost
ulum lumberCost Lumber cost
usst stockInitialDelay Stock initial delay
usrg stockReplenishInterval Stock replenishment interval
useu soldUnits Building sold units list
usca modelScale Model scale
uabi normalAbilities Normal abilities, including inventory; not learnable hero skills
uhab heroAbilities Learnable hero skills assigned to a hero
umdl unitModel Unit model path
ushu unitShadow Unit shadow image
ufoo foodCost Food cost
ushr shadowOnWater Shadow displayed on water
ucpt castPoint Cast point
ucbs castBackswing Cast backswing
umvs movementSpeed Movement speed
""",
    "ability": """
anam abilityName Ability name
atp1 tooltip Ability tooltip
aub1 extendedTooltip Ability extended tooltip
alev maximumLevels Maximum ability levels
aran castRange Cast range
aare areaOfEffect Area of effect
adur duration Duration
ahdu heroDuration Hero duration
acdn cooldown Cooldown
amcs manaCost Mana cost
atar allowedTargets Allowed targets filter; separate from Channel target type
aher heroAbility Hero ability flag; marks a learnable hero skill
aite itemAbility Item ability flag
aart normalIcon Normal command button icon path
ahky hotkey Cast hotkey
arhk learnHotkey Learn skill hotkey
abpx buttonPositionX Cast button column
abpy buttonPositionY Cast button row
arpx learnButtonPositionX Learn button column
arpy learnButtonPositionY Learn button row
arar learnIcon Learn skill icon path
aret learnTooltip Learn skill tooltip
arut learnExtendedTooltip Learn skill extended tooltip
arlv requiredHeroLevel Required hero level for the first skill rank
alsk heroLevelSkip Hero level interval between skill ranks
areq abilityRequirements Ability technology requirements
Ncl1 channelFollowThroughTime Channel follow-through time
Ncl2 channelTargetType Channel target type enumeration
Ncl3 channelOptions Channel options bit mask
Ncl5 channelDisableOtherAbilities Channel disables other abilities while casting
Ncl6 channelBaseOrder Channel base order string
Slo1 movementSlowFactor Movement slow factor
Slo2 attackSlowFactor Attack slow factor
Htb1 stormBoltDamage Storm Bolt damage
""",
    "item": "unam itemName Item name",
    "buff": "fnam buffName Buff name",
    "upgrade": "gnam upgradeName Upgrade name",
    "doodad": "dnam doodadName Doodad name",
    "destructable": "bnam destructableName Destructable name",
}
FILES = {
    "unit": "Units/UnitMetaData.slk", "item": "Units/UnitMetaData.slk",
    "ability": "Units/AbilityMetaData.slk", "buff": "Units/AbilityBuffMetaData.slk",
    "upgrade": "Units/UpgradeMetaData.slk", "doodad": "Doodads/DoodadMetaData.slk",
    "destructable": "Units/DestructableMetaData.slk",
}
TYPES = {"int": "Int", "real": "Real", "unreal": "Unreal", "bool": "Bool",
         "channelType": "Int", "channelFlags": "Int", "string": "String",
         "icon": "String", "model": "String", "shadowImage": "String",
         "abilityList": "String", "heroAbilityList": "String", "unitList": "String",
         "targetList": "String", "orderString": "String", "char": "String",
         "techList": "String"}


def parse_slk(text):
    cells, x, y = {}, 0, 0
    for line in text.splitlines():
        if not line.startswith("C;"):
            continue
        for part in re.findall(r'(?:[^;"\r\n]|"(?:[^"]|"")*")+', line)[1:]:
            if part.startswith("X"):
                x = int(part[1:])
            elif part.startswith("Y"):
                y = int(part[1:])
            elif part.startswith("K"):
                cells[x, y] = part[1:].strip('"')
    headers = {x: v for (x, y), v in cells.items() if y == 1}
    rows = [{h: cells.get((x, y), "") for x, h in headers.items()}
            for y in sorted({y for x, y in cells}) if y > 1]
    return {row["ID"]: row for row in rows}


def main():
    sources, tables, fields = {}, {}, []
    enum_url = "https://raw.githubusercontent.com/sumneko/w3x2lni/82916514a12b7edb15252d42225cd8cc8ce61cfd/data/enUS-1.27.1/mpq/UI/UnitEditorData.txt"
    enum_data = urlopen(enum_url, timeout=30).read()
    # Read just these sections: the full game INI permits duplicate sections.
    enums = {}
    for section in ("channelType", "channelFlags"):
        block = re.search(r"\[" + section + r"\]([^\[]+)", enum_data.decode("utf-8-sig")).group(1)
        values = []
        for number, key in re.findall(r"^\d+=(\d+),(\w+)", block, re.M):
            source_name = key.split("_")[-1].lower()
            name = {"targimage": "targetingImage", "unique": "uniqueCast", "unitpoint": "unitOrPoint"}.get(source_name, source_name)
            values.append({"name": name,
                           "value": 1 << int(number) if section == "channelFlags" else int(number),
                           "display_key": key})
        enums[section] = values
    sources["channel_enums"] = {"url": enum_url, "sha256": hashlib.sha256(enum_data).hexdigest().upper(),
                                "game_version": "1.27.1", "flags_encoding": "UI bit indices converted to bit masks"}
    for path in sorted(set(FILES.values())):
        data = urlopen(BASE + path, timeout=30).read()
        sources[path] = {"url": BASE + path, "sha256": hashlib.sha256(data).hexdigest().upper()}
        tables[path] = parse_slk(data.decode("utf-8-sig"))
    for category, entries in FIELDS.items():
        for line in entries.strip().splitlines():
            field_id, name, description = line.split(" ", 2)
            row = tables[FILES[category]][field_id]
            fields.append({
                "id": field_id, "name": name, "category": category,
                "description": description, "type": TYPES[row["type"]],
                "metadata_type": row["type"], "metadata_field": row["field"],
                "display_key": row["displayName"],
                "base_rawcodes": list(filter(None, row.get("useSpecific", "").split(","))),
                "scope": "level" if category in ("ability", "upgrade") else "variation" if category == "doodad" else "simple",
                "repeat": int(row.get("repeat") or 0), "data_pointer": int(row.get("data") or 0),
                "metadata_index": int(row.get("index") or 0),
                "source": FILES[category], "evidence": "pinned_metadata_and_static_codec_tests",
                "runtime_verified": False,
                "values": enums.get(row["type"], []),
                "value_kind": "flags" if row["type"] == "channelFlags" else "enum" if row["type"] == "channelType" else "rawcode_list" if row["type"] in ("abilityList", "heroAbilityList", "unitList") else "scalar",
            })
    catalog = {"schema_version": "1.0", "catalog_version": "1.0",
               "upstream_commit": COMMIT, "game_version": "upstream fixture; game patch not declared",
               "sources": sources, "fields": sorted(fields, key=lambda f: (f["category"], f["id"]))}
    destination = ROOT / "map-engine/data/object-fields.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(fields)} reviewed fields to {destination}")


if __name__ == "__main__":
    main()
