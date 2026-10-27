// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Completion.Strategies;

internal sealed class StandardValueCompletionStrategy : IXmlTagValueCompletionStrategy
{
    private readonly IReadOnlyList<IXmlCompletionProvider> _completion;
    private readonly IReadOnlyList<IXmlValueProposalProvider> _proposals;

    public StandardValueCompletionStrategy(
        IEnumerable<IXmlValueProposalProvider> proposals,
        IEnumerable<IXmlCompletionProvider> completion)
    {
        _proposals = [.. proposals];
        _completion = [.. completion];
    }

    public IEnumerable<CompletionItem> Handle(TagValueCompletionContext ctx)
    {
        if (ctx.StoryParamSide is not null) return [];
        if (ctx.TagDef is null || ctx.TagDef.ReferenceKind == ReferenceKind.BoneName) return [];

        var proposals = _proposals.GetProposals(ctx.TagDef.ValueType, ctx.TagDef, ctx.PartialValue)
            .Concat(_completion.GetProposals(ctx.TagDef, ctx.PartialValue, ctx.Index));

        return proposals.Select(p => new CompletionItem
        {
            Label = p.Label,
            Detail = p.Detail,
            LabelDetails = p.Description is not null
                ? new CompletionItemLabelDetails { Description = p.Description }
                : null,
            InsertText = p.InsertText ?? p.Label,
            Kind = CompletionItemKind.Value
        });
    }
}