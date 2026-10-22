// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A model's tree from its detail alone - the server's account of the file - for a panel that shows
// the structure without loading the model into a scene, as the Mod Browser's preview does. The
// model preview builds the same tree from its scene; both go through `meshPlacement` and
// `effectPlacement`, so a mesh or an effect that shares its bone's name is that bone's row in both.

import type { ModelDetail } from "../../protocol/modelPreview";
import {
    boneRowId,
    effectPlacement,
    meshPlacement,
    TreeItem,
} from "./previewTree";

/** The rows of a model's tree: its bones, the meshes on them and the effects its proxies carry. */
export function treeItemsFromDetail(detail: ModelDetail): TreeItem[] {
    const roots = new Set(
        detail.bones.filter((b) => b.parentIndex < 0).map((b) => b.index),
    );
    const boneName = (index: number): string =>
        detail.bones.find((b) => b.index === index)?.name ?? "";

    // The first mesh and the first effect that share a bone's name are that bone's row; a second
    // one stays a child of it rather than being dropped.
    const meshOfBone = new Map<number, string>();
    const effectOfBone = new Map<number, string>();
    const extras: TreeItem[] = [];

    detail.meshes.forEach((mesh) => {
        const placement = meshPlacement(mesh.boneIndex, roots, {
            bone: boneName(mesh.boneIndex),
            mesh: mesh.name,
        });
        if ("merge" in placement && !meshOfBone.has(placement.merge)) {
            meshOfBone.set(placement.merge, mesh.name);
            return;
        }
        extras.push({
            id: `mesh:${mesh.index}`,
            kind: "mesh",
            name: mesh.name,
            parentId: boneRowId(mesh.boneIndex),
            visible: mesh.visible,
            gatedOff: false,
        });
    });

    detail.proxies.forEach((proxy, i) => {
        const placement = effectPlacement(proxy.boneIndex, roots, {
            bone: boneName(proxy.boneIndex),
            effect: proxy.name,
        });
        if ("merge" in placement && !effectOfBone.has(placement.merge)) {
            effectOfBone.set(placement.merge, proxy.name);
            return;
        }
        extras.push({
            id: `proxy:${i}`,
            kind: "particle",
            name: proxy.name,
            parentId: boneRowId(proxy.boneIndex),
            visible: proxy.visible,
            gatedOff: false,
        });
    });

    const bones: TreeItem[] = detail.bones.map((bone) => {
        const effect = effectOfBone.get(bone.index);
        const mesh = meshOfBone.get(bone.index);
        return {
            id: boneRowId(bone.index),
            // The effect is what a reader is looking for, so a proxy row reads as one.
            kind:
                effect !== undefined
                    ? "particle"
                    : mesh !== undefined
                      ? "mesh"
                      : "bone",
            name: effect ?? mesh ?? bone.name,
            parentId: bone.parentIndex < 0 ? null : boneRowId(bone.parentIndex),
            visible: bone.visible,
            gatedOff: false,
        };
    });

    return [...bones, ...extras];
}
