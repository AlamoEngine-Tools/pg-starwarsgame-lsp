// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Symbols;

public static class GameSymbolKinds
{
    /// <summary>
    ///     The symbol kind a reference of the given <see cref="ReferenceKind" /> resolves against, or
    ///     <c>null</c> when the kind names something the index does not hold as a symbol (an enum
    ///     value, a bone, a hardcoded set) and so cannot be a <see cref="GameReference" />.
    /// </summary>
    public static GameSymbolKind? FromReferenceKind(ReferenceKind kind)
    {
        return kind switch
        {
            ReferenceKind.XmlObject => GameSymbolKind.XmlObject,
            ReferenceKind.LocalisationKey => GameSymbolKind.LocalisationKey,
            ReferenceKind.WorkspaceFile => GameSymbolKind.WorkspaceFile,
            ReferenceKind.ModelFile or ReferenceKind.TextureFile or ReferenceKind.AudioFile
                or ReferenceKind.MapFile => GameSymbolKind.Asset,
            _ => null
        };
    }
}
