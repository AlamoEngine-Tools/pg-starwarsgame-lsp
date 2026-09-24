// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {renderToStaticMarkup} from 'react-dom/server';

import {Modal} from './Modal';

const noop = (): void => undefined;

describe('Modal', () => {
    it('is a modal dialog with Cancel and the confirming button', () => {
        const html = renderToStaticMarkup(
            <Modal title="Set flag" confirmLabel="OK" onConfirm={noop} onCancel={noop}>body</Modal>);

        assert.match(html, /role="dialog"/);
        assert.match(html, /aria-modal="true"/);
        assert.match(html, />Cancel</);
        assert.match(html, />OK</);
    });

    it('disables the confirming button while the form cannot be confirmed, saying why', () => {
        const html = renderToStaticMarkup(
            <Modal title="Set flag" confirmLabel="OK" canConfirm={false} disabledReason="Name required"
                   onConfirm={noop} onCancel={noop}>body</Modal>);

        assert.match(html, /disabled=""/);
        assert.match(html, /Name required/);
    });

    /** Answers that act at once share the button row, apart from Cancel and the confirming button. */
    it('puts secondary actions on the button row, before Cancel', () => {
        const html = renderToStaticMarkup(
            <Modal title="T" confirmLabel="OK" onConfirm={noop} onCancel={noop}
                   actions={<button type="button">Fire</button>}>body</Modal>);

        assert.match(html, /class="modal-actions"><button type="button">Fire<\/button><\/div>.*>Cancel</);
    });

    /** A dialog that shows and acts, with nothing to confirm, closes with one button. */
    it('offers only Close when there is nothing to confirm', () => {
        const html = renderToStaticMarkup(<Modal title="Event" onCancel={noop}>body</Modal>);

        assert.match(html, />Close</);
        assert.equal(/>Cancel<|>OK</.test(html), false, html);
    });

    /** A short form has nothing to resize, and its dropdown must be free to hang below the body. */
    it('offers no resize handles on a form dialog', () => {
        const resizable = renderToStaticMarkup(
            <Modal title="T" confirmLabel="OK" onConfirm={noop} onCancel={noop}>body</Modal>);
        const form = renderToStaticMarkup(
            <Modal title="T" confirmLabel="OK" form onConfirm={noop} onCancel={noop}>body</Modal>);

        assert.match(resizable, /modal-resize/);
        assert.equal(/modal-resize/.test(form), false, form);
        assert.match(form, /class="modal modal-form"/);
    });
});
