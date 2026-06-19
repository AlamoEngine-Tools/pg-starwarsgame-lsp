// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Completion;

internal sealed class BoneNameValueCompletionStrategy : IXmlTagValueCompletionStrategy
{
    private readonly BoneNameCompletionHelper _boneHelper;

    public BoneNameValueCompletionStrategy(BoneNameCompletionHelper boneHelper)
    {
        _boneHelper = boneHelper;
    }

    public IEnumerable<CompletionItem> Handle(TagValueCompletionContext ctx)
    {
        if (ctx.TagDef?.ReferenceKind != ReferenceKind.BoneName) return [];

        var proposals = _boneHelper.GetProposals(ctx.EnclosingNode, ctx.PartialValue, ctx.Index);
        return proposals.Select(p => new CompletionItem
        {
            Label = p.Label,
            InsertText = p.Label,
            Kind = CompletionItemKind.Value
        });
    }
}