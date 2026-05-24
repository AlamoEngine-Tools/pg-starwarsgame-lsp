// // Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// // Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public abstract class NumberValueHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        // TODO: All number types can be floats - the game is lenient like that. So we probably want to chain validations.
        //       Depending on how errors should be handeled this abstract class has to be extended. 
        throw new NotImplementedException();
    }
}