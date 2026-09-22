// // Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core;

public sealed class AppProperties
{
    /// <summary>
    ///     The <c>source</c> every diagnostic this server publishes is stamped with.
    /// </summary>
    /// <remarks>
    ///     A constant, not a settable field. It was writable for no reason anyone acted on - nothing
    ///     in the tree ever assigned it - but a reassignment would have re-branded every diagnostic
    ///     already on screen, and the editor groups and filters by this string.
    /// </remarks>
    public const string LspServerId = "aet.pg.swg.lsp";
}