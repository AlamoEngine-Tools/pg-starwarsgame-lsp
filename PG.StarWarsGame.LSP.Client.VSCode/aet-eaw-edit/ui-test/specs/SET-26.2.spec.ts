// SET-26, the game's Data folder: never a mod either.
import path from "node:path";
import { notMod } from "./notAMod.js";

notMod("SET-26 FakeGame Data", path.join("C:\\temp\\aet-test-SET-26\\FakeGame", "Data", "XML", "test.xml"));
