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

## Links

The scanner's **Open in DPS Calculator** button puts the whole scan in the address: the compact JSON, compressed with raw DEFLATE and encoded as base64url, after `#invz=`. The calculator also accepts plain base64 JSON after `#inv=`. Scans too big for a link are copied to the clipboard instead.
