// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Completion;

namespace PG.StarWarsGame.LSP.Xml.Tests.Completion;

/// <summary>
///     A value-completion strategy picks ONE provider out of the injected collections (#312): the
///     first completion provider that can handle the tag, the one proposal provider declared for
///     the value type.
/// </summary>
public sealed class CompletionProviderLookupTest
{
    private static XmlTagDefinition RefTag()
    {
        return new XmlTagDefinition { Tag = "Target", ValueType = XmlValueType.NameReference };
    }

    // ── completion providers: first CanHandle wins ────────────────────────────

    [Fact]
    public void GetProposals_NoProviders_ReturnsEmpty()
    {
        IXmlCompletionProvider[] providers = [];

        var result = providers.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void GetProposals_NoMatchingProvider_ReturnsEmpty()
    {
        IXmlCompletionProvider[] providers = [new FakeProvider("Boolean", false)];

        var result = providers.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void GetProposals_MatchingProvider_ReturnsItsProposals()
    {
        var proposal = new ValueProposal { Label = "X_Wing" };
        IXmlCompletionProvider[] providers = [new FakeProvider("Ref", true, proposal)];

        var result = providers.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("X_Wing", result[0].Label);
    }

    [Fact]
    public void GetProposals_FirstMatchingProvider_Wins()
    {
        var p1 = new FakeProvider("P1", true, new ValueProposal { Label = "From_P1" });
        var p2 = new FakeProvider("P2", true, new ValueProposal { Label = "From_P2" });
        IXmlCompletionProvider[] providers = [p1, p2];

        var result = providers.GetProposals(RefTag(), "", GameIndex.Empty);

        Assert.Single(result);
        Assert.Equal("From_P1", result[0].Label);
    }

    [Fact]
    public void GetProposals_PassesTagPartialValueAndIndexToProvider()
    {
        var provider = new CapturingProvider();
        IXmlCompletionProvider[] providers = [provider];

        var tag = RefTag();
        var index = GameIndex.Empty;
        providers.GetProposals(tag, "X_", index);

        Assert.Same(tag, provider.CapturedTag);
        Assert.Equal("X_", provider.CapturedPartial);
        Assert.Same(index, provider.CapturedIndex);
    }

    // ── proposal providers: the one declared for the value type ───────────────

    [Fact]
    public void GetProposals_NoProviderForTheValueType_ReturnsEmpty()
    {
        IXmlValueProposalProvider[] providers =
            [new ForType(XmlValueType.Boolean, new ValueProposal { Label = "Yes" })];

        var result = providers.GetProposals(XmlValueType.Float, RefTag(), "");

        Assert.Empty(result);
    }

    [Fact]
    public void GetProposals_TheProviderForTheValueType_Answers()
    {
        IXmlValueProposalProvider[] providers =
        [
            new ForType(XmlValueType.Float, new ValueProposal { Label = "1.0" }),
            new ForType(XmlValueType.Boolean, new ValueProposal { Label = "Yes" })
        ];

        var result = providers.GetProposals(XmlValueType.Boolean, RefTag(), "");

        Assert.Equal(["Yes"], result.Select(p => p.Label));
    }

    [Fact]
    public void GetProposals_PassesTagAndPartialValueToTheProvider()
    {
        var provider = new ForType(XmlValueType.Boolean);
        IXmlValueProposalProvider[] providers = [provider];

        var tag = RefTag();
        providers.GetProposals(XmlValueType.Boolean, tag, "Ye");

        Assert.Same(tag, provider.CapturedTag);
        Assert.Equal("Ye", provider.CapturedPartial);
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

        public override string ToString()
        {
            return name;
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

    private sealed class ForType(XmlValueType valueType, params ValueProposal[] proposals) : IXmlValueProposalProvider
    {
        public XmlTagDefinition? CapturedTag { get; private set; }
        public string? CapturedPartial { get; private set; }

        public XmlValueType ValueType => valueType;

        public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue)
        {
            CapturedTag = tag;
            CapturedPartial = partialValue;
            return proposals;
        }
    }
}