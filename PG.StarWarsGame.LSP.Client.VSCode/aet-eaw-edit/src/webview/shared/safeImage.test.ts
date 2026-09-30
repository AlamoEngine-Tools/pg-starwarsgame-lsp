// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {safeImageSource} from './safeImage';

describe('safeImageSource', () => {
    // The contract the server actually produces - see GetEncyclopediaEntryHandler, which builds
    // "data:image/png;base64," + Convert.ToBase64String(png). Nothing else is ever legitimate,
    // so anything else is refused rather than sanitised.
    it('passes the data:image URI the server produces', () => {
        const uri = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==';

        assert.equal(safeImageSource(uri), uri);
    });

    it('passes other image subtypes', () => {
        for (const type of ['png', 'jpeg', 'gif', 'webp', 'bmp']) {
            const uri = `data:image/${type};base64,AAAA`;
            assert.equal(safeImageSource(uri), uri);
        }
    });

    // The alert this exists for. A webview renders values that originate in workspace files, so a
    // string that reached an <img src> unchecked could point anywhere - which is a network callback
    // the author never asked for, and it fires merely by opening a file.
    it('refuses an http URL', () => {
        assert.equal(safeImageSource('http://example.invalid/pixel.png'), undefined);
        assert.equal(safeImageSource('https://example.invalid/pixel.png'), undefined);
    });

    it('refuses a javascript: URL', () => {
        assert.equal(safeImageSource('javascript:alert(1)'), undefined);
        // Case and whitespace are not a way around it.
        assert.equal(safeImageSource('  JaVaScRiPt:alert(1)'), undefined);
    });

    it('refuses a data URI that is not an image', () => {
        assert.equal(safeImageSource('data:text/html;base64,PHNjcmlwdD4='), undefined);
        assert.equal(safeImageSource('data:image/svg+xml;base64,PHN2Zz4='), undefined,
            'SVG can carry script, so it is not in the allowed set');
    });

    it('refuses a data:image URI that is not base64', () => {
        // `data:image/png,<raw>` is legal syntax but never what the server emits, and the raw
        // form can carry characters the base64 form cannot.
        assert.equal(safeImageSource('data:image/png,%3Cscript%3E'), undefined);
    });

    it('refuses payload characters outside the base64 alphabet', () => {
        assert.equal(safeImageSource('data:image/png;base64,AAA"onerror="alert(1)'), undefined);
    });

    // The string that was CHECKED must be the string that is handed on. Returning the untrimmed
    // original while validating the trimmed copy is a gap in its own right, and it is also what
    // stopped a taint analyser following the guard - it saw the check applied to one value and a
    // different one reach the sink.
    it('returns the value it actually validated', () => {
        assert.equal(
            safeImageSource('  data:image/png;base64,AAAA  '),
            'data:image/png;base64,AAAA');
    });

    it('refuses null, undefined and empty', () => {
        assert.equal(safeImageSource(null), undefined);
        assert.equal(safeImageSource(undefined), undefined);
        assert.equal(safeImageSource(''), undefined);
    });
});
