// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml;

public abstract class XmlHandlerContextBase
{
    public XmlHandlerContextBase(string documentUri, GameIndex index, ISchemaProvider schema)
    {
        DocumentUri = documentUri;
        Index = index;
        Schema = schema;
    }

    public string DocumentUri { get; }
    public GameIndex Index { get; }
    public ISchemaProvider Schema { get; }
}