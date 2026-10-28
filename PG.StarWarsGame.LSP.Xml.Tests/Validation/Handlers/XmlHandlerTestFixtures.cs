// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

internal static class XmlHandlerTestFixtures
{
    public static readonly DiagnosticsContext EmptyCtx = new(
        new SchemaProviderBuilder().Build(),
        GameIndex.Empty,
        "file:///test.xml",
        "en") { Messages = TestCatalogues.English };

    public static XmlTagDefinition MakeTag(
        string name,
        XmlValueType type,
        TagSemanticType semanticType = TagSemanticType.Default,
        EnumDefinition? enumDef = null,
        ReferenceKind referenceKind = ReferenceKind.None)
    {
        return new XmlTagDefinition
        {
            Tag = name, ValueType = type, SemanticType = semanticType, Enum = enumDef, ReferenceKind = referenceKind
        };
    }

    public static XmlTagValueFact MakeFact(XmlTagDefinition tag, string rawValue)
    {
        return new XmlTagValueFact("file:///test.xml", 0, 0, rawValue.Length, tag, rawValue);
    }
}