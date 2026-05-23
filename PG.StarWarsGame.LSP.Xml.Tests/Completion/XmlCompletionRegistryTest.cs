// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

public sealed class XmlCompletionRegistryTest
{
    private static XmlTagDefinition FloatTag()
    {
        return new XmlTagDefinition { Tag = "Speed", ValueType = XmlValueType.Float };
    }

    private static XmlTagDefinition RefTag()
    {
        return new XmlTagDefinition { Tag = "Target", ValueType = XmlValueType.NameReference };
    }

    // ── dispatch ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetProposals_NoProviders_ReturnsEmpty()
    {
        var registry = new XmlCompletionRegistry([]);

        var result = registry.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void GetProposals_NoMatchingProvider_ReturnsEmpty()
    {
        var registry = new XmlCompletionRegistry([new FakeProvider("Boolean", false)]);

        var result = registry.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void GetProposals_MatchingProvider_ReturnsItsProposals()
    {
        var proposal = new ValueProposal { Label = "X_Wing" };
        var registry = new XmlCompletionRegistry([new FakeProvider("Ref", true, proposal)]);

        var result = registry.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("X_Wing", result[0].Label);
    }

    [Fact]
    public void GetProposals_FirstMatchingProvider_Wins()
    {
        var p1 = new FakeProvider("P1", true, new ValueProposal { Label = "From_P1" });
        var p2 = new FakeProvider("P2", true, new ValueProposal { Label = "From_P2" });
        var registry = new XmlCompletionRegistry([p1, p2]);

        var result = registry.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("From_P1", result[0].Label);
    }

    [Fact]
    public void GetProposals_PassesTagPartialValueAndIndexToProvider()
    {
        var provider = new CapturingProvider();
        var registry = new XmlCompletionRegistry([provider]);

        var tag = RefTag();
        var index = GameIndex.Empty;
        registry.GetProposals(tag, "X_", index);

        Assert.Same(tag, provider.CapturedTag);
        Assert.Equal("X_", provider.CapturedPartial);
        Assert.Same(index, provider.CapturedIndex);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeProvider(string name, bool canHandle, params ValueProposal[] proposals)
        : IXmlCompletionProvider
    {
        public bool CanHandle(XmlTagDefinition tag)
        {
            return canHandle;
        }

        public IReadOnlyList<ValueProposal> GetProposals(
            XmlTagDefinition tag, string partialValue, GameIndex index)
        {
            return proposals;
        }
    }

    private sealed class CapturingProvider : IXmlCompletionProvider
    {
        public XmlTagDefinition? CapturedTag { get; private set; }
        public string? CapturedPartial { get; private set; }
        public GameIndex? CapturedIndex { get; private set; }

        public bool CanHandle(XmlTagDefinition tag)
        {
            return true;
        }

        public IReadOnlyList<ValueProposal> GetProposals(
            XmlTagDefinition tag, string partialValue, GameIndex index)
        {
            CapturedTag = tag;
            CapturedPartial = partialValue;
            CapturedIndex = index;
            return [];
        }
    }
}