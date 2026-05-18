// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Validation;

namespace PG.StarWarsGame.LSP.Xml.Validation.Validators;

public sealed partial class FloatVector3Validator : IXmlValueValidator
{
    public XmlValueType ValueType => XmlValueType.FloatVector3;

    public XmlValidationResult Validate(string rawValue, XmlTagDefinition tag)
    {
        var parts = Separator().Split(rawValue.Trim());
        if (parts.Length != 3 || parts.Any(p => !LenientFloatParser.TryParse(p, out _)))
            return XmlValidationResult.Failure(
                $"'{rawValue.Trim()}' is not a valid Float3 for <{tag.Tag}>. Expected 3 floats separated by spaces or commas.");

        return XmlValidationResult.Valid();
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separator();
}