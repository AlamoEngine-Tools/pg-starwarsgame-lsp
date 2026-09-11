// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>Shared context passed to every <see cref="IXmlDiagnosticsHandler" /> invocation.</summary>
/// <param name="IconsAwaitingRepack">
///     Icon base names (no extension) that exist as raw source images but are absent from the
///     workspace's mega texture. Empty when the workspace ships no mega texture, since there is then
///     nothing for the sources to be out of sync with. Optional so that every existing construction
///     site, and every test, keeps working unchanged.
/// </param>
/// <param name="IconNames">
///     The GUI art the project's mega textures hold. Consulted only when a texture reference has
///     already failed to resolve as a file, so a workspace that ships no mega texture pays nothing.
///     Optional: without one, the file lookup decides on its own exactly as before.
/// </param>
/// <param name="Objects">
///     Resolves an object OTHER than the one being edited, variant chain merged - the seam a
///     cross-object rule needs ("does the object this tag names have behaviour X"). Optional, and a
///     handler that uses it must stay silent when it is absent: a context without one is a caller
///     that cannot answer the question, not an object that fails the check.
/// </param>
public record DiagnosticsContext(
    ISchemaProvider Schema,
    GameIndex Index,
    string DocumentUri,
    string Locale,
    IReadOnlySet<string>? IconsAwaitingRepack = null,
    Assets.IModelTextureIndex? ModelTextures = null,
    Assets.IIconNameIndex? IconNames = null,
    Symbols.IEffectiveObjectSource? Objects = null);

/// <summary>
///     Supplies the icons a workspace has drawn but not yet repacked.
/// </summary>
/// <remarks>
///     Defined in Core so the Xml diagnostics pipeline can consume it without referencing the server
///     project that builds the icon catalog.
/// </remarks>
public interface IIconRepackStatusProvider
{
    /// <summary>Base names awaiting a repack; empty when nothing is known or nothing is stale.</summary>
    IReadOnlySet<string> IconsAwaitingRepack { get; }
}