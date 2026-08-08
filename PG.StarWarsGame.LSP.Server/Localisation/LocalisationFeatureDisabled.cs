// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Shared gating for every <c>aet/*</c> localisation request, in the shape
///     <see cref="Story.StoryEditorFeature" /> uses.
///     <para>
///         A well-behaved client never sends these while the flag is off - its own commands and
///         views are gated on the same flag - so this surfaces only for stale or misconfigured
///         clients, and the message tells the user exactly which setting to flip.
///     </para>
///     <para>
///         The message was shared here from the start; the <em>check</em> was not, and fifteen
///         handlers had written out <c>config.Current.Features.Tools.Localisation</c> themselves.
///         That is one place per handler for a second flag to be forgotten, which is precisely what
///         <see cref="Story.StoryEditingFeature" /> exists to avoid on the story side.
///     </para>
/// </summary>
public static class LocalisationFeatureDisabled
{
    public const string Message =
        "Localisation features are disabled. Enable 'aet-eaw-edit.features.tools.localisation' in the editor settings.";

    /// <summary>
    ///     Whether the localisation tools may run at all.
    ///     <para>
    ///         For the handlers that answer a disabled request with an empty result rather than an
    ///         error - a language list, a baseline - where there is no message to carry.
    ///     </para>
    /// </summary>
    public static bool IsEnabled(ILspConfigurationProvider config)
    {
        return config.Current.Features.Tools.Localisation;
    }

    /// <summary>
    ///     The reason to refuse, or null to proceed. For the handlers that report why.
    /// </summary>
    public static string? Rejection(ILspConfigurationProvider config)
    {
        return IsEnabled(config) ? null : Message;
    }
}
