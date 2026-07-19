// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Marks a <see cref="GameReference.TargetId" /> as naming an owner-scoped symbol by its bare name,
///     to be resolved against any owner (see <see cref="Schema.TagSemanticType.OwnerAgnosticReference" />).
///     <para>
///         The marker rides in the id rather than in a new <see cref="GameReference" /> field because the
///         record is persisted to the on-disk index cache, so widening it would mean a serialization key
///         and a cache schema bump. Encoding intent in the id is the same convention the synthetic
///         <c>enum:{EnumName}/{Value}</c> references already use.
///     </para>
///     <para>
///         Consumers must strip the marker before showing an id to the user or matching it against the
///         symbol index - <see cref="TryGetBareName" /> exists so no caller has to know the literal.
///     </para>
/// </summary>
public static class OwnerAgnosticReferenceId
{
    private const string Prefix = "ability:";

    /// <summary>Wraps a bare symbol name so resolution knows to ignore the owner scope.</summary>
    public static string Create(string bareName)
    {
        return Prefix + bareName;
    }

    /// <summary>
    ///     True when <paramref name="targetId" /> carries the marker, yielding the bare name it wraps.
    /// </summary>
    public static bool TryGetBareName(string targetId, out string bareName)
    {
        if (targetId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            bareName = targetId[Prefix.Length..];
            return true;
        }

        bareName = targetId;
        return false;
    }

    /// <summary>The bare name, whether or not <paramref name="targetId" /> carries the marker.</summary>
    public static string StripMarker(string targetId)
    {
        TryGetBareName(targetId, out var bare);
        return bare;
    }
}
