// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Localisation;

// One project layer's own translation database (before it's flattened into the merged
// first-match-wins index). Lets consumers that need to reason about a *specific* file's
// dependency chain - the "Inherited" overlay, DAT export - merge only the layers below a given
// rank, rather than the single shipped-baseline-only view.
public sealed record LocalisationLayerEntry(ProjectLayer Layer, IKeyedTranslationDatabase Database);