// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

// Whether the root .pgproj already declares a "localisation" node - the same precedence check
// InitLocalisationProjectCommandHandler makes, exposed so the client knows whether to prompt for
// format/directory or just confirm.
public sealed record GetRootLocalisationConfigResult(bool Configured, string? Type, string? Directory)
{
    public static readonly GetRootLocalisationConfigResult NotConfigured = new(false, null, null);
}