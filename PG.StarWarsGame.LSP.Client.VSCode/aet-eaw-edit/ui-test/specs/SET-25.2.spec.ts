// SET-25, Notes: a Data folder without the game's folders is not a mod's either.
import path from "node:path";
import { notMod } from "./notAMod.js";

notMod("SET-25 Notes", path.join("C:\\temp\\aet-test-SET-25\\Notes", "Data", "Docs", "todo.xml"));
