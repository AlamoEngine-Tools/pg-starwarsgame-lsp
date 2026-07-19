// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

// Shared result shape for the structured localisation write requests (set/delete/add-language) —
// a real Success/Error field, unlike the fire-and-forget ExecuteCommandHandlerBase commands
// (createLocalisationKey, initLocalisationProject, importLocalisationProject), because these are
// called frequently from the live editor grid and it needs to know precisely whether an edit
// landed (in particular, whether the concurrency guard rejected a stale write).
public sealed record LocalisationWriteResult(bool Success, string? Error, string? NewContentHash)
{
    public static LocalisationWriteResult Ok(string newContentHash)
    {
        return new LocalisationWriteResult(true, null, newContentHash);
    }

    public static LocalisationWriteResult Fail(string error)
    {
        return new LocalisationWriteResult(false, error, null);
    }
}