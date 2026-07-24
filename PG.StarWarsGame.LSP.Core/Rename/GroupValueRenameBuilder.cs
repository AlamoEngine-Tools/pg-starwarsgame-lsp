// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Rename;

/// <summary>
///     Renames a <see cref="Schema.TagSemanticType.ReferenceGroup" /> grouping key (e.g. a
///     <c>Campaign_Set</c> value or an SFXEvent <c>Overlap_Test</c> value): the key has no single
///     declaration - it is the shared value of a tag on every member object - so the rename rewrites
///     that value in place across every member occurrence. Baseline (shipped-game) members are not in
///     the editable document set and are left untouched; only workspace documents are rewritten.
/// </summary>
public static class GroupValueRenameBuilder
{
    public static WorkspaceEdit? Build(string groupKey, string newValue, GameIndex index, ILogger logger)
    {
        if (!IsValidGroupValue(newValue)) return null;
        var trimmed = newValue.Trim();
        // The key is compared case-insensitively (as the group index is), but a case-only change is
        // still a real rename; only an exact no-op is rejected.
        if (string.Equals(trimmed, groupKey, StringComparison.Ordinal)) return null;

        var edits = new Dictionary<string, List<TextEdit>>(StringComparer.Ordinal);
        foreach (var doc in index.Documents.Values)
        {
            if (doc.GroupMemberships.IsDefaultOrEmpty) continue;
            foreach (var m in doc.GroupMemberships)
            {
                if (!string.Equals(m.Membership.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!edits.TryGetValue(doc.DocumentUri, out var list))
                    edits[doc.DocumentUri] = list = [];
                list.Add(new TextEdit
                {
                    NewText = trimmed,
                    Range = new LspRange(
                        new Position(m.TagLine, m.TagColumn),
                        new Position(m.TagLine, m.TagColumn + m.TagLength))
                });
            }
        }

        if (edits.Count == 0)
        {
            logger.LogDebug("Group rename {Key}: no editable members found", groupKey);
            return null;
        }

        var changes = edits.Select(kv => new WorkspaceEditDocumentChange(new TextDocumentEdit
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(kv.Key) },
            Edits = new TextEditContainer(kv.Value)
        })).ToList();

        logger.LogDebug("Group rename {Key} → {New}: {Files} file(s), {Edits} occurrence(s)",
            groupKey, trimmed, edits.Count, edits.Values.Sum(l => l.Count));
        return new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) };
    }

    // A grouping-key value is written as a plain XML text-node token. Reject empty/whitespace and any
    // character that would break the text node or the enclosing tag.
    private static bool IsValidGroupValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (var c in value.Trim())
            if (c is '<' or '>' or '&' || c < ' ')
                return false;
        return true;
    }
}
