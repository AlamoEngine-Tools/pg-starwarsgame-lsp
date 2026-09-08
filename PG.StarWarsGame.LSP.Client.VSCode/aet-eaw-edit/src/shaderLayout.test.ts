// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { after, describe, it } from 'node:test';

import { hasShaderSources } from './shaderLayout';

const roots: string[] = [];

/** A throwaway folder holding the given files, relative to it. */
function folderWith(...files: string[]): string {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'aet-shaders-'));
    roots.push(root);

    for (const file of files) {
        const full = path.join(root, file);
        fs.mkdirSync(path.dirname(full), { recursive: true });
        fs.writeFileSync(full, '// not a real shader');
    }

    return root;
}

after(() => {
    for (const root of roots) {
        fs.rmSync(root, { recursive: true, force: true });
    }
});

describe('hasShaderSources', () => {
    it('accepts the contents extracted directly', () => {
        assert.equal(hasShaderSources(folderWith('MeshBump.fx')), true);
    });

    it('accepts the archive extracted as-is, keeping its Shaders folder', () => {
        // Both layouts are correct answers to "extract this archive", so rejecting one would tell
        // half of users their perfectly good folder is wrong.
        assert.equal(hasShaderSources(folderWith(path.join('Shaders', 'MeshBump.fx'))), true);
    });

    it('rejects a folder with no shaders in it', () => {
        assert.equal(hasShaderSources(folderWith('readme.txt')), false);
    });

    it('does not go hunting deeper than one level', () => {
        // A folder whose shaders are buried three deep is more likely the wrong folder than a
        // layout worth supporting, and searching a whole drive for .fx files is not a good default.
        assert.equal(
            hasShaderSources(folderWith(path.join('a', 'b', 'c', 'MeshBump.fx'))), false);
    });

    it('matches the extension case-insensitively', () => {
        assert.equal(hasShaderSources(folderWith('MESHBUMP.FX')), true);
    });

    it('handles a folder that is not there without throwing', () => {
        assert.equal(hasShaderSources(path.join(os.tmpdir(), 'aet-shaders-absent-xyz')), false);
    });

    it('treats nothing configured as nothing found', () => {
        assert.equal(hasShaderSources(undefined), false);
        assert.equal(hasShaderSources('   '), false);
    });
});
