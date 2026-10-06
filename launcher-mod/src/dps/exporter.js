'use strict';

/**
 * Export formats for one measured fight: a JSON file with everything (format "dbb-dps"),
 * a CSV table of spells for spreadsheets, and a short text summary for chat.
 */

function round(n, d) {
    const f = Math.pow(10, d || 0);
    return Math.round((Number(n) || 0) * f) / f;
}

function clock(ms) {
    const total = Math.max(0, Math.floor(ms / 100));
    const tenths = total % 10;
    const s = Math.floor(total / 10);
    const m = Math.floor(s / 60);
    const h = Math.floor(m / 60);
    const pad = (x) => String(x).padStart(2, '0');
    return (h ? h + ':' + pad(m % 60) : String(m)) + ':' + pad(s % 60) + '.' + tenths;
}

function int(n) {
    return Math.round(Number(n) || 0).toLocaleString('en-US');
}

function spellRow(r) {
    return {
        spell: r.label,
        key: r.key,
        rank: r.rank || null,
        ranksSeen: r.ranks,
        hotbarKey: r.hotkey || null,
        equipped: r.equipped,
        casts: r.casts,
        hits: r.hits,
        crits: r.crits,
        critRate: round(r.critRate, 4),
        damage: r.damage,
        share: round(r.share, 4),
        dps: round(r.dps, 1),
        directDamage: r.hitDamage,
        dotDamage: r.dotDamage,
        dotTicks: r.dotTicks,
        summonDamage: r.summonDamage,
        averageHit: round(r.avgHit, 1),
        biggestHit: r.maxHit,
        damageByStat: r.byStat,
        scaling: r.scaling || null,
        damageType: r.damageType || null,
        description: r.description || null,
        powerIds: r.powerIds
    };
}

function toJson(report, meta) {
    const s = report.snapshot;
    const total = s.totals.damage;
    return {
        format: 'dbb-dps',
        version: 1,
        source: meta.source,
        exportedAt: new Date().toISOString(),
        character: { name: meta.character || '', class: meta.className || '' },
        levels: s.levels,
        timer: {
            state: s.state,
            startedAt: s.startedAt || null,
            stoppedAt: report.stoppedAt || null,
            elapsedMs: Math.round(s.elapsedMs),
            elapsed: clock(s.elapsedMs)
        },
        totals: {
            damage: total,
            dps: round(s.dps, 1),
            casts: s.totals.casts,
            hits: s.totals.hits,
            crits: s.totals.crits,
            critRate: round(s.critRate, 4),
            critDamage: s.totals.critDamage,
            dotDamage: s.totals.dotDamage,
            dotTicks: s.totals.dotTicks,
            summonDamage: s.totals.summonDamage
        },
        distribution: {
            byStat: {
                attack: { damage: s.byStat.attack, share: total ? round(s.byStat.attack / total, 4) : 0 },
                expertise: { damage: s.byStat.expertise, share: total ? round(s.byStat.expertise / total, 4) : 0 },
                unknown: { damage: s.byStat.unknown, share: total ? round(s.byStat.unknown / total, 4) : 0 }
            },
            byKind: {
                direct: { damage: total - s.totals.dotDamage, share: total ? round((total - s.totals.dotDamage) / total, 4) : 0 },
                dot: { damage: s.totals.dotDamage, share: total ? round(s.totals.dotDamage / total, 4) : 0 }
            },
            crits: {
                critDamage: s.totals.critDamage,
                share: total ? round(s.totals.critDamage / total, 4) : 0,
                rate: round(s.critRate, 4)
            }
        },
        spells: report.rows.map(spellRow),
        targets: report.targets,
        damagePerSecond: report.timeline,
        hitsColumns: ['ms', 'powerId', 'damage', 'crit', 'kind', 'target', 'summon'],
        hits: report.hits,
        outsideTimer: s.ignored,
        spellScan: meta.scan || null,
        notes: [
            'Damage is what your game client sent to the server for each hit (packet 0x0A) and DoT tick (packet 0x79). The server can add to it afterwards (the Soulthief passive, admin damage scaling), which is not included.',
            'Casts count packet 0x09 from your character. Hits and casts while the timer was stopped are not counted (see outsideTimer).',
            'Scaling: a direct hit counts toward the stat its power scales with ("1.49x attack"), a DoT tick toward its per-second stat ("2x Expertise/s").'
        ]
    };
}

function csvCell(v) {
    const s = v === null || v === undefined ? '' : String(v);
    return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
}

function toCsv(report, meta) {
    const s = report.snapshot;
    const lines = [];
    const row = (cells) => lines.push(cells.map(csvCell).join(','));
    row(['Spell', 'Rank', 'Hotbar key', 'Casts', 'Hits', 'Crits', 'Crit %', 'Damage', '% of total', 'DPS', 'Direct damage', 'DoT damage', 'Average hit', 'Biggest hit', 'Attack-scaled damage', 'Expertise-scaled damage', 'Scales with']);
    for (const r of report.rows) {
        row([
            r.label, r.rank || '', r.hotkey || '', r.casts, r.hits, r.crits, round(r.critRate * 100, 1), r.damage,
            round(r.share * 100, 1), round(r.dps, 1), r.hitDamage, r.dotDamage, round(r.avgHit, 0), r.maxHit,
            r.byStat.attack, r.byStat.expertise, r.scaling || ''
        ]);
    }
    lines.push('');
    row(['Character', meta.character || '']);
    row(['Time', clock(s.elapsedMs)]);
    row(['Total damage', s.totals.damage]);
    row(['DPS', round(s.dps, 1)]);
    row(['Casts', s.totals.casts]);
    row(['Hits', s.totals.hits]);
    row(['Crit rate %', round(s.critRate * 100, 1)]);
    row(['DoT damage', s.totals.dotDamage]);
    row(['Attack-scaled damage', s.byStat.attack]);
    row(['Expertise-scaled damage', s.byStat.expertise]);
    row(['Unclassified damage', s.byStat.unknown]);
    row(['Exported', new Date().toISOString()]);
    return lines.join('\r\n') + '\r\n';
}

function toSummary(report, meta) {
    const s = report.snapshot;
    const total = s.totals.damage || 0;
    const pct = (n) => (total ? Math.round((n / total) * 100) : 0) + '%';
    const who = meta.character ? meta.character + (meta.className ? ' (' + meta.className + ')' : '') : 'Dungeon Blitz';
    const out = [];
    out.push(who + ', ' + clock(s.elapsedMs));
    out.push(int(s.dps) + ' DPS, ' + int(total) + ' damage, ' + s.totals.casts + ' casts, ' + s.totals.hits + ' hits (' + Math.round(s.critRate * 100) + '% crit)');
    out.push('Attack ' + pct(s.byStat.attack) + ', Expertise ' + pct(s.byStat.expertise) + ', DoT ' + pct(s.totals.dotDamage));
    let i = 0;
    for (const r of report.rows) {
        if (!r.damage && !r.casts) continue;
        i += 1;
        out.push(
            i + '. ' + r.label + (r.rank ? ' r' + r.rank : '') + ': ' + int(r.damage) + ' (' + Math.round(r.share * 100) + '%), ' + r.casts + ' cast' + (r.casts === 1 ? '' : 's')
        );
        if (i >= 10) break;
    }
    return out.join('\n');
}

module.exports = { toJson, toCsv, toSummary, clock };
