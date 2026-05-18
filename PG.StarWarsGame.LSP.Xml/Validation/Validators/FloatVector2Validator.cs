// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class FloatVector2Validator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.FloatVector2;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var parts = Separator().Split(rawValue.Trim());
        if (parts.Length != 2 || parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid Float2 for <{tag.Tag}>. Expected 2 floats separated by spaces or commas.");
        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}
