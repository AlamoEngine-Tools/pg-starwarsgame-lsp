// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Core.Tests.Configuration;

public sealed class ServerOptionsTest
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var opts = ServerOptions.Default;

        Assert.Equal(TimeSpan.FromSeconds(30), opts.SchemaWaitTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), opts.ProgressReporterTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(100), opts.DiagnosticsDebounce);
        Assert.Equal(16, opts.ParseCacheCapacity);
    }

    [Fact]
    public void FromJson_ParsesParseCacheCapacity()
    {
        const string json = """{ "server": { "parseCacheCapacity": 4 } }""";

        var opts = ServerOptions.FromJson(json);

        Assert.Equal(4, opts.ParseCacheCapacity);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.SchemaWaitTimeout); // unrelated defaults kept
    }

    [Fact]
    public void FromJson_OverridesAllValues()
    {
        const string json = """
                            {
                              "server": {
                                "schemaWaitTimeoutSeconds": 60,
                                "progressReporterTimeoutSeconds": 5,
                                "diagnosticsDebounceMs": 200
                              }
                            }
                            """;

        var opts = ServerOptions.FromJson(json);

        Assert.Equal(TimeSpan.FromSeconds(60), opts.SchemaWaitTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), opts.ProgressReporterTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(200), opts.DiagnosticsDebounce);
    }

    [Fact]
    public void FromJson_PartialOverride_UnsetValuesRetainDefaults()
    {
        const string json = """{ "server": { "schemaWaitTimeoutSeconds": 60 } }""";

        var opts = ServerOptions.FromJson(json);

        Assert.Equal(TimeSpan.FromSeconds(60), opts.SchemaWaitTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), opts.ProgressReporterTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(100), opts.DiagnosticsDebounce);
    }

    [Fact]
    public void FromJson_EmptyObject_ReturnsDefaults()
    {
        var opts = ServerOptions.FromJson("{}");

        Assert.Equal(ServerOptions.Default, opts);
    }

    [Fact]
    public void WithDebugger_MakesWaitTimeoutsInfinite()
    {
        var opts = ServerOptions.Default.WithDebugger();

        Assert.Equal(Timeout.InfiniteTimeSpan, opts.SchemaWaitTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, opts.ProgressReporterTimeout);
    }

    [Fact]
    public void WithDebugger_SetsDebouncToZero()
    {
        var opts = ServerOptions.Default.WithDebugger();

        Assert.Equal(TimeSpan.Zero, opts.DiagnosticsDebounce);
    }

    [Fact]
    public void WithDebugger_PreservesCustomBaseValues()
    {
        const string json = """{ "server": { "schemaWaitTimeoutSeconds": 60 } }""";
        var opts = ServerOptions.FromJson(json).WithDebugger();

        Assert.Equal(Timeout.InfiniteTimeSpan, opts.SchemaWaitTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, opts.ProgressReporterTimeout);
    }
}