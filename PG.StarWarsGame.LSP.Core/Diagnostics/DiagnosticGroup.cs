// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Coarse category of a <see cref="DiagnosticId" /> - the first segment of
///     <c>aetswg-000-0000</c>. Grouping exists so a user can silence a whole class of diagnostic
///     ("stop checking whether asset files exist") without listing every id.
///     <para>
///         This axis is deliberately independent of the schema's <c>valueGroup</c> and of
///         <c>validationOverride.validationId</c>: those describe what a tag's value <em>is</em>,
///         whereas this describes what kind of problem was <em>reported</em>. Numbers are a
///         published contract - append new groups, never renumber existing ones.
///     </para>
/// </summary>
public enum DiagnosticGroup
{
    /// <summary>A reference that does not resolve, or is empty where a target is required.</summary>
    References = 1,

    /// <summary>A value outside the set its enum allows.</summary>
    Enums = 2,

    /// <summary>Malformed scalars and tuples: numbers, booleans, value shape and arity.</summary>
    Values = 3,

    /// <summary>Referenced asset file (model, texture, audio, map) is missing or unreadable.</summary>
    Assets = 4,

    /// <summary>Localisation keys and the text tables behind them.</summary>
    Localisation = 5,

    /// <summary>Document structure: duplicated singleton tags, misplaced or repeated elements.</summary>
    Structure = 6,

    /// <summary>Constraints spanning sibling tags of one object.</summary>
    CrossTag = 7,

    /// <summary>Variant inheritance (<c>Variant_Of_Existing_Type</c>) behaviour.</summary>
    Variants = 8,

    /// <summary>Campaign story chain, plot graph and story events.</summary>
    Story = 9,

    /// <summary>Symbol identity across layers: duplicates, shadowing, overrides.</summary>
    Symbols = 10,

    /// <summary>Engine-hardcoded expectations the schema encodes (ordered sets, reserved values).</summary>
    Engine = 11,

    /// <summary>
    ///     The document does not parse: malformed syntax, unexpected or missing tokens.
    ///     <para>
    ///         Numbers 1-2999 belong to Loretta, which supplies Lua's parse errors already
    ///         numbered - they are mapped straight across rather than reassigned, so the band is
    ///         reserved and never hand-assigned. Ours start at 3000.
    ///     </para>
    /// </summary>
    Syntax = 12,

    /// <summary>
    ///     The suppression mechanism reporting on itself: directives that name nothing, name
    ///     something unparseable, or cover nothing.
    ///     <para>
    ///         Its own group rather than <see cref="Syntax" /> so that silencing a language's parse
    ///         errors does not also blind the user to their broken suppression comments - the one
    ///         thing that would leave them with no way to find out why suppression is not working.
    ///     </para>
    /// </summary>
    Suppression = 13
}

/// <summary>Display names for <see cref="DiagnosticGroup" />, shown in suppression UI and hovers.</summary>
public static class DiagnosticGroups
{
    public static string NameOf(DiagnosticGroup group)
    {
        return group switch
        {
            DiagnosticGroup.References => "References",
            DiagnosticGroup.Enums => "Enum values",
            DiagnosticGroup.Values => "Value format",
            DiagnosticGroup.Assets => "Asset files",
            DiagnosticGroup.Localisation => "Localisation",
            DiagnosticGroup.Structure => "Document structure",
            DiagnosticGroup.CrossTag => "Cross-tag rules",
            DiagnosticGroup.Variants => "Variant inheritance",
            DiagnosticGroup.Story => "Story and campaigns",
            DiagnosticGroup.Symbols => "Symbols and layers",
            DiagnosticGroup.Engine => "Engine constraints",
            DiagnosticGroup.Syntax => "Syntax",
            DiagnosticGroup.Suppression => "Suppression comments",
            _ => group.ToString()
        };
    }
}
