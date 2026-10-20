// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A headless render probe around the REAL PreviewViewport: a particle system - the one `render.js`
// embeds, else a synthetic bump emitter with flat textures - drawn through the translated engine
// effect and through the preview's own shader, pixels read back, frames saved for the eye.
//
// Drives the viewport's loop by hand: headless Chromium under a virtual-time budget advances
// timers but not requestAnimationFrame.
import * as THREE from "three";
import {DDSLoader} from "three/examples/jsm/loaders/DDSLoader.js";

import {translateEffect} from "../../src/webview/preview/fx/effect";
import {parseFxManifest, selectTechnique} from "../../src/webview/preview/fx/fxParser";
import {materialStateFrom} from "../../src/webview/preview/fx/renderState";
import {emitter, properties, testSystem} from "../../src/webview/preview/particleFixture";
import {PreviewViewport} from "../../src/webview/preview/viewport";

type Particle = { position: { x: number; y: number; z: number } };
type Inner = {
    camera: THREE.PerspectiveCamera;
    loop: () => void;
    renderFrame: () => void;
    renderer: THREE.WebGLRenderer;
    controls: { target: THREE.Vector3; update: () => void; enableDamping: boolean };
    particleSystems: Map<string, {
        instance: {
            emitters: unknown[];
            emitterMesh: (i: number) => THREE.Mesh | null;
            emitterParticles: (i: number) => Particle[]
        }
    }>;
};

const lines: string[] = [];
const shots: string[] = [];
const log = (line: string): void => {
    lines.push(line);
};
for (const level of ["error", "warn"] as const) {
    const original = console[level].bind(console);
    console[level] = (...args: unknown[]): void => {
        const text = args.map((a) => String(a)).join(" ");
        if (!/deprecated/.test(text)) {
            lines.push(`${level}: ${text.slice(0, 400)}`);
        }
        original(...args);
    };
}

function flat(rgba: [number, number, number, number]): THREE.Texture {
    const data = new Uint8Array(8 * 8 * 4);
    for (let i = 0; i < 64; i++) {
        data.set(rgba, i * 4);
    }
    const texture = new THREE.DataTexture(data, 8, 8);
    texture.needsUpdate = true;
    return texture;
}

/** Ticks the viewport `n` times, 33 ms of virtual time apart. */
function frames(inner: Inner, n: number): Promise<void> {
    return new Promise((resolve) => {
        let left = n;
        const step = (): void => {
            inner.loop();
            if (--left <= 0) {
                resolve();
            } else {
                setTimeout(step, 33);
            }
        };
        setTimeout(step, 33);
    });
}

/** Renders one frame, counts the pixels that differ from the corner, keeps the frame as a PNG. */
function readback(inner: Inner): string {
    inner.renderFrame();
    const gl = inner.renderer.getContext() as WebGL2RenderingContext;
    const w = gl.drawingBufferWidth;
    const h = gl.drawingBufferHeight;
    const pixels = new Uint8Array(w * h * 4);
    gl.readPixels(0, 0, w, h, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    const corner = pixels.slice(0, 4);
    let differing = 0;
    for (let i = 0; i < w * h; i++) {
        const p = pixels.subarray(i * 4, i * 4 + 4);
        if (p[0] !== corner[0] || p[1] !== corner[1] || p[2] !== corner[2]) {
            differing++;
        }
    }
    const canvas = document.createElement("canvas");
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext("2d")!;
    const image = ctx.createImageData(w, h);
    for (let y = 0; y < h; y++) {
        image.data.set(pixels.subarray((h - 1 - y) * w * 4, (h - y) * w * 4), y * w * 4);
    }
    ctx.putImageData(image, 0, 0);
    shots.push(canvas.toDataURL("image/png"));
    return `${w}x${h} differing=${differing}`;
}

/** As `decodeTexture` in modelPreview.tsx reads a .dds. */
function decodeDds(data: string): { texture: THREE.Texture; summary: string } {
    const bytes = Uint8Array.from(atob(data), (c) => c.charCodeAt(0));
    const parsed = new DDSLoader().parse(bytes.buffer as ArrayBuffer, true);
    const compressed = new Set<number>([THREE.RGB_S3TC_DXT1_Format, THREE.RGBA_S3TC_DXT1_Format, THREE.RGBA_S3TC_DXT3_Format, THREE.RGBA_S3TC_DXT5_Format]).has(parsed.format as number);
    const texture = compressed
        ? new THREE.CompressedTexture(parsed.mipmaps as THREE.CompressedTextureMipmap[], parsed.width, parsed.height, parsed.format as THREE.CompressedPixelFormat)
        : new THREE.DataTexture((parsed.mipmaps[0] as { data: Uint8Array }).data, parsed.width, parsed.height);
    texture.needsUpdate = true;
    return {texture, summary: `${parsed.width}x${parsed.height} format=${parsed.format} compressed=${compressed}`};
}

function describeMesh(inner: Inner, label: string): void {
    const entry = inner.particleSystems.get("probe");
    const mesh = entry?.instance.emitterMesh(0) ?? null;
    if (mesh === null) {
        log(`${label}: no mesh`);
        return;
    }
    const geometry = mesh.geometry as THREE.BufferGeometry & { instanceCount?: number };
    const material = mesh.material as THREE.ShaderMaterial;
    log(`${label}: material=${material.constructor.name} particles=${entry!.instance.emitterParticles(0).length} instanceCount=${geometry.instanceCount ?? "n/a"} drawRange=${geometry.drawRange.count} depthTest=${material.depthTest} blending=${material.blending}`);
}

async function main(): Promise<void> {
    const sources = JSON.parse(document.getElementById("shaders")!.textContent!) as Record<string, string>;
    const read = (name: string): string | null => sources[name.toLowerCase()] ?? null;

    const canvas = document.createElement("canvas");
    canvas.width = 640;
    canvas.height = 640;
    canvas.style.width = "640px";
    canvas.style.height = "640px";
    document.body.appendChild(canvas);
    const labels = document.createElement("div");
    document.body.appendChild(labels);

    const viewport = new PreviewViewport(canvas, labels);
    const inner = viewport as unknown as Inner;
    inner.controls.enableDamping = false;

    const fieldTag = document.getElementById("field");
    if (fieldTag !== null) {
        const {field, textures, wind} = JSON.parse(fieldTag.textContent!) as {
            field: Parameters<PreviewViewport["addParticleSystem"]>[1];
            textures: Record<string, { format: string; data: string }>;
            wind: { heading: number; speed: number } | null;
        };
        viewport.addParticleSystem("probe", field, {origin: "gameplay"});
        if (wind !== null) {
            viewport.setWind(wind.heading, wind.speed);
            log(`wind from ${wind.heading} deg at ${wind.speed}`);
        }
        for (const [name, {format, data}] of Object.entries(textures)) {
            if (format !== "dds") {
                log(`texture ${name}: .${format} is not handled by the probe`);
                continue;
            }
            const {texture, summary} = decodeDds(data);
            viewport.setParticleTexture(name, texture);
            log(`texture ${name}: ${summary}`);
        }
        log(`system ${field.name}: ${field.emitters.map((e) => `${e.name} ${e.properties.blendMode} ${e.colorTexture}/${e.normalTexture ?? "-"}`).join("; ")}`);
    } else {
        viewport.addParticleSystem(
            "probe",
            testSystem([
                emitter({
                    name: "rocks",
                    normalTexture: "flat_n.tga",
                    properties: properties({
                        blendMode: "Bump",
                        particlesPerSecond: 20,
                        lifetime: 100,
                        freezeTime: 0.3,
                        skipTime: 0.3
                    }),
                }),
            ]),
            {origin: "gameplay"},
        );
        viewport.setParticleTexture("p_particle_master.tga", flat([255, 200, 120, 255]));
        viewport.setParticleTexture("flat_n.tga", flat([128, 128, 255, 255]));
        log("system: synthetic bump emitter, flat textures");
    }

    // The field ticks and freezes BEFORE its effect is applied - the real order of events, since
    // the shader comes back from the host after the system has started.
    await frames(inner, 40);

    for (const name of viewport.shaderNames()) {
        const source = read(name);
        if (source === null) {
            log(`no source for ${name}`);
            continue;
        }
        const technique = selectTechnique(parseFxManifest(source));
        const pass = technique?.passes[0];
        if (pass !== undefined) {
            viewport.applyShaderState(name, materialStateFrom(pass));
        }
        const result = translateEffect(source, read);
        log(`${name}: refusal=${result.refusal ?? "none"} missing=${JSON.stringify(result.missingIncludes)}`);
        if (result.effect !== null) {
            viewport.applyTranslatedEffect(name, result.effect);
        }
    }
    await frames(inner, 2);

    // A close-up on the first particle: both paths, one pose.
    const entry = inner.particleSystems.get("probe")!;
    const first = entry.instance.emitterParticles(0)[0]?.position ?? {x: 0, y: 0, z: 0};
    const target = new THREE.Vector3(first.x, first.y, first.z);
    const pose = (): void => {
        inner.camera.position.set(target.x + 25, target.y + 15, target.z + 70);
        inner.camera.near = 1;
        inner.camera.far = 6000;
        inner.camera.updateProjectionMatrix();
        inner.controls.target.copy(target);
        inner.controls.update();
    };

    pose();
    inner.loop();
    pose();
    describeMesh(inner, "translated");
    log(`TRANSLATED: ${readback(inner)}`);
    viewport.setTranslatedShaders(false);
    inner.loop();
    pose();
    describeMesh(inner, "hand");
    log(`HAND:       ${readback(inner)}`);
    viewport.setTranslatedShaders(true);
    inner.loop();
    pose();
    describeMesh(inner, "translated again");
    log(`TRANSLATED: ${readback(inner)}`);

    document.getElementById("out")!.textContent = lines.join("\n");
    const gallery = document.createElement("div");
    gallery.id = "shots";
    gallery.textContent = shots.join("\n");
    document.body.appendChild(gallery);
}

main().catch((e: unknown) => {
    lines.push(`FAILED: ${String(e)} ${(e as Error).stack ?? ""}`);
    document.getElementById("out")!.textContent = lines.join("\n");
});
