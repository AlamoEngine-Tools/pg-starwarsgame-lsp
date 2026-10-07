// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     Two readers, in order. The game's own reader (<see cref="XmlGameReader" />) decides first:
///     a file it drops gets that one error, and nothing else is reported for its structure. A file
///     it reads is then read as standard XML (Document conformance, as editors and XML tools read
///     it); its first error is strict-only, because the game accepted the file. What the game
///     tolerated while reading is reported in both cases where it applies.
/// </summary>
public sealed class XmlStructuralValidator : IXmlStructuralValidator
{
    private static readonly XmlReaderSettings Settings = new()
    {
        ConformanceLevel = ConformanceLevel.Document,
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null
    };

    public IReadOnlyList<XmlStructureError> Validate(string text)
    {
        var game = XmlGameReader.Read(text);
        if (game.Error is { } fatal) return [fatal];

        var results = new List<XmlStructureError>();
        if (StrictFirstError(text) is { } strict) results.Add(strict);
        results.AddRange(game.Tolerances);
        return results;
    }

    /// <summary>
    ///     The first error standard XML tools report for <paramref name="text" />, read as a whole
    ///     document; null when it is well-formed. Line and position are 1-based.
    /// </summary>
    public static XmlException? StrictReadError(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), Settings);
        try
        {
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            return ex;
        }

        return null;
    }

    private static XmlStructureError? StrictFirstError(string text)
    {
        if (StrictReadError(text) is { } ex)
        {
            var line = Math.Max(0, ex.LineNumber - 1);
            var col = Math.Max(0, ex.LinePosition - 1);
            var at = XmlUtility.PositionToOffset(text, line, col);
            var (category, reason) = Categorize(ex.Message, text, line, at);
            var repair = category == XmlStrictnessCategory.MalformedDeclaration ? DeclarationRepair(text) : null;
            return new XmlStructureError(line, col, reason, category, Repair: repair);
        }

        return null;
    }

    // The game skips the declaration to its first '>', so a '?' before that '>' changes nothing it reads.
    private static XmlRepair? DeclarationRepair(string text)
    {
        var open = text.IndexOf("<?", StringComparison.Ordinal);
        if (open < 0) return null;
        var close = text.IndexOf('>', open);
        if (close < 0 || text[close - 1] == '?') return null;
        return new XmlRepair("End the declaration with '?>'", [new XmlTextEdit(close, 0, "?")]);
    }

    // XmlException carries no error code, and its message words one construct several ways (a
    // declaration ending in '" >' is "Name cannot begin with '>'", a name after '&' is "expected
    // ';'"), so the category comes from the TEXT at the error. The game read the file, so every
    // error here is one standard XML tools give and the game does not.
    private static (XmlStrictnessCategory, string) Categorize(string message, string text, int line, int at)
    {
        var detail = message.Split(" Line ", 2)[0].TrimEnd('.');
        if (line == 0 && DeclarationRepair(text) is not null)
            return (XmlStrictnessCategory.MalformedDeclaration,
                "XML declaration must end in '?>': The game reads it, standard XML tools reject the file");
        if (InsideEntityReference(text, at) ||
            message.Contains("undeclared entity", StringComparison.Ordinal))
            return (XmlStrictnessCategory.StrayAmpersand,
                "'&' that starts no entity: The game reads it as a plain character, standard XML tools reject the file");
        return (XmlStrictnessCategory.StrictOnly,
            $"Invalid XML ({detail}): The game reads the file, standard XML tools reject it");
    }

    // True when the error sits in an unterminated reference: an '&' at the error, or one reached
    // walking back over the name the reader was collecting. A ';', whitespace or markup ends the walk.
    private static bool InsideEntityReference(string text, int at)
    {
        if (text.Length == 0) return false;
        var i = Math.Min(at, text.Length - 1);
        if (text[i] == '&') return true;
        for (i--; i >= 0; i--)
        {
            var c = text[i];
            if (c == '&') return true;
            if (c is ';' or '<' or '>' or '"' or '\'' || char.IsWhiteSpace(c)) return false;
        }

        return false;
    }
}