// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Shared user-facing error returned by every aet/* localisation request that arrives while
///     the <c>features.tools.localisation</c> flag is off. A well-behaved client never sends these
///     (its own commands and views are gated on the same flag), so this surfaces only for stale or
///     misconfigured clients — the message tells the user exactly which setting to flip.
/// </summary>
public static class LocalisationFeatureDisabled
{
    public const string Message =
        "Localisation features are disabled. Enable 'aet-eaw-edit.features.tools.localisation' in the editor settings.";
}
