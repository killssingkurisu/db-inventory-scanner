'use strict';

const { EventEmitter } = require('events');

/**
 * The fight being measured: a stopwatch, and every hit and cast that arrived while it ran.
 *
 * Damage is what the player's own client sent to the server (packet 0x0A for hits, 0x79 for DoT
 * ticks). The server can still add to it afterwards (the Soulthief passive, an admin damage
 * scale), so this is the number the client computed, which is also the number the floating
 * combat text shows.
 */

const MAX_HITS_LOGGED = 50000;
const STATS = ['attack', 'expertise', 'unknown'];

function emptyStats() {
    return { attack: 0, expertise: 0, unknown: 0 };
}

class DpsMeter extends EventEmitter {
    constructor({ powers, now } = {}) {
        super();
        this.powers = powers || null;
        this.now = now || (() => Date.now());
        this.autoStart = false;
        this.equipped = [];      // [{ group, key, label, rank }] from the latest spell scan
        this.scanAbilities = {}; // group -> ability entry from the scan
        this.reset();
    }

    setPowers(powers) {
        this.powers = powers;
        // Labels and scaling come from the table, so rows built before it changed are refreshed.
        for (const row of this.rows.values()) {
            const p = this.powers && this.powers.get(row.lastPowerId);
            if (p && !row.summon) {
                row.label = p.label;
            }
        }
        this.emit('change');
    }

    setSpellScan(scan) {
        this.equipped = [];
        this.scanAbilities = {};
        if (scan && Array.isArray(scan.abilities)) {
            for (const a of scan.abilities) {
                if (a && a.key) {
                    this.scanAbilities[a.key] = a;
                }
            }
        }
        if (scan && Array.isArray(scan.hotbar)) {
            for (const h of scan.hotbar) {
                if (h && h.key) {
                    this.equipped.push({ group: h.key, key: String(h.slotKey || h.slot || ''), label: h.name || h.key, rank: h.rank || 0 });
                }
            }
        }
        this.emit('change');
    }

    /* ---------- the stopwatch ---------- */

    get state() {
        return this._state;
    }

    elapsedMs() {
        return this.accumMs + (this._state === 'running' ? Math.max(0, this.now() - this.segmentStart) : 0);
    }

    start() {
        if (this._state === 'running') {
            return;
        }
        if (this._state === 'idle') {
            this.startedAt = new Date(this.now()).toISOString();
        }
        this._state = 'running';
        this.segmentStart = this.now();
        this.emit('change');
    }

    stop() {
        if (this._state !== 'running') {
            return;
        }
        this.accumMs += Math.max(0, this.now() - this.segmentStart);
        this._state = 'stopped';
        this.stoppedAt = new Date(this.now()).toISOString();
        this.emit('change');
    }

    toggle() {
        if (this._state === 'running') this.stop();
        else this.start();
    }

    reset() {
        this._state = 'idle';
        this.accumMs = 0;
        this.segmentStart = 0;
        this.startedAt = '';
        this.stoppedAt = '';
        this.rows = new Map();
        this.targets = new Map();
        this.timeline = [];
        this.hitsLog = [];
        this.totals = { damage: 0, hits: 0, casts: 0, crits: 0, critDamage: 0, dotDamage: 0, dotTicks: 0, summonDamage: 0 };
        this.byStat = emptyStats();
        this.ignored = { hits: 0, damage: 0, casts: 0 };
        this.levels = [];
        this.emit('change');
    }

    /* ---------- input ---------- */

    rowFor(powerId, summonName) {
        const p = this.powers ? this.powers.get(powerId) : null;
        let key;
        let label;
        if (p) {
            // Hotbar abilities by their base power (every rank in one row). Everything else
            // (basic attacks, rune procs, pets) by its name, so two "Sword Melee" powers share a row.
            key = p.ability && p.ability[2] > 0 ? p.group : 'name:' + p.label;
            label = p.label;
        } else if (summonName) {
            key = 'summon:' + summonName;
            label = summonName;
        } else {
            key = 'power:' + powerId;
            label = 'Power #' + powerId;
        }
        let row = this.rows.get(key);
        if (!row) {
            row = {
                key,
                label,
                ranks: new Set(),
                powerIds: new Set(),
                lastPowerId: powerId,
                casts: 0,
                hits: 0,
                crits: 0,
                critDamage: 0,
                hitDamage: 0,
                dotDamage: 0,
                dotTicks: 0,
                summonDamage: 0,
                damage: 0,
                maxHit: 0,
                byStat: emptyStats(),
                firstAt: 0,
                lastAt: 0,
                summon: !p && Boolean(summonName),
                monster: Boolean(p && p.monster)
            };
            this.rows.set(key, row);
        }
        row.lastPowerId = powerId;
        row.powerIds.add(powerId);
        if (p && p.rank) {
            row.ranks.add(p.rank);
        }
        return row;
    }

    /** Called for each damage event the relay attributes to the player. */
    recordDamage({ kind, powerId, damage, crit, targetName, summon }) {
        const amount = Math.round(Math.abs(Number(damage) || 0));
        if (!amount) {
            return;
        }
        if (this._state !== 'running' && this.autoStart && this._state === 'idle') {
            this.start();
        }
        if (this._state !== 'running') {
            this.ignored.hits += 1;
            this.ignored.damage += amount;
            this.emit('ignored');
            return;
        }
        const t = this.elapsedMs();
        const row = this.rowFor(powerId, summon ? summon.name : '');
        const stat = this.powers ? this.powers.statFor(powerId, kind) : 'unknown';
        const s = STATS.includes(stat) ? stat : 'unknown';

        row.damage += amount;
        row.byStat[s] += amount;
        if (!row.firstAt) row.firstAt = t || 1;
        row.lastAt = t;
        if (kind === 'dot') {
            row.dotDamage += amount;
            row.dotTicks += 1;
            this.totals.dotDamage += amount;
            this.totals.dotTicks += 1;
        } else {
            row.hits += 1;
            row.hitDamage += amount;
            if (amount > row.maxHit) row.maxHit = amount;
            this.totals.hits += 1;
            if (crit) {
                row.crits += 1;
                row.critDamage += amount;
                this.totals.crits += 1;
                this.totals.critDamage += amount;
            }
        }
        if (summon) {
            row.summonDamage += amount;
            this.totals.summonDamage += amount;
        }
        this.totals.damage += amount;
        this.byStat[s] += amount;

        const name = targetName || 'Unknown target';
        const tg = this.targets.get(name) || { name, damage: 0, hits: 0 };
        tg.damage += amount;
        tg.hits += 1;
        this.targets.set(name, tg);

        const sec = Math.floor(t / 1000);
        while (this.timeline.length <= sec) this.timeline.push(0);
        this.timeline[sec] += amount;

        if (this.hitsLog.length < MAX_HITS_LOGGED) {
            this.hitsLog.push([Math.round(t), powerId, amount, crit ? 1 : 0, kind === 'dot' ? 'dot' : 'hit', name, summon ? summon.name : '']);
        }
        this.emit('change');
    }

    recordCast({ powerId }) {
        // A cast alone never starts the clock (auto-start waits for the first hit).
        if (this._state !== 'running') {
            this.ignored.casts += 1;
            return;
        }
        const row = this.rowFor(powerId, '');
        row.casts += 1;
        this.totals.casts += 1;
        this.emit('change');
    }

    noteLevel(level) {
        if (level && this.levels[this.levels.length - 1] !== level) {
            this.levels.push(level);
        }
    }

    /* ---------- output ---------- */

    rowView(row, total, seconds) {
        const scan = this.scanAbilities[row.key] || null;
        const p = this.powers ? this.powers.get(row.lastPowerId) : null;
        const ranks = Array.from(row.ranks).sort((a, b) => a - b);
        return {
            key: row.key,
            label: row.label,
            rank: ranks.length ? ranks[ranks.length - 1] : scan ? scan.rank || 0 : 0,
            ranks,
            casts: row.casts,
            hits: row.hits,
            crits: row.crits,
            critRate: row.hits ? row.crits / row.hits : 0,
            damage: row.damage,
            share: total ? row.damage / total : 0,
            dps: seconds > 0 ? row.damage / seconds : 0,
            hitDamage: row.hitDamage,
            dotDamage: row.dotDamage,
            dotTicks: row.dotTicks,
            summonDamage: row.summonDamage,
            avgHit: row.hits ? row.hitDamage / row.hits : 0,
            maxHit: row.maxHit,
            byStat: Object.assign({}, row.byStat),
            scaling: scan && scan.scalingText ? scan.scalingText : p && p.scaling.text ? p.scaling.text : '',
            description: scan && scan.description ? scan.description : p ? p.description : '',
            damageType: p ? p.damageType : '',
            powerIds: Array.from(row.powerIds),
            summon: row.summon,
            monster: row.monster,
            hotkey: '',
            equipped: false
        };
    }

    snapshot() {
        const ms = this.elapsedMs();
        // At least one second, so the first hit after Start doesn't read as millions per second.
        const seconds = ms > 0 ? Math.max(ms / 1000, 1) : 0;
        const total = this.totals.damage;
        const views = new Map();
        for (const row of this.rows.values()) {
            views.set(row.key, this.rowView(row, total, seconds));
        }
        // Equipped spells come first, in hotbar order, even before they've done anything.
        const equippedRows = [];
        for (const e of this.equipped) {
            let v = views.get(e.group);
            if (!v) {
                v = this.rowView(
                    {
                        key: e.group, label: e.label, ranks: new Set(e.rank ? [e.rank] : []), powerIds: new Set(), lastPowerId: 0,
                        casts: 0, hits: 0, crits: 0, critDamage: 0, hitDamage: 0, dotDamage: 0, dotTicks: 0, summonDamage: 0,
                        damage: 0, maxHit: 0, byStat: emptyStats(), summon: false, monster: false
                    },
                    total,
                    seconds
                );
            }
            v.equipped = true;
            v.hotkey = e.key;
            views.delete(e.group);
            equippedRows.push(v);
        }
        const others = Array.from(views.values()).sort((a, b) => b.damage - a.damage || b.casts - a.casts);
        const last = this.timeline.length;
        return {
            state: this._state,
            autoStart: this.autoStart,
            startedAt: this.startedAt,
            elapsedMs: ms,
            totals: Object.assign({}, this.totals),
            dps: seconds > 0 ? total / seconds : 0,
            critRate: this.totals.hits ? this.totals.crits / this.totals.hits : 0,
            byStat: Object.assign({}, this.byStat),
            ignored: Object.assign({}, this.ignored),
            equipped: equippedRows,
            others,
            timeline: this.timeline.slice(Math.max(0, last - 60)),
            timelineStart: Math.max(0, last - 60),
            levels: this.levels.slice()
        };
    }

    /** Everything, for the export file. */
    report() {
        const snap = this.snapshot();
        const rows = snap.equipped.concat(snap.others);
        return {
            snapshot: snap,
            rows,
            targets: Array.from(this.targets.values()).sort((a, b) => b.damage - a.damage),
            timeline: this.timeline.slice(),
            hits: this.hitsLog.slice(),
            stoppedAt: this.stoppedAt
        };
    }
}

module.exports = { DpsMeter };
