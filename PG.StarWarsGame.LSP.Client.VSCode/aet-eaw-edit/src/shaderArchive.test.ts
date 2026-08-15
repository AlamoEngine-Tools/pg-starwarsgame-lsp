// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import * as path from 'node:path';
import { describe, it } from 'node:test';

import { checkArchive, entryVerdict, extractionVerdict, sha256 } from './shaderArchive';

const TARGET = path.resolve('/tmp/aet-shaders');

describe('entryVerdict', () => {
    it('keeps a shader source, flattened to its base name', () => {
        const verdict = entryVerdict('Shaders/MeshBumpColorize.fx', TARGET);

        assert.deepEqual(verdict, { keep: true, relativePath: 'MeshBumpColorize.fx' });
    });

    it('keeps a header too, since the effects include them', () => {
        assert.equal(
            entryVerdict('Shaders/AlamoEngine.fxh', TARGET).keep, true);
    });

    it('refuses anything that is not a shader source', () => {
        // A third-party archive is being unpacked into a directory the extension later reads; the
        // only thing wanted out of it is shader text.
        for (const name of ['Shaders/readme.txt', 'Shaders/tool.exe', 'Shaders/notes.fx.bak']) {
            assert.equal(entryVerdict(name, TARGET).keep, false, name);
        }
    });

    it('refuses a directory entry', () => {
        assert.equal(entryVerdict('Shaders/', TARGET).keep, false);
        assert.equal(entryVerdict('', TARGET).keep, false);
    });

    it('refuses an absolute path, in both spellings', () => {
        assert.equal(entryVerdict('/etc/evil.fx', TARGET).keep, false);
        assert.equal(entryVerdict('C:/Windows/evil.fx', TARGET).keep, false);
    });

    it('refuses a traversal, however it is spelled', () => {
        // ZIP SLIP: an entry names its own path, so nothing stops it being `../../`. Checking the
        // RESOLVED path is what catches `a/../../b` and backslash separators, which a scan for
        // ".." in the text does not.
        for (const name of [
            '../evil.fx',
            '../../evil.fx',
            'Shaders/../../evil.fx',
            'Shaders/a/../../../evil.fx',
            '..\\evil.fx',
            'Shaders\\..\\..\\evil.fx',
        ]) {
            const verdict = entryVerdict(name, TARGET);

            // Flattening to the base name is what makes these safe; either refusing them or
            // reducing them to a plain leaf inside the target is acceptable, escaping is not.
            if (verdict.keep) {
                const destination = path.resolve(TARGET, verdict.relativePath);
                assert.ok(destination.startsWith(path.resolve(TARGET) + path.sep),
                    `${name} escaped to ${destination}`);
                assert.equal(path.basename(destination), verdict.relativePath);
            }
        }
    });

    it('never lets an entry write outside the target, whatever it is called', () => {
        const nasty = 'Shaders/' + '../'.repeat(12) + 'etc/evil.fx';
        const verdict = entryVerdict(nasty, TARGET);

        if (verdict.keep) {
            assert.ok(path.resolve(TARGET, verdict.relativePath)
                .startsWith(path.resolve(TARGET) + path.sep));
        }
    });
});

describe('checkArchive', () => {
    const bytes = Buffer.from('some archive bytes');
    const digest = createHash('sha256').update(bytes).digest('hex');

    it('accepts the archive that was pinned', () => {
        assert.deepEqual(checkArchive(bytes, digest), { ok: true });
    });

    it('is not case-sensitive about the pinned hex', () => {
        assert.equal(checkArchive(bytes, digest.toUpperCase()).ok, true);
    });

    it('rejects a mismatch, naming both digests', () => {
        // The URL is public and unauthenticated, so this is the only control that matters.
        const result = checkArchive(bytes, '0'.repeat(64));

        assert.equal(result.ok, false);
        assert.match((result as { problem: string }).problem, /checksum did not match/);
        assert.match((result as { problem: string }).problem, new RegExp(digest));
    });

    it('rejects an empty download rather than reporting a hash of nothing', () => {
        const result = checkArchive(new Uint8Array(), digest);

        assert.equal(result.ok, false);
        assert.match((result as { problem: string }).problem, /empty/);
    });

    it('rejects a wrong length before hashing, when one is known', () => {
        const result = checkArchive(bytes, digest, 190933);

        assert.equal(result.ok, false);
        assert.match((result as { problem: string }).problem, /190933 bytes/);
    });
});

describe('sha256', () => {
    it('produces lower-case hex, the form the pin is written in', () => {
        assert.match(sha256(Buffer.from('x')), /^[0-9a-f]{64}$/);
    });
});

describe('extractionVerdict', () => {
    it('accepts an extraction that produced shaders', () => {
        assert.deepEqual(extractionVerdict(122), { ok: true });
    });

    it('rejects one that produced none', () => {
        // A directory configured but empty is a worse place to debug from than never having run
        // the download at all.
        const result = extractionVerdict(0);

        assert.equal(result.ok, false);
        assert.match((result as { problem: string }).problem, /no \.fx files/);
    });
});
