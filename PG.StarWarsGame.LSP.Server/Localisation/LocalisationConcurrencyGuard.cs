// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;

namespace PG.StarWarsGame.LSP.Server.Localisation;

// Shared "did this file change since the caller last fetched it" check for the structured
// localisation write requests. Strict by design (project convention): a missing hash is rejected
// outright rather than silently skipping the check, and a mismatch is rejected rather than
// attempting any kind of merge.
public static class LocalisationConcurrencyGuard
{
    public static async Task<LocalisationWriteResult?> CheckAsync(
        IFileSystem fs, string filePath, string? expectedContentHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedContentHash))
            return LocalisationWriteResult.Fail(
                "No content hash provided — fetch the current entries before writing.");

        var currentContent = await fs.File.ReadAllTextAsync(filePath, ct);
        var currentHash = LocalisationContentHash.Compute(currentContent);
        if (!string.Equals(currentHash, expectedContentHash, StringComparison.Ordinal))
            return LocalisationWriteResult.Fail(
                "The file changed on disk since it was last loaded. Reload before editing again.");

        return null;
    }
}
