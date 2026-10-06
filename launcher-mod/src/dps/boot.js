'use strict';

/**
 * Entry point of the modded launcher (package.json "main"). Sets up the DPS overlay, then hands
 * over to the launcher's own main script, which runs unchanged. Anything the overlay throws is
 * logged and leaves the launcher exactly as it was without the mod.
 */

const path = require('path');

try {
    const { DpsOverlay } = require('./index');
    const overlay = new DpsOverlay({ appRoot: path.resolve(__dirname, '..') });
    overlay.prepare();
    global.__dbDpsOverlay = overlay;
} catch (err) {
    console.error('[DPS] Overlay disabled: ' + ((err && err.stack) || err));
}

const pkg = require('../package.json');
require(path.join(__dirname, '..', pkg.dpsOverlayOriginalMain || 'main.js'));
