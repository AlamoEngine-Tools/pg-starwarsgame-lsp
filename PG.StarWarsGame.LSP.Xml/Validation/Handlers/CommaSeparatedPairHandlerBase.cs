// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

public abstract class CommaSeparatedPairHandlerBase : SingleValueTypeHandlerBase
{
    protected static string[] SplitOnFirstComma(string raw)
    {
        var idx = raw.IndexOf(',');
        if (idx < 0)
            return [raw];
        return [raw[..idx], raw[(idx + 1)..]];
    }

    protected static XmlDiagnosticResult? TryValidateSfxEvent(string sfxEventName, string tagName, GameIndex index)
    {
        if (sfxEventName.Length == 0)
            return null;
        if (index.Baseline.Symbols.Count == 0 && index.WorkspaceDefinitions.Count == 0)
            return null;
        if (index.Resolve(sfxEventName) is not null)
            return null;
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"'{sfxEventName}' could not be resolved as an SFX event for <{tagName}>.");
    }

    protected static XmlDiagnosticResult? TryValidateGameObjectName(string name, string tagName, GameIndex index)
    {
        if (index.Baseline.Symbols.Count == 0 && index.WorkspaceDefinitions.Count == 0)
            return null;
        if (index.Resolve(name) is not null)
            return null;
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"'{name}' could not be resolved as a game object for <{tagName}>.");
    }
}