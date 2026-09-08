// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Recognising a folder of shader sources. Kept free of `vscode` so it can be unit tested.

import * as fs from 'fs';
import * as path from 'path';

/**
 * Where the `.fx` files sit relative to the folder the user pointed at.
 *
 * The published archive contains a `Shaders/` directory, and people extract it as-is about as often
 * as they extract its contents - so accepting only one layout would reject half of correct answers
 * with a message saying the folder is wrong.
 */
export const SHADER_SUBDIRECTORIES = ['', 'Shaders'];

/** Whether a folder actually holds shader sources, rather than merely existing. */
export function hasShaderSources(directory: string | undefined): boolean {
    if (directory === undefined || directory.trim() === '') {
        return false;
    }

    for (const sub of SHADER_SUBDIRECTORIES) {
        const candidate = sub === '' ? directory : path.join(directory, sub);

        try {
            if (fs.readdirSync(candidate).some(name => name.toLowerCase().endsWith('.fx'))) {
                return true;
            }
        } catch {
            // Missing or unreadable is the same as "not here" for this question.
        }
    }

    return false;
}
