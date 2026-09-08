// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { subMeshGeometryReply } from './subMeshGeometry';
import { type LspGateway } from './lspGateway';

/** A gateway that records what it was asked and answers what the test tells it to. */
function stubGateway(
    answer: { ok: true; value: unknown } | { ok: false; message: string },
): { lsp: LspGateway; asked: { method: string; params: unknown }[] } {
    const asked: { method: string; params: unknown }[] = [];

    const lsp = {
        request: async (method: string, params: unknown) => {
            asked.push({ method, params });
            return answer.ok
                ? { ok: true, value: answer.value }
                : { ok: false, reason: 'failed', message: answer.message };
        },
    } as unknown as LspGateway;

    return { lsp, asked };
}

const page = { model: 'ev_stardestroyer', table: 'vertices', offset: 0 };

describe('subMeshGeometryReply', () => {
    it('asks the server for the page the webview named', async () => {
        const { lsp, asked } = stubGateway({ ok: true, value: { page } });

        await subMeshGeometryReply(lsp, {
            type: 'requestSubMeshGeometry',
            modelReference: 'ev_stardestroyer',
            meshIndex: 2,
            subMeshIndex: 1,
            table: 'faces',
            offset: 300,
            count: 100,
        });

        assert.equal(asked.length, 1);
        assert.equal(asked[0].method, 'aet/getSubMeshGeometry');
        assert.deepEqual(asked[0].params, {
            modelReference: 'ev_stardestroyer',
            meshIndex: 2,
            subMeshIndex: 1,
            table: 'faces',
            offset: 300,
            count: 100,
        });
    });

    it('hands the answer back under the type the webview listens for', async () => {
        const { lsp } = stubGateway({ ok: true, value: { page } });

        assert.deepEqual(await subMeshGeometryReply(lsp, { type: 'requestSubMeshGeometry' }),
            { type: 'subMeshGeometry', result: { page } });
    });

    // A dead server is a fact about this request, not a reason for the tab to sit blank: the panel
    // says why in the same slot the page would have filled.
    it('reports a failure as the reply, not as nothing', async () => {
        const { lsp } = stubGateway({ ok: false, message: 'The language server is not running.' });

        assert.deepEqual(await subMeshGeometryReply(lsp, { type: 'requestSubMeshGeometry' }), {
            type: 'subMeshGeometry',
            result: { page: null, error: 'The language server is not running.' },
        });
    });

    // The message is whatever a webview posted, so every field is checked rather than trusted -
    // and the defaults have to be the ones that ask for something valid.
    it('falls back to the first page of vertices when the message says nothing', async () => {
        const { lsp, asked } = stubGateway({ ok: true, value: { page } });

        await subMeshGeometryReply(lsp, { type: 'requestSubMeshGeometry' });

        assert.deepEqual(asked[0].params, {
            modelReference: '',
            meshIndex: 0,
            subMeshIndex: 0,
            table: 'vertices',
            offset: 0,
            count: 100,
        });
    });

    // Sub-mesh 0 of mesh 0 at offset 0 is a real request, so a zero must survive the defaulting
    // that a `??` chain would let through but a `||` would silently replace.
    it('keeps zeroes, which name the first of everything', async () => {
        const { lsp, asked } = stubGateway({ ok: true, value: { page } });

        await subMeshGeometryReply(lsp, {
            type: 'requestSubMeshGeometry',
            modelReference: 'ev_victory',
            meshIndex: 0,
            subMeshIndex: 0,
            offset: 0,
        });

        assert.equal((asked[0].params as { meshIndex: number }).meshIndex, 0);
        assert.equal((asked[0].params as { subMeshIndex: number }).subMeshIndex, 0);
        assert.equal((asked[0].params as { offset: number }).offset, 0);
    });
});
