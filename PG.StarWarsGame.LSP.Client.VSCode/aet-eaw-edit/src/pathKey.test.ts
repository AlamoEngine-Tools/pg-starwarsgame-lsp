// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { isInside, pathKey, samePath } from "./pathKey";

describe("pathKey", () => {
    it("folds case", () => {
        assert.equal(pathKey("C:/Dev/EaWX/Rev"), pathKey("c:/dev/eawx/rev"));
    });

    it("folds the separator", () => {
        assert.equal(pathKey("C:\\dev\\EaWX\\Rev"), pathKey("C:/dev/EaWX/Rev"));
    });

    it("folds a trailing separator, of either kind and however many", () => {
        const key = pathKey("c:/dev/EaWX/Rev");

        assert.equal(pathKey("c:/dev/EaWX/Rev/"), key);
        assert.equal(pathKey("c:\\dev\\EaWX\\Rev\\"), key);
        assert.equal(pathKey("c:/dev/EaWX/Rev//"), key);
    });

    it("keeps two directories apart that differ past the fold", () => {
        assert.notEqual(
            pathKey("c:/dev/EaWX/Rev"),
            pathKey("c:/dev/EaWX/Rev2"),
        );
    });

    it("spells a drive's root one way", () => {
        assert.equal(pathKey("C:\\"), pathKey("c:/"));
        assert.equal(pathKey("C:\\"), pathKey("c:"));
    });
});

describe("samePath", () => {
    it("takes two spellings of one file as one", () => {
        assert.equal(
            samePath(
                "C:\\dev\\EaWX\\Rev\\Data\\XML\\Units.xml",
                "c:/dev/eawx/rev/data/xml/units.xml",
            ),
            true,
        );
    });

    it("tells two files apart", () => {
        assert.equal(samePath("c:/dev/a.xml", "c:/dev/b.xml"), false);
    });
});

describe("isInside", () => {
    it("holds a file under the directory", () => {
        assert.equal(isInside("c:/dev/Rev/Data/XML/a.xml", "c:/dev/Rev"), true);
    });

    it("holds the directory itself", () => {
        assert.equal(isInside("c:/dev/Rev", "c:/dev/Rev"), true);
    });

    it("ends on a segment boundary", () => {
        assert.equal(isInside("c:/dev/Rev2/a.xml", "c:/dev/Rev"), false);
    });

    it("ignores case and the separator on both sides", () => {
        assert.equal(
            isInside("C:\\Dev\\REV\\Data\\a.xml", "c:/dev/rev/data"),
            true,
        );
    });

    it("holds a file under a directory spelled with a trailing separator", () => {
        assert.equal(isInside("c:/dev/Rev/Data/a.xml", "c:/dev/Rev/"), true);
        assert.equal(isInside("c:/dev/Rev/Data/a.xml", "c:\\dev\\Rev\\"), true);
    });

    it("holds the directory itself under either spelling of it", () => {
        assert.equal(isInside("c:/dev/Rev/", "c:/dev/Rev"), true);
        assert.equal(isInside("c:/dev/Rev", "c:/dev/Rev/"), true);
    });

    it("holds every file of a drive under its root", () => {
        assert.equal(isInside("c:/dev/a.xml", "C:\\"), true);
        assert.equal(isInside("d:/dev/a.xml", "C:\\"), false);
    });

    it("holds every rooted path under the root", () => {
        assert.equal(isInside("/home/mods/a.xml", "/"), true);
    });

    it("leaves a file outside the directory out", () => {
        assert.equal(isInside("c:/elsewhere/a.xml", "c:/dev/Rev"), false);
    });
});
