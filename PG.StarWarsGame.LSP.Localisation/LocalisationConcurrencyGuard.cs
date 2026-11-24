// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Localisation;

// Shared "did this file change since the caller last fetched it" check for the structured
// localisation write requests. Strict by design (project convention): a missing hash is rejected
// outright rather than silently skipping the check, and a mismatch is rejected rather than
// attempting any kind of merge.
public static class LocalisationConcurrencyGuard
{
    /// <param name="currentContent">The file's text as it stands, which the batch is composed from.</param>
    /// <param name="expectedContentHash">The hash of the text the caller last read.</param>
    /// <returns>Null when they agree; otherwise the refusal.</returns>
    public static LocalisationWriteResult? Check(
        string currentContent, string? expectedContentHash, IMessageCatalog messages)
    {
        if (string.IsNullOrWhiteSpace(expectedContentHash))
            return LocalisationWriteResult.Fail(
                messages.Format("LocConcurrency_NoContentHashProvidedFetch"));

        var currentHash = LocalisationContentHash.Compute(currentContent);
        if (!string.Equals(currentHash, expectedContentHash, StringComparison.Ordinal))
            return LocalisationWriteResult.Fail(
                messages.Format("LocConcurrency_FileChangedDiskSinceWas"));

        return null;
    }
}