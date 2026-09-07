// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { renderToStaticMarkup } from 'react-dom/server';

import { Field } from './Field';

const render = (element: React.JSX.Element): string => renderToStaticMarkup(element);

describe('Field', () => {
    it('puts the label above the control it names', () => {
        const html = render(<Field label="Ground height"><input type="range" /></Field>);

        assert.match(html, /class="field"/);
        assert.match(html, /class="field-label"[^>]*>Ground height/);
        assert.match(html, /<input/);
    });

    /**
     * The readout goes to the FAR edge, the way a section title's count does.
     *
     * Without that it simply butted up against the label - "Wind speed1.0", "Light around45 deg",
     * "Speed1.00x" - and the whole settings pane read as a set of typos.
     */
    it('sends the readout to the far end of the label', () => {
        const html = render(<Field label="Wind speed" value="1.0"><input /></Field>);

        assert.match(html, /class="section-count">1\.0</);
        // Inside the label, which is the element carrying the auto margin that pushes it over.
        assert.match(html, /class="field-label".*section-count.*<\/span>/s);
    });

    /**
     * A readout that is derived rather than typed sometimes has to explain itself - which damage
     * table row a factor came from, say. The explanation belongs on the number, not on the label:
     * the label's InfoBadge describes the setting, and this describes what the number is.
     */
    it('can put a tooltip on the readout without touching the label', () => {
        const html = render(
            <Field label="Damage type" value="1.0" valueTitle="Not named by the table, so 1.0">
                <select />
            </Field>);

        assert.match(html, /class="section-count" title="Not named by the table, so 1\.0">1\.0</);
    });

    it('gives the readout no title attribute when there is nothing to explain', () => {
        const html = render(<Field label="Wind speed" value="1.0"><input /></Field>);

        assert.match(html, /<span class="section-count">1\.0<\/span>/);
    });

    it('leaves the readout out when there is no value to report', () => {
        const html = render(<Field label="Backdrop"><input /></Field>);

        assert.equal(/section-count/.test(html), false);
    });

    it('carries its explanation on the label, not under the control', () => {
        const html = render(
            <Field label="Backdrop" info="Never lit and never in the depth buffer.">
                <input />
            </Field>);

        assert.match(html, /info-badge/);
    });

    /**
     * A note is support text under the control. The warning variant has to stay the same kind of
     * text in the same place - same size, same position - or a panel jumps as soon as something is
     * off, and the sentence becomes an alert the eye deals with on every glance.
     */
    it('renders a note under the control', () => {
        const html = render(
            <Field label="Detail" note="LOD0 is the lowest fidelity."><select /></Field>);

        assert.match(html, /class="field-note">LOD0 is the lowest fidelity\.</);
    });

    it('marks a warning note as a warning and changes nothing else about it', () => {
        const plain = render(<Field label="Detail" note="Something."><select /></Field>);
        const warned = render(<Field label="Detail" note="Something." warn><select /></Field>);

        assert.match(warned, /class="field-warn">Something\.</);
        // The only difference is the class: same element, same place, same content.
        assert.equal(warned.replace('field-warn', 'field-note'), plain);
    });

    /**
     * Both shapes are already in the tree and they are not interchangeable. A <label> wrapping one
     * control means clicking its name reaches the control, which is the better default; a field
     * holding anything other than a single control - a button, a group - cannot be a label at all,
     * because a label with two controls in it belongs to neither.
     */
    it('is a div by default, which is what a field holding more than one thing needs', () => {
        const html = render(<Field label="Detail"><select /></Field>);

        assert.match(html, /^<div class="field"/);
    });

    it('can be the label for its control, so clicking the name reaches it', () => {
        const html = render(<Field label="Zoom" as="label"><input type="range" /></Field>);

        assert.match(html, /^<label class="field"/);
    });

    /**
     * The second layout of the same thing: a colour well or a checkbox sits BESIDE its name rather
     * than under it, because the control is small and a line of its own wastes the row. The note,
     * where there is one, still goes under the pair - it describes the setting, not the row.
     */
    it('can put the control beside its name instead of under it', () => {
        const html = render(
            <Field label="Shadow colour" inline note="Multiplies."><input type="color" /></Field>);

        assert.match(html, /<span class="view-row"><span class="field-label">Shadow colour<\/span>/);
        assert.match(html, /<input[^>]*><\/span><p class="field-note">/);
    });

    it('has no row wrapper when the control goes under the label', () => {
        const html = render(<Field label="Detail"><select /></Field>);

        assert.equal(/view-row/.test(html), false);
    });

    it('has no note element at all when there is nothing to say', () => {
        const html = render(<Field label="Detail"><select /></Field>);

        assert.equal(/field-note|field-warn/.test(html), false);
    });
});
