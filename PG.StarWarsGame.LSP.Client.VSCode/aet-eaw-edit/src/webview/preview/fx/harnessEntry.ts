// Entry point for the scratchpad GLSL compile oracle. Re-exports the pipeline so the harness bundles
// the REAL modules rather than testing a copy that has quietly gone stale.

export { flattenIncludes } from './includes';
export { parseFxManifest, selectTechnique, hasProgrammableShader } from './fxParser';
export { buildFragmentShader, buildVertexShader, declareUniforms } from './glslProgram';
export {
    attributeFor, collectSamplerTextures, collectSemantics, collectUniformDefaults, uniformSourceFor,
} from './semantics';
export { translateEffect } from './effect';
export { materialStateFrom } from './renderState';
