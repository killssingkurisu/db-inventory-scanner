'use strict';

const fs = require('fs');
const path = require('path');
const http = require('http');
const https = require('https');
const { spawnSync } = require('child_process');

const { GameRelay } = require('./relay');
const { DpsMeter } = require('./meter');
const { PowerTable, dataFromSwz } = require('./powers');
const { SpellScanStore } = require('./spellScans');
const exporter = require('./exporter');

const VERSION = '1.0.0';
const SOURCE = 'DB DPS Overlay ' + VERSION;
const PORT_BASE = 47690;
const PORT_LAST = 47890;
/** The client logs in to this host on this port (LinkUpdater.const_1264, Connection.LOGINSERVER_PORT). */
const LOGIN_HOST = 'dungeonblitzr.theminesa.studio';
const GAME_PORT = 8080;

function log(message) {
    console.log('[DPS] ' + message);
}

/** Every game server address the client may open: the login host, and each server in servers.json. */
function gameTargets(appRoot) {
    const hosts = [LOGIN_HOST];
    try {
        const servers = JSON.parse(fs.readFileSync(path.join(appRoot, 'servers.json'), 'utf8'));
        for (const s of (servers && servers.servers) || []) {
            try {
                const h = new URL(String(s.url)).hostname.toLowerCase();
                if (h && !hosts.includes(h)) hosts.push(h);
            } catch (_e) {
                // not a URL
            }
        }
    } catch (_e) {
        // servers.json is optional here
    }
    return hosts.map((host) => ({ host, port: GAME_PORT }));
}

/**
 * Free loopback ports, found before Chromium starts. The host mapping has to be on the command
 * line before the network service launches, which is before anything asynchronous can run in
 * this process, so a short-lived copy of this executable (in Node mode) does the probing.
 */
function findFreePorts(count) {
    const script =
        "const net=require('net');const out=[];let p=" + PORT_BASE + ';' +
        '(function next(){if(out.length>=' + count + '||p>' + PORT_LAST + '){process.stdout.write(JSON.stringify(out));return;}' +
        "const port=p++;const s=net.createServer();s.once('error',()=>next());" +
        "s.listen(port,'127.0.0.1',()=>s.close(()=>{out.push(port);next();}));})();";
    const env = Object.assign({}, process.env, { ELECTRON_RUN_AS_NODE: '1' });
    const r = spawnSync(process.execPath, ['-e', script], { env, encoding: 'utf8', timeout: 10000, windowsHide: true });
    if (r.error) {
        throw r.error;
    }
    const ports = JSON.parse(String(r.stdout || '[]').trim() || '[]');
    if (!Array.isArray(ports) || ports.length < count) {
        throw new Error('no free loopback ports between ' + PORT_BASE + ' and ' + PORT_LAST);
    }
    return ports;
}

function fetchBuffer(url, timeoutMs) {
    return new Promise((resolve, reject) => {
        const lib = url.startsWith('https:') ? https : http;
        const req = lib.get(url, { timeout: timeoutMs }, (res) => {
            if (res.statusCode !== 200) {
                res.resume();
                reject(new Error('HTTP ' + res.statusCode));
                return;
            }
            const parts = [];
            res.on('data', (d) => parts.push(d));
            res.on('end', () => resolve(Buffer.concat(parts)));
            res.on('error', reject);
        });
        req.on('timeout', () => req.destroy(new Error('timed out')));
        req.on('error', reject);
    });
}

function stamp(d) {
    const pad = (x) => String(x).padStart(2, '0');
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + ' ' + pad(d.getHours()) + pad(d.getMinutes());
}

function safeFileName(s) {
    return String(s || '').replace(/[\\/:*?"<>|]+/g, '').trim() || 'character';
}

class DpsOverlay {
    constructor({ appRoot }) {
        this.appRoot = appRoot;
        this.electron = require('electron');
        this.app = this.electron.app;
        this.enabled = false;
        this.targets = [];
        this.relays = [];
        this.meter = new DpsMeter();
        this.scans = null;
        this.powersInfo = { source: 'none', count: 0 };
        this.gameContents = null;
        this.gameOrigin = '';
        this.livePowersFrom = '';
        this.character = '';
        this.className = '';
        this.level = '';
        this.enteredUnmapped = '';
        this.gameLoadedAt = 0;
        this.dirty = true;
        this.lastSent = 0;
        this.lastExport = '';
        this.settings = { autoStart: false, hidden: false, compact: { x: 16, y: 16, open: true } };
        this.meter.on('change', () => {
            this.dirty = true;
        });
        this.meter.on('ignored', () => {
            this.dirty = true;
        });
    }

    get preloadPath() {
        return path.join(__dirname, 'preload.js');
    }

    /** Synchronous, before 'ready': picks ports and maps the game socket onto them. */
    prepare() {
        if (process.env.DUNGEON_BLITZ_DPS === '0') {
            log('Off (DUNGEON_BLITZ_DPS=0).');
            return false;
        }
        this.targets = gameTargets(this.appRoot);
        const ports = findFreePorts(this.targets.length);
        this.relays = this.targets.map(
            (t, i) => new GameRelay({ listenPort: ports[i], upstreamHost: t.host, upstreamPort: t.port })
        );
        const rules = this.relays.map((r) => 'MAP ' + r.upstreamHost + ':' + r.upstreamPort + ' 127.0.0.1:' + r.listenPort);
        const existing = this.app.commandLine.getSwitchValue('host-resolver-rules');
        this.app.commandLine.appendSwitch('host-resolver-rules', (existing ? existing + ', ' : '') + rules.join(', '));
        log('Game socket routed through the meter: ' + rules.join(', '));
        this.enabled = true;

        this.app.on('web-contents-created', (_e, wc) => this.watchContents(wc));
        this.app.whenReady().then(() => this.onReady()).catch((err) => log('Start failed: ' + ((err && err.stack) || err)));
        return true;
    }

    async onReady() {
        const { session, ipcMain } = this.electron;
        this.loadSettings();
        this.meter.autoStart = Boolean(this.settings.autoStart);

        // The game page gets the overlay through a session preload; the launcher's own window
        // runs it too and it does nothing there (it only acts on http(s) pages).
        const preloads = session.defaultSession.getPreloads();
        if (!preloads.includes(this.preloadPath)) {
            session.defaultSession.setPreloads(preloads.concat([this.preloadPath]));
        }
        ipcMain.handle('dbdps:cmd', (event, cmd, arg) => this.command(event, cmd, arg));

        this.loadBundledPowers();
        this.scans = new SpellScanStore({ documents: this.app.getPath('documents') });
        this.scans.on('change', (scan) => this.applyScan(scan));
        this.scans.refresh();
        this.scans.watch();

        for (const relay of this.relays) {
            relay.on('connection', (tracker) => this.followConnection(relay, tracker));
            relay.on('status', () => {
                this.dirty = true;
            });
            const ok = await relay.listen();
            if (!ok) log('Relay for ' + relay.label + ' could not listen on ' + relay.listenPort + ': ' + relay.error);
        }

        setInterval(() => this.push(), 250);
        this.app.on('before-quit', () => {
            for (const r of this.relays) r.close();
            if (this.scans) this.scans.close();
        });
    }

    /* ---------- data ---------- */

    loadBundledPowers() {
        let data = null;
        try {
            data = JSON.parse(fs.readFileSync(this.cachePath(), 'utf8'));
            this.powersInfo = { source: 'cached ' + (data.source || ''), count: data.powers.length };
        } catch (_e) {
            data = null;
        }
        if (!data) {
            data = JSON.parse(fs.readFileSync(path.join(__dirname, 'powers-snapshot.json'), 'utf8'));
            this.powersInfo = { source: 'bundled ' + (data.source || ''), count: data.powers.length };
        }
        this.meter.setPowers(new PowerTable(data));
    }

    cachePath() {
        return path.join(this.app.getPath('userData'), 'dps-powers-cache.json');
    }

    async loadLivePowers(origin) {
        if (!origin || this.livePowersFrom === origin) {
            return;
        }
        this.livePowersFrom = origin;
        const url = origin.replace(/\/$/, '') + '/p/cbq/Game.swz';
        try {
            const buf = await fetchBuffer(url, 20000);
            const data = dataFromSwz(buf, url + ' (' + new Date().toISOString().slice(0, 10) + ')');
            this.meter.setPowers(new PowerTable(data));
            this.powersInfo = { source: 'live ' + url, count: data.powers.length };
            try {
                fs.writeFileSync(this.cachePath(), JSON.stringify(data));
            } catch (_e) {
                // the cache is a convenience
            }
            log('Spell data loaded from ' + url + ' (' + data.powers.length + ' powers)');
        } catch (err) {
            log('Live spell data unavailable (' + ((err && err.message) || err) + '); using ' + this.powersInfo.source);
        }
        this.dirty = true;
    }

    applyScan(scan) {
        this.meter.setSpellScan(scan);
        if (scan && scan.className && (!this.character || scan.character.toLowerCase() === this.character.toLowerCase())) {
            this.className = scan.className;
        }
        this.dirty = true;
    }

    followConnection(relay, tracker) {
        tracker.on('character', ({ name }) => {
            if (name && name !== this.character) {
                this.character = name;
                this.className = '';
                if (this.scans) {
                    this.scans.setCharacter(name);
                    this.applyScan(this.scans.current);
                }
                this.dirty = true;
            }
        });
        tracker.on('cast', (e) => this.meter.recordCast(e));
        tracker.on('damage', (e) => this.meter.recordDamage(e));
        tracker.on('enterWorld', (w) => {
            this.level = w.level || this.level;
            this.meter.noteLevel(this.level);
            const mapped = this.relays.some((r) => r.upstreamHost === String(w.host).toLowerCase() && r.upstreamPort === w.port);
            this.enteredUnmapped = mapped ? '' : w.host + ':' + w.port;
            if (!mapped) log('The game was sent to ' + w.host + ':' + w.port + ', which the meter does not relay.');
            this.dirty = true;
        });
    }

    /* ---------- windows ---------- */

    watchContents(wc) {
        wc.on('did-finish-load', () => {
            const url = wc.getURL();
            if (!/^https?:/i.test(url)) {
                return;
            }
            this.gameContents = wc;
            this.gameLoadedAt = Date.now();
            try {
                this.gameOrigin = new URL(url).origin;
            } catch (_e) {
                this.gameOrigin = '';
            }
            this.loadLivePowers(this.gameOrigin);
            this.dirty = true;
        });
        wc.on('destroyed', () => {
            if (this.gameContents === wc) this.gameContents = null;
        });
        wc.on('before-input-event', (event, input) => {
            if (wc !== this.gameContents || input.type !== 'keyDown' || input.isAutoRepeat) {
                return;
            }
            if (input.control || input.alt || input.meta) {
                return;
            }
            const handled = { F6: 'toggle', F7: 'reset', F8: 'hide' }[input.key];
            if (handled) {
                event.preventDefault();
                this.command(null, handled);
            }
        });
    }

    push() {
        const wc = this.gameContents;
        if (!wc || wc.isDestroyed()) {
            return;
        }
        const now = Date.now();
        // While the clock runs the numbers move every frame of this timer; otherwise only on change,
        // plus a slow heartbeat for the link status.
        if (!this.dirty && this.meter.state !== 'running' && now - this.lastSent < 2000) {
            return;
        }
        this.dirty = false;
        this.lastSent = now;
        wc.send('dbdps:snapshot', this.view());
    }

    view() {
        return {
            version: VERSION,
            meter: this.meter.snapshot(),
            character: this.character,
            className: this.className,
            level: this.level,
            link: this.linkStatus(),
            scan: this.scans ? this.scans.status() : null,
            powers: this.powersInfo,
            settings: this.settings,
            lastExport: this.lastExport
        };
    }

    linkStatus() {
        const failed = this.relays.filter((r) => !r.listening && r.error);
        if (!this.enabled) return { state: 'off', text: 'The meter is off.' };
        if (failed.length) {
            return { state: 'error', text: "Couldn't open the meter's local port " + failed[0].listenPort + ' (' + failed[0].error + '). Restart the launcher.' };
        }
        if (this.enteredUnmapped) {
            return { state: 'error', text: 'This level runs on ' + this.enteredUnmapped + ', which the meter can’t read.' };
        }
        const open = this.relays.reduce((n, r) => n + r.open.size, 0);
        const ever = this.relays.reduce((n, r) => n + r.connections, 0);
        if (open > 0) return { state: 'live', text: this.character ? 'Reading hits for ' + this.character : 'Connected, waiting for your character' };
        if (ever === 0 && this.gameLoadedAt && Date.now() - this.gameLoadedAt > 45000) {
            return { state: 'error', text: 'The game connected without passing through the meter. Restart the launcher.' };
        }
        return { state: 'waiting', text: 'Waiting for the game to connect' };
    }

    /* ---------- commands from the overlay and hotkeys ---------- */

    async command(event, cmd, arg) {
        const m = this.meter;
        switch (cmd) {
            case 'hello':
                break;
            case 'start':
                m.start();
                break;
            case 'stop':
                m.stop();
                break;
            case 'toggle':
                m.toggle();
                break;
            case 'reset':
                m.reset();
                break;
            case 'hide':
                this.settings.hidden = !this.settings.hidden;
                this.saveSettings();
                break;
            case 'show':
                this.settings.hidden = false;
                this.saveSettings();
                break;
            case 'autoStart':
                this.settings.autoStart = Boolean(arg);
                m.autoStart = this.settings.autoStart;
                this.saveSettings();
                break;
            case 'compact':
                if (arg && typeof arg === 'object') {
                    this.settings.compact = {
                        x: Math.round(Number(arg.x) || 0),
                        y: Math.round(Number(arg.y) || 0),
                        open: arg.open !== false
                    };
                    this.saveSettings();
                }
                break;
            case 'export':
                return this.exportResults(event);
            case 'copy':
                this.electron.clipboard.writeText(exporter.toSummary(m.report(), this.meta()));
                return { ok: true };
            case 'reveal':
                if (this.lastExport) this.electron.shell.showItemInFolder(this.lastExport);
                break;
            case 'rescan':
                if (this.scans) this.applyScan(this.scans.refresh());
                break;
            case 'scanFolder':
                if (this.scans) this.electron.shell.openPath(this.scans.primaryFolder());
                break;
            default:
                return { ok: false, error: 'unknown command' };
        }
        this.dirty = true;
        this.push();
        return { ok: true, view: this.view() };
    }

    meta() {
        const scan = this.scans ? this.scans.status() : null;
        return {
            source: SOURCE,
            character: this.character,
            className: this.className,
            scan: scan ? { file: scan.file, scannedAt: scan.scannedAt } : null
        };
    }

    async exportResults(event) {
        const { dialog, BrowserWindow } = this.electron;
        const win = event ? BrowserWindow.fromWebContents(event.sender) : null;
        const folder = path.join(this.app.getPath('documents'), 'Dungeon Blitz DPS');
        try {
            fs.mkdirSync(folder, { recursive: true });
        } catch (_e) {
            // the dialog still opens
        }
        const base = 'DPS ' + safeFileName(this.character) + ' ' + stamp(new Date());
        const result = await dialog.showSaveDialog(win, {
            title: 'Export DPS results',
            defaultPath: path.join(folder, base + '.json'),
            filters: [
                { name: 'Everything (JSON)', extensions: ['json'] },
                { name: 'Spell table (CSV, for spreadsheets)', extensions: ['csv'] }
            ]
        });
        if (result.canceled || !result.filePath) {
            return { ok: false, canceled: true };
        }
        const report = this.meter.report();
        const meta = this.meta();
        const file = result.filePath;
        const body = /\.csv$/i.test(file) ? exporter.toCsv(report, meta) : JSON.stringify(exporter.toJson(report, meta), null, 2);
        fs.writeFileSync(file, body, 'utf8');
        this.lastExport = file;
        log('Exported ' + file);
        this.dirty = true;
        return { ok: true, file };
    }

    /* ---------- settings ---------- */

    settingsPath() {
        return path.join(this.app.getPath('userData'), 'dps-overlay.json');
    }

    loadSettings() {
        try {
            const s = JSON.parse(fs.readFileSync(this.settingsPath(), 'utf8'));
            if (s && typeof s === 'object') {
                this.settings = Object.assign(this.settings, s);
            }
        } catch (_e) {
            // first run
        }
    }

    saveSettings() {
        try {
            fs.writeFileSync(this.settingsPath(), JSON.stringify(this.settings, null, 2));
        } catch (_e) {
            // not fatal
        }
    }
}

module.exports = { DpsOverlay, gameTargets, findFreePorts, VERSION };
