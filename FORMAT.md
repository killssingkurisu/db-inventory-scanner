# Scan file format

DB Inventory Scanner saves a scan as UTF-8 JSON. The [DPS Calculator](https://killssingkurisu.github.io/db-dps-calculator/) imports it from a file, from pasted text, or from a link.

```json
{
  "format": "dbb-inventory",
  "version": 1,
  "source": "DB Inventory Scanner 1.0.0",
  "scannedAt": "2026-10-05T18:42:07Z",
  "character": { "name": "ksq", "class": "Rogue" },
  "gear": [
    {
      "name": "Key to the City",
      "gearId": 1019,
      "tier": 1,
      "slot": "mainhand",
      "rarity": "R",
      "focus": "Expertise",
      "runes": ["ProcMassiveTime"],
      "skillRune": "PoisonStrike",
      "magic": "Speed+CraftDrop",
      "level": 28,
      "equipped": true,
      "stats": { "attack": 472, "expertise": 400, "defense": 0 },
      "charms": ["expertise", "expertise", "attack@7"],
      "confidence": 1.0
    }
  ],
  "charms": [
    { "key": "twilightSliver", "name": "Twilight Sliver", "count": 6 },
    { "key": "expertise", "name": "Infinite Sapphire", "count": 5 }
  ],
  "notes": ["Page 2 slot 7: read \"Tak Oggs Heavy Levelr\", saved as \"Tak-Ogg's Heavy Leveler\" (81% sure)"]
}
```

| Field | Meaning |
|---|---|
| `format`, `version` | Always `"dbb-inventory"` and `1` for this layout. |
| `source` | The program and version that made the scan. |
| `scannedAt` | When the scan finished, UTC, ISO 8601. |
| `character.name` | The name in the inventory header. |
| `character.class` | `Rogue`, `Mage` or `Paladin`, taken from the class of the equipped gear. |
| `character.talents` | Optional, not written yet. A talent build string in the talent calculator's format (`3225h3…`, the discipline digit first). The calculator keeps it with the load when it belongs to the scan's class. Reserved for a scanner version that reads the talent tree. |
| `gear[]` | Every gear piece found: the equipped ones and everything in the bag pages. |
| `charms[]` | Charms in the charm bags, one entry per kind, with how many there are. |
| `notes` | Optional. Things the scanner wasn't sure of, for a person to check. |

## Gear

`name`, `gearId`, `tier`, `slot`, `rarity`, `focus`, `runes`, `skillRune`, `magic` and `level` come from the game's item list once the name read from the tooltip has been matched, so they always describe a real item.

| Field | Values |
|---|---|
| `gearId` | The game's GearID. It is unique within a class (the three classes reuse the same numbers). |
| `tier` | `0` Magic, `1` Rare, `2` Legendary. |
| `slot` | `mainhand`, `offhand`, `hat`, `armor`, `gloves`, `boots`. |
| `rarity` | `M` Magic, `R` Rare, `L` Legendary. |
| `focus` | `Attack`, `Expertise`, `Armor`, `Balanced` or `Spread`: which stats the item favours. |
| `runes` | Proc runes, using the calculator's keys (`ProcMassive`, `CritChance`, `ProcFire`, …). Magic and rare items have one, legendary items two. |
| `skillRune` | The skill rune (the game's PowerRune) on rare and legendary items, `""` on magic items. |
| `magic` | The magic rune, such as `Speed+CraftDrop`, or `""`. |
| `level` | The item's level in the game's item list. |
| `equipped` | `true` for the pieces the character is wearing. |
| `stats` | Optional. Attack, Expertise and Defense exactly as the tooltip showed them. The calculator uses these instead of its own tables. Missing when the numbers couldn't be read. |
| `charms` | Only meaningful on equipped gear: the charm keys in its three sockets, `null` for an empty socket. |
| `confidence` | How closely the read name matched the item, from 0 to 1. |

## Charm keys

A charm key is the gem's stat and its rank, the same keys the calculator uses:

- `attack`, `expertise`, `defense`, `hp`, `critChance`, `critPower`, `gearFind`, `goldFind`, `materialFind`: the top rank (10) of that gem, such as *Infinite Sapphire* for `expertise`.
- `attack@7`: rank 7 of the Attack gem (*Radiant Citrine*). Ranks go from 1 to 10.
- `eyeOfDiscovery`, `gleamingShard`, `shimmeringFragment`, `twilightSliver`: the special charms.
- `expertise+defense:R`: a gem with a Magic Forge bonus, here *Infinite Sapphire of Deflecting*. After the `+` comes the second stat, then its tier: `R` adds half of what the same rank of that gem gives, `L` all of it. The game names them with a suffix:

  | Second stat | `R` (half) | `L` (full) |
  |---|---|---|
  | `gearFind` | of Luck | of Fortune |
  | `critChance` | of Skill | of Precision |
  | `goldFind` | of Greed | of Wealth |
  | `materialFind` | of Foraging | of Scouring |
  | `critPower` | of Carnage | of Ruin |
  | `hp` | of Health | of Fortitude |
  | `attack` | of Strength | of Might |
  | `expertise` | of the Mind | of Brilliance |
  | `defense` | of Deflecting | of Protection |

  So *Radiant Citrine of the Mind* is `attack@7+expertise:R` and *Infinite Amethyst of Ruin* is `critChance+critPower:L`.

[data/catalog.json](data/catalog.json) lists every charm with its key and its English and Turkish names.

## Spells

A spell scan is saved as its own file, `"format": "dbb-spells"`, version 1, with `source`, `scannedAt`, `character` (the name typed in the scanner, and the class of the abilities read) and a `spells` object. The same object may also appear in an inventory scan.

```json
{
  "format": "dbb-spells",
  "version": 1,
  "source": "DB Inventory Scanner 1.1.0",
  "scannedAt": "2026-10-06T15:02:11Z",
  "character": { "name": "ksq", "class": "Rogue" },
  "spells": {
    "scannedAt": "2026-10-06T15:02:11Z",
    "readFrom": "tome+hotbar",
    "abilities": [
      {
        "key": "PoisonStrike",
        "name": "Poison Strike",
        "class": "Rogue",
        "baseClass": "Rogue",
        "category": "Assault",
        "hotbarSlot": 1,
        "rank": 10,
        "maxRank": 10,
        "bought": true,
        "equipped": "1",
        "tome": { "page": 1, "pageClass": "Rogue", "tier": 1, "slot": 1 },
        "powerId": 993,
        "powerName": "PoisonStrike10",
        "manaCost": "20",
        "cooldownMs": 0,
        "damageType": "Physical",
        "description": "Deal two venomous strikes that Blind, Cripple, Weaken and apply a deadly poison to your foe [Stats: 1.49x attack, 2x Expertise/s (5s), -10% Speed (5s), -10% Melee Damage (5s)]",
        "scalingText": "1.49x attack, 2x Expertise/s (5s), -10% Speed (5s), -10% Melee Damage (5s)",
        "scaling": [
          { "multiplier": 1.49, "stat": "attack", "perSecond": false, "seconds": 0 },
          { "multiplier": 2, "stat": "expertise", "perSecond": true, "seconds": 5 }
        ],
        "tooltip": { "name": "Poison Strike", "rankLine": "Rank 10 Mana Cost: 20", "description": "Deal two venomous strikes that Blind, Cripple, Weaken and apply a deadly poison to your foe [Stats: 1.49x attack, 2x Expertise/s (5s), -10% Speed (5s), -10%" },
        "confidence": 1.0
      }
    ],
    "hotbar": [
      { "slot": 1, "slotKey": "1", "key": "PoisonStrike", "name": "Poison Strike", "rank": 10 }
    ]
  }
}
```

| Field | Meaning |
|---|---|
| `readFrom` | `tome+hotbar` when the Tome of Power was open, `hotbar` when only the hotbar could be read (then `abilities` holds just the equipped spells). |
| `key` | The game's AbilityName. The power used at rank N is `<key><N>` (`PoisonStrike10`), whose PowerID is `powerId`: the id in the game's combat packets. |
| `class`, `baseClass` | The class or discipline the ability belongs to (`Rogue`, `Executioner`, `ShadowWalker`, …) and its base class. |
| `category`, `hotbarSlot` | From the game's ability list. `hotbarSlot` 1–3 are the tier abilities (keys 1, 2, 3), 4–6 a discipline's master abilities (keys 4, E, Q). |
| `rank`, `bought` | The rank the tooltip showed. `0` and `bought: false` for an ability you haven't trained. |
| `equipped` | The hotbar key the ability is on (`"1"`, `"E"`, …) or `null`. |
| `tome` | Where it is in the Tome of Power: page 1 is your class, 2–4 the disciplines, 5 the master page; tier 1–3 is the row. |
| `description` | The game's whole tooltip text for that rank. The tooltip box only shows three lines of it; what it showed is in `tooltip.description`. |
| `scalingText`, `scaling` | The Stats part of the description, before any "Next rank". A term without `/s` is the hit and scales with `stat`; one with `/s` is damage over time for `seconds`. |
| `hotbar` | The six hotbar keys in order, empty keys left out. |

## Links

The scanner's **Open in DPS Calculator** button puts the whole scan in the address: the compact JSON, compressed with raw DEFLATE and encoded as base64url, after `#invz=`. The calculator also accepts plain base64 JSON after `#inv=`. Scans too big for a link are copied to the clipboard instead.
