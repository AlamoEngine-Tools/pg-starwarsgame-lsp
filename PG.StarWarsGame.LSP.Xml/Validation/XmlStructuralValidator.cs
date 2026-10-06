// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml;
using PG.StarWarsGame.LSP.Core.Diagnostics;

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

    private static XmlStructureError? StrictFirstError(string text)
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
            var line = Math.Max(0, ex.LineNumber - 1);
            var col = Math.Max(0, ex.LinePosition - 1);
            var (category, reason) = Categorize(ex.Message, line);
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

    // XmlException carries no error code, so the category comes from its message. The game read
    // the file, so every message here is one standard XML tools give and the game does not.
    private static (XmlStrictnessCategory, string) Categorize(string message, int line)
    {
        var detail = message.Split(" Line ", 2)[0].TrimEnd('.');
        if (line == 0 && message.Contains("'?>'", StringComparison.Ordinal))
            return (XmlStrictnessCategory.MalformedDeclaration,
                "XML declaration must end in '?>': The game reads it, standard XML tools reject the file");
        if (message.Contains("EntityName", StringComparison.Ordinal) ||
            message.Contains("undeclared entity", StringComparison.Ordinal))
            return (XmlStrictnessCategory.StrayAmpersand,
                "'&' that starts no entity: The game reads it as a plain character, standard XML tools reject the file");
        return (XmlStrictnessCategory.StrictOnly,
            $"Invalid XML ({detail}): The game reads the file, standard XML tools reject it");
    }
}