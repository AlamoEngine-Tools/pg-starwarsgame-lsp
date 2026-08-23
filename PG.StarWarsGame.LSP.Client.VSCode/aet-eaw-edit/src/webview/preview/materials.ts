// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The material seam.
//
// An Alamo sub-mesh names a .fx shader and carries arbitrary parameters; glTF has no equivalent, so
// the exporter passes both through in material.extras and this decides what to actually draw. It is
// the single boundary where "MeshBumpColorize.fx" becomes render state.
//
// What lives here is the ARCHETYPE fallback: a reading of the shader NAME. It is deliberately not
// the end state - the real shader sources say exactly what each one does - but it is not a
// stopgap either. Mods ship their own .fx files that we will never have sources for, so this path
// is permanent and every unknown shader lands on it.

/** How a sub-mesh composites against what is already drawn. */
export type BlendMode = 'opaque' | 'alpha' | 'additive';

/** Which texture slot a shader parameter feeds. */
export interface TextureBindings {
    base?: string;
    normal?: string;
    gloss?: string;
}

/** What to draw, and why. */
export interface MaterialSpec {
    /**
     * Skip this sub-mesh entirely.
     *
     * Shadow volumes, collision hulls and the heat-distortion pass are geometry the engine consumes
     * rather than shows. Drawing them puts a black shell around the model, which reads as a broken
     * preview rather than as extra detail.
     */
    hidden: boolean;
    blend: BlendMode;
    /** Additive passes must not occlude what follows them. */
    depthWrite: boolean;
    textures: TextureBindings;
    /** The team colour applies to this sub-mesh. */
    colorize: boolean;
    /**
     * Whether the scene's lights affect this sub-mesh.
     *
     * The additive family does no lighting at all - `Additive.fxh` is `texel * constant` - and
     * lighting it anyway adds a specular lobe that a black albedo cannot suppress. Added to what an
     * additive blend already is, that draws the whole quad as a grey rectangle.
     */
    lit: boolean;
    /** Which rule matched, for the material inspector and for diagnosing a wrong-looking model. */
    archetype: string;
}

/**
 * The extras the exporter writes onto each glTF material.
 *
 * Mirrors `ModelGlbExporter.BuildMaterial`. Parameter keys are namespaced `param:<Name>` so a mod is
 * free to name a shader parameter `alamoShader` without colliding with the metadata.
 */
export interface MaterialExtras {
    alamoShader?: string;
    alamoMesh?: string;
    /** The mesh's position in the FILE, which is how the inspector joins back to the server. */
    alamoMeshIndex?: number;
    /** Which sub-mesh of that mesh this material draws. */
    alamoSubMeshIndex?: number;
    alamoVertexFormat?: string;
    alamoSkinning?: string;
    alamoAlt?: number;
    alamoLod?: number;
    alamoHidden?: boolean;
    alamoCollidable?: boolean;
    [key: string]: unknown;
}

/**
 * Shaders whose geometry the engine uses but never shows.
 *
 * Matched on a substring of the shader name because each has both a `Mesh` and an `RSkin` variant.
 * Order matters: `shadowvolume` is tested before `collision` so `Rv_transport.alo`'s
 * `shadow-Collision` is read as the shadow volume its shader says it is.
 */
const NON_VISUAL: readonly [string, string][] = [
    ['shadowvolume', 'shadow-volume'],
    ['collision', 'collision'],
    ['lightvisualize', 'non-visual'],
    ['occludedunit', 'non-visual'],
    ['heat', 'non-visual'],
];

/**
 * A mesh NAME that reads as a collision hull.
 *
 * Needed because the shader is not the only way a collision hull is authored: across the shipped
 * models 563 sub-meshes carry a `*Collision.fx`, and another 187 are collision hulls with no
 * collision shader at all - 174 of them on `alDefault.fx`, which just means nothing was assigned.
 * `Rv_nebulonb.alo` is the case that surfaced it, a mesh called `COLLISION` on the default shader.
 *
 * Whole segments only, so this catches `COLLISION`, `floor_collision` and the stations' `HP04_LC_Coll`
 * without reaching a `Collector` or a `Collar`.
 */
const COLLISION_NAME = /collision|(^|[_-])coll([_-]|\d|$)/i;

/** Parameter names, lowercased, that feed each texture slot. */
const TEXTURE_SLOTS: Record<keyof TextureBindings, string[]> = {
    base: ['basetexture', 'diffusetexture', 'colortexture'],
    normal: ['normaltexture', 'bumptexture'],
    gloss: ['glosstexture', 'speculartexture'],
};

/**
 * The prefix that marks a mesh as faction-coloured.
 *
 * From the engine: a mesh whose name starts with `FC_` takes the team colour whatever its shader is,
 * which is how a unit's faction stripe is authored on an otherwise ordinary material.
 */
const FACTION_COLOUR_PREFIX = 'fc_';

/** Reads a `param:<name>` value, case-insensitively, as a string. */
export function textureParam(extras: MaterialExtras, name: string): string | undefined {
    const wanted = `param:${name}`.toLowerCase();

    for (const [key, value] of Object.entries(extras)) {
        if (key.toLowerCase() === wanted && typeof value === 'string' && value.length > 0) {
            return value;
        }
    }

    return undefined;
}

/**
 * Every NUMERIC shader parameter the sub-mesh carries, by the name the effect declares it under.
 *
 * This is where a sub-mesh's material properties actually live - `Diffuse`, `Specular`, `Emissive`,
 * `Shininess`, `Colorization`, `UVOffset`. They are neither engine state nor constants in the
 * effect: two meshes sharing one shader differ precisely here, so a translated shader that ignores
 * them draws every sub-mesh with whatever the header happened to declare.
 *
 * Texture parameters are left out - their values are file names, and they bind through
 * `collectSamplerTextures` instead.
 */
export function numericParams(extras: MaterialExtras): Map<string, number[]> {
    const params = new Map<string, number[]>();

    for (const [key, value] of Object.entries(extras)) {
        if (!key.toLowerCase().startsWith('param:')) {
            continue;
        }

        // All three shapes the value can arrive in. The exporter writes a Float3 or Float4 as a
        // JSON ARRAY and a Float or Int as a JSON NUMBER - reading only strings dropped every one
        // of them, which is how a collision hull that declares blue came out in the fallback cyan.
        // The string form is kept for a mod's own exporter, or a hand-edited glb.
        const parts: unknown[] = Array.isArray(value)
            ? value
            : typeof value === 'number'
                ? [value]
                : typeof value === 'string' ? value.split(',').map(part => part.trim()) : [];

        if (parts.length === 0 || parts.some(part => part === '' || !Number.isFinite(Number(part)))) {
            continue;
        }

        params.set(key.slice('param:'.length), parts.map(Number));
    }

    return params;
}

/**
 * The flat colour a collision hull or shadow volume is drawn in, if it declares one.
 *
 * NOT a constant in the renderer, and not one in the shader either: `MeshCollision.fx` declares
 * `float4 Color <UIType = "ColorSwatch"> = {0, 0, 1, 0.5}` and the two ShadowVolume shaders declare
 * `DebugColor = {0, 1, 1, 1}`, both authorable per sub-mesh. Every shipped model writes the value
 * explicitly and every one matches its shader's default, which is why the blue and the cyan look
 * fixed - but a mod can change either, so the value is read rather than assumed.
 */
export function debugColour(
    extras: MaterialExtras,
): { r: number; g: number; b: number; a: number } | null {
    const params = numericParams(extras);
    const value = params.get('Color') ?? params.get('DebugColor');

    if (value === undefined || value.length < 3) {
        return null;
    }

    return { r: value[0], g: value[1], b: value[2], a: value[3] ?? 1 };
}

/** Every texture the sub-mesh binds, in the slot the renderer uses it for. */
function bindTextures(extras: MaterialExtras): TextureBindings {
    const bindings: TextureBindings = {};

    for (const [slot, names] of Object.entries(TEXTURE_SLOTS) as [keyof TextureBindings, string[]][]) {
        for (const name of names) {
            const value = textureParam(extras, name);
            if (value !== undefined) {
                bindings[slot] = value;
                break;
            }
        }
    }

    return bindings;
}

/**
 * What to draw for one sub-mesh.
 *
 * Reads the shader name rather than the parameters, because the name is the only thing every shader
 * has - including the ones a mod wrote that we have never seen.
 */
export function resolveMaterial(extras: MaterialExtras): MaterialSpec {
    const shader = (extras.alamoShader ?? '').toLowerCase();
    const mesh = (extras.alamoMesh ?? '').toLowerCase();
    const textures = bindTextures(extras);

    // The team colour reaches a sub-mesh two ways: a *Colorize shader, or an FC_-prefixed mesh on
    // any shader at all. Both are in the shipped data, so checking only one misses half the cases.
    const colorize = shader.includes('colorize') || mesh.startsWith(FACTION_COLOUR_PREFIX);

    // The shader first, where there is one that says so.
    const marked = NON_VISUAL.find(([token]) => shader.includes(token));

    // Then the name, but ONLY on a mesh the file already hides. 186 of those 187 hulls are hidden,
    // and requiring it is what keeps the rule off visible geometry whose name merely contains the
    // letters. A hidden mesh not named like collision - `B_Base`, `blink lights`, `Scale` - stays
    // ordinary geometry that happens to be switched off.
    const namedCollision = extras.alamoHidden === true && COLLISION_NAME.test(extras.alamoMesh ?? '');

    if (marked !== undefined || namedCollision) {
        return {
            hidden: true, blend: 'opaque', depthWrite: false, textures, colorize: false,
            lit: false, archetype: marked?.[1] ?? 'collision',
        };
    }

    if (shader.includes('additive')) {
        // Additive glows are drawn on top of the hull and must not occlude anything behind them.
        return {
            hidden: false, blend: 'additive', depthWrite: false, textures, colorize,
            lit: false, archetype: 'additive',
        };
    }

    if (shader.includes('shield')) {
        return {
            hidden: false, blend: 'additive', depthWrite: false, textures, colorize,
            lit: false, archetype: 'shield',
        };
    }

    if (shader.includes('alpha')) {
        // No depth write, the same as the additive family and for the same reason: every shipped
        // alpha effect declares `ZWriteEnable = FALSE`, and writing depth from a blended mesh leaves
        // the outline of its cut-out standing in front of whatever is drawn next.
        return {
            hidden: false, blend: 'alpha', depthWrite: false, textures, colorize,
            lit: true, archetype: 'alpha',
        };
    }

    // Everything else is opaque, including every shader we have never seen. Bump and gloss are not
    // separate archetypes: they differ only in which textures they bind, which the bindings above
    // already carry.
    return {
        hidden: false,
        blend: 'opaque',
        depthWrite: true,
        textures,
        colorize,
        lit: true,
        archetype: shader.length > 0 ? 'opaque' : 'unknown',
    };
}

/**
 * Whether a sub-mesh is drawn at the given detail and damage levels.
 *
 * The rule is the engine's: a mesh with no ALT or LOD tag is always drawn, and a tagged one only
 * when its level matches. Untagged geometry is the hull itself, so getting this backwards hides
 * the model and shows only its damage states.
 */
export function isVisibleAt(extras: MaterialExtras, alt: number, lod: number): boolean {
    return extras.alamoHidden !== true && isVisibleAtLevel(extras, alt, lod);
}

/**
 * Whether this mesh is the model's SHIELD BUBBLE - the thing a shield ability reveals.
 *
 * By NAME, which the user chose: `shield`, or `shield_` and anything after it. The SHADER cannot
 * decide it - measured on `Rv_moncalcruiser.alo`, which carries three meshes on `MeshShield.fx`:
 * `shield`, `engines_big` and `engines_small`. Revealing on the shader lit the engines every time
 * DEFEND was switched on.
 *
 * Nothing in the data declares which mesh a shield ability reveals, so this is a convention rather
 * than a rule. A mesh that matches the name but is NOT on a shield shader still counts - the author
 * named it, and their word wins - but it is worth telling them about; see
 * {@link shieldMeshOffShader}.
 */
export function isShieldMesh(extras: MaterialExtras): boolean {
    return /^shield(_|$)/i.test((extras.alamoMesh ?? '').trim());
}

/**
 * A mesh the shield rules will reveal that is not actually on a shield shader.
 *
 * Not an error - the name is what decides, and the model may be doing something deliberate - but a
 * shield bubble drawn with a hull shader will look like solid geometry rather than a field, and
 * that is worth saying once rather than leaving the author to wonder.
 */
export function shieldMeshOffShader(extras: MaterialExtras): boolean {
    return isShieldMesh(extras)
        && !(extras.alamoShader ?? '').toLowerCase().includes('shield');
}

/**
 * The LEVEL question alone: is this mesh part of the current damage and detail state?
 *
 * Without `alamoHidden`, which is a different question - "does the model draw this at all" - and
 * one the row chain already asks through its own `inFile` link. Bundled together they made one flag
 * gate a mesh TWICE, so an ability that talked the model into drawing its shield was still vetoed
 * by the level link reading the same flag, and overriding one did nothing at all.
 */
export function isVisibleAtLevel(extras: MaterialExtras, alt: number, lod: number): boolean {
    if (extras.alamoAlt !== undefined && extras.alamoAlt !== alt) {
        return false;
    }
    if (extras.alamoLod !== undefined && extras.alamoLod !== lod) {
        return false;
    }

    return true;
}

/**
 * Every distinct texture name the scene needs, so each is fetched once.
 *
 * EVERY texture parameter, not only the three slots the archetype samples. A translated shader
 * samples whatever its effect declares - MeshShield reads a wave and a ripple texture besides its
 * base - and a name never fetched leaves that sampler reading black, which draws something
 * plausible and wrong. The extra fetches cost nothing on the archetype path, which simply ignores
 * the textures it has no slot for.
 */
export function collectTextureNames(materials: Iterable<MaterialExtras>): string[] {
    const names = new Set<string>();

    for (const extras of materials) {
        if (resolveMaterial(extras).hidden) {
            continue;
        }

        // A parameter is a texture when its value is a string that is not a number list; that is
        // exactly the complement of what `numericParams` takes.
        const numeric = numericParams(extras);

        for (const [key, value] of Object.entries(extras)) {
            if (key.toLowerCase().startsWith('param:') && typeof value === 'string'
                && value.length > 0 && !numeric.has(key.slice('param:'.length))) {
                names.add(value);
            }
        }
    }

    // Code-unit order, not localeCompare: collation treats punctuation as insignificant, so
    // "hull_b.tga" sorts before "hull.tga" in some locales and after it in others. This list feeds a
    // fetch order, and a locale-dependent one is a difference between machines for no benefit.
    return [...names].sort();
}

/**
 * Whether a sub-mesh should cast into the shadow MAP.
 *
 * The engine casts from the authored shadow volume, never from the render mesh, so a model that
 * carries one hands the job over entirely - casting from both would draw the same shadow twice from
 * two different silhouettes, which is exactly the artefact a shadow volume exists to avoid. The 63%
 * of shipped models that author no volume keep casting from the hull, because otherwise they would
 * cast nothing at all.
 */
export function castsShadowMap(
    subject: { hasVolumes: boolean; hidden: boolean; blend: BlendMode },
): boolean {
    if (subject.hasVolumes || subject.hidden) {
        return false;
    }

    // A glow has no silhouette worth casting, and an alpha card would cast its whole quad.
    return subject.blend === 'opaque';
}
