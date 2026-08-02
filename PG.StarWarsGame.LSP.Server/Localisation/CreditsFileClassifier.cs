// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>The kind of localisation file, as carried on <see cref="LocProjectInfo" />.</summary>
public static class LocCategory
{
    /// <summary>Keyed: one entry per key, order irrelevant. The usual MasterText case.</summary>
    public const string Text = "text";

    /// <summary>Ordered: duplicate keys allowed, row order significant.</summary>
    public const string Credits = "credits";
}

/// <summary>
///     Decides whether a localisation file is a credits file, from its name and the project's
///     optional <c>localisation.credits</c> settings.
///     <para>
///         Name-based by default because the engine's own files follow the convention and no
///         existing <c>.pgproj</c> declares anything; the explicit list exists so a mod that
///         deviates is not stuck with a wrong answer, and <c>none</c> so one whose text file merely
///         looks like a credits file can say so.
///     </para>
/// </summary>
public static class CreditsFileClassifier
{
    private const string ConventionPrefix = "credits";

    public static string Classify(string filePath, LocalisationCreditsSettings? settings)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName)) return LocCategory.Text;

        // Absent settings behave as the default so a project that declares nothing still gets the
        // engine's naming honoured.
        var detection = settings?.Detection ?? LocalisationCreditsSettings.Convention;

        if (string.Equals(detection, LocalisationCreditsSettings.None, StringComparison.OrdinalIgnoreCase))
            return LocCategory.Text;

        var byConvention =
            string.Equals(detection, LocalisationCreditsSettings.Convention, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(fileName)
                .StartsWith(ConventionPrefix, StringComparison.OrdinalIgnoreCase);

        return byConvention || IsListed(fileName, settings?.Files)
            ? LocCategory.Credits
            : LocCategory.Text;
    }

    /// <summary>
    ///     Whether the file is named in the project's list. Matched with or without the extension:
    ///     an entry that names the file unambiguously should work either way, and silently ignoring
    ///     <c>"creditstext"</c> because it lacks <c>.csv</c> would be a configuration that looks
    ///     applied but is not.
    /// </summary>
    private static bool IsListed(string fileName, IReadOnlyList<string>? files)
    {
        if (files is null || files.Count == 0) return false;

        var bare = Path.GetFileNameWithoutExtension(fileName);

        foreach (var entry in files)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;

            var trimmed = entry.Trim();
            if (string.Equals(trimmed, fileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, bare, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
