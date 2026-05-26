// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlStructuralValidator : IXmlStructuralValidator
{
    private static readonly XmlReaderSettings Settings = new()
    {
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null
    };

    public IReadOnlyList<XmlStructureError> Validate(string text)
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
            return [new XmlStructureError(line, col, ex.Message)];
        }

        return [];
    }
}