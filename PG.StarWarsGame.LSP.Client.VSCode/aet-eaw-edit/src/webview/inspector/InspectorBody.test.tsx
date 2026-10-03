// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';
import {renderToStaticMarkup} from 'react-dom/server';

import {InspectorBody} from './InspectorBody';
import {type InspectorSubject} from '../preview/inspectorSubject';
import {type GeometryTable} from '../../protocol/modelPreview';

const meshSubject: InspectorSubject = {
    rowId: 'part0:hull:0',
    sources: [{
        kind: 'mesh',
        name: 'hull',
        boneName: 'Root',
        vertexCount: 120,
        triangleCount: 40,
        drawn: true,
        extras: {alamoShader: 'MeshBumpColorize.fx', alamoMesh: 'hull'},
        meshIndex: 2,
        subMeshIndex: 0,
    }],
    modelDetail: null,
    modelReference: 'ev_stardestroyer',
};

const boneSubject: InspectorSubject = {
    rowId: 'part0:hp:0',
    sources: [{
        kind: 'bone', index: 3, name: 'HP_F-L', parentName: 'Root', parentIndex: 0, visible: true,
        billboard: null,
    }],
    modelDetail: null,
    modelReference: 'ev_stardestroyer',
};

function render(subject: InspectorSubject, selectedTable: GeometryTable | null): string {
    return renderToStaticMarkup(
        <InspectorBody
            subject={subject}
            selectedTable={selectedTable}
            geometry={null}
            geometryError={null}
            onGeometry={() => undefined}
            onOpenTable={() => undefined}
        />);
}

describe('InspectorBody geometry box', () => {
    it('shows the selected tab and its panel before any rows have arrived', () => {
        // The reported fault: nothing was selected and no body appeared until a tab was pressed,
        // which made the box read as a flyout rather than a tab box.
        const html = render(meshSubject, 'vertices');

        assert.match(html, /<button[^>]*role="tab"[^>]*aria-selected="true"[^>]*>Vertices/);
        assert.match(html, /role="tabpanel"/);
    });

    it('selects nothing for a row with no geometry, and keeps the tabs in view, inert', () => {
        const html = render(boneSubject, null);

        assert.ok(!html.includes('aria-selected="true"'), html);
        assert.match(html, /<button[^>]*role="tab"[^>]*disabled=""/);
    });
});
