// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Persistence;

namespace PG.StarWarsGame.LSP.Core.Tests.Persistence;

/// <summary>
///     The guard on the version itself. Every other test here assumes somebody remembered to bump
///     <c>_typeVersion</c> when they changed a document's shape; this is what makes forgetting it
///     go red instead of shipping a file the next release cannot read.
/// </summary>
public sealed class DocumentSignatureTest
{
    private sealed record Entry
    {
        public string File { get; init; } = string.Empty;
        public double X { get; init; }
    }

    private sealed record Layout
    {
        public string Campaign { get; init; } = string.Empty;
        public List<Entry> Entries { get; init; } = [];
    }

    // Same shape, properties declared in a different order.
    private sealed record LayoutReordered
    {
        public List<Entry> Entries { get; init; } = [];
        public string Campaign { get; init; } = string.Empty;
    }

    private sealed record LayoutRenamedProperty
    {
        public string CampaignName { get; init; } = string.Empty;
        public List<Entry> Entries { get; init; } = [];
    }

    private sealed record LayoutChangedType
    {
        public int Campaign { get; init; }
        public List<Entry> Entries { get; init; } = [];
    }

    private sealed record EntryWithExtraField
    {
        public string File { get; init; } = string.Empty;
        public double X { get; init; }
        public double Y { get; init; }
    }

    private sealed record LayoutWithChangedNestedType
    {
        public string Campaign { get; init; } = string.Empty;
        public List<EntryWithExtraField> Entries { get; init; } = [];
    }

    // ── stability ────────────────────────────────────────────────────────────

    [Fact]
    public void Of_IsStableAcrossCalls()
    {
        Assert.Equal(DocumentSignature.Of(typeof(Layout)), DocumentSignature.Of(typeof(Layout)));
    }

    // Declaration order is not part of a JSON document's shape, and reflection does not promise a
    // stable order anyway - so a signature that changed when someone moved a property would cry
    // wolf on every tidy-up.
    [Fact]
    public void Of_IgnoresDeclarationOrder()
    {
        Assert.Equal(DocumentSignature.Of(typeof(Layout)), DocumentSignature.Of(typeof(LayoutReordered)));
    }

    // ── what must change the signature ───────────────────────────────────────

    [Fact]
    public void Of_RenamedProperty_Changes()
    {
        Assert.NotEqual(DocumentSignature.Of(typeof(Layout)), DocumentSignature.Of(typeof(LayoutRenamedProperty)));
    }

    [Fact]
    public void Of_ChangedPropertyType_Changes()
    {
        Assert.NotEqual(DocumentSignature.Of(typeof(Layout)), DocumentSignature.Of(typeof(LayoutChangedType)));
    }

    // The nested type is where a forgotten bump hides: the document's own properties are untouched
    // and only an element type three levels down grew a field.
    [Fact]
    public void Of_ChangedNestedType_Changes()
    {
        Assert.NotEqual(
            DocumentSignature.Of(typeof(Layout)), DocumentSignature.Of(typeof(LayoutWithChangedNestedType)));
    }

    // ── the guard as it will be used ─────────────────────────────────────────

    // A document pins its shape next to its version. The pin is what a reviewer sees change in the
    // diff, and what fails the build when only the shape moved.
    [Fact]
    public void Describe_ReadsAsTheShapeItPins()
    {
        var described = DocumentSignature.Describe(typeof(Entry));

        Assert.Contains("file:String", described);
        Assert.Contains("x:Double", described);
    }

    [Fact]
    public void Guard_ShapeChangedWithoutAVersionBump_Fails()
    {
        Assert.True(TypeVersion.TryParse("aetswg-1.0.0", out var version));
        var pinned = new DocumentShapePin("aetswg.Layout", version, DocumentSignature.Of(typeof(Layout)));

        // Somebody adds a field to a nested type and leaves the version alone.
        Assert.False(pinned.Matches(typeof(LayoutWithChangedNestedType), version));

        // Bumping the version is what makes it pass again - once the pin is updated with it.
        Assert.True(TypeVersion.TryParse("aetswg-1.1.0", out var bumped));
        var repinned = pinned with
        {
            Version = bumped, Signature = DocumentSignature.Of(typeof(LayoutWithChangedNestedType))
        };
        Assert.True(repinned.Matches(typeof(LayoutWithChangedNestedType), bumped));
    }
}
