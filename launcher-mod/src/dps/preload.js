'use strict';

/**
 * The DPS overlay on the game page: two panels in the grey gutters beside the game's 3:2
 * picture (the page gives the whole window to Flash and the game clips itself to a centred
 * 3:2 box, see the page's #game-container comment), or one movable card when the window is
 * too narrow for gutters.
 *
 * Runs as a session preload, so it also loads in the launcher's own window; it only acts on
 * an http(s) page that holds the game's object.
 */

const { ipcRenderer } = require('electron');
const fs = require('fs');
const path = require('path');

const SIDE_MIN = 150; // narrowest gutter that still takes a panel, in CSS px
const SIDE_MAX = 300;

const CSS = `
#dbdps {
  --ink: rgba(28, 26, 13, 0.95);
  --ink-solid: #1c1a0d;
  --well: #2a2612;
  --parch: #eee2bc;
  --parch-dim: #b5a983;
  --brass: #b8973f;
  --brass-dim: rgba(184, 151, 63, 0.38);
  --cyan: #00ccff;
  --citrine: #e3b341;
  --sapphire: #4f8fe6;
  --unclassed: #6f6a58;
  --alarm: #d6453d;
  position: fixed; top: 0; left: 0; right: 0; bottom: 0; pointer-events: none; z-index: 2147483000;
  font-family: "DBDPS Averia", Georgia, "Times New Roman", serif;
  font-size: var(--fs, 12px); line-height: 1.35; color: var(--parch);
  -webkit-font-smoothing: antialiased; user-select: none;
}
#dbdps * { box-sizing: border-box; margin: 0; padding: 0; }
#dbdps i { font-style: normal; }
#dbdps[hidden], #dbdps [hidden] { display: none !important; }
#dbdps .panel {
  position: absolute; pointer-events: auto; display: flex; flex-direction: column; gap: 0.7em;
  background: var(--ink); border: 1px solid var(--brass); border-radius: 5px;
  box-shadow: inset 0 0 0 3px var(--ink-solid), inset 0 0 0 4px var(--brass-dim), 0 6px 18px rgba(0,0,0,0.35);
  padding: 0.85em 0.85em 0.75em; overflow: hidden;
}
#dbdps .head { display: flex; align-items: center; gap: 0.45em; min-height: 1.5em; }
#dbdps .head h2 { font-size: 1.08em; font-weight: 700; flex: 1; letter-spacing: 0.01em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
#dbdps .head .sub { font-size: 0.85em; color: var(--parch-dim); font-weight: 400; }
#dbdps .dot { width: 0.6em; height: 0.6em; border-radius: 50%; border: 1px solid var(--brass); flex: none; }
#dbdps .dot.running { background: var(--alarm); border-color: var(--alarm); animation: dbdps-pulse 1.2s ease-in-out infinite; }
#dbdps .dot.stopped { background: var(--brass); }
@keyframes dbdps-pulse { 50% { opacity: 0.35; } }
@media (prefers-reduced-motion: reduce) { #dbdps .dot.running { animation: none; } }
#dbdps .iconbtn { all: unset; cursor: pointer; color: var(--parch-dim); width: 1.4em; height: 1.4em; display: grid; place-items: center; border-radius: 3px; font-size: 1.05em; }
#dbdps .iconbtn:hover { color: var(--parch); background: var(--well); }
#dbdps .clock { font-size: 2.05em; font-weight: 700; line-height: 1; }
#dbdps .d { display: inline-block; width: 0.6em; text-align: center; }
#dbdps .sep { display: inline-block; width: 0.28em; text-align: center; }
#dbdps .controls { display: flex; gap: 0.45em; }
#dbdps .btn {
  all: unset; cursor: pointer; flex: 1; text-align: center; padding: 0.38em 0.4em; border-radius: 3px;
  border: 1px solid var(--brass); color: var(--parch); font-weight: 700; font-size: 0.95em; white-space: nowrap;
}
#dbdps .btn:hover { background: var(--well); }
#dbdps .btn.primary { background: linear-gradient(#ecc85b, #c99a2e); color: #20180a; border-color: #6b5418; }
#dbdps .btn.primary:hover { background: linear-gradient(#f5d670, #d6a737); }
#dbdps .btn.primary.stop { background: linear-gradient(#e0614f, #ae3326); color: #fff3e8; border-color: #5e1810; }
#dbdps .btn:focus-visible, #dbdps .iconbtn:focus-visible, #dbdps .toggle:focus-visible { outline: 2px solid var(--cyan); outline-offset: 1px; }
#dbdps .dps { display: flex; flex-direction: column; }
#dbdps .dps .num { font-size: 2.35em; font-weight: 700; color: var(--cyan); line-height: 1.05; text-shadow: 0 1px 0 #000; }
#dbdps .dps .unit { color: var(--parch-dim); font-size: 0.88em; }
#dbdps .facts { display: grid; grid-template-columns: 1fr auto; gap: 0.15em 0.6em; font-size: 0.95em; }
#dbdps .facts dt { color: var(--parch-dim); }
#dbdps .facts dd { text-align: right; font-weight: 700; }
#dbdps svg.spark { width: 100%; height: 2.4em; display: block; }
#dbdps h3 { font-size: 0.95em; font-weight: 700; color: var(--parch); border-top: 1px solid var(--brass-dim); padding-top: 0.55em; }
#dbdps .split { display: flex; height: 0.75em; border-radius: 2px; overflow: hidden; background: var(--well); }
#dbdps .split i, #dbdps .bar i { display: block; height: 100%; }
#dbdps .atk { background: var(--citrine); }
#dbdps .exp { background: var(--sapphire); }
#dbdps .unk { background: var(--unclassed); }
#dbdps .legend { display: flex; flex-wrap: wrap; gap: 0.15em 0.8em; font-size: 0.9em; }
#dbdps .legend span { display: inline-flex; align-items: center; gap: 0.35em; }
#dbdps .legend b { width: 0.65em; height: 0.65em; border-radius: 1px; display: inline-block; }
#dbdps .note { font-size: 0.88em; color: var(--parch-dim); }
#dbdps .row { display: flex; gap: 0.45em; }
#dbdps .row .btn { font-weight: 400; }
#dbdps .toggle { all: unset; cursor: pointer; display: inline-flex; align-items: center; gap: 0.45em; font-size: 0.92em; color: var(--parch); }
#dbdps .toggle b { width: 1.9em; height: 1em; border-radius: 0.5em; background: var(--well); border: 1px solid var(--brass-dim); position: relative; flex: none; }
#dbdps .toggle b::after { content: ""; position: absolute; top: 0.1em; left: 0.12em; width: 0.7em; height: 0.7em; border-radius: 50%; background: var(--parch-dim); transition: left 0.12s; }
#dbdps .toggle[aria-pressed="true"] b { background: #0b4357; border-color: var(--cyan); }
#dbdps .toggle[aria-pressed="true"] b::after { left: 1.05em; background: var(--cyan); }
#dbdps .status { font-size: 0.82em; color: var(--parch-dim); display: flex; flex-direction: column; gap: 0.3em; border-top: 1px solid var(--brass-dim); padding-top: 0.55em; }
#dbdps .status .link { display: flex; gap: 0.4em; align-items: baseline; }
#dbdps .status .link::before { content: ""; width: 0.5em; height: 0.5em; border-radius: 50%; background: var(--brass); flex: none; transform: translateY(-0.05em); }
#dbdps .status .link.live::before { background: var(--cyan); }
#dbdps .status .link.error { color: #f0a59c; }
#dbdps .status .link.error::before { background: var(--alarm); }
#dbdps .status a { color: var(--parch); cursor: pointer; text-decoration: underline; text-decoration-color: var(--brass-dim); }
#dbdps .spells { min-height: 0; flex: 1; overflow-y: auto; display: flex; flex-direction: column; gap: 0.2em; margin-right: -0.4em; padding-right: 0.4em; }
#dbdps .spells::-webkit-scrollbar { width: 6px; }
#dbdps .spells::-webkit-scrollbar-thumb { background: var(--brass-dim); border-radius: 3px; }
#dbdps .group { font-size: 0.85em; color: var(--parch-dim); padding: 0.5em 0 0.15em; }
#dbdps .spell { display: flex; flex-direction: column; gap: 0.2em; padding: 0.35em 0.4em; border-radius: 3px; cursor: pointer; }
#dbdps .spell:hover { background: rgba(42, 38, 18, 0.8); }
#dbdps .spell .l1 { display: flex; align-items: baseline; gap: 0.4em; min-width: 0; }
#dbdps kbd { font: inherit; font-size: 0.8em; font-weight: 700; min-width: 1.45em; text-align: center; padding: 0 0.25em; border: 1px solid var(--brass); border-radius: 3px; background: var(--well); line-height: 1.35; flex: none; }
#dbdps .spell .name { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-weight: 700; }
#dbdps .spell.idle .name { font-weight: 400; color: var(--parch-dim); }
#dbdps .spell .rank { color: var(--cyan); font-size: 0.85em; flex: none; }
#dbdps .bar { display: flex; height: 0.45em; border-radius: 2px; overflow: hidden; background: var(--well); }
#dbdps .bar .fill { display: flex; height: 100%; }
#dbdps .spell .l2 { display: flex; gap: 0.5em; font-size: 0.88em; color: var(--parch-dim); }
#dbdps .spell .l2 .dmg { color: var(--parch); font-weight: 700; }
#dbdps .spell .l2 .casts { margin-left: auto; }
#dbdps .spell .more { display: grid; grid-template-columns: 1fr auto; gap: 0.1em 0.6em; font-size: 0.86em; padding-top: 0.25em; }
#dbdps .spell .more dt { color: var(--parch-dim); }
#dbdps .spell .more dd { text-align: right; }
#dbdps .spell .more .desc { grid-column: 1 / -1; color: var(--parch-dim); font-style: italic; padding-top: 0.2em; }
#dbdps .empty { color: var(--parch-dim); font-size: 0.92em; padding: 0.3em 0.1em; }
#dbdps .reveal {
  all: unset; pointer-events: auto; position: absolute; left: 0; top: 50%; transform: translateY(-50%);
  writing-mode: vertical-rl; padding: 0.7em 0.3em; cursor: pointer; font-weight: 700; font-size: 0.9em;
  background: var(--ink); color: var(--parch-dim); border: 1px solid var(--brass-dim); border-left: 0; border-radius: 0 4px 4px 0;
}
#dbdps .reveal:hover { color: var(--parch); }
#dbdps.compact .panel.fight { gap: 0.55em; }
#dbdps.compact .grip { cursor: move; }
#dbdps .pill { all: unset; pointer-events: auto; position: absolute; cursor: move; display: flex; gap: 0.6em; align-items: baseline;
  background: var(--ink); border: 1px solid var(--brass); border-radius: 4px; padding: 0.35em 0.7em; }
#dbdps .pill .num { color: var(--cyan); font-weight: 700; font-size: 1.15em; }
`;

function onGamePage() {
    if (!/^https?:$/.test(location.protocol)) return false;
    return Boolean(document.getElementById('game-container') || document.querySelector('object[type="application/x-shockwave-flash"], embed'));
}

function loadFonts() {
    const dir = path.join(__dirname, '..', 'renderer', 'assets', 'fonts');
    for (const [file, weight] of [['averia-serif-libre-400.woff2', '400'], ['averia-serif-libre-700.woff2', '700']]) {
        try {
            const face = new FontFace('DBDPS Averia', fs.readFileSync(path.join(dir, file)), { weight });
            document.fonts.add(face);
            face.load().catch(() => {});
        } catch (_e) {
            // Georgia stands in.
        }
    }
}

/* ---------- formatting ---------- */

function esc(s) {
    return String(s === undefined || s === null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function int(n) {
    return Math.round(Number(n) || 0).toLocaleString('en-US');
}

function short(n) {
    n = Math.round(Number(n) || 0);
    if (n >= 1e9) return (n / 1e9).toFixed(n >= 1e10 ? 1 : 2) + 'B';
    if (n >= 1e7) return (n / 1e6).toFixed(n >= 1e8 ? 0 : 1) + 'M';
    return n.toLocaleString('en-US');
}

function pct(part, whole) {
    return whole ? Math.round((part / whole) * 100) + '%' : '0%';
}

function clock(ms) {
    const t = Math.max(0, Math.floor(ms / 100));
    const s = Math.floor(t / 10);
    const m = Math.floor(s / 60);
    const h = Math.floor(m / 60);
    const pad = (x) => String(x).padStart(2, '0');
    return (h ? h + ':' + pad(m % 60) : String(m)) + ':' + pad(s % 60) + '.' + (t % 10);
}

/** Digits in fixed cells, so a running number doesn't shuffle sideways. */
function cells(text) {
    return String(text)
        .split('')
        .map((c) => (/\d/.test(c) ? '<i class="d">' + c + '</i>' : '<i class="sep">' + esc(c) + '</i>'))
        .join('');
}

function when(iso) {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' }) + ', ' + d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
}

/* ---------- the overlay ---------- */

class Overlay {
    constructor() {
        this.view = null;
        this.mode = '';
        this.open = new Set(); // spell keys with details shown
        this.rowEls = new Map();
        this.copiedAt = 0;
        this.drag = null;
    }

    mount() {
        const style = document.createElement('style');
        style.id = 'dbdps-style';
        style.textContent = CSS;
        document.head.appendChild(style);

        const root = document.createElement('div');
        root.id = 'dbdps';
        root.setAttribute('role', 'complementary');
        root.setAttribute('aria-label', 'Damage meter');
        root.innerHTML = `
          <section class="panel fight" aria-label="Fight">
            <div class="head grip">
              <span class="dot" data-el="dot"></span>
              <h2>Damage meter</h2>
              <button class="iconbtn" data-cmd="collapse" title="Shrink" hidden>&#8211;</button>
              <button class="iconbtn" data-cmd="hide" title="Hide (F8)">&#215;</button>
            </div>
            <div class="clock" data-el="clock"></div>
            <div class="controls">
              <button class="btn primary" data-cmd="toggle" data-el="toggle">Start</button>
              <button class="btn" data-cmd="reset">Reset</button>
            </div>
            <div class="dps"><span class="num" data-el="dps"></span><span class="unit">damage per second</span></div>
            <dl class="facts">
              <dt>Total damage</dt><dd data-el="total"></dd>
              <dt>Casts</dt><dd data-el="casts"></dd>
              <dt>Hits</dt><dd data-el="hits"></dd>
              <dt>Crit rate</dt><dd data-el="crit"></dd>
            </dl>
            <svg class="spark" data-el="spark" viewBox="0 0 120 24" preserveAspectRatio="none" aria-hidden="true"></svg>
            <h3>Scaling</h3>
            <div class="split" data-el="split" title="Share of damage by the stat it scales with"></div>
            <div class="legend" data-el="legend"></div>
            <p class="note" data-el="kinds"></p>
            <p class="note" data-el="ignored" hidden></p>
            <div class="row">
              <button class="btn" data-cmd="export">Export&#8230;</button>
              <button class="btn" data-cmd="copy" data-el="copy">Copy summary</button>
            </div>
            <button class="toggle" data-cmd="autoStart" data-el="auto" aria-pressed="false"><b></b>Start on first hit</button>
            <div class="status" data-el="status"></div>
          </section>
          <section class="panel spellpanel" aria-label="Spells">
            <div class="head"><h2>Spells</h2></div>
            <div class="spells" data-el="spells"></div>
          </section>
          <button class="reveal" data-cmd="show" hidden>Damage meter</button>
          <button class="pill" data-el="pill" hidden></button>`;
        document.body.appendChild(root);
        this.root = root;
        this.el = {};
        for (const n of root.querySelectorAll('[data-el]')) this.el[n.dataset.el] = n;
        this.fight = root.querySelector('.fight');
        this.spellPanel = root.querySelector('.spellpanel');
        this.reveal = root.querySelector('.reveal');

        // Clicks act on mousedown and never take focus away from the game, so the keyboard keeps
        // driving the character after pressing a button here.
        root.addEventListener('mousedown', (e) => this.onMouseDown(e), true);
        window.addEventListener('mousemove', (e) => this.onDrag(e));
        window.addEventListener('mouseup', () => this.endDrag());
        window.addEventListener('resize', () => this.layout());
        ipcRenderer.on('dbdps:snapshot', (_e, view) => this.render(view));
        ipcRenderer.invoke('dbdps:cmd', 'hello').then((r) => r && r.view && this.render(r.view)).catch(() => {});
        this.layout();
    }

    focusGame() {
        const game = document.getElementById('DungeonBlitz') || document.querySelector('object, embed');
        if (game && typeof game.focus === 'function') game.focus();
    }

    onMouseDown(e) {
        const target = e.target.closest('[data-cmd], .spell, .grip, .pill, a[data-act]');
        if (!target || e.button !== 0) return;
        e.preventDefault();
        if (target.matches('.pill') || (target.matches('.grip') && this.mode === 'compact' && !e.target.closest('[data-cmd]'))) {
            const box = (target.matches('.pill') ? target : this.fight).getBoundingClientRect();
            this.drag = { dx: e.clientX - box.left, dy: e.clientY - box.top, moved: false, pill: target.matches('.pill'), x0: e.clientX, y0: e.clientY };
            return;
        }
        if (target.matches('a[data-act]')) {
            this.send(target.dataset.act);
            return;
        }
        if (target.matches('.spell')) {
            const key = target.dataset.key;
            if (this.open.has(key)) this.open.delete(key);
            else this.open.add(key);
            if (this.view) this.renderSpells(this.view);
            return;
        }
        const cmd = target.dataset.cmd;
        if (cmd === 'collapse') {
            this.setCompactOpen(false);
            return;
        }
        if (cmd === 'autoStart') {
            this.send('autoStart', !(this.view && this.view.settings.autoStart));
            return;
        }
        if (cmd === 'copy') {
            this.send('copy').then(() => {
                this.copiedAt = Date.now();
                this.el.copy.textContent = 'Copied';
                setTimeout(() => {
                    if (Date.now() - this.copiedAt >= 1400) this.el.copy.textContent = 'Copy summary';
                }, 1500);
            });
            return;
        }
        this.send(cmd);
    }

    send(cmd, arg) {
        return ipcRenderer
            .invoke('dbdps:cmd', cmd, arg)
            .then((r) => {
                if (r && r.view) this.render(r.view);
                this.focusGame();
                return r;
            })
            .catch(() => this.focusGame());
    }

    onDrag(e) {
        if (!this.drag) return;
        if (Math.abs(e.clientX - this.drag.x0) + Math.abs(e.clientY - this.drag.y0) > 3) this.drag.moved = true;
        if (!this.drag.moved) return;
        const x = Math.max(0, Math.min(window.innerWidth - 60, e.clientX - this.drag.dx));
        const y = Math.max(0, Math.min(window.innerHeight - 30, e.clientY - this.drag.dy));
        this.compactPos = { x, y };
        this.placeCompact();
    }

    endDrag() {
        if (!this.drag) return;
        const d = this.drag;
        this.drag = null;
        if (d.pill && !d.moved) {
            this.setCompactOpen(true);
            return;
        }
        if (d.moved) this.saveCompact();
        this.focusGame();
    }

    setCompactOpen(open) {
        this.compactOpen = open;
        this.saveCompact();
        this.layout();
    }

    saveCompact() {
        const p = this.compactPos || { x: 16, y: 16 };
        ipcRenderer.invoke('dbdps:cmd', 'compact', { x: p.x, y: p.y, open: this.compactOpen !== false }).catch(() => {});
    }

    /* ---------- layout ---------- */

    layout() {
        if (!this.root) return;
        const W = window.innerWidth;
        const H = window.innerHeight;
        const boxW = Math.min(W, H * 1.5);
        const boxH = Math.min(H, W / 1.5);
        const gutter = (W - boxW) / 2;
        const top = (H - boxH) / 2;
        const hidden = Boolean(this.view && this.view.settings.hidden);
        this.root.hidden = false;
        const side = gutter >= SIDE_MIN;
        this.mode = side ? 'side' : 'compact';
        this.root.classList.toggle('compact', !side);
        this.root.querySelector('[data-cmd="collapse"]').hidden = side;

        if (side) {
            const width = Math.min(SIDE_MAX, Math.floor(gutter - 16));
            const fs = Math.max(11, Math.min(14, width / 14));
            this.root.style.setProperty('--fs', fs.toFixed(1) + 'px');
            for (const [panel, left] of [[this.fight, gutter - 8 - width], [this.spellPanel, W - gutter + 8]]) {
                Object.assign(panel.style, {
                    left: Math.round(left) + 'px',
                    top: Math.round(top + 8) + 'px',
                    width: width + 'px',
                    maxHeight: Math.round(boxH - 16) + 'px',
                    height: ''
                });
                panel.hidden = hidden;
            }
            this.el.pill.hidden = true;
            this.reveal.hidden = !hidden;
            return;
        }

        // Compact: one card the player can move, or a small pill when shrunk.
        this.root.style.setProperty('--fs', '11.5px');
        this.reveal.hidden = !hidden;
        if (this.compactOpen === undefined && this.view) {
            const c = this.view.settings.compact || {};
            this.compactOpen = c.open !== false;
            this.compactPos = { x: Number(c.x) || 16, y: Number(c.y) || 16 };
        }
        this.compactPos = this.compactPos || { x: 16, y: 16 };
        const open = this.compactOpen !== false;
        this.fight.hidden = hidden || !open;
        this.spellPanel.hidden = hidden || !open;
        this.el.pill.hidden = hidden || open;
        this.placeCompact();
    }

    placeCompact() {
        if (this.mode !== 'compact') return;
        const p = this.compactPos;
        const width = 230;
        const fightH = this.fight.hidden ? 0 : this.fight.offsetHeight;
        Object.assign(this.fight.style, { left: p.x + 'px', top: p.y + 'px', width: width + 'px', maxHeight: '', height: '' });
        Object.assign(this.spellPanel.style, {
            left: p.x + 'px',
            top: p.y + fightH + 6 + 'px',
            width: width + 'px',
            height: '',
            maxHeight: Math.max(120, window.innerHeight - (p.y + fightH + 14)) + 'px'
        });
        Object.assign(this.el.pill.style, { left: p.x + 'px', top: p.y + 'px' });
    }

    /* ---------- rendering ---------- */

    render(view) {
        const firstView = !this.view;
        this.view = view;
        const m = view.meter;
        const t = m.totals;
        const el = this.el;

        el.dot.className = 'dot ' + m.state;
        el.clock.innerHTML = cells(clock(m.elapsedMs));
        el.toggle.textContent = m.state === 'running' ? 'Stop' : m.state === 'stopped' ? 'Resume' : 'Start';
        el.toggle.classList.toggle('stop', m.state === 'running');
        el.toggle.title = (m.state === 'running' ? 'Stop' : 'Start') + ' the timer (F6)';
        el.dps.innerHTML = cells(short(m.dps));
        el.total.textContent = int(t.damage);
        el.casts.textContent = int(t.casts);
        el.hits.textContent = int(t.hits);
        el.crit.textContent = Math.round(m.critRate * 100) + '%';
        el.auto.setAttribute('aria-pressed', String(Boolean(view.settings.autoStart)));
        el.pill.innerHTML = '<span class="num">' + cells(short(m.dps)) + '</span><span>' + esc(clock(m.elapsedMs)) + '</span>';

        // Scaling: damage split by the stat each hit scales with.
        const s = m.byStat;
        const sum = s.attack + s.expertise + s.unknown;
        el.split.innerHTML = sum
            ? '<i class="atk" style="width:' + (s.attack / sum) * 100 + '%"></i><i class="exp" style="width:' + (s.expertise / sum) * 100 + '%"></i><i class="unk" style="width:' + (s.unknown / sum) * 100 + '%"></i>'
            : '';
        let legend = '<span><b class="atk"></b>Attack ' + pct(s.attack, sum) + '</span><span><b class="exp"></b>Expertise ' + pct(s.expertise, sum) + '</span>';
        if (s.unknown) legend += '<span><b class="unk"></b>Unclassified ' + pct(s.unknown, sum) + '</span>';
        el.legend.innerHTML = legend;
        el.kinds.textContent = t.damage
            ? 'Over time ' + pct(t.dotDamage, t.damage) + ' of damage. Crits ' + pct(t.critDamage, t.damage) + ' of damage.'
            : 'Hits split by Attack and Expertise scaling, from each spell’s stats.';
        const ig = m.ignored;
        el.ignored.hidden = !(ig.hits && m.state !== 'running');
        el.ignored.textContent = ig.hits ? int(ig.hits) + ' hit' + (ig.hits === 1 ? '' : 's') + ' (' + short(ig.damage) + ' damage) landed while the timer was stopped and weren’t counted.' : '';

        this.renderSpark(m);
        this.renderStatus(view);
        this.renderSpells(view);
        if (firstView || this.lastHidden !== view.settings.hidden) {
            this.lastHidden = view.settings.hidden;
            this.layout();
        } else if (this.mode === 'compact') {
            this.placeCompact();
        }
    }

    renderSpark(m) {
        const svg = this.el.spark;
        const data = m.timeline || [];
        if (data.length < 2) {
            svg.innerHTML = '';
            svg.style.display = 'none';
            return;
        }
        svg.style.display = '';
        // Rolling 3-second damage, so single big hits don't spike the line to nothing in between.
        const smooth = data.map((_, i) => (data[i] + (data[i - 1] || 0) + (data[i - 2] || 0)) / Math.min(3, i + 1));
        const max = Math.max.apply(null, smooth) || 1;
        const n = smooth.length;
        const pts = smooth.map((v, i) => ((i / (n - 1)) * 120).toFixed(1) + ',' + (23 - (v / max) * 21).toFixed(1));
        svg.innerHTML =
            '<polygon points="0,24 ' + pts.join(' ') + ' 120,24" fill="rgba(0,204,255,0.14)"/>' +
            '<polyline points="' + pts.join(' ') + '" fill="none" stroke="#00ccff" stroke-width="1.2" vector-effect="non-scaling-stroke"/>';
    }

    renderStatus(view) {
        const link = view.link || {};
        const scan = view.scan;
        let html = '<span class="link ' + esc(link.state || '') + '">' + esc(link.text || '') + '</span>';
        if (scan) {
            html += '<span>Spells from your scan of ' + esc(when(scan.scannedAt)) + ' (<a data-act="rescan">check again</a>)</span>';
        } else {
            html += '<span>No spell scan yet. Run “Scan spells” in DB Inventory Scanner to list your equipped spells (<a data-act="scanFolder">scan folder</a>).</span>';
        }
        if (view.lastExport) {
            html += '<span>Saved ' + esc(view.lastExport.split(/[\\/]/).pop()) + ' (<a data-act="reveal">show</a>)</span>';
        }
        html += '<span>F6 start or stop, F7 reset, F8 hide</span>';
        if (this.lastStatus !== html) {
            this.el.status.innerHTML = html;
            this.lastStatus = html;
        }
    }

    renderSpells(view) {
        const m = view.meter;
        const list = this.el.spells;
        const equipped = m.equipped || [];
        const others = (m.others || []).filter((r) => r.damage > 0 || r.casts > 0);
        const maxDamage = Math.max(1, ...equipped.map((r) => r.damage), ...others.map((r) => r.damage));
        const scan = view.scan;

        const wanted = [];
        if (equipped.length) {
            wanted.push({ group: 'Equipped' });
            for (const r of equipped) wanted.push(r);
            if (others.length) wanted.push({ group: 'Other damage' });
        }
        for (const r of others) wanted.push(r);

        if (!wanted.length) {
            const msg = scan
                ? 'Press Start (F6), then fight. Every spell that lands a hit is listed here.'
                : 'Press Start (F6), then fight. Every spell that lands a hit is listed here, with its casts and share of your damage.';
            if (!list.querySelector('.empty')) {
                list.innerHTML = '<p class="empty"></p>';
                this.rowEls.clear();
            }
            list.querySelector('.empty').textContent = msg;
            return;
        }
        const empty = list.querySelector('.empty');
        if (empty) empty.remove();

        const seen = new Set();
        let prev = null;
        for (const item of wanted) {
            const id = item.group ? 'g:' + item.group : 'r:' + item.key;
            seen.add(id);
            let node = this.rowEls.get(id);
            if (!node) {
                node = document.createElement(item.group ? 'div' : 'div');
                node.className = item.group ? 'group' : 'spell';
                this.rowEls.set(id, node);
            }
            if (item.group) {
                node.textContent = item.group;
            } else {
                this.fillSpell(node, item, maxDamage, m.totals.damage);
            }
            const next = prev ? prev.nextSibling : list.firstChild;
            if (next !== node) list.insertBefore(node, next);
            prev = node;
        }
        for (const [id, node] of this.rowEls) {
            if (!seen.has(id)) {
                node.remove();
                this.rowEls.delete(id);
            }
        }
    }

    fillSpell(node, r, maxDamage, total) {
        node.dataset.key = r.key;
        node.classList.toggle('idle', !r.damage && !r.casts);
        const fill = r.damage / maxDamage;
        const st = r.byStat;
        const sum = st.attack + st.expertise + st.unknown || 1;
        const segs =
            '<i class="atk" style="width:' + (st.attack / sum) * 100 + '%"></i>' +
            '<i class="exp" style="width:' + (st.expertise / sum) * 100 + '%"></i>' +
            '<i class="unk" style="width:' + (st.unknown / sum) * 100 + '%"></i>';
        const tip = [r.label + (r.rank ? ', rank ' + r.rank : ''), r.scaling ? 'Stats: ' + r.scaling : ''].filter(Boolean).join('\n');
        let html =
            '<div class="l1">' +
            (r.hotkey ? '<kbd>' + esc(r.hotkey) + '</kbd>' : '') +
            '<span class="name">' + esc(r.label) + '</span>' +
            (r.rank ? '<span class="rank">r' + esc(r.rank) + '</span>' : '') +
            '</div>' +
            '<div class="bar"><span class="fill" style="width:' + (fill * 100).toFixed(1) + '%">' + segs + '</span></div>' +
            '<div class="l2"><span class="dmg">' + short(r.damage) + '</span><span>' + pct(r.damage, total) + '</span><span class="casts">' + int(r.casts) + ' cast' + (r.casts === 1 ? '' : 's') + '</span></div>';
        if (this.open.has(r.key)) {
            html += '<dl class="more">' +
                '<dt>DPS</dt><dd>' + int(r.dps) + '</dd>' +
                '<dt>Hits</dt><dd>' + int(r.hits) + '</dd>' +
                '<dt>Crit rate</dt><dd>' + Math.round(r.critRate * 100) + '%</dd>' +
                '<dt>Average hit</dt><dd>' + int(r.avgHit) + '</dd>' +
                '<dt>Biggest hit</dt><dd>' + int(r.maxHit) + '</dd>' +
                (r.dotDamage ? '<dt>Over time</dt><dd>' + short(r.dotDamage) + ' (' + int(r.dotTicks) + ' ticks)</dd>' : '') +
                (r.summonDamage ? '<dt>By summons</dt><dd>' + short(r.summonDamage) + '</dd>' : '') +
                '<dt>Attack-scaled</dt><dd>' + pct(st.attack, r.damage) + '</dd>' +
                '<dt>Expertise-scaled</dt><dd>' + pct(st.expertise, r.damage) + '</dd>' +
                (r.scaling ? '<dd class="desc">' + esc(r.scaling) + '</dd>' : '') +
                '</dl>';
        }
        if (node._html !== html) {
            node.innerHTML = html;
            node._html = html;
            node.title = tip;
        }
    }
}

function boot() {
    if (!onGamePage() || document.getElementById('dbdps')) return;
    loadFonts();
    new Overlay().mount();
}

if (/^https?:$/.test(location.protocol)) {
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot, { once: true });
    } else {
        boot();
    }
}
