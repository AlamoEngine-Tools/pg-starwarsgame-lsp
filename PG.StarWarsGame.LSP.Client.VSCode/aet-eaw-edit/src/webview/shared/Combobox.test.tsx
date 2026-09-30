// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {renderToStaticMarkup} from 'react-dom/server';

import {Combobox} from './Combobox';

const render = (element: React.JSX.Element): string => renderToStaticMarkup(element);

describe('Combobox', () => {
    it('is a text field with the combobox role, closed until it is focused', () => {
        const html = render(
            <Combobox value="Hoth" onChange={() => undefined} options={[{value: 'Hoth'}]} ariaLabel="Planet"/>);

        assert.match(html, /role="combobox"/);
        assert.match(html, /aria-expanded="false"/);
        assert.match(html, /aria-label="Planet"/);
        assert.match(html, /value="Hoth"/);
        assert.equal(/role="listbox"/.test(html), false, html);
    });

    /** A field that searches says so before anything is typed into it. */
    it('announces itself with an icon when asked for one', () => {
        const plain = render(<Combobox value="" onChange={() => undefined} options={[]}/>);
        const search = render(<Combobox value="" onChange={() => undefined} options={[]} icon="search"/>);

        assert.equal(/suggest-icon/.test(plain), false, plain);
        assert.match(search, /class="suggest with-icon"/);
        assert.match(search, /suggest-icon/);
    });

    /** No <datalist>: its filter by the field's own text is the bug this component replaces. */
    it('never renders a native datalist', () => {
        const html = render(<Combobox value="" onChange={() => undefined} options={[{value: 'Kuat'}]}/>);

        assert.equal(/<datalist|list="/.test(html), false, html);
    });
});
