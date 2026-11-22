// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The models the preview holds: each one as it was read from its GLB bytes, the version the bytes
// came with, and the detail read off the model's file (#436).
//
// A scene refresh takes every part out of the viewport and asks for all of them again. What it asks
// with is the version it holds; the server answers "still yours" for a model whose file did not
// change, and the part is rebuilt from the model kept here - nothing is read, converted or sent. A
// model the new scene has no part for is let go, so what is kept is the scene on screen and nothing
// from the ones before it.
//
// The bytes themselves are not kept (#442). They are read once, by the reader the store is given,
// and what that made of them is the one copy of the model: kept beside it, the bytes were a second.
//
// A model is asked for once, however many parts of the scene name it (#442). Every part asks before
// any reply is in, so twenty turrets of one model were twenty requests and the model was sent twenty
// times. Only the first part's request goes out; the others wait on it, and its reply - the model, the
// word that the one held still stands, or nothing - is the answer to each of them.
//
// Every request is numbered and the host echoes the number on its reply. Two refreshes close
// together ask for the same part twice, and the replies land in whatever order the server and the
// pipe finish them; only the reply to the latest request for a part is that part's model.
//
// Not React and not the viewport: models and a ledger, so it is tested on its own.

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

/** The model for a part, and whether it is new to this store. */
export interface TakenModel<TModel> {
    model: TModel;
    /** The model the part was asked for by. */
    modelReference: string;
    /** True when the reply carried the model's bytes; false when it stands for the model held. */
    fresh: boolean;
    /** True when the model took the place of an older version: the model's file changed. */
    replaced: boolean;
}

interface Held<TModel> {
    version: string;
    model: TModel;
}

/** A request that went out, and the parts that asked for the same model while its reply was due. */
interface Outgoing<TRequest> {
    /** The part whose request went out. */
    partId: string;
    waiting: Map<string, TRequest>;
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

export class ModelStore<
    TDetail,
    TModel,
    TRequest extends ModelRequest = ModelRequest,
> {
    private readonly models = new Map<string, Held<TModel>>();
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
    /** The request out for each model, by the model's key, until its reply is taken. */
    private readonly outgoing = new Map<string, Outgoing<TRequest>>();
    /** What each part that waited on another's request is answered with, until it takes it. */
    private readonly settled = new Map<string, TakenModel<TModel> | null>();
    /** The parts a taken reply answers beside its own, by the part the reply was for. */
    private readonly answered = new Map<string, TRequest[]>();

    /**
     * @param read Makes a model of its bytes. Called once for each version of a model; the bytes
     * are not kept.
     */
    constructor(private readonly read: (bytes: Uint8Array) => TModel) {}

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
     * The first part of a scene to ask for a model sends its request. A part asking for the same
     * model while that reply is due waits on it instead: see {@link rides}.
     *
     * @returns The version to send as `have`, or undefined when the model is not held.
     */
    asking(request: TRequest): string | undefined {
        const key = ModelStore.keyOf(
            request.modelReference,
            request.animations,
        );
        const before = this.asked.get(request.partId);
        if (before !== undefined && before.key !== key) {
            this.outgoing.get(before.key)?.waiting.delete(request.partId);
        }
        this.settled.delete(request.partId);
        this.asked.set(request.partId, {
            key,
            modelReference: request.modelReference,
            request: ++this.sequence,
        });

        // The part whose request is out asking again goes out again: the reply to its earlier
        // request is no longer the one it waits on.
        const out = this.outgoing.get(key);
        if (out === undefined) {
            this.outgoing.set(key, {
                partId: request.partId,
                waiting: new Map(),
            });
        } else if (out.partId !== request.partId) {
            out.waiting.set(request.partId, request);
        }

        return this.models.get(key)?.version;
    }

    /**
     * Whether a part's request stays here: another part of the scene asked for the same model and
     * its reply is due, which answers this part too.
     */
    rides(partId: string): boolean {
        const request = this.asked.get(partId);
        return (
            request !== undefined &&
            (this.outgoing.get(request.key)?.waiting.has(partId) ?? false)
        );
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
     * The model a reply stands for: the one read from the bytes it carries, which is kept, or the
     * one already held when the server says it still stands. Null when there is none - the model
     * did not load, or the reply is not the answer to the request its part is waiting on.
     *
     * A reply also answers every part that asked for its model while it was due: see
     * {@link answeredWith}.
     */
    take(reply: ModelReply): TakenModel<TModel> | null {
        const request = this.asked.get(reply.partId);
        if (request === undefined || !this.awaits(reply)) {
            return null;
        }
        this.asked.delete(reply.partId);

        // A part that waited on another's request: what that reply came to.
        if (this.settled.has(reply.partId)) {
            const settled = this.settled.get(reply.partId) ?? null;
            this.settled.delete(reply.partId);
            return settled;
        }

        const taken = this.modelOf(request, reply);

        const out = this.outgoing.get(request.key);
        if (out?.partId === reply.partId) {
            this.outgoing.delete(request.key);
            for (const waiting of out.waiting.values()) {
                this.settled.set(
                    waiting.partId,
                    taken === null
                        ? null
                        : {
                              model: taken.model,
                              modelReference: waiting.modelReference,
                              fresh: false,
                              replaced: false,
                          },
                );
            }
            if (out.waiting.size > 0) {
                this.answered.set(reply.partId, [...out.waiting.values()]);
            }
        }

        return taken;
    }

    /**
     * The requests that stayed here because this part's was out, once its reply is taken. Each of
     * them is answered by that reply: `take` under the part's own name gives what it came to, which
     * is nothing when the model did not load. Given once.
     */
    answeredWith(partId: string): TRequest[] {
        const waiting = this.answered.get(partId) ?? [];
        this.answered.delete(partId);
        return waiting;
    }

    /** What a reply holds for the request it answers. */
    private modelOf(
        request: { key: string; modelReference: string },
        reply: ModelReply,
    ): TakenModel<TModel> | null {
        const data = reply.data ?? null;
        if (data !== null) {
            const version = reply.version ?? null;
            const previous = this.models.get(request.key);
            const model = this.read(data);
            if (version !== null) {
                this.models.set(request.key, { version, model });
            }
            // Bytes in place of OTHER bytes are a changed file: what was read off the old one no
            // longer describes it. A model arriving for the first time has nothing to outdate - its
            // detail is asked for beside it and may well land first.
            if (previous !== undefined && previous.version !== version) {
                this.details.delete(request.modelReference.toLowerCase());
            }
            return {
                model,
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
                      model: held.model,
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
        this.outgoing.clear();
        this.settled.clear();
        this.answered.clear();
    }

    /** The detail kept for a model, or null. */
    detailOf(modelReference: string): TDetail | null {
        return this.details.get(modelReference.toLowerCase()) ?? null;
    }

    /** Keeps a model's detail until its file changes or the scene stops using it. */
    keepDetail(modelReference: string, detail: TDetail): void {
        this.details.set(modelReference.toLowerCase(), detail);
    }
}
