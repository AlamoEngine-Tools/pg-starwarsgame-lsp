// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Persistence;

/// <summary>Whether a <c>.pgproj</c> can be loaded, and what to tell the user when it cannot.</summary>
public sealed record PgprojFormatCheck(bool CanLoad, string? Message);

/// <summary>
///     The format contract for the user's own project file.
///     <para>
///         It identifies itself the way every other document here does - <c>_type</c> and
///         <c>_typeVersion</c>, Scout's two fields - rather than carrying a second convention
///         invented for this one file. That is also what lets the same <see cref="TypeVersion" />
///         parsing serve both.
///     </para>
///     <para>
///         The fields earn their keep before any migration exists. Today the server ignores
///         properties it does not know while the shipped <c>pgproj.schema.json</c> is
///         <c>additionalProperties: false</c> - so a project written by a newer extension loads on an
///         older one while the editor paints every new key as an error. With a declared version, the
///         older extension can say "this project needs a newer aet-eaw-edit" instead. That only works
///         if the fields are already there when the first change lands, which is why they go in now
///         and why both writers stamp them.
///     </para>
///     <para>
///         Refusal is deliberately stricter than <c>SchemaVersionGate</c>, which blocks only on a
///         MAJOR change. A game schema is data we read; a <c>.pgproj</c> is a document we also
///         migrate and write back, and the write is what makes reading a newer one dangerous: fall
///         back to defaults, save, and whatever the newer extension put there is gone. So any
///         version above ours is refused, patch bumps included.
///     </para>
/// </summary>
public static class PgprojFormat
{
    /// <summary>The document's <c>_type</c>.</summary>
    public const string TypeName = "aetswg.ModProject";

    /// <summary>
    ///     The highest format this build understands, and the one both writers stamp. A file that
    ///     declares nothing is this version: every <c>.pgproj</c> written so far predates the fields.
    /// </summary>
    public static readonly TypeVersion Current = TypeVersion.Of("aetswg", 1);

    public static PgprojFormatCheck Check(string? declaredVersion, string? declaredType = null)
    {
        // A file claiming to be some other document is not this one at an odd version; reading it
        // anyway would adopt whatever fields happened to match.
        if (declaredType is not null && !string.Equals(declaredType, TypeName, StringComparison.Ordinal))
            return new PgprojFormatCheck(false,
                $"This file declares itself as '{declaredType}', not a '{TypeName}'. It cannot be "
                + "opened as a mod project.");

        if (declaredVersion is null) return new PgprojFormatCheck(true, null);

        if (!TypeVersion.TryParse(declaredVersion.Trim(), out var declared))
            return new PgprojFormatCheck(false,
                $"This project declares an unreadable format version '{declaredVersion}'. It must look "
                + $"like '{Current}'. Fix or remove '_typeVersion' to open the project.");

        if (!string.Equals(declared.Namespace, Current.Namespace, StringComparison.Ordinal))
            return new PgprojFormatCheck(false,
                $"This project declares version '{declared}', which is not a version of "
                + $"'{Current.Namespace}'. It cannot be opened as a mod project.");

        if (declared.CompareTo(Current) <= 0) return new PgprojFormatCheck(true, null);

        return new PgprojFormatCheck(false,
            $"This project is in format {declared.Version}, which this version of aet-eaw-edit does "
            + $"not understand (it reads and writes {Current.Version}). Update the extension to open "
            + "it - opening it anyway would risk writing over settings a newer version put there.");
    }
}
