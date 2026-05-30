// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public abstract class CommaSeparatedPairHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected static string[] SplitOnFirstComma(string raw)
    {
        var idx = raw.IndexOf(',');
        if (idx < 0)
            return [raw];
        return [raw[..idx], raw[(idx + 1)..]];
    }
}
