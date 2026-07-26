// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Lua.Diagnostics;

/// <summary>
///     Maps Loretta's parse-error ids onto ours.
///     <para>
///         Loretta already numbers its diagnostics <c>LUA0014</c>, <c>LUA1012</c> and so on, so the
///         mapping is just the prefix stripped: the number carries straight over into
///         <see cref="DiagnosticGroup.Syntax" />. That keeps assignment mechanical - there is no
///         hand-maintained table to fall out of date when Loretta adds a code - and it means a
///         suppression a user writes stays meaningful against Loretta's own documentation.
///     </para>
/// </summary>
public static class LorettaDiagnosticIds
{
    /// <summary>
    ///     First number in <see cref="DiagnosticGroup.Syntax" /> not reserved for Loretta. Loretta's
    ///     highest code is 2000 as of 0.2.13; the band is rounded up to leave it room to grow, and
    ///     any syntax diagnostic of our own must be numbered from here.
    /// </summary>
    public const int FirstOwnSyntaxNumber = 3000;

    private const string Prefix = "LUA";
    private const int DigitCount = 4;

    /// <summary>
    ///     Maps <c>LUA####</c> to a <see cref="DiagnosticId" />. Returns false for anything else -
    ///     an unrecognised shape, or a number past what a <see cref="DiagnosticId" /> can hold.
    ///     <para>
    ///         Failing quietly is deliberate. Loretta is a dependency whose id shapes we do not
    ///         control, and a diagnostic without an id is merely unsuppressable - visible, and
    ///         therefore safe. Throwing would lose every diagnostic in the document instead.
    ///     </para>
    /// </summary>
    public static bool TryMap(string? lorettaId, out DiagnosticId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(lorettaId)) return false;

        var span = lorettaId.AsSpan().Trim();
        if (span.Length != Prefix.Length + DigitCount) return false;
        if (!span[..Prefix.Length].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        if (!int.TryParse(span[Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture,
                out var number))
            return false;

        id = new DiagnosticId(DiagnosticGroup.Syntax, number);
        return true;
    }
}
