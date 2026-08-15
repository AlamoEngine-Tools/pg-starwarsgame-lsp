// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What is actually IN this file, for the thing currently selected in the tree.
//
// The preview answers "what does this model look like"; until now nothing answered "what is it made
// of". That is the question a modder is asking when a mesh comes out black, or takes no faction
// colour, or refuses to disappear at ALT 1 - and every one of those is answered by the shader it
// names and the parameters bound to it.
//
// Almost all of it is already in the browser: `ModelGlbExporter` writes the shader, the vertex
// format, the skinning mode, the ALT/LOD tags and EVERY shader parameter into the glTF material
// extras, so this module needs no server round trip. Kept free of three.js so the whole shape is
// testable - the viewport supplies plain data, this decides what the panel says.

import { type ModelDetail, type SubMeshGeometryPage } from '../../protocol/modelPreview';
import { resolveMaterial, type MaterialExtras } from './materials';

/** How a value should be shown. Only `colour` carries a swatch. */
export type InspectorValueKind =
    'text' | 'number' | 'vector' | 'colour' | 'texture' | 'matrix';

export interface InspectorRow {
    label: string;
    value: string;
    kind: InspectorValueKind;
    /** A CSS colour for the swatch, on `colour` rows only. */
    swatch?: string;
    /** Shown on hover, for a value whose meaning is not obvious from the number. */
    hint?: string;
}

export interface InspectorSection {
    title: string;
    rows: InspectorRow[];
    /**
     * Why the section is empty.
     *
     * A section with nothing in it stays on screen and says so, rather than vanishing - a panel
     * whose shape changes per selection makes the reader wonder what else is being withheld.
     */
    note?: string;
}

export interface InspectorPanel {
    title: string;
    subtitle?: string;
    sections: InspectorSection[];
}

/** A mesh row's facts, as the viewport can read them without the server. */
export interface MeshInspection {
    kind: 'mesh';
    /** The ALO's own mesh name, not the glTF node name. */
    name: string;
    /** The bone this mesh rides, or the empty string when it rides none. */
    boneName: string;
    vertexCount: number;
    triangleCount: number;
    /** Whether it is on screen right now, after every gate and override. */
    drawn: boolean;
    extras: MaterialExtras;
    /**
     * Position in the FILE's mesh list, for joining to what the server knows about it.
     *
     * The index rather than the name: nothing stops a model carrying two meshes called the same
     * thing, and a name join would show one mesh's bounding box against the other without saying so.
     */
    meshIndex?: number;
    /** Which sub-mesh of that mesh - the other half of the key the geometry tables are fetched by. */
    subMeshIndex?: number;
}

export interface BoneInspection {
    kind: 'bone';
    index: number;
    name: string;
    parentIndex: number;
    parentName: string;
    visible: boolean;
    /** The billboard mode the bone declares, or null for the common `Disable`. */
    billboard: string | null;
}

export interface ParticleInspection {
    kind: 'particle';
    name: string;
    boneName: string;
    emitterCount: number;
    playing: boolean;
}

export type Inspection = MeshInspection | BoneInspection | ParticleInspection;

/**
 * Parameter names that hold a colour.
 *
 * By NAME rather than by shape, deliberately. A Float3 is just as often a UV offset, a scroll rate
 * or a scale, and drawing a swatch beside one of those states something the reader has no way to
 * check. The shipped effects are consistent about the spelling; both are accepted because a mod's
 * own shader may use either.
 */
const COLOUR_NAME = /colou?r/i;

/** How many decimals are worth showing before the value is just float noise. */
const DECIMALS = 4;

/**
 * One shader parameter, formatted by its type.
 *
 * The values arrive as JSON: the exporter writes a Float3 or Float4 as an ARRAY and a Float or Int
 * as a NUMBER, so stringifying them prints `[1,1,1,1]` and `0.30000001192092896`. Both are the
 * author's own value rendered as something they never typed.
 */
export function formatParameter(name: string, value: unknown): InspectorRow {
    const row = (over: Partial<InspectorRow>): InspectorRow =>
        ({ label: name, value: '', kind: 'text', ...over });

    if (typeof value === 'string') {
        return row({ value, kind: 'texture' });
    }

    if (typeof value === 'number' && Number.isFinite(value)) {
        return row({ value: number_(value), kind: 'number' });
    }

    if (Array.isArray(value) && value.length > 0
        && value.every(part => typeof part === 'number' && Number.isFinite(part))) {
        const parts = value as number[];
        const text = parts.map(number_).join(', ');

        // The alpha stays in the text even though the swatch cannot show it - a collision hull's
        // 0.5 is the entire reason its colour is worth reading.
        return COLOUR_NAME.test(name)
            ? row({ value: text, kind: 'colour', swatch: swatchOf(parts) })
            : row({ value: text, kind: 'vector' });
    }

    // A mod's own exporter can write anything into extras. Saying so costs this one row; throwing
    // would cost the whole panel.
    return row({ value: 'unreadable value', hint: 'This parameter is not a number, vector or texture name.' });
}

/** A bulk-geometry page, laid out as columns and rows of text. */
export interface GeometryTableView {
    columns: string[];
    rows: string[][];
    /** Why the table is empty, when empty is a fact about the file rather than a failure. */
    note?: string;
}

/**
 * Lays one page of bulk geometry out for display.
 *
 * Every value is formatted the same way a shader parameter is - trimmed of float noise, components
 * comma-separated - so the same number reads the same wherever it appears in the panel.
 */
export function geometryTable(page: SubMeshGeometryPage): GeometryTableView {
    if (page.table === 'vertices') {
        return {
            columns: ['#', 'Position', 'Normal', 'UV0', 'UV1', 'Tangent', 'Binormal', 'Colour',
                'Bone indices', 'Bone weights'],
            rows: page.vertices.map(row => [
                String(row.index),
                vector(row.position),
                vector(row.normal),
                vector(row.texCoord0),
                vector(row.texCoord1),

                // A zero tangent is left showing. Only the U3U3 formats bind them, so 69% of shipped
                // vertices store zeros - and seeing that is how an author confirms the format.
                vector(row.tangent),
                vector(row.binormal),
                vector(row.color),
                row.boneIndices.join(', '),
                vector(row.boneWeights),
            ]),
        };
    }

    if (page.table === 'faces') {
        return {
            columns: ['#', 'V0', 'V1', 'V2'],
            rows: page.faces.map(row =>
                [String(row.index), String(row.v0), String(row.v1), String(row.v2)]),
        };
    }

    if (page.table === 'boneMapping') {
        return {
            columns: ['Slot', 'Bone', 'Name'],
            rows: page.boneMapping.map(row =>
                [String(row.slot), String(row.boneIndex), row.name]),

            // A static sub-mesh has no skin table at all, and that is the common case - the Star
            // Destroyer's own hull is one. Saying so beats an empty grid, which reads as a request
            // that failed.
            note: page.boneMapping.length === 0
                ? 'This sub-mesh is not skinned, so it has no bone mapping - every vertex rides the '
                    + 'bone the mesh is attached to.'
                : undefined,
        };
    }

    // A table this build does not know about, from a newer server. Nothing to draw, and nothing to
    // throw over either.
    return { columns: [], rows: [] };
}

/**
 * One panel for a selection that is more than one thing.
 *
 * A merged bone/mesh row is the case this exists for: the ALO gives a rigid sub-mesh the same name
 * as the bone that is its origin, the tree draws them as a single row, and selecting it must not
 * force a choice about which half to describe.
 */
export function inspectPanels(
    sources: readonly Inspection[], detail?: ModelDetail,
): InspectorPanel {
    const panels = sources.map(source => inspectPanel(source, detail));

    return {
        title: panels[0]?.title ?? 'Nothing selected',
        subtitle: panels[0]?.subtitle,
        sections: panels.flatMap(panel => panel.sections),
    };
}

/**
 * The panel for whatever is selected.
 *
 * `detail` is optional throughout: it arrives a round trip behind the geometry, and a panel that
 * stays blank until it lands reads as broken rather than as still loading.
 */
export function inspectPanel(source: Inspection, detail?: ModelDetail): InspectorPanel {
    switch (source.kind) {
        case 'mesh':
            return meshPanel(source, detail);
        case 'bone':
            return bonePanel(source, detail);
        default:
            return particlePanel(source);
    }
}

function meshPanel(source: MeshInspection, detail?: ModelDetail): InspectorPanel {
    const spec = resolveMaterial(source.extras);
    const extras = source.extras;
    const stored = source.meshIndex === undefined
        ? undefined
        : detail?.meshes.find(entry => entry.index === source.meshIndex);

    const mesh: InspectorRow[] = [
        text('Name', source.name),
        text('Attached to bone', source.boneName === '' ? 'none' : source.boneName),
        text('Drawn', drawnValue(source, spec.hidden)),
        text('Collidable', extras.alamoCollidable === true ? 'yes' : 'no'),

        // "Untagged" and "level 0" are different statements about the file, and a defaulted 0 would
        // read as the author having pinned the mesh to the undamaged state.
        text('Damage state', extras.alamoAlt === undefined ? 'none' : String(extras.alamoAlt)),
        text('Detail', extras.alamoLod === undefined ? 'none' : String(extras.alamoLod)),
        { label: 'Triangles', value: count(source.triangleCount), kind: 'number' },
        { label: 'Vertices', value: count(source.vertexCount), kind: 'number' },
    ];

    // The box the FILE stores, not one measured off the exported geometry - the engine culls and
    // hit-tests against this, so a box that disagrees with the geometry is itself the finding.
    if (stored !== undefined) {
        mesh.push(
            { label: 'Bounds min', value: vector(stored.boundsMin), kind: 'vector' },
            { label: 'Bounds max', value: vector(stored.boundsMax), kind: 'vector' },
            {
                label: 'Sub-meshes',
                value: String(stored.subMeshCount),
                kind: 'number',
                hint: 'Each sub-mesh binds its own shader and parameters. This panel describes one '
                    + 'of them.',
            });
    }

    const material: InspectorRow[] = [
        text('Shader', extras.alamoShader ?? 'none'),
        text('Vertex format', extras.alamoVertexFormat ?? 'unknown'),
        text('Skinning', extras.alamoSkinning ?? 'unknown'),
        text('Archetype', spec.archetype, archetypeHint(spec.archetype)),
        text('Blend', spec.blend),
        text('Faction colour', spec.colorize ? 'yes' : 'no',
            'Whether the team colour reaches this sub-mesh, from a *Colorize shader or an FC_ mesh name.'),
    ];

    return {
        title: source.name,
        subtitle: extras.alamoShader,
        sections: [
            { title: 'Mesh', rows: mesh },
            { title: 'Material', rows: material },
            parameterSection(extras),
        ],
    };
}

/**
 * Every shader parameter, in the order the file declares them.
 *
 * Declaration order rather than alphabetical: it is the order the author sees in their own tool, and
 * sorting would separate parameters that belong together.
 */
function parameterSection(extras: MaterialExtras): InspectorSection {
    const rows: InspectorRow[] = [];

    for (const [key, value] of Object.entries(extras)) {
        if (!key.toLowerCase().startsWith('param:')) {
            continue;
        }

        rows.push(formatParameter(key.slice('param:'.length), value));
    }

    return {
        title: 'Shader parameters',
        rows,
        note: rows.length === 0
            ? 'This sub-mesh binds no parameters, so its shader draws with whatever its own header declares.'
            : undefined,
    };
}

function bonePanel(source: BoneInspection, detail?: ModelDetail): InspectorPanel {
    const stored = detail?.bones.find(entry => entry.index === source.index);
    const proxy = detail?.proxies.find(entry => entry.boneIndex === source.index);

    const rows: InspectorRow[] = [
        text('Name', source.name),
        { label: 'Index', value: String(source.index), kind: 'number' },
        text('Parent', source.parentIndex < 0
            ? 'none (root)'
            : `${source.parentName} (${source.parentIndex})`),
        text('Visible', source.visible ? 'yes' : 'no'),
    ];

    // Only when it declares one: `Disable` is the overwhelming majority and a row saying so on every
    // bone of a Star Destroyer is noise the reader has to look past.
    // The server's value is the FILE's; the client's exists only because the exporter copies it into
    // a node's extras, and only when it is not `Disable`. Prefer the file, fall back to the extras
    // so the row is there before the detail arrives.
    const billboard = stored !== undefined && stored.billboard !== 'Disable'
        ? stored.billboard
        : source.billboard;

    if (billboard !== null) {
        rows.push(text('Billboard', billboard));
    }

    // In the FILE's axes. The client's own matrices have been through the Z-up to Y-up correction
    // and the bone/mesh split, so they are not numbers the author could look up anywhere.
    if (stored !== undefined) {
        rows.push(
            { label: 'Relative transform', value: matrix(stored.relativeTransform), kind: 'matrix' },
            { label: 'Absolute transform', value: matrix(stored.absoluteTransform), kind: 'matrix' });
    }

    const sections: InspectorSection[] = [{ title: 'Bone', rows }];

    // A proxy is a bone with a role - the attachment point a particle system hangs off. The
    // exporter writes none of this into the glTF, so it is unreadable without the server.
    if (proxy !== undefined) {
        sections.push({
            title: 'Proxy',
            rows: [
                text('Name', proxy.name),
                text('Visible', proxy.visible ? 'yes' : 'no'),
                text('Damage state', proxy.alt === null ? 'none' : String(proxy.alt)),
                text('Detail', proxy.lod === null ? 'none' : String(proxy.lod)),
                text('Stays hidden when repaired', proxy.altDecreaseStayHidden ? 'yes' : 'no',
                    'When set, dropping back to a lower damage state leaves this effect off rather '
                    + 'than restoring it.'),
            ],
        });
    }

    return { title: source.name, sections };
}

function particlePanel(source: ParticleInspection): InspectorPanel {
    return {
        title: source.name,
        sections: [{
            title: 'Particle system',
            rows: [
                text('Name', source.name),
                text('Attached to bone', source.boneName === '' ? 'none' : source.boneName),
                { label: 'Emitters', value: String(source.emitterCount), kind: 'number' },
                text('Playing', source.playing ? 'yes' : 'no'),
            ],
        }],
    };
}

function drawnValue(source: MeshInspection, nonVisual: boolean): string {
    if (source.drawn) {
        return 'yes';
    }

    if (source.extras.alamoHidden === true) {
        return 'no - hidden in the file';
    }

    if (nonVisual) {
        return 'no - the engine consumes this geometry rather than showing it';
    }

    return 'no - switched off, or gated by the current damage state or detail level';
}

function archetypeHint(archetype: string): string | undefined {
    switch (archetype) {
        case 'collision':
            return 'A collision hull. The engine uses it for hit testing and never draws it.';
        case 'shadow-volume':
            return 'A shadow volume. The engine extrudes it and never draws it.';
        case 'non-visual':
            return 'Geometry the engine consumes rather than shows.';
        case 'unknown':
            return 'No shader is bound, so the preview cannot tell what this should look like.';
        default:
            return undefined;
    }
}

function text(label: string, value: string, hint?: string): InspectorRow {
    return { label, value, kind: 'text', hint };
}

function count(value: number): string {
    return value.toLocaleString();
}

function vector(parts: readonly number[]): string {
    return parts.map(number_).join(', ');
}

/** Four rows of four, one per line, so the translation reads as the last row the author authored. */
function matrix(values: readonly number[]): string {
    const rows: string[] = [];

    for (let at = 0; at < values.length; at += 4) {
        rows.push(vector(values.slice(at, at + 4)));
    }

    return rows.join('\n');
}

/** Trims float noise without rounding a real value away. */
function number_(value: number): string {
    if (Number.isInteger(value)) {
        return String(value);
    }

    return String(Number(value.toFixed(DECIMALS)));
}

function swatchOf(parts: readonly number[]): string {
    const channel = (at: number): string => {
        const value = parts[at] ?? 0;

        return Math.round(Math.min(1, Math.max(0, value)) * 255).toString(16).padStart(2, '0');
    };

    return `#${channel(0)}${channel(1)}${channel(2)}`;
}
