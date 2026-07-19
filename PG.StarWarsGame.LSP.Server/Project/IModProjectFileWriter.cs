// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Project;

public interface IModProjectFileWriter
{
    // Sets (or replaces) the top-level "localisation" node of the .pgproj at pgprojPath to
    // { type, directory } and writes it back, preserving every other property.
    Task SetLocalisationAsync(string pgprojPath, string type, string directory, CancellationToken ct);
}