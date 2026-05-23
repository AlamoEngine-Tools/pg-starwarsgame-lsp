// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlIndexFactProducer : IXmlIndexFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string documentUri, GameIndex index)
    {
        if (!index.Documents.TryGetValue(documentUri, out var doc))
            return [];

        var facts = new List<XmlFact>();

        foreach (var sym in doc.Symbols)
        {
            if (sym.Origin is not FileOrigin fo)
                continue;
            if (!index.WorkspaceDefinitions.TryGetValue(sym.Id, out var all) || all.Length <= 1)
                continue;

            facts.Add(new XmlSymbolFact(documentUri, fo.Line, fo.Column ?? 0, 0, sym.Id, all));
        }

        foreach (var reference in doc.References)
        {
            var resolved = index.Resolve(reference.TargetId);
            facts.Add(new XmlReferenceFact(
                documentUri,
                reference.Line,
                reference.Column,
                reference.Length,
                reference.TargetId,
                resolved,
                reference.ExpectedTypeName));
        }

        return facts;
    }
}