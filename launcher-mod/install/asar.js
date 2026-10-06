'use strict';

/**
 * Minimal Electron .asar reader/writer (no dependencies).
 *
 * Layout: a Pickle holding the header size, a Pickle holding the header JSON string, then the
 * file data. Each file entry has { size, offset } with offset (a decimal string) relative to the
 * start of the data block; "unpacked" entries live in app.asar.unpacked instead.
 */

let fs;
try {
    fs = require('original-fs'); // inside Electron: plain file access, no asar redirection
} catch (_e) {
    fs = require('fs');
}

function align4(n) {
    return n + ((4 - (n % 4)) % 4);
}

function readArchive(file) {
    const buf = fs.readFileSync(file);
    const sizePickle = buf.readUInt32LE(4); // header pickle total size
    const jsonLen = buf.readUInt32LE(12);
    const json = buf.toString('utf8', 16, 16 + jsonLen);
    const header = JSON.parse(json);
    const dataStart = 8 + sizePickle;
    return { buf, header, dataStart, data: buf.subarray(dataStart) };
}

function entry(header, p) {
    let node = header;
    for (const part of p.split('/').filter(Boolean)) {
        if (!node.files || !node.files[part]) return null;
        node = node.files[part];
    }
    return node;
}

function readFile(archive, p) {
    const e = entry(archive.header, p);
    if (!e || e.files || e.unpacked || e.link) {
        throw new Error('not a packed file in the archive: ' + p);
    }
    const off = Number(e.offset);
    return archive.data.subarray(off, off + e.size);
}

/**
 * Writes `archive` with `changes` applied: { 'path/in/archive': Buffer | null }. A Buffer adds or
 * replaces that file (its bytes are appended after the original data); null removes the entry.
 * Original file data is copied untouched, so every untouched entry keeps its offset.
 */
function writeArchive(archive, changes, outFile) {
    const header = JSON.parse(JSON.stringify(archive.header));
    const extra = [];
    let offset = archive.data.length;
    for (const [p, content] of Object.entries(changes)) {
        const parts = p.split('/').filter(Boolean);
        let node = header;
        for (let i = 0; i < parts.length - 1; i++) {
            node.files = node.files || {};
            if (!node.files[parts[i]] || !node.files[parts[i]].files) {
                node.files[parts[i]] = { files: {} };
            }
            node = node.files[parts[i]];
        }
        const name = parts[parts.length - 1];
        node.files = node.files || {};
        if (content === null) {
            delete node.files[name];
            continue;
        }
        node.files[name] = { size: content.length, offset: String(offset) };
        extra.push(content);
        offset += content.length;
    }
    // Drop now-empty directories left by removals.
    (function prune(n) {
        if (!n.files) return;
        for (const k of Object.keys(n.files)) {
            const c = n.files[k];
            if (c.files) {
                prune(c);
                if (!Object.keys(c.files).length) delete n.files[k];
            }
        }
    })(header);

    const json = Buffer.from(JSON.stringify(header), 'utf8');
    const headerPayload = 4 + align4(json.length); // u32 string length + padded string
    const headerPickle = 4 + headerPayload; // u32 payload size + payload
    const head = Buffer.alloc(8 + headerPickle);
    head.writeUInt32LE(4, 0); // size pickle payload: one u32
    head.writeUInt32LE(headerPickle, 4);
    head.writeUInt32LE(headerPayload, 8);
    head.writeUInt32LE(json.length, 12);
    json.copy(head, 16);
    const out = Buffer.concat([head, archive.data].concat(extra));
    fs.writeFileSync(outFile, out);
    return out.length;
}

/** Every packed file path under `dir` in the archive ('' for the root). */
function listFiles(header, dir) {
    const out = [];
    const start = dir ? entry(header, dir) : header;
    (function walk(node, prefix) {
        if (!node || !node.files) return;
        for (const [k, v] of Object.entries(node.files)) {
            const p = prefix ? prefix + '/' + k : k;
            if (v.files) walk(v, p);
            else out.push(p);
        }
    })(start, dir || '');
    return out;
}

module.exports = { readArchive, writeArchive, readFile, entry, listFiles, fs };
