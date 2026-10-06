'use strict';

const net = require('net');
const { EventEmitter } = require('events');
const P = require('./protocol');

/**
 * Follows one game connection and turns its packets into the player's casts and damage.
 *
 * Entity ids are the client's own numbering on both directions of one connection (the server
 * translates for each viewer), so everything is kept per connection. The player's body is the
 * entity the client reports with isPlayer set (0x08); its summons are the entities the client
 * reports with that body (or another of its summons) as summoner.
 */
class CombatTracker extends EventEmitter {
    constructor(label) {
        super();
        this.label = label;
        this.ownId = 0;
        this.characterName = '';
        this.summons = new Map(); // id -> { name, powerId }
        this.entities = new Map(); // id -> { name, isPlayer, team }
        this.recentDots = []; // [at, targetId, powerId, amount] sent by this client, to skip echoes
        this.packets = { up: 0, down: 0 };
    }

    isOwnSource(id) {
        return id !== 0 && (id === this.ownId || this.summons.has(id));
    }

    isFriendlyTarget(id) {
        if (id === this.ownId || this.summons.has(id)) {
            return true;
        }
        const e = this.entities.get(id);
        return Boolean(e && (e.isPlayer || e.team === P.TEAM.PLAYER));
    }

    targetName(id) {
        const e = this.entities.get(id);
        return e ? e.name : '';
    }

    fromClient(id, payload) {
        this.packets.up += 1;
        switch (id) {
            case P.PKT.ENT_FULL_UPDATE: {
                const e = P.parseEntityFullUpdate(payload);
                this.entities.set(e.id, { name: e.name, isPlayer: e.isPlayer, team: e.team });
                if (e.isPlayer) {
                    if (this.ownId !== e.id || this.characterName !== e.name) {
                        this.ownId = e.id;
                        this.characterName = e.name;
                        this.emit('character', { id: e.id, name: e.name });
                    }
                } else if (e.summonerId && this.isOwnSource(e.summonerId)) {
                    this.summons.set(e.id, { name: e.name, powerId: e.powerId });
                }
                break;
            }
            case P.PKT.POWER_CAST: {
                const c = P.parsePowerCast(payload);
                if (c.sourceId && c.sourceId === this.ownId) {
                    this.emit('cast', { powerId: c.powerId });
                }
                break;
            }
            case P.PKT.POWER_HIT: {
                const h = P.parsePowerHit(payload);
                if (h.damage > 0 && this.isOwnSource(h.sourceId) && !this.isFriendlyTarget(h.targetId)) {
                    this.emit('damage', this.damageEvent('hit', h.sourceId, h.powerId, h.damage, h.isCrit, h.targetId));
                }
                break;
            }
            case P.PKT.BUFF_TICK_DOT: {
                const d = P.parseBuffTickDot(payload);
                if (d.amount !== 0 && this.isOwnSource(d.sourceId) && !this.isFriendlyTarget(d.targetId)) {
                    const now = Date.now();
                    this.recentDots.push([now, d.targetId, d.powerId, Math.abs(d.amount)]);
                    if (this.recentDots.length > 64) this.recentDots.shift();
                    this.emit('damage', this.damageEvent('dot', d.sourceId, d.powerId, Math.abs(d.amount), false, d.targetId));
                }
                break;
            }
            default:
                break;
        }
    }

    fromServer(id, payload) {
        this.packets.down += 1;
        switch (id) {
            case P.PKT.NEWLY_RELEVANT_ENTITY: {
                const e = P.parseNewlyRelevantEntity(payload);
                this.entities.set(e.id, { name: e.name, isPlayer: e.isPlayer, team: e.team });
                break;
            }
            case P.PKT.ENTER_WORLD: {
                this.emit('enterWorld', P.parseEnterWorld(payload));
                break;
            }
            case P.PKT.BUFF_TICK_DOT: {
                // A DoT of ours ticking on a mob another party member's client runs comes back
                // from the server instead of going out from ours.
                const d = P.parseBuffTickDot(payload);
                const amount = Math.abs(d.amount);
                if (!amount || !this.isOwnSource(d.sourceId) || this.isFriendlyTarget(d.targetId)) {
                    break;
                }
                const now = Date.now();
                const echo = this.recentDots.findIndex(
                    (r) => now - r[0] < 1500 && r[1] === d.targetId && r[2] === d.powerId && r[3] === amount
                );
                if (echo >= 0) {
                    this.recentDots.splice(echo, 1);
                    break;
                }
                this.emit('damage', this.damageEvent('dot', d.sourceId, d.powerId, amount, false, d.targetId));
                break;
            }
            default:
                break;
        }
    }

    damageEvent(kind, sourceId, powerId, damage, crit, targetId) {
        const summon = sourceId !== this.ownId ? this.summons.get(sourceId) || null : null;
        return {
            kind,
            powerId: summon && !powerId ? summon.powerId : powerId,
            damage,
            crit: Boolean(crit),
            targetId,
            targetName: this.targetName(targetId),
            summon
        };
    }
}

/**
 * A loopback TCP relay for one game server address. Bytes are forwarded as they arrive, in both
 * directions, before anything reads them; a copy goes through a PacketSplitter into the tracker.
 * A packet the tracker can't read is skipped; the stream itself is never altered.
 */
class GameRelay extends EventEmitter {
    constructor({ listenPort, upstreamHost, upstreamPort }) {
        super();
        this.listenPort = listenPort;
        this.upstreamHost = upstreamHost;
        this.upstreamPort = upstreamPort;
        this.server = null;
        this.listening = false;
        this.error = '';
        this.connections = 0;
        this.open = new Set();
        this.lastPacketAt = 0;
        this.seq = 0;
    }

    get label() {
        return this.upstreamHost + ':' + this.upstreamPort;
    }

    listen() {
        return new Promise((resolve) => {
            const server = net.createServer({ pauseOnConnect: false }, (client) => this.accept(client));
            server.on('error', (err) => {
                this.error = String((err && err.message) || err);
                this.listening = false;
                this.emit('status');
                resolve(false);
            });
            server.listen(this.listenPort, '127.0.0.1', () => {
                this.server = server;
                this.listening = true;
                this.error = '';
                this.emit('status');
                resolve(true);
            });
        });
    }

    close() {
        for (const pair of this.open) {
            pair.client.destroy();
            pair.upstream.destroy();
        }
        this.open.clear();
        if (this.server) {
            this.server.close();
            this.server = null;
        }
        this.listening = false;
    }

    accept(client) {
        const upstream = net.connect({ host: this.upstreamHost, port: this.upstreamPort });
        client.setNoDelay(true);
        upstream.setNoDelay(true);
        const tracker = new CombatTracker(this.label + '#' + ++this.seq);
        const pair = { client, upstream, tracker };
        this.open.add(pair);
        this.connections += 1;
        this.emit('connection', tracker);
        this.emit('status');

        const up = new P.PacketSplitter((id, payload) => {
            this.lastPacketAt = Date.now();
            tracker.fromClient(id, payload);
        });
        const down = new P.PacketSplitter((id, payload) => {
            this.lastPacketAt = Date.now();
            tracker.fromServer(id, payload);
        });

        client.on('data', (chunk) => {
            if (!upstream.write(chunk)) client.pause();
            up.push(chunk);
        });
        upstream.on('drain', () => client.resume());
        upstream.on('data', (chunk) => {
            if (!client.write(chunk)) upstream.pause();
            down.push(chunk);
        });
        client.on('drain', () => upstream.resume());

        let closed = false;
        const finish = () => {
            if (closed) return;
            closed = true;
            this.open.delete(pair);
            client.destroy();
            upstream.destroy();
            tracker.emit('closed');
            this.emit('status');
        };
        client.on('end', () => upstream.end());
        upstream.on('end', () => client.end());
        client.on('close', finish);
        upstream.on('close', finish);
        client.on('error', finish);
        upstream.on('error', (err) => {
            this.error = String((err && err.message) || err);
            finish();
        });
    }

    status() {
        return {
            target: this.label,
            listenPort: this.listenPort,
            listening: this.listening,
            error: this.error,
            connections: this.connections,
            open: this.open.size,
            lastPacketAt: this.lastPacketAt
        };
    }
}

module.exports = { GameRelay, CombatTracker };
