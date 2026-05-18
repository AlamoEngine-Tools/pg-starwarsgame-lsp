// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class FloatVector3ListValidator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.FloatVector3List;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
            return XmlValidationResult.Failure($"'' is not a valid Float3 list for <{tag.Tag}>.");

        var parts = Separator().Split(trimmed);
        if (parts.Length == 0 || parts.Length % 3 != 0)
            return XmlValidationResult.Failure(
                $"'{trimmed}' is not a valid Float3 list for <{tag.Tag}>. Expected a multiple of 3 floats.");

        if (parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return XmlValidationResult.Failure(
                $"'{trimmed}' is not a valid Float3 list for <{tag.Tag}>. Expected space-separated floats.");
        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
