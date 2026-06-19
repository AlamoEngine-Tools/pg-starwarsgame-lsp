// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Completion;

internal sealed class StoryParamValueCompletionStrategy : IXmlTagValueCompletionStrategy
{
    private readonly StoryParamValueProposalProvider _storyProposals;

    public StoryParamValueCompletionStrategy(StoryParamValueProposalProvider storyProposals)
    {
        _storyProposals = storyProposals;
    }

    public IEnumerable<CompletionItem> Handle(TagValueCompletionContext ctx)
    {
        if (ctx.StoryParamSide is null) return [];

        var storyCtx = StoryEventCompletionContextReader.Read(ctx.Doc, ctx.LineIndex, ctx.Character);

        ParamDefinition? paramDef;
        if (string.Equals(ctx.StoryParamSide, "Event", StringComparison.OrdinalIgnoreCase))
        {
            var typeDef = storyCtx.EventType is not null
                ? ctx.Schema.GetEnum("StoryEventType")?.Values
                    .FirstOrDefault(v => string.Equals(v.Name, storyCtx.EventType, StringComparison.OrdinalIgnoreCase))
                : null;
            paramDef = typeDef?.Params?.FirstOrDefault(p => p.Position == ctx.StoryParamPosition);
        }
        else
        {
            var typeDef = storyCtx.RewardType is not null
                ? ctx.Schema.GetEnum("StoryRewardType")?.Values
                    .FirstOrDefault(v => string.Equals(v.Name, storyCtx.RewardType, StringComparison.OrdinalIgnoreCase))
                : null;
            paramDef = typeDef?.Params?.FirstOrDefault(p => p.Position == ctx.StoryParamPosition);
        }

        if (paramDef is null) return [];

        var proposals = _storyProposals.GetProposals(paramDef, ctx.PartialValue, ctx.Index);

        return proposals.Select(p => new CompletionItem
        {
            Label = p.Label,
            Detail = p.Detail,
            InsertText = p.InsertText ?? p.Label,
            Kind = CompletionItemKind.Value
        });
    }
}