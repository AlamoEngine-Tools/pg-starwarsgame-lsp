// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

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

    /// <summary>
    ///     Returns <paramref name="result" /> positioned precisely at one side of the fact's
    ///     first-comma pair (slot 0 = before, slot 1 = after), so a broken token highlights only
    ///     itself instead of the whole tuple value. Falls back to the unchanged result (whole-value
    ///     range) when the slot is absent or empty.
    /// </summary>
    protected static XmlDiagnosticResult AtPairSlot(
        XmlDiagnosticResult result, XmlTagValueFact fact, int slotIndex)
    {
        if (PairSlotSpan(fact.RawValue, slotIndex) is not { } span)
            return result;

        var (line, column) = XmlUtility.AdvancePosition(fact.Line, fact.Column, fact.RawValue, span.Offset);
        return result with { OverrideLine = line, OverrideColumn = column, OverrideLength = span.Length };
    }

    // Span of one side of the first-comma pair within the UNTRIMMED raw value, with each slot's
    // surrounding whitespace excluded — offsets stay valid against the fact's own position even
    // when the value spans multiple lines.
    private static (int Offset, int Length)? PairSlotSpan(string raw, int slotIndex)
    {
        var comma = raw.IndexOf(',');
        int start, end; // [start, end)
        if (slotIndex == 0)
        {
            start = 0;
            end = comma < 0 ? raw.Length : comma;
        }
        else
        {
            if (comma < 0) return null;
            start = comma + 1;
            end = raw.Length;
        }

        while (start < end && char.IsWhiteSpace(raw[start])) start++;
        while (end > start && char.IsWhiteSpace(raw[end - 1])) end--;
        return end > start ? (start, end - start) : null;
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