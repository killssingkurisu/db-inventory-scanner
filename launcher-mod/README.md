# DB DPS Overlay

A damage meter for the Dungeon Blitz: R launcher. It sits in the grey space to the left and right of the game, in three windows:

- **Damage Meter** (top left): a timer you start and stop (F6), reset (F7) and hide (F8); DPS, total damage, casts, hits and crit rate; **DPS over time** for the whole fight (the DPS over the last 5 seconds at each moment, and your running average); **Scaling**: how much of your damage scales with Attack and how much with Expertise, how much came over time (DoTs) and from crits. A direct hit counts for the stat in its spell's Stats line ("1.49x attack"); every damage-over-time tick counts for Expertise, because the game puts your Expertise into each DoT when it lands (poison, bleed, burn, whichever spell applied it);
- **Rotation** (under the Damage Meter): your casts in order, as one line of text that wraps, like `MA2 s2 s3 RA1 s4 s1 MA2`. `s3` is the spell in hotbar slot 3 (s1 to s6 are keys 1, 2, 3, 4, E, Q, from the game's own data); `MA` (melee attack) and `RA` (ranged attack) are basic attacks in a row, with the number of hits they landed. Hover any part for the spell, the time and its damage;
- **Spells** (top right, as wide as the Damage Meter): your hotbar spells by slot (from your spell scan, or the ones you've used when there's no scan), each with its rank, share of your damage, casts, and on a click its DPS, hits, crit rate, average and biggest hit, DoT damage and its Stats line. Basic attacks, rune procs and pets are listed under *Other damage*;
- **Export…** to a JSON file in the GO style (everything: spells, the rotation, every hit) or a CSV table for spreadsheets, and **Copy summary** for chat.

Drag any window by its title to move it, and resize it from its bottom-right corner; it stays where you put it. *Reset layout* (in the Damage Meter's status lines) puts them all back.

Hits and casts only count while the timer runs. Turn on *Start on first hit* to start it with your first hit instead. F8 (or ×) hides the windows and leaves a small *Damage Meter* tab in the top-left corner of the grey area to bring them back.

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

The game client works out each hit's damage and sends it to the server: packet 0x0A for a hit (target, attacker, damage, power, crit) and 0x79 for a damage-over-time tick. Casts are packet 0x09. To read them, the game's connection runs through a relay on `127.0.0.1`:

1. Chromium's `--host-resolver-rules` send the game website's plain-HTTP traffic (`dungeonblitzr.theminesa.studio:80`, and the other servers in the launcher's list) to a small proxy in the launcher, which passes every request on to the real site unchanged.
2. The one exception is `DungeonBlitz.swf`: the copy handed to Flash has two values changed, the login host (`LinkUpdater.const_1264`, now `127.0.0.1`) and the login port (`Connection.LOGINSERVER_PORT`, now the relay's port). Every other byte of the client's code is the same. Flash's own sockets ignore the host rules, which is why the client itself has to be told.
3. The relay forwards the client's bytes to the real login server as they arrive and reads a copy. From the server it forwards whole packets; "enter world" (0x21), which names the server the client reconnects to for the next level, is pointed at another relay for that server, so every connection after login goes through the meter too.
4. Flash asks for a socket policy before connecting; the launcher answers that on `127.0.0.1:843` and on each relay, so the question never reaches the server.
5. Training dummies: the game client works out DoT ticks on the house dummies but, unlike ticks on monsters, never sends them (Buff's tick skips targets whose behaviour is HomeDummy). The SWF the meter serves sends them too (one instruction: the HomeDummy check reads as false), and the relay counts those ticks and keeps them: a tick on a HomeDummy is never passed to the server, so the server sees what an unmodified client sends.

Nothing on the server changes, and the server sees the same computer connecting as before. If the patch can't be applied (a new client the meter doesn't recognise), the original SWF is served and the status line says so. `dps-overlay.log` in the launcher's data folder (`%APPDATA%\dungeon-blitz-r-launcher`) records each step.

Your hits are the ones whose attacker is your character (the entity your client reports as the player) or one of its summons; hits on other players and heals aren't counted. The server can add to your damage after it arrives (the Soulthief passive, admin damage scaling), so the meter shows what your client sent, which is also what the floating numbers show.

Set `DUNGEON_BLITZ_DPS=0` in the environment to start the launcher without the meter.

## Export format

The JSON export is laid out the way GOOD (the Genshin Open Object Description that Genshin Optimizer imports) lays out an inventory: a header, then flat lists of objects that name things by the game's own keys.

`format: "dbb-dps"`, `version: 2`, `source`, `exportedAt`, then:

- `character`: `key`, `name`, `class`, `spellScan` (the scan file the hotbar came from).
- `fight`: `startedAt`, `stoppedAt`, `durationMs`, `duration`, `levels`, `damage`, `dps`, `casts`, `hits`, `crits`, `critRate`, `critDamage`, `dotDamage`, `dotTicks`, `summonDamage`, `outsideTimer`.
- `distribution`: `byStat` (attack, expertise, unknown), `byKind` (direct, dot), `crits`, each with `damage` and `share`.
- `spells[]`: `key` (the ability, e.g. `PoisonStrike`, or the power's base name for basic attacks and procs, e.g. `RapierMelee`), `name`, `rank`, `slotKey` (hotbar key), `equipped`, `casts`, `hits`, `crits`, `critRate`, `damage`, `share`, `dps`, `directDamage`, `dotDamage`, `dotTicks`, `averageHit`, `biggestHit`, `damageByStat`, `scaling`, `powerIds`.
- `rotation.text`: the Rotation window's line, e.g. `"MA2 s2 s3 RA1 s4 s1 MA2"`.
- `rotation.steps[]`: the casts in order as DPS Calculator combo steps: ability keys, and one `"basic"` per basic attack.
- `rotation.casts[]`: the parts of the Rotation line in order, with `index`, `atMs`, `at`, `endMs`, `key` (ability key, or `basic`), `name`, `kind` (`spell`, `melee`, `ranged`, `other`), `label` (as shown: `s1`…`s6`, `MA3`, `RA12`), `slot` (1–6) and `slotKey` (`1`, `2`, `3`, `4`, `E`, `Q`), `casts` (basic attacks in the run), `rank`, `powerId`, `damage`, `directDamage`, `dotDamage`, `dotTicks`, `hits`, `crits`. Each part is credited with its power's hits and DoT ticks until that power is cast again.
- `targets[]`, `damagePerSecond[]` (one entry per second of the timer), `hits[]` (`atMs`, `powerId`, `damage`, `crit`, `kind`, `target`, `summon`), `notes`.

The CSV has the spell table, the fight totals and the rotation, one cast per row.

## Checks

`node launcher-mod/test/run.js [path/to/app.asar]` decodes packets, runs the relays and the web proxy over real sockets (including a login server that sends the client on to a game server), checks the meter's arithmetic and the export formats, and with an `app.asar` installs, re-installs and uninstalls on a copy of it. With `DBDPS_LIVE_SWF=<DungeonBlitz.swf>` it also patches a real client and checks the three changes: login host, login port and the training-dummy check. `node launcher-mod/test/overlay-preview.js <out-dir> [backdrop.png] [fonts-dir]` renders the panels at several window sizes with Playwright.
