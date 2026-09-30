// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>Whether an object is of a kind, including the honest third answer.</summary>
public enum KindMatch
{
    /// <summary>The index cannot judge this predicate - it does not hold the facts it rests on.</summary>
    Unknown,

    /// <summary>The object satisfies every group the kind declares.</summary>
    Yes,

    /// <summary>A group the kind declares definitely fails.</summary>
    No
}

/// <summary>
///     Decides whether an object satisfies a kind's predicate: any entry within a group, every
///     group across the kind.
/// </summary>
/// <remarks>
///     <para>
///         Behaviours come through <see cref="GameIndex.BehaviorsOf" />, so a variant answers with
///         its base's - which is what the engine reads. Flags come off the symbol. Membership comes
///         from nowhere yet: no index captures which squadron lists a unit, so a kind resting on it
///         answers <see cref="KindMatch.Unknown" /> rather than pretending.
///     </para>
///     <para>
///         Unknown is not a failure and must not be treated as one. Completion proposes an Unknown
///         candidate, because hiding a name the author needs is worse than offering one they do not;
///         validation stays silent on it, because an error on data we cannot read is the one outcome
///         with no recovery for the author.
///     </para>
/// </remarks>
public static class ObjectKinds
{
    public static KindMatch Match(GameIndex index, GameSymbol symbol, ObjectKindDefinition kind)
    {
        var unknown = false;

        if (kind.Behaviors.Count > 0)
        {
            var behaviors = index.BehaviorsOf(symbol);
            if (!kind.Behaviors.Any(behaviors.Contains))
                return KindMatch.No;
        }

        if (kind.Flags.Count > 0)
        {
            if (symbol.Flags is null)
                unknown = true;
            else if (!kind.Flags.Any(f => symbol.Flags.Contains(f, StringComparer.OrdinalIgnoreCase)))
                return KindMatch.No;
        }

        // Nothing records that a unit is listed in some squadron's or company's unit list. The
        // engine sets that flag after load; the index has no equivalent pass, so this cannot be
        // answered at all today.
        if (kind.MemberOf.Count > 0)
            unknown = true;

        if (!kind.HasPredicate)
            unknown = true;

        return unknown ? KindMatch.Unknown : KindMatch.Yes;
    }

    /// <summary>Whether a kind reference should PROPOSE this object: anything not ruled out.</summary>
    public static bool CanPropose(GameIndex index, GameSymbol symbol, ObjectKindDefinition kind)
    {
        return Match(index, symbol, kind) != KindMatch.No;
    }

    /// <summary>
    ///     The boolean tags worth carrying on a symbol: exactly the ones some kind tests. Indexing
    ///     every true boolean an object declares would be most of its tags for no one's benefit,
    ///     and hardcoding the two heroes use would quietly break the next kind that needs another.
    /// </summary>
    public static IReadOnlyCollection<string> FlagTagsUsedBy(IEnumerable<ObjectKindDefinition> kinds)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in kinds)
        foreach (var flag in kind.Flags)
            tags.Add(flag);
        return tags;
    }
}