# DB DPS Overlay

A damage meter for the Dungeon Blitz: R launcher. It sits in the grey space to the left and right of the game and shows:

- a timer you start and stop (F6), reset (F7) and hide (F8);
- total damage, damage per second, casts, hits and crit rate, with a DPS line for the last minute;
- **Scaling**: how much of your damage scales with Attack and how much with Expertise, how much came over time (DoTs) and from crits;
- **Spells**: your equipped spells in hotbar order (1, 2, 3, 4, E, Q), each with its rank, share of your damage, casts, and on a click its DPS, hits, crit rate, average and biggest hit, DoT damage and its Stats line. Basic attacks, rune procs and pets are listed under *Other damage*;
- **Export…** to a JSON file (everything, every hit) or a CSV table for spreadsheets, and **Copy summary** for chat.

Hits and casts only count while the timer runs. Turn on *Start on first hit* to start it with your first hit instead.

## Install

1. Close the game and the launcher.
2. Unzip, then run **Install DPS overlay.cmd**. It uses the launcher's own program to install, so nothing else is needed.
3. Start the launcher as usual.

The launcher is changed in one place: `resources\app.asar` gets a `dps` folder, and its `package.json` starts `dps/boot.js`, which sets up the meter and then runs the launcher exactly as before. The original is kept as `app.asar.dps-backup`. **Uninstall DPS overlay.cmd** takes it out again.

A launcher update replaces `app.asar`, which removes the overlay. Run *Install DPS overlay.cmd* again after an update.

If the launcher isn't in `%LOCALAPPDATA%\Programs\Dungeon Blitz R`, drag its folder onto the .cmd file.

## Spells from DB Inventory Scanner

The meter names every hit from the game's own data (it loads the server's `Game.swz`, so "power 993" is Poison Strike rank 10, "1.49x attack, 2x Expertise/s (5s)"). Which spells are on your hotbar comes from the scanner: open the Tome of Power, press **Scan spells** in DB Inventory Scanner, and the overlay picks the newest spell scan for your character up from the scanner's folder by itself.

## How it reads your damage

The game client works out each hit's damage and sends it to the server: packet 0x0A for a hit (target, attacker, damage, power, crit) and 0x79 for a damage-over-time tick. Casts are packet 0x09. The launcher asks Chromium to send only the game socket, `dungeonblitzr.theminesa.studio:8080`, through a relay on `127.0.0.1` (the `--host-resolver-rules` switch). The relay passes every byte on unchanged as it arrives and reads a copy. The game, the web page and the server are not modified.

Your hits are the ones whose attacker is your character (the entity your client reports as the player) or one of its summons; hits on other players and heals aren't counted. The server can add to your damage after it arrives (the Soulthief passive, admin damage scaling), so the meter shows what your client sent, which is also what the floating numbers show.

Set `DUNGEON_BLITZ_DPS=0` in the environment to start the launcher without the meter.

## Export format

`format: "dbb-dps"`, version 1: `character`, `levels`, `timer` (`elapsedMs`, `startedAt`, `stoppedAt`), `totals` (`damage`, `dps`, `casts`, `hits`, `crits`, `critRate`, `critDamage`, `dotDamage`, `summonDamage`), `distribution` (`byStat` attack/expertise/unknown, `byKind` direct/dot, `crits`), `spells[]` (per spell: `casts`, `hits`, `crits`, `damage`, `share`, `dps`, `directDamage`, `dotDamage`, `averageHit`, `biggestHit`, `damageByStat`, `scaling`, `rank`, `hotbarKey`, `powerIds`), `targets[]`, `damagePerSecond[]` (one entry per second of the timer), `hits[]` (`[ms, powerId, damage, crit, kind, target, summon]`), `outsideTimer`, `spellScan`.

## Checks

`node launcher-mod/test/run.js [path/to/app.asar]` decodes packets, runs the relay over real sockets, checks the meter's arithmetic and the export formats, and with an `app.asar` installs, re-installs and uninstalls on a copy of it. `node launcher-mod/test/overlay-preview.js <out-dir> [backdrop.png] [fonts-dir]` renders the panels at several window sizes with Playwright.
