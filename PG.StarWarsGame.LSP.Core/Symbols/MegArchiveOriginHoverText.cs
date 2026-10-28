// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public static class MegArchiveOriginHoverText
{
    public static string Describe(MegArchiveOrigin origin, IMessageCatalog messages)
    {
        var archiveName = Path.GetFileName(origin.ArchivePath);
        return messages.Format("MegArchiveOrigin_Packed", archiveName, origin.InternalPath);
    }
}