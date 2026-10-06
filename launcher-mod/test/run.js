'use strict';

/**
 * Checks for the DPS overlay that run under plain Node (12+): packet decoding, the relay
 * end to end over real sockets, the meter's arithmetic, the export formats, the power table,
 * and the asar installer against a copy of a launcher archive.
 *
 *   node launcher-mod/test/run.js [path/to/app.asar]
 */

const assert = require('assert');
const fs = require('fs');
const net = require('net');
const os = require('os');
const path = require('path');

const P = require('../src/dps/protocol');
const { GameRelay, CombatTracker } = require('../src/dps/relay');
const { DpsMeter } = require('../src/dps/meter');
const { PowerTable, parseScaling } = require('../src/dps/powers');
const exporter = require('../src/dps/exporter');
const { readScan } = require('../src/dps/spellScans');

let passed = 0;
const failures = [];
async function check(name, fn) {
    try {
        await fn();
        passed += 1;
        console.log('  ok   ' + name);
    } catch (err) {
        failures.push(name);
        console.log('  FAIL ' + name + '\n       ' + ((err && err.stack) || err).split('\n').slice(0, 4).join('\n       '));
    }
}

/* ---------- a writer that encodes like the client's Packet class ---------- */

class BitWriter {
    constructor() {
        this.bits = [];
    }
    raw(value, n) {
        for (let i = n - 1; i >= 0; i--) this.bits.push(Math.floor(value / Math.pow(2, i)) % 2);
        return this;
    }
    bool(b) {
        return this.raw(b ? 1 : 0, 1);
    }
    uint(v) {
        const need = v > 0 ? Math.floor(Math.log2(v)) + 1 : 1;
        const use = Math.max(2, (need + 1) & ~1);
        this.raw(use / 2 - 1, 4);
        return this.raw(v, use);
    }
    sint(v) {
        this.bool(v < 0);
        return this.uint(Math.abs(v));
    }
    sint3(v) {
        this.bool(v < 0);
        const a = Math.abs(v);
        const need = a > 0 ? Math.floor(Math.log2(a)) + 1 : 1;
        const use = Math.max(2, (need + 1) & ~1);
        this.raw(use / 2 - 1, 3);
        return this.raw(a, use);
    }
    str(s) {
        const b = Buffer.from(s, 'utf8');
        this.raw(b.length, 16);
        for (const x of b) this.raw(x, 8);
        return this;
    }
    buffer() {
        const bits = this.bits.slice();
        while (bits.length % 8) bits.push(0);
        const out = Buffer.alloc(bits.length / 8);
        for (let i = 0; i < bits.length; i++) if (bits[i]) out[i >> 3] |= 0x80 >> (i & 7);
        return out;
    }
}

function frame(id, payload) {
    const h = Buffer.alloc(4);
    h.writeUInt16BE(id, 0);
    h.writeUInt16BE(payload.length, 2);
    return Buffer.concat([h, payload]);
}

const pkt = {
    fullUpdate: (id, name, { isPlayer = false, team = 1, summoner = 0, power = 0 } = {}) => {
        const w = new BitWriter().uint(id).sint(1200).sint(-40).sint(0).str(name).raw(team, 2).bool(isPlayer).sint3(0).bool(false);
        w.bool(Boolean(summoner));
        if (summoner) w.uint(summoner);
        w.bool(Boolean(power));
        if (power) w.uint(power);
        return frame(0x08, w.raw(0, 2).bool(true).bool(false).bool(false).bool(false).bool(false).buffer());
    },
    cast: (source, power) => frame(0x09, new BitWriter().uint(source).uint(power).bool(true).bool(false).bool(false).bool(false).bool(false).bool(false).buffer()),
    hit: (target, source, damage, power, crit) =>
        frame(0x0a, new BitWriter().uint(target).uint(source).sint(damage).uint(power).bool(false).bool(false).bool(crit).buffer()),
    dot: (target, source, power, amount) => frame(0x79, new BitWriter().uint(target).uint(source).uint(power).sint(amount).raw(0, 5).buffer()),
    spawn: (id, name, isPlayer, team) => {
        const w = new BitWriter().uint(id).str(name).raw(isPlayer ? 1 : 0, 1);
        if (isPlayer) w.str('Rogue');
        else w.sint(500).sint(20).sint(0).raw(team, 2);
        return frame(0x0f, w.buffer());
    },
    enterWorld: (host, port, level) =>
        frame(0x21, new BitWriter().uint(4321).uint(0).str('').bool(false).str(host).uint(port).str('LevelsHome.swf/a_Level_Home').raw(50, 6).raw(50, 6)
            .str(level).str('').str('').bool(false).bool(false).bool(false).buffer())
};

/* ---------- a small power table ---------- */

const POWER_DATA = {
    powers: [
        [993, 'PoisonStrike10', 'PoisonStrike', 'Poison Strike', 'Physical', '20', 0, 'Deal two venomous strikes [Stats: 1.49x attack, 2x Expertise/s (5s), -10% Speed (5s)]', 0],
        [984, 'PoisonStrike1', 'PoisonStrike', 'Poison Strike', 'Physical', '20', 0, 'x [Stats: 1x attack, 2x attack/s (5s) | Next rank: 1.1x attack, 2x attack/s (5s)]', 0],
        [500, 'FireBolt5', 'FireBolt', 'Fire Bolt', 'Fire', '25', 0, 'Hurl fire', 0],
        [3, 'SwordMelee', '', 'Sword Melee', 'Physical', '0,5', 0, '', 0],
        [4000, 'SkeletonSlash', '', '', 'Physical', '0', 0, '', 1]
    ],
    abilities: { PoisonStrike: ['Rogue', 'Assault', 1, 10] }
};

async function main() {
    const asarArg = process.argv[2] || '';
    console.log('Protocol');

    await check('uint/sint round trip, 0 to 2^30', () => {
        for (const v of [0, 1, 2, 3, 4, 255, 256, 65535, 123456, 2 ** 30 - 1]) {
            const r = new P.BitReader(new BitWriter().uint(v).sint(-v).sint(v).buffer());
            assert.strictEqual(r.uint(), v);
            assert.strictEqual(r.sint(), -v || 0);
            assert.strictEqual(r.sint(), v);
        }
    });

    const serverBitBuffer = process.env.DBDPS_SERVER_BITBUFFER;
    if (serverBitBuffer && fs.existsSync(serverBitBuffer)) {
        await check("hit and DoT payloads match the server's own encoder byte for byte", () => {
            const { BitBuffer } = require(serverBitBuffer);
            const bb = new BitBuffer(false);
            bb.writeMethod4(77); bb.writeMethod4(12); bb.writeMethod24(98765); bb.writeMethod4(993);
            bb.writeMethod15(false); bb.writeMethod15(false); bb.writeMethod15(true);
            assert.ok(bb.toBuffer().equals(pkt.hit(77, 12, 98765, 993, true).subarray(4)));
            const d = new BitBuffer(false);
            d.writeMethod4(77); d.writeMethod4(12); d.writeMethod4(993); d.writeMethod45(-4321); d.writeMethod20 ? d.writeMethod20(5, 0) : d.writeMethod11(0, 5);
            assert.ok(d.toBuffer().equals(pkt.dot(77, 12, 993, -4321).subarray(4)));
        });
    }

    await check('0x0A hit decodes target, source, damage, power, crit', () => {
        const h = P.parsePowerHit(pkt.hit(301, 12, 48211, 993, true).subarray(4));
        assert.deepStrictEqual(h, { targetId: 301, sourceId: 12, damage: 48211, powerId: 993, animOverrideId: 0, effectOverrideId: 0, isCrit: true });
    });

    await check('0x08 own body and summon', () => {
        const a = P.parseEntityFullUpdate(pkt.fullUpdate(12, 'ksq', { isPlayer: true }).subarray(4));
        assert.strictEqual(a.isPlayer, true);
        assert.strictEqual(a.name, 'ksq');
        const b = P.parseEntityFullUpdate(pkt.fullUpdate(40, 'NecroSkeleton', { team: 1, summoner: 12, power: 4000 }).subarray(4));
        assert.strictEqual(b.summonerId, 12);
        assert.strictEqual(b.powerId, 4000);
    });

    await check('0x0F, 0x21 and 0x79', () => {
        const e = P.parseNewlyRelevantEntity(pkt.spawn(301, 'GoblinBrute', false, 2).subarray(4));
        assert.deepStrictEqual([e.id, e.name, e.isPlayer, e.team], [301, 'GoblinBrute', false, 2]);
        const w = P.parseEnterWorld(pkt.enterWorld('dungeonblitzr.theminesa.studio', 8080, 'CraftTown').subarray(4));
        assert.deepStrictEqual([w.host, w.port, w.level, w.mapLevel], ['dungeonblitzr.theminesa.studio', 8080, 'CraftTown', 50]);
        const d = P.parseBuffTickDot(pkt.dot(301, 12, 993, 1500).subarray(4));
        assert.deepStrictEqual([d.targetId, d.sourceId, d.powerId, d.amount], [301, 12, 993, 1500]);
    });

    await check('splitter: any chunking, policy exchange skipped, bad packet ignored', () => {
        const stream = Buffer.concat([
            Buffer.from('<policy-file-request/>\0'),
            pkt.hit(1, 2, 3, 4, false),
            frame(0x0a, Buffer.from([0xff])), // truncated payload: unreadable, must not stop the rest
            pkt.cast(2, 993),
            pkt.dot(5, 2, 993, 77)
        ]);
        for (let size = 1; size <= stream.length; size += 3) {
            const got = [];
            const s = new P.PacketSplitter((id) => got.push(id));
            for (let i = 0; i < stream.length; i += size) s.push(stream.subarray(i, i + size));
            assert.deepStrictEqual(got, [0x0a, 0x0a, 0x09, 0x79]);
        }
    });

    console.log('Powers');
    const table = new PowerTable(POWER_DATA);
    await check('names, ranks and scaling from the Stats line', () => {
        const p = table.get(993);
        assert.deepStrictEqual([p.group, p.rank, p.label], ['PoisonStrike', 10, 'Poison Strike']);
        assert.strictEqual(table.statFor(993, 'hit'), 'attack');
        assert.strictEqual(table.statFor(993, 'dot'), 'expertise');
        assert.strictEqual(table.statFor(984, 'dot'), 'attack', 'only the current rank counts, not "Next rank"');
        assert.strictEqual(table.statFor(500, 'hit'), 'expertise', 'elemental without a Stats line');
        assert.strictEqual(table.statFor(3, 'hit'), 'attack');
        assert.strictEqual(table.statFor(999999, 'hit'), 'unknown');
        assert.strictEqual(parseScaling('[Stats: 3x heal]').heal, true);
    });

    await check('bundled snapshot of the live game data', () => {
        const data = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'src', 'dps', 'powers-snapshot.json'), 'utf8'));
        const t = new PowerTable(data);
        assert.ok(t.size > 2000);
        const p = t.get(993);
        assert.strictEqual(p.label, 'Poison Strike');
        assert.strictEqual(p.rank, 10);
        assert.strictEqual(p.scaling.hit.mult, 1.49);
        assert.strictEqual(p.scaling.dot.stat, 'expertise');
    });

    console.log('Tracker');
    await check('only the own body and its summons count; friendly targets and heals ignored', () => {
        const t = new CombatTracker('t');
        const dmg = [];
        const casts = [];
        t.on('damage', (e) => dmg.push(e));
        t.on('cast', (e) => casts.push(e));
        const feed = (buf, dir) => (dir === 'down' ? t.fromServer(buf[0] * 256 + buf[1], buf.subarray(4)) : t.fromClient(buf[0] * 256 + buf[1], buf.subarray(4)));
        feed(pkt.fullUpdate(12, 'ksq', { isPlayer: true }));
        feed(pkt.fullUpdate(40, 'NecroSkeleton', { summoner: 12, power: 4000 }));
        feed(pkt.spawn(301, 'GoblinBrute', false, 2), 'down');
        feed(pkt.spawn(77, 'MediaTek', true, 1), 'down');
        feed(pkt.cast(12, 993));
        feed(pkt.cast(301, 5)); // a mob this client owns casting
        feed(pkt.hit(301, 12, 1000, 993, false));
        feed(pkt.hit(301, 40, 300, 4000, false)); // summon
        feed(pkt.hit(12, 301, 999, 5, false)); // mob hits us
        feed(pkt.hit(77, 12, 50, 993, false)); // party member
        feed(pkt.hit(12, 12, -400, 993, false)); // heal on self
        feed(pkt.dot(301, 12, 993, 200));
        feed(pkt.dot(301, 12, 993, 200), 'down'); // server echo of the same tick: skipped
        feed(pkt.dot(302, 12, 993, 150), 'down'); // tick run by another client: counted
        assert.strictEqual(casts.length, 1);
        assert.deepStrictEqual(dmg.map((d) => [d.kind, d.damage, d.powerId, d.targetName]), [
            ['hit', 1000, 993, 'GoblinBrute'],
            ['hit', 300, 4000, 'GoblinBrute'],
            ['dot', 200, 993, 'GoblinBrute'],
            ['dot', 150, 993, '']
        ]);
        assert.strictEqual(dmg[1].summon.name, 'NecroSkeleton');
    });

    console.log('Meter');
    await check('timer, totals, DPS, per spell, scaling split, crits, casts', () => {
        let now = 1000000;
        const m = new DpsMeter({ powers: table, now: () => now });
        m.recordDamage({ kind: 'hit', powerId: 993, damage: 500 });
        assert.strictEqual(m.snapshot().ignored.hits, 1, 'not counted before Start');
        m.start();
        m.recordCast({ powerId: 993 });
        m.recordDamage({ kind: 'hit', powerId: 993, damage: 1000, crit: true, targetName: 'Goblin' });
        now += 2000;
        m.recordDamage({ kind: 'dot', powerId: 993, damage: 400, targetName: 'Goblin' });
        m.recordCast({ powerId: 3 });
        m.recordDamage({ kind: 'hit', powerId: 3, damage: 600 });
        now += 2000;
        m.stop();
        now += 5000; // stopped: time doesn't count
        m.recordDamage({ kind: 'hit', powerId: 3, damage: 999 });
        const s = m.snapshot();
        assert.strictEqual(s.elapsedMs, 4000);
        assert.strictEqual(s.totals.damage, 2000);
        assert.strictEqual(s.dps, 500);
        assert.strictEqual(s.totals.casts, 2);
        assert.strictEqual(s.totals.hits, 2);
        assert.strictEqual(s.totals.crits, 1);
        assert.deepStrictEqual(s.byStat, { attack: 1600, expertise: 400, unknown: 0 });
        const ps = s.others.find((r) => r.key === 'PoisonStrike');
        assert.deepStrictEqual([ps.damage, ps.casts, ps.hits, ps.dotDamage, ps.rank, ps.critRate, ps.share], [1400, 1, 1, 400, 10, 1, 0.7]);
        assert.strictEqual(s.ignored.hits, 2);
        m.start(); // resume
        now += 1000;
        assert.strictEqual(m.snapshot().elapsedMs, 5000);
        m.reset();
        assert.strictEqual(m.snapshot().totals.damage, 0);
        assert.strictEqual(m.state, 'idle');
    });

    await check('auto-start on first hit; first second never divides by less than 1 s', () => {
        let now = 0;
        const m = new DpsMeter({ powers: table, now: () => now });
        m.autoStart = true;
        m.recordCast({ powerId: 993 });
        assert.strictEqual(m.state, 'idle');
        m.recordDamage({ kind: 'hit', powerId: 993, damage: 5000 });
        now += 100;
        const s = m.snapshot();
        assert.strictEqual(s.state, 'running');
        assert.strictEqual(s.dps, 5000);
    });

    await check('equipped spells from a scan come first, in hotbar order', () => {
        const m = new DpsMeter({ powers: table, now: () => 0 });
        m.setSpellScan({ abilities: [{ key: 'PoisonStrike', rank: 10, scalingText: '1.49x attack, 2x Expertise/s (5s)' }], hotbar: [{ key: 'PoisonStrike', slotKey: '1', name: 'Poison Strike', rank: 10 }, { key: 'Evade', slotKey: 'E', name: 'Evade', rank: 3 }] });
        const s = m.snapshot();
        assert.deepStrictEqual(s.equipped.map((r) => [r.key, r.hotkey, r.rank]), [['PoisonStrike', '1', 10], ['Evade', 'E', 3]]);
        assert.strictEqual(s.equipped[0].scaling, '1.49x attack, 2x Expertise/s (5s)');
    });

    console.log('Export');
    await check('JSON, CSV and summary agree with the meter', () => {
        let now = 0;
        const m = new DpsMeter({ powers: table, now: () => now });
        m.start();
        m.recordCast({ powerId: 993 });
        m.recordCast({ powerId: 993 });
        m.recordDamage({ kind: 'hit', powerId: 993, damage: 3000, crit: true, targetName: 'Goblin' });
        m.recordDamage({ kind: 'dot', powerId: 993, damage: 1000, targetName: 'Goblin' });
        now = 10000;
        m.stop();
        const meta = { source: 'test', character: 'ksq', className: 'Rogue' };
        const j = exporter.toJson(m.report(), meta);
        assert.strictEqual(j.format, 'dbb-dps');
        assert.strictEqual(j.totals.damage, 4000);
        assert.strictEqual(j.totals.dps, 400);
        assert.strictEqual(j.totals.casts, 2);
        assert.strictEqual(j.spells[0].casts, 2);
        assert.strictEqual(j.distribution.byStat.attack.share, 0.75);
        assert.strictEqual(j.distribution.byKind.dot.damage, 1000);
        assert.strictEqual(j.hits.length, 2);
        assert.strictEqual(j.timer.elapsed, '0:10.0');
        const csv = exporter.toCsv(m.report(), meta).split('\r\n');
        assert.ok(csv[0].startsWith('Spell,Rank,Hotbar key,Casts'));
        assert.ok(csv[1].startsWith('Poison Strike,10,,2,1,1,100,4000,100,400'), csv[1]);
        const sum = exporter.toSummary(m.report(), meta);
        assert.ok(sum.includes('400 DPS, 4,000 damage, 2 casts, 1 hits (100% crit)'), sum);
        assert.ok(sum.includes('1. Poison Strike r10: 4,000 (100%), 2 casts'), sum);
    });

    console.log('Spell scans');
    await check('reads the scanner file format', () => {
        const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dbdps-'));
        const file = path.join(dir, 'DB spells ksq.json');
        fs.writeFileSync(file, '﻿' + JSON.stringify({ format: 'dbb-spells', version: 1, character: { name: 'ksq', class: 'Rogue' }, spells: { scannedAt: '2026-10-06T15:00:00Z', abilities: [{ key: 'PoisonStrike', rank: 10 }], hotbar: [{ key: 'PoisonStrike', slotKey: '1' }] } }));
        const s = readScan(file);
        assert.strictEqual(s.character, 'ksq');
        assert.strictEqual(s.hotbar[0].slotKey, '1');
        fs.writeFileSync(file, JSON.stringify({ format: 'dbb-inventory', version: 1, gear: [] }));
        assert.strictEqual(readScan(file), null, 'a gear-only scan has no spells');
    });

    console.log('Relay');
    await check('bytes pass through unchanged both ways; hits reach the meter', async () => {
        const toServer = [];
        const fromServerScript = Buffer.concat([pkt.spawn(301, 'GoblinBrute', false, 2), pkt.enterWorld('dungeonblitzr.theminesa.studio', 8080, 'GoblinRiver')]);
        const upstream = net.createServer((sock) => {
            sock.on('data', (d) => toServer.push(d));
            // Answer in awkward pieces.
            let i = 0;
            const tick = setInterval(() => {
                if (i >= fromServerScript.length) {
                    clearInterval(tick);
                    return;
                }
                sock.write(fromServerScript.subarray(i, i + 7));
                i += 7;
            }, 2);
        });
        await new Promise((r) => upstream.listen(0, '127.0.0.1', r));
        const relay = new GameRelay({ listenPort: 0, upstreamHost: '127.0.0.1', upstreamPort: upstream.address().port });
        assert.ok(await relay.listen());
        const port = relay.server.address().port;
        const m = new DpsMeter({ powers: table });
        m.start();
        let level = '';
        relay.on('connection', (tr) => {
            tr.on('damage', (e) => m.recordDamage(e));
            tr.on('cast', (e) => m.recordCast(e));
            tr.on('enterWorld', (w) => (level = w.level));
        });
        const client = net.connect(port, '127.0.0.1');
        const received = [];
        client.on('data', (d) => received.push(d));
        await new Promise((r) => client.on('connect', r));
        const clientScript = Buffer.concat([
            pkt.fullUpdate(12, 'ksq', { isPlayer: true }),
            pkt.cast(12, 993),
            pkt.hit(301, 12, 1234, 993, true),
            pkt.dot(301, 12, 993, 66)
        ]);
        for (let i = 0; i < clientScript.length; i += 5) {
            client.write(clientScript.subarray(i, i + 5));
            await new Promise((r) => setTimeout(r, 1));
        }
        await new Promise((r) => setTimeout(r, 300));
        assert.ok(Buffer.concat(toServer).equals(clientScript), 'server got exactly what the client sent');
        assert.ok(Buffer.concat(received).equals(fromServerScript), 'client got exactly what the server sent');
        const s = m.snapshot();
        assert.strictEqual(s.totals.damage, 1300);
        assert.strictEqual(s.totals.casts, 1);
        assert.strictEqual(level, 'GoblinRiver');
        client.destroy();
        await new Promise((r) => setTimeout(r, 50));
        assert.strictEqual(relay.open.size, 0, 'closing the client closes the pair');
        relay.close();
        upstream.close();
    });

    await check('a refused upstream closes the client instead of hanging', async () => {
        const dead = net.createServer();
        await new Promise((r) => dead.listen(0, '127.0.0.1', r));
        const deadPort = dead.address().port;
        await new Promise((r) => dead.close(r));
        const relay = new GameRelay({ listenPort: 0, upstreamHost: '127.0.0.1', upstreamPort: deadPort });
        await relay.listen();
        const client = net.connect(relay.server.address().port, '127.0.0.1');
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error('client was left open')), 2000);
            client.on('close', () => {
                clearTimeout(timer);
                resolve();
            });
            client.on('error', () => {});
        });
        relay.close();
    });

    console.log('Installer');
    if (asarArg && fs.existsSync(asarArg)) {
        await check('install, re-install and uninstall on a copy of the launcher archive', () => {
            const asar = require('../install/asar');
            const { execFileSync } = require('child_process');
            const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'dbdps-launcher-'));
            fs.mkdirSync(path.join(dir, 'resources'));
            const target = path.join(dir, 'resources', 'app.asar');
            fs.copyFileSync(asarArg, target);
            const before = asar.readArchive(target);
            const originals = asar.listFiles(before.header, '');
            const run = (...extra) => execFileSync(process.execPath, [path.join(__dirname, '..', 'install', 'install.js'), '--app-dir', dir].concat(extra), { encoding: 'utf8' });
            run();
            let a = asar.readArchive(target);
            let pkg = JSON.parse(asar.readFile(a, 'package.json'));
            assert.strictEqual(pkg.main, 'dps/boot.js');
            assert.strictEqual(pkg.dpsOverlayOriginalMain, 'main.js');
            assert.ok(fs.existsSync(target + '.dps-backup'));
            for (const f of originals) {
                if (f === 'package.json') continue;
                assert.ok(asar.readFile(a, f).equals(asar.readFile(before, f)), f + ' unchanged');
            }
            assert.ok(asar.readFile(a, 'dps/preload.js').equals(fs.readFileSync(path.join(__dirname, '..', 'src', 'dps', 'preload.js'))));
            run(); // again: an update in place
            a = asar.readArchive(target);
            pkg = JSON.parse(asar.readFile(a, 'package.json'));
            assert.strictEqual(pkg.dpsOverlayOriginalMain, 'main.js', 'a second install keeps the real entry point');
            run('--uninstall');
            a = asar.readArchive(target);
            pkg = JSON.parse(asar.readFile(a, 'package.json'));
            assert.strictEqual(pkg.main, 'main.js');
            assert.strictEqual(asar.entry(a.header, 'dps'), null);
            assert.deepStrictEqual(asar.listFiles(a.header, '').sort(), originals.slice().sort());
            for (const f of originals) {
                if (f === 'package.json') {
                    assert.deepStrictEqual(JSON.parse(asar.readFile(a, f)), JSON.parse(asar.readFile(before, f)));
                    continue;
                }
                assert.ok(asar.readFile(a, f).equals(asar.readFile(before, f)), f + ' restored');
            }
        });
    } else {
        console.log('  skip (pass a launcher app.asar to test the installer)');
    }

    console.log('\n' + passed + ' passed, ' + failures.length + ' failed');
    process.exit(failures.length ? 1 : 0);
}

main();
