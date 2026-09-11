// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The behaviour list of a resolved object, as one set.
/// </summary>
/// <remarks>
///     <para>
///         Behaviours are spread across three tags, and which one an object uses is a property of
///         how it was authored rather than of what it is: measured over the shipped corpus,
///         <c>SpaceBehavior</c> carries them 98 times, plain <c>Behavior</c> 32 and
///         <c>LandBehavior</c> 19. A diagnostic that reads only one of them reports a missing
///         behaviour on an object that plainly has it, so the union is the only safe reading.
///     </para>
///     <para>
///         Takes an <see cref="EffectiveObject" /> rather than a node, so a behaviour inherited
///         through a <c>Variant_Of_Existing_Type</c> chain counts exactly as much as one written
///         directly - which is what the engine sees.
///     </para>
/// </remarks>
public static class ObjectBehaviors
{
    /// <summary>The tags that carry behaviour lists, in no particular order - the result is a union.</summary>
    public static readonly ImmutableArray<string> BehaviorTags =
        ["Behavior", "SpaceBehavior", "LandBehavior"];

    private static readonly char[] Separators = [',', ' ', '\t', '\n', '\r'];

    /// <summary>Every behaviour the object has, from any of the three tags. Empty when it has none.</summary>
    public static ImmutableHashSet<string> Of(EffectiveObject obj)
    {
        if (!obj.Found || obj.Tags.IsDefaultOrEmpty)
            return ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in obj.Tags)
        {
            if (!BehaviorTags.Contains(tag.TagName, StringComparer.OrdinalIgnoreCase)) continue;

            foreach (var token in tag.Value.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                builder.Add(token.Trim());
        }

        return builder.ToImmutable();
    }

    /// <summary>Whether the object declares the named behaviour, case-insensitively as the engine reads it.</summary>
    public static bool Has(EffectiveObject obj, string behavior)
    {
        return Of(obj).Contains(behavior);
    }
}
