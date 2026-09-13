// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     The tags the engine matches byte for byte, and what it does when the spelling differs.
/// </summary>
/// <remarks>
///     <para>
///         The general rule is the opposite: <c>DatabaseMapClass::Map_DB_Data_To_Class</c> copies a
///         tag name into <c>uppercase_key_name</c>, uppercases it, and matches that - so casing is
///         free for everything the table parses. These are the exceptions, and they are exceptions
///         because they have a PARSER OF THEIR OWN.
///     </para>
///     <para>
///         That is the test for adding to this list, and it is not "someone saw a bug": find the
///         comparison. A hand-rolled parser using <c>std::operator==</c> or <c>strcmp</c> belongs
///         here; one using <c>_stricmp</c> or the mapper does not. The three tags known to sit
///         outside the engine's XML field table are <c>Active_Plot</c>, <c>Suspended_Plot</c> and
///         <c>Story_Name</c> - the first two are confirmed below, and <c>Story_Name</c> has not been
///         traced yet, so it is deliberately absent rather than assumed safe.
///     </para>
/// </remarks>
public static class CaseSensitiveTags
{
    /// <summary>
    ///     What the engine does with a mis-spelled tag, and whether that differs from the intent.
    /// </summary>
    /// <param name="Consequence">Stated as the engine's behaviour, not as advice.</param>
    /// <param name="ChangesBehaviour">False where the wrong spelling reaches the right outcome anyway.</param>
    public sealed record Rule(string Consequence, bool ChangesBehaviour);

    /// <summary>
    ///     <c>StoryModeClass::Load_Plots</c> (<c>00ab3665</c>) compares each key against these two
    ///     literals with <c>std::operator==</c>, then passes <c>key == "Active_Plot"</c> straight to
    ///     <c>Load_Single_Plot</c> as its <c>is_active</c> argument.
    /// </summary>
    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        // Anything that is not byte-exactly this yields is_active = false.
        ["Active_Plot"] = new("the plot loads as suspended and never starts", true),

        // Also unrecognised, but an unrecognised plot is suspended anyway - so this one is correct
        // for the wrong reason, and only the debug build would ever mention it.
        ["Suspended_Plot"] = new("the tag is not recognised, and a plot it does not recognise is "
                                 + "suspended anyway - so this happens to do what you wanted", false),
    };

    /// <summary>
    ///     Whether this name is one of the few worth reading back out of the document text.
    /// </summary>
    /// <remarks>
    ///     Takes the name HAP already lower-cased, so the caller can skip the expensive part for the
    ///     overwhelming majority of elements. Without this the casing check reads the authored
    ///     spelling of EVERY tag in every file to answer "no" almost every time.
    /// </remarks>
    public static bool IsCandidate(string lowerCasedName)
    {
        return Rules.ContainsKey(lowerCasedName);
    }

    /// <summary>
    ///     The rule for a tag whose spelling the engine fixes, or null when casing is free - which
    ///     it is for all but a handful.
    /// </summary>
    public static Rule? For(string authoredName, out string expected)
    {
        foreach (var (canonical, rule) in Rules)
        {
            if (!canonical.Equals(authoredName, StringComparison.OrdinalIgnoreCase)) continue;

            expected = canonical;
            return canonical.Equals(authoredName, StringComparison.Ordinal) ? null : rule;
        }

        expected = string.Empty;
        return null;
    }
}
