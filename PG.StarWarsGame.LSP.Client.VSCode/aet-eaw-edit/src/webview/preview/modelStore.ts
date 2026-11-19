// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The models the preview holds: each one's GLB bytes and the version they came with, and the detail
// read off the model's file (#436).
//
// A scene refresh takes every part out of the viewport and asks for all of them again. What it asks
// with is the version it holds; the server answers "still yours" for a model whose file did not
// change, and the part is rebuilt from the bytes kept here - nothing is read, converted or sent. A
// model the new scene has no part for is let go, so what is kept is the scene on screen and nothing
// from the ones before it.
//
// Every request is numbered and the host echoes the number on its reply. Two refreshes close
// together ask for the same part twice, and the replies land in whatever order the server and the
// pipe finish them; only the reply to the latest request for a part is that part's model.
//
// Not React and not the viewport: bytes and a ledger, so it is tested on its own.

/** What identifies a model request: the model and the clips asked for with it. */
export interface ModelRequest {
    partId: string;
    modelReference: string;
    /** The part's own clips; absent or empty bakes the clips the scene reported. */
    animations?: string[];
}

/** What a reply carries for the model that was asked for. */
export interface ModelReply {
    partId: string;
    /** The number of the request this answers, as the host echoes it. */
    request?: number;
    /** The version the server answers with, when there is a model at all. */
    version?: string | null;
    /** The GLB, when the server sent one. */
    data?: Uint8Array | null;
    /** The server says the copy asked with still stands. */
    unchanged?: boolean;
}

/** A model's bytes for a part, and whether they are new to this store. */
export interface TakenModel {
    bytes: Uint8Array;
    /** The model the part was asked for by. */
    modelReference: string;
    /** True when the bytes arrived with this reply; false when they are the ones already held. */
    fresh: boolean;
    /** True when the bytes took the place of an older version: the model's file changed. */
    replaced: boolean;
}

interface Held {
    version: string;
    bytes: Uint8Array;
}

/** The part of a scene that says which models it will ask for. */
export interface SceneModels {
    parts: readonly { resolved: boolean; modelRef: string }[];
    breakoffProps?: readonly {
        modelRef?: string | null;
        animations?: readonly string[] | null;
    }[];
    deathClones?: readonly {
        modelFile?: string | null;
        animations?: readonly string[] | null;
    }[];
}

/**
 * The models a scene asks for, each as its request names it: the resolved parts with the scene's
 * clips, and the wrecks and death clones with clips of their own. The store keys what it holds the
 * same way, so this is what `retain` keeps.
 */
export function modelRequestsOf(
    scene: SceneModels,
): Pick<ModelRequest, "modelReference" | "animations">[] {
    const requests: Pick<ModelRequest, "modelReference" | "animations">[] =
        scene.parts
            .filter((part) => part.resolved)
            .map((part) => ({ modelReference: part.modelRef }));

    for (const prop of scene.breakoffProps ?? []) {
        if ((prop.modelRef ?? "") !== "") {
            requests.push({
                modelReference: prop.modelRef ?? "",
                animations: [...(prop.animations ?? [])],
            });
        }
    }

    for (const clone of scene.deathClones ?? []) {
        if ((clone.modelFile ?? "") !== "") {
            requests.push({
                modelReference: clone.modelFile ?? "",
                animations: [...(clone.animations ?? [])],
            });
        }
    }

    return requests;
}

export class ModelStore<TDetail = unknown> {
    private readonly models = new Map<string, Held>();
    private readonly details = new Map<string, TDetail>();
    /**
     * The latest request each part is waiting on, so its reply finds the model it was for and a
     * reply to an earlier request is told apart.
     */
    private readonly asked = new Map<
        string,
        { key: string; modelReference: string; request: number }
    >();
    /** The number of the last request made. */
    private sequence = 0;

    /**
     * The key a model is held under. The clips are part of it: the same file baked with other clips
     * is another GLB.
     */
    static keyOf(
        modelReference: string,
        animations?: readonly string[],
    ): string {
        return `${modelReference.toLowerCase()}|${(animations ?? []).join(",").toLowerCase()}`;
    }

    /** How many models are held. */
    get size(): number {
        return this.models.size;
    }

    /**
     * Notes a request on its way out and says which version is held for it, if one is.
     *
     * @returns The version to send as `have`, or undefined when the model is not held.
     */
    asking(request: ModelRequest): string | undefined {
        const key = ModelStore.keyOf(
            request.modelReference,
            request.animations,
        );
        this.asked.set(request.partId, {
            key,
            modelReference: request.modelReference,
            request: ++this.sequence,
        });
        return this.models.get(key)?.version;
    }

    /** The number of the request a part is waiting on, to send with it and have echoed back. */
    requestOf(partId: string): number | undefined {
        return this.asked.get(partId)?.request;
    }

    /**
     * Whether a reply answers the request its part is waiting on. False for a reply to an earlier
     * request for that part, and for one a new scene overtook: neither is anything to show or to
     * count. A reply that names no request is taken for the one being waited on.
     */
    awaits(reply: Pick<ModelReply, "partId" | "request">): boolean {
        const request = this.asked.get(reply.partId);
        return (
            request !== undefined &&
            (reply.request === undefined || reply.request === request.request)
        );
    }

    /**
     * The bytes a reply stands for: the ones it carries, which are kept, or the ones already held
     * when the server says they still stand. Null when there are none - the model did not load, or
     * the reply is not the answer to the request its part is waiting on.
     */
    take(reply: ModelReply): TakenModel | null {
        const request = this.asked.get(reply.partId);
        if (request === undefined || !this.awaits(reply)) {
            return null;
        }
        this.asked.delete(reply.partId);

        const data = reply.data ?? null;
        if (data !== null) {
            const version = reply.version ?? null;
            const previous = this.models.get(request.key);
            if (version !== null) {
                this.models.set(request.key, { version, bytes: data });
            }
            // Bytes in place of OTHER bytes are a changed file: what was read off the old one no
            // longer describes it. A model arriving for the first time has nothing to outdate - its
            // detail is asked for beside it and may well land first.
            if (previous !== undefined && previous.version !== version) {
                this.details.delete(request.modelReference.toLowerCase());
            }
            return {
                bytes: data,
                modelReference: request.modelReference,
                fresh: true,
                replaced:
                    previous !== undefined && previous.version !== version,
            };
        }

        if (reply.unchanged === true) {
            const held = this.models.get(request.key);
            return held === undefined
                ? null
                : {
                      bytes: held.bytes,
                      modelReference: request.modelReference,
                      fresh: false,
                      replaced: false,
                  };
        }

        return null;
    }

    /**
     * Lets go of every model the scene has no part for, and of its detail.
     *
     * @param requests The model each part of the new scene is asked for by.
     */
    retain(
        requests: Iterable<Pick<ModelRequest, "modelReference" | "animations">>,
    ): void {
        const keys = new Set<string>();
        const references = new Set<string>();
        for (const request of requests) {
            keys.add(
                ModelStore.keyOf(request.modelReference, request.animations),
            );
            references.add(request.modelReference.toLowerCase());
        }

        for (const key of [...this.models.keys()]) {
            if (!keys.has(key)) {
                this.models.delete(key);
            }
        }
        for (const reference of [...this.details.keys()]) {
            if (!references.has(reference)) {
                this.details.delete(reference);
            }
        }
        // A reply still on its way belongs to the scene that asked; this one asks for itself.
        this.asked.clear();
    }

    /** The detail kept for a model, or null. */
    detailOf(modelReference: string): TDetail | null {
        return this.details.get(modelReference.toLowerCase()) ?? null;
    }

    /** Keeps a model's detail until its bytes change or the scene stops using it. */
    keepDetail(modelReference: string, detail: TDetail): void {
        this.details.set(modelReference.toLowerCase(), detail);
    }
}
