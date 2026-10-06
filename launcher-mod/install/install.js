'use strict';

/**
 * Installs (or removes) the DPS overlay in the Dungeon Blitz: R launcher.
 *
 * Run it with the launcher's own executable in Node mode, which is what the .cmd files do:
 *   set ELECTRON_RUN_AS_NODE=1
 *   "%LOCALAPPDATA%\Programs\Dungeon Blitz R\Dungeon Blitz R.exe" install.js [--uninstall] [--app-dir <dir>]
 * Plain Node 12+ works too.
 *
 * What it changes in resources\app.asar: adds the dps\ folder and points package.json "main" at
 * dps/boot.js (the original entry is kept in "dpsOverlayOriginalMain" and still runs). Nothing
 * else in the archive is touched. The first install keeps the original as app.asar.dps-backup.
 * Run it again after a launcher update: an update replaces app.asar and so removes the overlay.
 */

process.noAsar = true;
const path = require('path');
const { execFileSync } = require('child_process');
const asar = require('./asar');
const fs = asar.fs;

function arg(name) {
    const i = process.argv.indexOf(name);
    return i >= 0 ? process.argv[i + 1] : '';
}

function fail(message, code) {
    console.error('\n' + message);
    process.exit(code || 1);
}

function launcherDir() {
    const given = arg('--app-dir');
    if (given) return path.resolve(given);
    const exeDir = path.dirname(process.execPath);
    if (fs.existsSync(path.join(exeDir, 'resources', 'app.asar'))) return exeDir;
    return path.join(process.env.LOCALAPPDATA || '', 'Programs', 'Dungeon Blitz R');
}

function launcherRunning() {
    if (process.platform !== 'win32') return [];
    try {
        const out = execFileSync('tasklist', ['/FO', 'CSV', '/NH', '/FI', 'IMAGENAME eq Dungeon Blitz R.exe'], { encoding: 'utf8', windowsHide: true });
        return out
            .split(/\r?\n/)
            .map((l) => l.split('","'))
            .filter((c) => c.length > 1 && /Dungeon Blitz R\.exe/i.test(c[0]))
            .map((c) => Number(c[1]))
            .filter((pid) => pid && pid !== process.pid && pid !== process.ppid);
    } catch (_e) {
        return [];
    }
}

function modFiles(srcDir) {
    const out = {};
    for (const name of fs.readdirSync(srcDir)) {
        const p = path.join(srcDir, name);
        if (fs.statSync(p).isFile() && /\.(js|json)$/.test(name)) {
            out['dps/' + name] = fs.readFileSync(p);
        }
    }
    if (!out['dps/boot.js'] || !out['dps/index.js'] || !out['dps/preload.js']) {
        fail("The overlay's files are missing next to the installer (" + srcDir + ').');
    }
    return out;
}

function main() {
    const uninstall = process.argv.includes('--uninstall');
    const dir = launcherDir();
    const asarPath = path.join(dir, 'resources', 'app.asar');
    const backup = asarPath + '.dps-backup';
    if (!fs.existsSync(asarPath)) {
        fail("Couldn't find the launcher at " + dir + '. Pass its folder with --app-dir.');
    }
    const running = launcherRunning();
    if (running.length && !process.argv.includes('--force')) {
        fail('Dungeon Blitz: R is running. Close the game and the launcher, then run this again.', 2);
    }

    const archive = asar.readArchive(asarPath);
    const pkg = JSON.parse(asar.readFile(archive, 'package.json').toString('utf8'));
    const installed = Boolean(pkg.dpsOverlayOriginalMain);

    if (uninstall) {
        if (!installed) {
            console.log('The DPS overlay is not installed in ' + dir + '.');
            return;
        }
        const restored = Object.assign({}, pkg, { main: pkg.dpsOverlayOriginalMain });
        delete restored.dpsOverlayOriginalMain;
        delete restored.dpsOverlayVersion;
        const changes = { 'package.json': Buffer.from(JSON.stringify(restored, null, 2) + '\n') };
        for (const f of asar.listFiles(archive.header, 'dps')) changes[f] = null;
        writeVerified(archive, changes, asarPath, (check) => {
            const p = JSON.parse(asar.readFile(check, 'package.json').toString('utf8'));
            return p.main === restored.main && !asar.entry(check.header, 'dps');
        });
        console.log('Removed the DPS overlay. The launcher is back to its original entry point (' + restored.main + ').');
        return;
    }

    const srcDir = path.resolve(__dirname, '..', 'src', 'dps');
    const files = modFiles(fs.existsSync(srcDir) ? srcDir : path.resolve(__dirname, '..', 'dps'));
    const bootVersion = (/const VERSION = '([^']+)'/.exec(files['dps/index.js'].toString('utf8')) || [])[1] || '';

    if (!installed && !fs.existsSync(backup)) {
        fs.copyFileSync(asarPath, backup);
        console.log('Kept the original as ' + backup);
    }

    const originalMain = installed ? pkg.dpsOverlayOriginalMain : pkg.main || 'main.js';
    if (!installed && !asar.entry(archive.header, originalMain)) {
        fail("The launcher's entry point " + originalMain + ' is not in app.asar; nothing was changed.');
    }
    const nextPkg = Object.assign({}, pkg, { main: 'dps/boot.js', dpsOverlayOriginalMain: originalMain, dpsOverlayVersion: bootVersion });
    const changes = Object.assign({ 'package.json': Buffer.from(JSON.stringify(nextPkg, null, 2) + '\n') }, files);
    // Files from an older overlay version that this one no longer ships.
    for (const f of asar.listFiles(archive.header, 'dps')) {
        if (!(f in changes)) changes[f] = null;
    }
    writeVerified(archive, changes, asarPath, (check) => {
        const p = JSON.parse(asar.readFile(check, 'package.json').toString('utf8'));
        return p.main === 'dps/boot.js' && asar.readFile(check, 'dps/boot.js').equals(files['dps/boot.js']) && Boolean(asar.entry(check.header, originalMain));
    });
    console.log((installed ? 'Updated' : 'Installed') + ' the DPS overlay ' + bootVersion + ' in ' + dir + '.');
    console.log('Start the launcher as usual. F6 starts or stops the timer, F7 resets it, F8 hides the panels.');
}

function checks(file, ok) {
    try {
        return ok(asar.readArchive(file));
    } catch (err) {
        console.error(err);
        return false;
    }
}

/**
 * Writes `data` over `file` in place. Used when another program has the file open, which on
 * Windows stops it being replaced by a rename but not written to. A shorter archive leaves old
 * bytes after its end if the file can't be cut short; the asar header says where the data
 * ends, so those are never read.
 */
function overwriteInPlace(file, data) {
    const fd = fs.openSync(file, 'r+');
    try {
        fs.writeSync(fd, data, 0, data.length, 0);
        try {
            fs.ftruncateSync(fd, data.length);
        } catch (_e) {
            // left as is, see above
        }
        fs.fsyncSync(fd);
    } finally {
        fs.closeSync(fd);
    }
}

function writeVerified(archive, changes, asarPath, ok) {
    const tmp = asarPath + '.dps-tmp';
    asar.writeArchive(archive, changes, tmp);
    if (!checks(tmp, ok)) {
        try {
            fs.unlinkSync(tmp);
        } catch (_e) {
            // nothing to clean
        }
        fail('The rebuilt app.asar did not check out; the launcher was left as it was.');
    }
    try {
        fs.renameSync(tmp, asarPath);
        return;
    } catch (err) {
        if (!['EPERM', 'EBUSY', 'EACCES'].includes(err.code)) throw err;
    }
    // Something else holds app.asar open (an Electron app that read it, an antivirus scan).
    console.log('app.asar is open in another program, so it is written in place.');
    const data = fs.readFileSync(tmp);
    const original = fs.readFileSync(asarPath);
    try {
        overwriteInPlace(asarPath, data);
    } catch (err) {
        fs.unlinkSync(tmp);
        fail("Couldn't write app.asar (" + err.message + '); the launcher was left as it was. Close every program that might have it open and try again.');
    }
    if (!checks(asarPath, ok)) {
        overwriteInPlace(asarPath, original);
        fs.unlinkSync(tmp);
        fail('The written app.asar did not check out, so the original was put back. Close every program that might have it open and try again.');
    }
    fs.unlinkSync(tmp);
}

main();
