// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema;

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
        var inner = new SchemaProviderBuilder().Build();

        proxy.Configure(inner);

        Assert.True(proxy.ReadyAsync.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Configure_WithDeferredProvider_CompletesReadyAsyncWhenInnerCompletes()
    {
        var proxy = new SchemaProviderProxy();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new SchemaProviderBuilder().WithReady(ready.Task).Build();

        proxy.Configure(inner);
        Assert.False(proxy.ReadyAsync.IsCompleted);

        ready.SetResult();
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
        var inner = new SchemaProviderBuilder().WithTag(tag).Build();

        proxy.Configure(inner);

        Assert.Same(tag, proxy.GetTag("MyTag"));
    }

    [Fact]
    public void SchemaRefreshed_WiredToInnerAfterConfigure()
    {
        var proxy = new SchemaProviderProxy();
        var inner = new SchemaProviderBuilder().Build();
        var fired = false;
        proxy.Configure(inner);

        proxy.SchemaRefreshed += (_, _) => fired = true;
        inner.Refresh();

        Assert.True(fired);
    }
}