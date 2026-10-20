// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Core.Messages;

/// <summary>
///     The server's catalogue in whatever locale the configuration names at the time of the call.
/// </summary>
/// <remarks>
///     The locale is read per call rather than once, so a changed setting takes effect on the next
///     text without a restart - the same as the diagnostics, which pick their catalogue per run.
/// </remarks>
public sealed class ConfiguredMessageCatalog(ILspConfigurationProvider configuration) : IMessageCatalog
{
    public string Format(string key, params object?[] args)
    {
        return ResxMessageCatalog.Server(configuration.Current.Locale).Format(key, args);
    }
}