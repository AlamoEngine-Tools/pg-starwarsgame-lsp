// SET-25, BareXml: an xml folder that is not a mod's activates the extension and nothing else.
import path from "node:path";
import { notMod } from "./notAMod.js";

notMod("SET-25 BareXml", path.join("C:\\temp\\aet-test-SET-25\\BareXml", "xml", "test.xml"));
