// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Server.Tests;

public sealed class SchemaProviderProxyTest
{
    [Fact]
    public void ReadyAsync_BeforeConfigure_IsNotCompleted()
    {
        var proxy = new SchemaProviderProxy();

        Assert.False(proxy.ReadyAsync.IsCompleted);
    }

    [Fact]
    public void Configure_WithAlreadyReadyProvider_CompletesReadyAsync()
    {
        var proxy = new SchemaProviderProxy();
        var inner = new InstantReadyProvider();

        proxy.Configure(inner);

        Assert.True(proxy.ReadyAsync.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Configure_WithDeferredProvider_CompletesReadyAsyncWhenInnerCompletes()
    {
        var proxy = new SchemaProviderProxy();
        var inner = new DeferredReadyProvider();

        proxy.Configure(inner);
        Assert.False(proxy.ReadyAsync.IsCompleted);

        inner.MarkReady();
        await proxy.ReadyAsync;

        Assert.True(proxy.ReadyAsync.IsCompletedSuccessfully);
    }

    [Fact]
    public void BeforeConfigure_AllMembersReturnEmpty()
    {
        var proxy = new SchemaProviderProxy();

        Assert.Empty(proxy.AllTags);
        Assert.Empty(proxy.AllObjectTypes);
        Assert.Empty(proxy.AllEnums);
        Assert.Empty(proxy.AllHardcodedSets);
        Assert.Empty(proxy.AllMetafiles);
        Assert.Null(proxy.GetTag("Anything"));
        Assert.Empty(proxy.GetAllTagDefinitions("Anything"));
        Assert.Null(proxy.GetObjectType("Anything"));
        Assert.Empty(proxy.GetTagsForType("Anything"));
        Assert.Null(proxy.GetEnum("Anything"));
    }

    [Fact]
    public void AfterConfigure_MembersDelegateToInner()
    {
        var proxy = new SchemaProviderProxy();
        var tag = new XmlTagDefinition { Tag = "MyTag", ValueType = XmlValueType.Boolean };
        var inner = new InstantReadyProvider { Tag = tag };

        proxy.Configure(inner);

        Assert.Same(tag, proxy.GetTag("MyTag"));
    }

    [Fact]
    public void SchemaRefreshed_WiredToInnerAfterConfigure()
    {
        var proxy = new SchemaProviderProxy();
        var inner = new InstantReadyProvider();
        var fired = false;
        proxy.Configure(inner);

        proxy.SchemaRefreshed += (_, _) => fired = true;
        inner.Fire();

        Assert.True(fired);
    }

    private sealed class InstantReadyProvider : ISchemaProvider
    {
        public XmlTagDefinition? Tag { get; init; }

        public Task ReadyAsync => Task.CompletedTask;

        public event EventHandler? SchemaRefreshed;

        public XmlTagDefinition? GetTag(string tagName)
        {
            return tagName == Tag?.Tag ? Tag : null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public void Fire()
        {
            SchemaRefreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class DeferredReadyProvider : ISchemaProvider
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadyAsync => _tcs.Task;

        public event EventHandler? SchemaRefreshed;

        public XmlTagDefinition? GetTag(string tagName)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string enumName)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public void MarkReady()
        {
            _tcs.TrySetResult();
        }
    }
}