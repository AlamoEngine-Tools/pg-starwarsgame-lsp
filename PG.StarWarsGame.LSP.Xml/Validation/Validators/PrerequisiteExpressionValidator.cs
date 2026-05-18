// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

/// <summary>
///     Validates the boolean prerequisite expression format used by Required_Special_Structures.
///     Syntax: one or more OR-groups separated by commas or spaces (AND).
///     Within an OR-group, names are separated by '|'.
///     '|' binds more tightly than ',' or space, so:
///     "A | B, C | D"  →  (A OR B) AND (C OR D)
/// </summary>
public sealed partial class PrerequisiteExpressionValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.GameObjectTypeReferenceList;
    public TagSemanticType SemanticType => TagSemanticType.PrerequisiteExpression;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return XmlValidationResult.Failure(
                $"<{tag.Tag}> expects a prerequisite expression; value must not be empty.");

        if (!ExpressionPattern().IsMatch(trimmed))
            return XmlValidationResult.Failure(
                $"'{trimmed}' is not a valid prerequisite expression for <{tag.Tag}>. " +
                "Expected game object names connected by '|' (OR) and ',' or spaces (AND). " +
                "Example: \"StructA | StructB, StructC\" means (StructA OR StructB) AND StructC.");

        return XmlValidationResult.Valid();
    }

    // Matches a sequence of word-character names separated by |, comma, or whitespace.
    // Ensures no empty operands: each separator must be followed immediately by a name.
    [GeneratedRegex(@"^\w+(\s*[|,]\s*\w+|\s+\w+)*$")]
    private static partial Regex ExpressionPattern();
}