#!/usr/bin/env python3
"""Build data/catalog.json, the scanner's list of every gear item, charm and ability in Dungeon Blitz.

Inputs are the game's own data files from the Dungeon Blitz: R client
(src/client/content/localhost/p/..., or the live server's /p/...):
  --login-swz  p/cbp/Login.swz     (GearTypes: every item's name, slot, rarity, stats and runes)
  --game-swz   p/cbq/Game.swz      (CharmTypes with English names; AbilityTypes and PlayerPowerTypes)
  --game-tr    p/cbq/Game.tr.swz   (optional: Turkish charm names)

Only abilities, into an existing catalog (keeps its gear and charms):
  --abilities-swz p/cbq/Game.swz

The .swz files are the client's packed XML (a rolling XOR key, then zlib per chunk).

Output fields use the DPS Calculator's names, so a scan imports without any lookups:
  slot  mainhand/offhand/hat/armor/gloves/boots
  focus Attack/Expertise/Armor/Balanced/Spread  (from the item's StatRune)
  runes proc runes (ProcMassive, CritChance, ...), skillRune (PowerRune), magic (MagicRune)
"""
import argparse
import json
import re
import struct
import zlib
import xml.etree.ElementTree as ET
from collections import OrderedDict

SLOT = {"Sword": "mainhand", "Shield": "offhand", "Hat": "hat", "Armor": "armor", "Gloves": "gloves", "Boots": "boots"}
TIER = {"M": 0, "R": 1, "L": 2}
CHARM_TYPES = {"Trog": "gearFind", "Infernal": "critChance", "Undead": "goldFind", "Mythic": "materialFind",
               "Draconic": "critPower", "Sylvan": "hp", "Melee": "attack", "Magic": "expertise", "Armor": "defense"}
SPECIAL_CHARMS = {"TripleFind": "eyeOfDiscovery", "DoubleFind1": "gleamingShard",
                  "DoubleFind2": "shimmeringFragment", "DoubleFind3": "twilightSliver"}


def rot(k, s):
    return ((k << (32 - s)) | (k >> s)) & 0xFFFFFFFF if s else k


def swz_chunks(path):
    raw = open(path, "rb").read()
    key, count = struct.unpack(">II", raw[:8])
    pos, rk, out = 8, key, []
    for _ in range(count):
        n = struct.unpack(">I", raw[pos:pos + 4])[0]
        pos += 4
        buf = bytearray(n)
        for j in range(n):
            buf[j] = raw[pos + j] ^ (rk & 0xFF)
            rk = rot(rk, j & 7)
        pos += n
        out.append(zlib.decompress(bytes(buf)).decode("utf-8", "replace"))
    return out


def chunk(chunks, root_tag):
    for c in chunks:
        if re.search(r"<%s\b" % root_tag, c):
            return ET.fromstring(c.encode("utf-8"))
    raise SystemExit("no <%s> in archive" % root_tag)


def text(el, tag):
    t = el.find(tag)
    return (t.text or "").strip() if t is not None and t.text else ""


def build(login_swz, game_swz, game_tr):
    gears = chunk(swz_chunks(login_swz), "GearTypes")
    items = []
    for g in gears:
        rarity = text(g, "Rarity")
        cls = text(g, "UsedBy")
        slot = SLOT.get(g.get("Type"))
        if rarity not in TIER or cls not in ("Rogue", "Mage", "Paladin") or not slot:
            continue
        stat = text(g, "StatRune")
        focus = stat[len(cls):] if stat.startswith(cls) else "Balanced"
        runes = [r for r in (text(g, "ProcRune"), text(g, "ProcRune2")) if r]
        items.append(OrderedDict([
            ("n", text(g, "DisplayName")), ("c", cls), ("s", slot), ("id", int(g.get("GearID") or 0)),
            ("t", TIER[rarity]), ("r", rarity), ("f", focus), ("p", runes),
            ("k", text(g, "PowerRune")), ("m", text(g, "MagicRune")), ("l", int(text(g, "Level") or 0)),
        ]))

    def charms_of(path):
        out = OrderedDict()
        for c in chunk(swz_chunks(path), "CharmTypes"):
            out[c.get("CharmName")] = c
        return out

    en = charms_of(game_swz)
    tr = charms_of(game_tr) if game_tr else {}
    charms = []
    for name, c in en.items():
        ptype = text(c, "PrimaryType")
        key = None
        m = re.match(r"^([A-Za-z]+?)(\d\d)$", name)
        if m and m.group(1) in CHARM_TYPES and ptype == m.group(1):
            rank = int(m.group(2))
            key = CHARM_TYPES[ptype] + ("" if rank == 10 else "@%d" % rank)
        elif name in SPECIAL_CHARMS:
            key = SPECIAL_CHARMS[name]
        if not key:
            continue
        names = [text(c, "DisplayName")]
        if name in tr and text(tr[name], "DisplayName") not in names:
            names.append(text(tr[name], "DisplayName"))
        charms.append(OrderedDict([("id", int(text(c, "CharmID") or 0)), ("name", name), ("key", key), ("names", names),
                                   ("icon", text(c, "IconName")), ("desc", text(c, "Description"))]))
    return OrderedDict([("version", 1), ("gear", items), ("charms", charms)])


CLASSES = {"Rogue": "Rogue", "Executioner": "Rogue", "Shadowwalker": "Rogue", "ShadowWalker": "Rogue", "Soulthief": "Rogue",
           "Paladin": "Paladin", "Sentinel": "Paladin", "Justicar": "Paladin", "Templar": "Paladin",
           "Mage": "Mage", "Frostwarden": "Mage", "Flameseer": "Mage", "Necromancer": "Mage"}


def abilities(game_swz):
    """Every class ability with each rank's power: what the Tome of Power and the hotbar show.

    An ability's rank N is the player power named <AbilityName><N> (PoisonStrike10). Its
    Description is the tooltip text, "[Stats: ...]" included, as the live client shows it.
    """
    chunks = swz_chunks(game_swz)
    powers = {}
    for pw in chunk(chunks, "PlayerPowerTypes"):
        powers[pw.get("PowerName")] = pw
    out = []
    current = None
    for ab in chunk(chunks, "AbilityTypes"):
        name = ab.get("AbilityName")
        if name:
            current = None
            cls = text(ab, "Class")
            if name == "Template" or cls not in CLASSES:
                continue
            current = OrderedDict([
                ("k", name), ("n", ""), ("c", cls), ("b", CLASSES[cls]), ("cat", text(ab, "Category")),
                ("h", int(text(ab, "HotbarLocation") or 0)), ("max", int(text(ab, "Rank") or 1)), ("r", []),
            ])
            out.append(current)
        elif current is not None:
            current["max"] = max(current["max"], int(text(ab, "Rank") or 0))
    for a in out:
        for rank in range(1, a["max"] + 1):
            pw = powers.get("%s%d" % (a["k"], rank))
            if pw is None:
                continue
            if not a["n"]:
                a["n"] = text(pw, "DisplayName")
            mana = text(pw, "ManaCost")
            a["r"].append([rank, int(text(pw, "PowerID") or 0), pw.get("PowerName"), mana if mana != "----" else "",
                           int(float(text(pw, "CoolDownTime") or 0)), text(pw, "DamageType").replace("----", ""),
                           text(pw, "Description").replace("----", "")])
        if not a["n"]:
            base = powers.get(a["k"])
            a["n"] = text(base, "DisplayName") if base is not None else a["k"]
    return [a for a in out if a["r"]]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--login-swz", default="")
    ap.add_argument("--game-swz", default="")
    ap.add_argument("--game-tr", default="")
    ap.add_argument("--abilities-swz", default="", help="only refresh the abilities of an existing catalog")
    ap.add_argument("--out", default="data/catalog.json")
    a = ap.parse_args()
    if a.abilities_swz:
        with open(a.out, encoding="utf-8") as fh:
            cat = json.load(fh, object_pairs_hook=OrderedDict)
        cat["abilities"] = abilities(a.abilities_swz)
    elif a.login_swz and a.game_swz:
        cat = build(a.login_swz, a.game_swz, a.game_tr)
        cat["abilities"] = abilities(a.game_swz)
    else:
        ap.error("give --login-swz and --game-swz, or --abilities-swz")
    with open(a.out, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(cat, fh, ensure_ascii=False, separators=(",", ":"))
    print("wrote %s: %d gear, %d charms, %d abilities" % (a.out, len(cat["gear"]), len(cat["charms"]), len(cat.get("abilities", []))))


if __name__ == "__main__":
    main()
