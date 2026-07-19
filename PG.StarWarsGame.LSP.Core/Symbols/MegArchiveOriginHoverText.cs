// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

public static class MegArchiveOriginHoverText
{
    public static string Describe(MegArchiveOrigin origin)
    {
        var archiveName = Path.GetFileName(origin.ArchivePath);
        return
            $"📦 Packed in `{archiveName}` → `{origin.InternalPath}` - read-only, cannot be renamed or navigated to.";
    }
}