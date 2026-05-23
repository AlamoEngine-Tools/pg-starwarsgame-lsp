// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public sealed class StoryParamValueHandler : XmlDiagnosticsHandler<StoryParamFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryParamFact fact, DiagnosticsContext ctx)
    {
        if (fact.Def is null || fact.RawValue.Length == 0)
            return [];

        var msg = fact.Def.ValueType switch
        {
            XmlValueType.Int or XmlValueType.UInt =>
                int.TryParse(fact.RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? null
                    : $"'{fact.RawValue}' is not a valid integer.",
            XmlValueType.Float =>
                float.TryParse(fact.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? null
                    : $"'{fact.RawValue}' is not a valid number.",
            XmlValueType.Boolean =>
                IsBooleanValue(fact.RawValue)
                    ? null
                    : $"'{fact.RawValue}' is not a valid boolean value. Use 0, 1, true, or false.",
            XmlValueType.FloatVector3 =>
                ValidateFloatVector3(fact.RawValue),
            _ => null
        };

        return msg is null ? [] : [new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning, msg)];
    }

    private static bool IsBooleanValue(string value)
    {
        return value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateFloatVector3(string value)
    {
        var parts = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return $"'{value}' is not a valid 3D vector. Expected three space- or comma-separated numbers.";
        foreach (var part in parts)
            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return $"'{part}' is not a valid number in vector '{value}'.";
        return null;
    }
}