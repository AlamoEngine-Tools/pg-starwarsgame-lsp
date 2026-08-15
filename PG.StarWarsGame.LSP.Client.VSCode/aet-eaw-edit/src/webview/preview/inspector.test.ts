// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    formatParameter,
    inspectPanel,
    inspectPanels,
    geometryTable,
    type BoneInspection,
    type InspectorPanel,
    type MeshInspection,
    type ParticleInspection,
} from './inspector';
import { type ModelDetail, type SubMeshGeometryPage } from '../../protocol/modelPreview';

const mesh = (over: Partial<MeshInspection> = {}): MeshInspection => ({
    kind: 'mesh',
    name: 'hull',
    boneName: 'Root',
    vertexCount: 120,
    triangleCount: 40,
    drawn: true,
    extras: { alamoShader: 'MeshBumpColorize.fx', alamoMesh: 'hull' },
    ...over,
});

const bone = (over: Partial<BoneInspection> = {}): BoneInspection => ({
    kind: 'bone',
    index: 3,
    name: 'HP_F-L',
    parentName: 'Root',
    parentIndex: 0,
    visible: true,
    billboard: null,
    ...over,
});

const rowsOf = (panel: InspectorPanel, title: string): Record<string, string> => {
    const section = panel.sections.find(s => s.title === title);
    assert.ok(section !== undefined, `no section called ${title} in ${panel.sections.map(s => s.title).join(', ')}`);

    return Object.fromEntries(section.rows.map(row => [row.label, row.value]));
};

describe('formatParameter', () => {
    it('formats a float without trailing noise', () => {
        // The exporter writes a C# float widened to a JSON double, so 0.3f arrives as
        // 0.30000001192092896. Printing that verbatim makes an author think their value is wrong.
        assert.equal(formatParameter('Shininess', 0.30000001192092896).value, '0.3');
        assert.equal(formatParameter('Shininess', 32).value, '32');
    });

    it('formats a vector as its components, not as JSON', () => {
        const formatted = formatParameter('UVOffset', [0.5, 0.25]);

        assert.equal(formatted.value, '0.5, 0.25');
        assert.equal(formatted.kind, 'vector');
    });

    it('reads a colour parameter as a colour and gives it a swatch', () => {
        const formatted = formatParameter('Colorization', [1, 0, 0, 0.5]);

        assert.equal(formatted.kind, 'colour');
        assert.equal(formatted.swatch, '#ff0000');
        // The alpha still has to be readable - a collision hull's 0.5 is the whole point of it.
        assert.equal(formatted.value, '1, 0, 0, 0.5');
    });

    it('decides colour by NAME, never by shape', () => {
        // A Float3 is just as likely to be an offset or a scale, and a swatch on one of those is a
        // lie the reader cannot check.
        assert.equal(formatParameter('UVScroll', [1, 0, 0]).kind, 'vector');
        assert.equal(formatParameter('DebugColor', [0, 1, 1, 1]).kind, 'colour');
        assert.equal(formatParameter('Colour', [0, 1, 1, 1]).kind, 'colour');
    });

    it('treats a string value as a texture', () => {
        const formatted = formatParameter('BaseTexture', 'i_hull.tga');

        assert.equal(formatted.kind, 'texture');
        assert.equal(formatted.value, 'i_hull.tga');
    });

    it('says so rather than throwing on a value it cannot read', () => {
        // A mod's own exporter can write anything here. An inspector that dies takes the panel with
        // it, and a shrug in one row costs only that row.
        const formatted = formatParameter('Weird', { nested: true } as unknown);

        assert.equal(formatted.kind, 'text');
        assert.notEqual(formatted.value, '');
    });
});

describe('inspectPanel for a mesh', () => {
    it('names the mesh and the bone it rides', () => {
        const rows = rowsOf(inspectPanel(mesh()), 'Mesh');

        assert.equal(rows.Name, 'hull');
        assert.equal(rows['Attached to bone'], 'Root');
    });

    it('reports geometry counts', () => {
        const rows = rowsOf(inspectPanel(mesh()), 'Mesh');

        assert.equal(rows.Triangles, '40');
        assert.equal(rows.Vertices, '120');
    });

    it('carries the shader, vertex format and skinning', () => {
        const panel = inspectPanel(mesh({
            extras: {
                alamoShader: 'MeshBumpColorize.fx',
                alamoVertexFormat: 'alD3dVertNU2U3U3',
                alamoSkinning: 'None',
            },
        }));

        const rows = rowsOf(panel, 'Material');

        assert.equal(rows.Shader, 'MeshBumpColorize.fx');
        assert.equal(rows['Vertex format'], 'alD3dVertNU2U3U3');
        assert.equal(rows.Skinning, 'None');
    });

    it('lists every shader parameter in the order the file declares them', () => {
        const panel = inspectPanel(mesh({
            extras: {
                alamoShader: 'MeshBump.fx',
                'param:BaseTexture': 'i_hull.tga',
                'param:Shininess': 32,
                'param:Colorization': [1, 1, 1, 1],
            },
        }));

        const section = panel.sections.find(s => s.title === 'Shader parameters');

        assert.deepEqual(section?.rows.map(row => row.label),
            ['BaseTexture', 'Shininess', 'Colorization']);
    });

    it('keeps the parameter section and says why when there are none', () => {
        // Disable, don't hide: a sub-mesh with no parameters is a finding, and a section that
        // vanishes leaves the reader wondering whether the panel is broken.
        const panel = inspectPanel(mesh({ extras: { alamoShader: 'alDefault.fx' } }));
        const section = panel.sections.find(s => s.title === 'Shader parameters');

        assert.equal(section?.rows.length, 0);
        assert.ok((section?.note ?? '').length > 0);
    });

    it('never shows an alamo key as if it were a parameter', () => {
        // The namespace exists precisely because a mod may name a parameter `alamoShader`.
        const panel = inspectPanel(mesh({
            extras: { alamoShader: 'MeshBump.fx', alamoMesh: 'hull', 'param:alamoShader': 'x.tga' },
        }));

        const section = panel.sections.find(s => s.title === 'Shader parameters');

        assert.deepEqual(section?.rows.map(row => row.label), ['alamoShader']);
    });

    it('reports the ALT and LOD tags when the mesh carries them', () => {
        const rows = rowsOf(inspectPanel(mesh({
            extras: { alamoShader: 'a.fx', alamoAlt: 2, alamoLod: 1 },
        })), 'Mesh');

        assert.equal(rows['Damage state'], '2');
        assert.equal(rows.Detail, '1');
    });

    it('says a mesh is untagged rather than pretending it is level 0', () => {
        // "No ALT declared" and "ALT 0" are different statements about the file.
        const rows = rowsOf(inspectPanel(mesh()), 'Mesh');

        assert.equal(rows['Damage state'], 'none');
        assert.equal(rows.Detail, 'none');
    });

    it('explains a mesh that is not drawn', () => {
        const hiddenInFile = inspectPanel(mesh({
            drawn: false,
            extras: { alamoShader: 'a.fx', alamoHidden: true },
        }));

        assert.match(rowsOf(hiddenInFile, 'Mesh').Drawn, /hidden in the file/i);
    });

    it('names the archetype for geometry the engine never shows', () => {
        const panel = inspectPanel(mesh({
            extras: { alamoShader: 'MeshCollision.fx', alamoMesh: 'COLLISION' },
        }));

        assert.equal(rowsOf(panel, 'Material').Archetype, 'collision');
    });

    it('reports collidable from the file, not from the archetype', () => {
        // `IsCollidable` means "takes part in collision", which most visible geometry does - it is
        // not a synonym for being a collision hull.
        const rows = rowsOf(inspectPanel(mesh({
            extras: { alamoShader: 'MeshBump.fx', alamoCollidable: true },
        })), 'Mesh');

        assert.equal(rows.Collidable, 'yes');
    });
});

describe('inspectPanel for a bone', () => {
    it('names the bone, its index and its parent', () => {
        const rows = rowsOf(inspectPanel(bone()), 'Bone');

        assert.equal(rows.Name, 'HP_F-L');
        assert.equal(rows.Index, '3');
        assert.equal(rows.Parent, 'Root (0)');
    });

    it('says a root has no parent instead of printing -1', () => {
        const rows = rowsOf(inspectPanel(bone({ parentIndex: -1, parentName: '' })), 'Bone');

        assert.equal(rows.Parent, 'none (root)');
    });

    it('reports a billboard mode only when the bone declares one', () => {
        assert.equal(rowsOf(inspectPanel(bone()), 'Bone').Billboard, undefined);
        assert.equal(rowsOf(inspectPanel(bone({ billboard: 'Parallel' })), 'Bone').Billboard,
            'Parallel');
    });
});

describe('inspectPanel for a particle system', () => {
    it('names the system, its emitters and the bone it hangs off', () => {
        const source: ParticleInspection = {
            kind: 'particle',
            name: 'p_hp_imperial_damage',
            boneName: 'HP_F-L_EmitDamage',
            emitterCount: 4,
            playing: false,
        };

        const rows = rowsOf(inspectPanel(source), 'Particle system');

        assert.equal(rows.Name, 'p_hp_imperial_damage');
        assert.equal(rows.Emitters, '4');
        assert.equal(rows['Attached to bone'], 'HP_F-L_EmitDamage');
    });
});

describe('inspectPanels', () => {
    it('shows a merged bone/mesh row as one panel carrying both', () => {
        // The ALO gives a rigid sub-mesh the same name as the bone that is its origin, and the tree
        // draws them as ONE row. Selecting it must not have to choose which half to describe.
        const panel = inspectPanels([mesh({ name: 'engine_glow' }), bone({ name: 'engine_glow' })]);

        assert.deepEqual(panel.sections.map(s => s.title),
            ['Mesh', 'Material', 'Shader parameters', 'Bone']);
    });

    it('takes its title from the first source', () => {
        assert.equal(inspectPanels([mesh({ name: 'hull' }), bone()]).title, 'hull');
    });

    it('says so rather than going blank when nothing is selected', () => {
        const panel = inspectPanels([]);

        assert.equal(panel.sections.length, 0);
        assert.ok(panel.title.length > 0);
    });
});

const detail = (over: Partial<ModelDetail> = {}): ModelDetail => ({
    model: 'Ev_test',
    bones: [{
        index: 3,
        name: 'HP_F-L',
        parentIndex: 0,
        visible: true,
        billboard: 'Disable',
        relativeTransform: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 20, 30, 1],
        absoluteTransform: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 20, 40, 1],
    }],
    meshes: [{
        index: 7,
        name: 'hull',
        boneIndex: 3,
        visible: true,
        collidable: true,
        alt: null,
        lod: null,
        boundsMin: [-1, -2, -3],
        boundsMax: [4, 5, 6],
        subMeshCount: 2,
        vertexCount: 120,
        triangleCount: 40,
    }],
    proxies: [],
    lightCount: 0,
    ...over,
});

describe('inspectPanels with the server detail', () => {
    it('adds the bounding box the FILE stores', () => {
        const panel = inspectPanels([mesh({ meshIndex: 7 })], detail());
        const rows = rowsOf(panel, 'Mesh');

        assert.equal(rows['Bounds min'], '-1, -2, -3');
        assert.equal(rows['Bounds max'], '4, 5, 6');
    });

    it('joins meshes on the INDEX, never the name', () => {
        // Nothing stops a model carrying two meshes with one name, and a name join would show one
        // mesh's box against the other - silently, and in the author's own units.
        const twins = detail({
            meshes: [
                { ...detail().meshes[0], index: 7, name: 'hull', boundsMin: [-1, -2, -3] },
                { ...detail().meshes[0], index: 8, name: 'hull', boundsMin: [-9, -9, -9] },
            ],
        });

        const rows = rowsOf(inspectPanels([mesh({ meshIndex: 8 })], twins), 'Mesh');

        assert.equal(rows['Bounds min'], '-9, -9, -9');
    });

    it('leaves the bounds out rather than guessing when the mesh is not in the detail', () => {
        const rows = rowsOf(inspectPanels([mesh({ meshIndex: 99 })], detail()), 'Mesh');

        assert.equal(rows['Bounds min'], undefined);
    });

    it('shows both bone transforms as four rows of four', () => {
        const rows = rowsOf(inspectPanels([bone({ index: 3 })], detail()), 'Bone');

        assert.equal(rows['Relative transform'],
            '1, 0, 0, 0\n0, 1, 0, 0\n0, 0, 1, 0\n10, 20, 30, 1');
        assert.match(rows['Absolute transform'], /10, 20, 40, 1$/);
    });

    it('describes a bone that is a proxy', () => {
        const withProxy = detail({
            proxies: [{
                name: 'p_smoke_small',
                boneIndex: 3,
                visible: true,
                altDecreaseStayHidden: true,
                alt: 2,
                lod: null,
            }],
        });

        const rows = rowsOf(inspectPanels([bone({ index: 3 })], withProxy), 'Proxy');

        assert.equal(rows.Name, 'p_smoke_small');
        assert.equal(rows['Damage state'], '2');
        // The flag exists nowhere else in the client - the exporter writes no proxies into the glTF.
        assert.equal(rows['Stays hidden when repaired'], 'yes');
    });

    it('gives a bone that is not a proxy no proxy section', () => {
        const panel = inspectPanels([bone({ index: 3 })], detail());

        assert.equal(panel.sections.find(s => s.title === 'Proxy'), undefined);
    });

    it('still describes what it can when no detail has arrived', () => {
        // The reply is a round trip behind the geometry, and a panel that stays blank until it lands
        // reads as broken rather than as loading.
        const rows = rowsOf(inspectPanels([mesh()], undefined), 'Mesh');

        assert.equal(rows.Name, 'hull');
        assert.equal(rows['Bounds min'], undefined);
    });
});

describe('billboard reporting', () => {
    it('prefers the file value the server sends over the one the exporter left in the glTF', () => {
        // Both sources carry it, and the server's is the file's own. The client's exists only
        // because the exporter writes it into a node's extras, and only when it is not Disable.
        const withBillboard = detail({
            bones: [{ ...detail().bones[0], index: 3, billboard: 'ZAxisView' }],
        });

        const rows = rowsOf(inspectPanels([bone({ index: 3, billboard: null })], withBillboard),
            'Bone');

        assert.equal(rows.Billboard, 'ZAxisView');
    });

    it('stays quiet about the Disable that almost every bone declares', () => {
        // A row saying "Disable" on all 58 bones of a Star Destroyer is noise to read past.
        const rows = rowsOf(inspectPanels([bone({ index: 3, billboard: null })], detail()), 'Bone');

        assert.equal(rows.Billboard, undefined);
    });
});

describe('geometryTable', () => {
    const page = (over: Partial<SubMeshGeometryPage> = {}): SubMeshGeometryPage => ({
        model: 'Ev_test',
        meshIndex: 0,
        subMeshIndex: 0,
        table: 'vertices',
        offset: 0,
        totalVertices: 3,
        totalFaces: 1,
        vertices: [],
        faces: [],
        boneMapping: [],
        ...over,
    });

    const vertex = {
        index: 4,
        position: [1, 2.5, 3],
        normal: [0, 0, 1],
        texCoord0: [0.5, 0.25],
        texCoord1: [0, 0],
        tangent: [0, 0, 0],
        binormal: [0, 0, 0],
        color: [1, 1, 1, 1],
        boneIndices: [0, 1, 0, 0],
        boneWeights: [1, 0, 0, 0],
    };

    it('lays vertices out one column per channel', () => {
        const table = geometryTable(page({ vertices: [vertex] }));

        assert.deepEqual(table.columns,
            ['#', 'Position', 'Normal', 'UV0', 'UV1', 'Tangent', 'Binormal', 'Colour',
                'Bone indices', 'Bone weights']);
        assert.equal(table.rows[0][0], '4');
        assert.equal(table.rows[0][1], '1, 2.5, 3');
    });

    it('keeps a zero tangent visible rather than tidying it away', () => {
        // Tangents are all-zero on 69% of shipped vertices because only the U3U3 formats bind them.
        // Seeing the zero is how an author confirms that, so it is not blanked out.
        const table = geometryTable(page({ vertices: [vertex] }));

        assert.equal(table.rows[0][5], '0, 0, 0');
    });

    it('lays faces out as their three vertex indices', () => {
        const table = geometryTable(page({
            table: 'faces',
            faces: [{ index: 0, v0: 1, v1: 2, v2: 3 }],
        }));

        assert.deepEqual(table.columns, ['#', 'V0', 'V1', 'V2']);
        assert.deepEqual(table.rows[0], ['0', '1', '2', '3']);
    });

    it('lays the bone mapping out as slot, bone and name', () => {
        const table = geometryTable(page({
            table: 'boneMapping',
            boneMapping: [{ slot: 0, boneIndex: 7, name: 'B_Spine' }],
        }));

        assert.deepEqual(table.columns, ['Slot', 'Bone', 'Name']);
        assert.deepEqual(table.rows[0], ['0', '7', 'B_Spine']);
    });

    it('comes back empty for a table it does not know', () => {
        const table = geometryTable(page({ table: 'nonsense' }));

        assert.deepEqual(table.columns, []);
        assert.deepEqual(table.rows, []);
    });
});

describe('geometryTable notes', () => {
    const empty = (table: string): SubMeshGeometryPage => ({
        model: 'm', meshIndex: 0, subMeshIndex: 0, table, offset: 0,
        totalVertices: 5507, totalFaces: 3814, vertices: [], faces: [], boneMapping: [],
    });

    it('explains an empty bone mapping instead of showing a bare grid', () => {
        // Measured on the real Star Destroyer: its first sub-mesh is static, so the skin table is
        // legitimately empty. An empty grid reads as a failed request.
        const table = geometryTable(empty('boneMapping'));

        assert.match(table.note ?? '', /not skinned/i);
    });

    it('says nothing extra when a table has rows', () => {
        const table = geometryTable({
            ...empty('boneMapping'),
            boneMapping: [{ slot: 0, boneIndex: 1, name: 'B_Spine' }],
        });

        assert.equal(table.note, undefined);
    });
});
