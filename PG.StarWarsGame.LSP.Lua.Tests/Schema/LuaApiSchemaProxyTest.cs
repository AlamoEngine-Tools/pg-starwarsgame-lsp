// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Schema;

namespace PG.StarWarsGame.LSP.Lua.Tests.Schema;

public sealed class LuaApiSchemaProxyTest
{
    private const string MinimalSchema = """
        ---@param typeName string
        ---@xmlref XmlObject
        --- Finds the first object.
        function Find_First_Object(typeName) end
        """;

    [Fact]
    public void AllFunctionNames_IsEmpty_BeforeConfigure()
    {
        var proxy = new LuaApiSchemaProxy();
        Assert.Empty(proxy.AllFunctionNames);
    }

    [Fact]
    public void GetXmlRefs_ReturnsEmpty_BeforeConfigure()
    {
        var proxy = new LuaApiSchemaProxy();
        Assert.Empty(proxy.GetXmlRefs("Find_First_Object"));
    }

    [Fact]
    public void GetFunctionDescription_ReturnsNull_BeforeConfigure()
    {
        var proxy = new LuaApiSchemaProxy();
        Assert.Null(proxy.GetFunctionDescription("Find_First_Object"));
    }

    [Fact]
    public void AfterConfigure_DelegatesToConfiguredProvider()
    {
        var proxy = new LuaApiSchemaProxy();
        var provider = new LuaApiSchemaProvider([MinimalSchema]);

        proxy.Configure(provider);

        Assert.Contains("Find_First_Object", proxy.AllFunctionNames);
        Assert.Single(proxy.GetXmlRefs("Find_First_Object"));
        Assert.NotNull(proxy.GetFunctionDescription("Find_First_Object"));
    }

    [Fact]
    public void Configure_CanBeCalledMultipleTimes_UsesLatest()
    {
        var proxy = new LuaApiSchemaProxy();
        var first = new LuaApiSchemaProvider(["function Foo() end"]);
        var second = new LuaApiSchemaProvider(["function Bar() end"]);

        proxy.Configure(first);
        Assert.Contains("Foo", proxy.AllFunctionNames);

        proxy.Configure(second);
        Assert.DoesNotContain("Foo", proxy.AllFunctionNames);
        Assert.Contains("Bar", proxy.AllFunctionNames);
    }
}
