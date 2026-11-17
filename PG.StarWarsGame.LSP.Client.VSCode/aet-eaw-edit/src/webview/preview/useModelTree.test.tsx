// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { TREE_KINDS } from "./previewLabels";
import {
    type ModelTree,
    type ModelTreeInputs,
    useModelTree,
} from "./useModelTree";

const noop = (): void => undefined;

function inputs(overrides: Partial<ModelTreeInputs>): ModelTreeInputs {
    return {
        alt: 0,
        hiddenEmitters: new Set(),
        levels: { alt: [], lod: [] },
        lod: 0,
        searchPressed: false,
        selected: new Set(),
        setAlt: noop,
        setHiddenEmitters: noop,
        setLod: noop,
        setTreeItems: noop,
        treeItems: [],
        viewportRef: { current: null },
        ...overrides,
    };
}

/** What the tree holds on the first render, through a component that only calls it. */
function firstRender(given: ModelTreeInputs): ModelTree {
    let seen: ModelTree | undefined;
    const Probe = (): null => {
        seen = useModelTree(given);
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useModelTree", () => {
    it("opens with every kind shown, nothing folded and no filter", () => {
        const tree = firstRender(inputs({}));

        assert.equal(tree.kinds.size, TREE_KINDS.length);
        assert.equal(tree.collapsed.size, 0);
        assert.equal(tree.boneFilter, "");
        assert.equal(tree.searchOpen, false);
        assert.deepEqual(tree.treeRows, []);
        assert.equal(tree.unfilteredRowCount, 0);
    });

    it("keeps the search open while it is pressed", () => {
        assert.equal(
            firstRender(inputs({ searchPressed: true })).searchOpen,
            true,
        );
    });

    it("counts a model at rest as untouched and a damaged one as touched", () => {
        assert.equal(firstRender(inputs({})).touched, false);
        assert.equal(firstRender(inputs({ alt: 1 })).touched, true);
        assert.equal(
            firstRender(inputs({ hiddenEmitters: new Set([0]) })).touched,
            true,
        );
    });

    it("counts the highest defined LOD as at rest and a lower one as touched", () => {
        const levels = { alt: [], lod: [0, 1, 2] };

        assert.equal(firstRender(inputs({ levels, lod: 2 })).touched, false);
        assert.equal(firstRender(inputs({ levels, lod: 0 })).touched, true);
    });
});
