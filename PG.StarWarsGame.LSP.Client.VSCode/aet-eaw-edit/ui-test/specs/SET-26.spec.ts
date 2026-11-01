// SET-26, the game's own folder: never a mod.
import path from "node:path";
import { notMod } from "./notAMod.js";

notMod("SET-26 FakeGame", path.join("C:\\temp\\aet-test-SET-26\\FakeGame", "Data", "XML", "test.xml"));
