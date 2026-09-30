// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {renderToStaticMarkup} from 'react-dom/server';

import {IntegerField} from './IntegerField';

describe('IntegerField', () => {
    /** Not a native number input: its spinner is unthemed, and it accepts 1.5 and 1e3. */
    it('is a themed text field with decrement and increment buttons', () => {
        const html = renderToStaticMarkup(<IntegerField value="2" onChange={() => undefined} ariaLabel="Value"/>);

        assert.equal(/type="number"/.test(html), false, html);
        assert.match(html, /inputMode="numeric"|inputmode="numeric"/);
        assert.match(html, /aria-label="Value"/);
        assert.match(html, /title="Decrease"/);
        assert.match(html, /title="Increase"/);
    });

    /** Decrementing is not deleting: the glyph is a minus, never the trash can "remove" names. */
    it('decrements with a minus, not a trash can', () => {
        const html = renderToStaticMarkup(<IntegerField value="2" onChange={() => undefined}/>);

        assert.match(html, /tabler-icon-minus/);
        assert.equal(/tabler-icon-trash/.test(html), false, html);
    });

    it('marks a value that is not a whole number as invalid', () => {
        const html = renderToStaticMarkup(<IntegerField value="1.5" onChange={() => undefined}/>);

        assert.match(html, /aria-invalid="true"/);
    });
});
