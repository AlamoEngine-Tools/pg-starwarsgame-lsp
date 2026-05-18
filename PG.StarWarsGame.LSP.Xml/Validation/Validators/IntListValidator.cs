// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class IntListValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.IntList;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
            return XmlValidationResult.Failure($"'' is not a valid integer list for <{tag.Tag}>.");

        var parts = Separator().Split(trimmed);
        if (parts.Any(p => !int.TryParse(p, out _)))
            return XmlValidationResult.Failure(
                $"'{trimmed}' is not a valid integer list for <{tag.Tag}>. Expected space-separated integers.");
        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
