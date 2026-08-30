// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One page of a sub-mesh's bulk geometry, fetched for whichever panel asked.
//
// Shared because two panels ask now: the model preview, which has always had the tables, and the
// inspector tab, which is where they are actually read. A second copy in the second panel is the
// exact drift `WebviewPanelHost` was written to undo - and this one would have drifted silently,
// since a wrong default here shows up as an empty table rather than as an error.
//
// Free of `vscode` on purpose: it needs the gateway and nothing else, which is what lets it be
// tested at all.

import { type LspGateway } from './lspGateway';
import { type GetSubMeshGeometryResult } from '../protocol/modelPreview';

/** A message from a webview: a tagged bag whose fields are checked rather than trusted. */
interface GeometryRequest {
    [key: string]: unknown;
}

/**
 * Fetches the page a webview asked for, as the message to post back.
 *
 * Returns the reply rather than posting it, so the caller stays the one thing that knows about its
 * own webview - and so this can be exercised without one.
 */
export async function subMeshGeometryReply(
    lsp: LspGateway, message: GeometryRequest,
): Promise<{ type: 'subMeshGeometry'; result: GetSubMeshGeometryResult }> {
    const result = await lsp.request<GetSubMeshGeometryResult>(
        'aet/getSubMeshGeometry', {
            modelReference: String(message.modelReference ?? ''),

            // Number(x ?? d) rather than (x || d): mesh 0, sub-mesh 0 and offset 0 are the first of
            // each and the commonest request there is, and a falsy default would rewrite every one
            // of them into something else.
            meshIndex: Number(message.meshIndex ?? 0),
            subMeshIndex: Number(message.subMeshIndex ?? 0),
            table: String(message.table ?? 'vertices'),
            offset: Number(message.offset ?? 0),
            count: Number(message.count ?? 100),
        });

    return {
        type: 'subMeshGeometry',

        // A failure travels in the same slot the page would have: the panel that asked has one
        // place to look, and a dead server reads as a stated reason instead of a table that never
        // arrives.
        result: result.ok ? result.value : { page: null, error: result.message },
    };
}
