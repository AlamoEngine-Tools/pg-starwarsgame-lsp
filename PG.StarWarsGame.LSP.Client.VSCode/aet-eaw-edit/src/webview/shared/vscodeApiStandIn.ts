// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The VS Code API stood in for, for a unit test whose module graph reaches `vscodeApi`. That module
// calls `acquireVsCodeApi` the moment it loads, so this has to be the test's FIRST import - imports run
// in order. What the page would post goes nowhere.

(globalThis as { acquireVsCodeApi?: () => unknown }).acquireVsCodeApi = () => ({
    postMessage: (): void => undefined,
});

export {};
