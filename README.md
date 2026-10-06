<img src="docs/icon.png" width="72" align="right" alt="">

# DB Inventory Scanner

A small Windows program that reads every gear piece, charm and spell in Dungeon Blitz and saves them in files the [DPS Calculator](https://killssingkurisu.github.io/db-dps-calculator/) and the [DPS overlay](launcher-mod/README.md) read.


## Download

Get the latest version from [Releases](https://github.com/killssingkurisu/db-inventory-scanner/releases/latest):

- **DbScanner-Setup-x.y.z.exe** installs it for your Windows user (no administrator rights) and adds it to the Start menu.
- **DbScanner-x.y.z-portable.zip** is the same program without an installer. Unzip it anywhere and run `DbScanner.exe`.
- **DB-DPS-Overlay-x.y.z.zip** is the damage meter for the Dungeon Blitz: R launcher. See [launcher-mod/README.md](launcher-mod/README.md).

It needs Windows 10 or 11. Everything else it uses is already part of Windows: .NET Framework 4.8 and the built-in text recognition. The program isn't code-signed, so Windows SmartScreen may say "Windows protected your PC" the first time; choose **More info › Run anyway**.

Text recognition needs English installed as a Windows language. It is on almost every PC; if the scanner says it's missing, add **English (United States)** in *Settings › Time & language › Language & region*.

## Scanning

1. Start the game (the Dungeon Blitz: R launcher or the browser), go in with the character you want, and open your inventory on the **Gear** tab. Make sure no other window covers the game.
2. Open DB Inventory Scanner, pick the game window from the list (it picks the likeliest one for you) and press **Scan inventory**.
3. Keep your hands off the mouse while it works. It hovers over your equipped gear, each page of your bags, the charms socketed in your gear and each page of your charms. Moving the mouse or pressing **Esc** stops the scan and keeps what was read so far.

A full inventory takes a minute or two. If the game runs slowly on your computer, choose the **Careful** speed.

When it's done, the scan is saved as a `.json` file in *Documents\DB Inventory Scanner*, and you can:

- press **Open in DPS Calculator** to open the calculator with the scan loaded,
- press **Copy scan**, then **Paste scan** in the calculator's *Scanned gear* panel, or
- drop the saved file onto the *Scanned gear* panel (or use **Import scan file**).

## Scanning spells

1. In the game, open the **Tome of Power**.
2. In the scanner, check the name under *Character* (the Tome doesn't show it; the scanner fills in the last name it read) and press **Scan spells**.
3. Hands off the mouse. It turns every page of the Tome and hovers every ability, closes the Tome, then hovers the six hotbar keys (1, 2, 3, 4, E, Q).

It reads each ability's name, rank, mana cost and the description the tooltip shows, matches the name against the game's ability list, and saves `DB spells <name> <date>.json` with every spell you own, its rank, the whole description for that rank (the tooltip cuts it short), its scaling ("1.49x attack, 2x Expertise/s (5s)"), its power id, and which hotbar key it's on. With the Tome closed it reads just the hotbar. The DPS overlay picks the newest file up by itself.

In the calculator, the *Scanned gear* panel lists every piece by slot. **Use equipped gear and charms** puts what you're wearing into the calculator, with the stats the game showed, and **Equip** tries any other piece. Sign in with Google at the top of the calculator to keep your scans and saved builds in your Google Drive and get them on any computer.

## What it reads

| | |
|---|---|
| Gear | name, rarity and the Attack / Expertise / Defense values shown in the tooltip, plus whether you're wearing it. Slot, focus, runes, skill rune, magic and level come from the game's item list once the name is matched. |
| Charms | every charm in your charm bags with how many you have, and the three charms socketed in each piece of equipped gear. |
| Character | the name in the inventory header and the class (from the class of your gear). |
| Spells | every ability in the Tome of Power with its rank (trained or not), mana cost and full description for that rank, and the six hotbar keys. |

Lockboxes, chests and other items that aren't gear are skipped. Anything it isn't sure of is listed in the log and in the file's `notes`.

The file format is described in [FORMAT.md](FORMAT.md).

## Troubleshooting

- **"Couldn't find the inventory"**: open the inventory on the Gear tab, keep the whole game window on screen and try again. The game can be any size, but text is read better when the window is large (1280 × 800 or bigger).
- **Names or numbers read wrong**: tick **Save screenshots for troubleshooting** and scan again. The scanner saves every picture it looked at and every piece of text it read in a `debug-…` folder next to your scans; open an issue with that folder zipped.
- **The mouse goes to the wrong place**: Windows display scaling is handled (the game lays itself out in scaled pixels, so at 150% a 2560 × 1440 screen is laid out as 1707 × 960), but if the game is on a second monitor with a different scaling, move it to the main monitor.
- **"Couldn't find your hotbar or the Tome of Power"**: open the Tome, or close every game window so the hotbar at the bottom shows, and try again.

The scanner only looks at the game window and only moves the mouse inside it. Nothing is sent anywhere: the scan stays on your computer until you import it into the calculator yourself.

## How it works

- **Where things are.** The game lays out its 1152 × 768 interface with a scale and offset that depend on the window size (in scaled pixels on a scaled display). The scanner works them out the same way the game does, so every slot and tooltip line is found by position, then checks it by looking for the dark tooltip box where it should be. Tome of Power and hotbar positions come from the client's a_ScreenTome and a_Hud symbols.
- **Reading.** Each tooltip line is cut out, enlarged and turned into dark text on white for Windows' text recognition (`Windows.Media.Ocr`). Rarity comes from the colour of the name. Stack counts on charms are read by matching the digits against the game's own font.
- **Matching.** Every item in Dungeon Blitz is one of 3,546 predefined gear pieces or 94 charms, and every spell one of 117 class abilities. The read name is matched against that list ([data/catalog.json](data/catalog.json), built from the game's data files by `tools/build_catalog.py`), allowing for typical OCR mistakes and for names cut off at the edge of the tooltip.

## Building

On Windows, with the .NET SDK (any version from 6 on):

```
dotnet build src/DbScanner/DbScanner.csproj -c Release -o out/app
iscc /DAppVersion=1.0.0 installer\DbScanner.iss      (Inno Setup 6, for the installer)
```

The checks run anywhere with Mono, libgdiplus and Tesseract standing in for Windows' text recognition (`sh tests/run.sh`), and `sh tools/check-build.sh` type-checks the whole app. On Windows, `tests/WinOcrCheck` runs the same checks with the real Windows text recognition.

Every push is built by GitHub Actions. Pushing a tag such as `v1.0.1` builds the installer and portable zip and publishes them as a release.

To refresh the item list after a game update:

```
python3 tools/build_catalog.py --login-swz <client>/p/cbp/Login.swz --game-swz <client>/p/cbq/Game.swz --game-tr <client>/p/cbq/Game.tr.swz
```

or just the abilities, keeping the items: `python3 tools/build_catalog.py --abilities-swz <client>/p/cbq/Game.swz`.

## License

MIT, see [LICENSE](LICENSE). Dungeon Blitz and its item names belong to their owners; this is a fan-made tool.
