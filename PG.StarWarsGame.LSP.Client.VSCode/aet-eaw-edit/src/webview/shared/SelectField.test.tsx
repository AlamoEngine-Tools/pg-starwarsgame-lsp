// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {renderToStaticMarkup} from 'react-dom/server';

import {SelectField} from './SelectField';

const OPTIONS = [{value: '', label: 'Any lifecycle'}, {value: 'Armed'}, {value: 'Fired'}];

describe('SelectField', () => {
    /** The smart field in place of a native select: a combobox showing the chosen option's label. */
    it('is a combobox that shows the chosen option by its label', () => {
        const html = renderToStaticMarkup(
            <SelectField value="" options={OPTIONS} onChange={() => undefined} ariaLabel="Lifecycle"/>);

        assert.match(html, /role="combobox"/);
        assert.match(html, /value="Any lifecycle"/);
        assert.equal(/<select/.test(html), false, html);
    });

    it('shows a value that has no label as itself', () => {
        const html = renderToStaticMarkup(
            <SelectField value="Fired" options={OPTIONS} onChange={() => undefined} ariaLabel="Lifecycle"/>);

        assert.match(html, /value="Fired"/);
    });
});
