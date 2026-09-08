// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The first component test in this project, and a check that the harness can carry one.
//
// esbuild.test.js already collected .test.tsx and tsconfig.test.json already included it; nothing
// had ever been put there. `renderToStaticMarkup` is what makes it work without adding a dependency
// or a DOM: react-dom is already here, and rendering to a string touches no document, which is the
// one thing the harness comment rules out.
//
// What this can and cannot check. It sees the markup a component produces - classes, nesting, what
// is present and what is absent - which is where this dock's layout invariants actually live. It
// does not see layout, so a rule about what something LOOKS like still needs a live repro.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { renderToStaticMarkup } from 'react-dom/server';

import { DockSection } from './DockSection';

const render = (element: React.JSX.Element): string => renderToStaticMarkup(element);

describe('DockSection', () => {
    it('renders its title, its count and its children', () => {
        const html = render(
            <DockSection title="Hardpoints" count={4}><p>body</p></DockSection>);

        assert.match(html, /Hardpoints/);
        assert.match(html, /section-count[^>]*>4</);
        assert.match(html, /<p>body<\/p>/);
    });

    it('leaves the count out entirely when there is no number to report', () => {
        const html = render(<DockSection title="Scene"><p>body</p></DockSection>);

        assert.equal(/section-count/.test(html), false);
    });

    /**
     * TWO elements each carrying `margin-left: auto` split the free space between them, so a chip
     * appearing shoved the count 88 pixels left and the thing the reader was looking at moved out
     * from under their eye. One wrapper, one auto margin, everything inside it in a fixed order.
     */
    it('puts the count and any end content inside ONE title-end wrapper', () => {
        const html = render(
            <DockSection title="Bones" count={12} end={<button type="button">Add</button>}>
                <p>body</p>
            </DockSection>);

        const ends = html.match(/title-end/g) ?? [];
        assert.equal(ends.length, 1, html);

        const wrapper = /<span class="title-end">(.*?)<\/span><\/(?:div|button)>/s.exec(html);
        assert.ok(wrapper !== null, html);
        assert.match(wrapper[1], /section-count/);
        assert.match(wrapper[1], /Add/);
    });
});

describe('a foldable DockSection', () => {
    const folding = { id: 'lights', collapsed: false, onToggle: () => undefined };

    it('makes the whole heading the hit area, not just the chevron', () => {
        const html = render(<DockSection title="Lights" {...folding}><p>body</p></DockSection>);

        assert.match(html, /<button[^>]*class="dock-section-title"/);
        assert.match(html, /aria-expanded="true"/);
    });

    /**
     * Unmounted rather than hidden. A folded section has no business holding a slider that still
     * answers to the keyboard, and these are live controls bound to the viewport.
     */
    it('unmounts its body when folded, rather than hiding it', () => {
        const html = render(
            <DockSection title="Lights" {...folding} collapsed><p>body</p></DockSection>);

        assert.equal(/<p>body<\/p>/.test(html), false);
        assert.match(html, /aria-expanded="false"/);
    });

    // Icons come from Tabler behind Icon.tsx; codicons are kept only for the severity marks shared
    // with the rest of the editor. A fold chevron is not a severity mark.
    it('draws its chevron through Icon rather than as a codicon', () => {
        const html = render(<DockSection title="Lights" {...folding}><p>body</p></DockSection>);

        assert.equal(/codicon-chevron/.test(html), false, html);
        assert.match(html, /<svg/);
    });

    /**
     * A section with no fold behaviour is a heading, not a disabled button. Rendering a button that
     * does nothing would fail the reader in the other direction - it looks pressable and is not.
     */
    it('is a plain heading, not a button, when nothing was given to fold it with', () => {
        const html = render(<DockSection title="Scene"><p>body</p></DockSection>);

        assert.equal(/<button/.test(html), false);
        assert.equal(/aria-expanded/.test(html), false);
    });
});
