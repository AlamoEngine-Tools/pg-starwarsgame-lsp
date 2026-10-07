// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Whether the server offers a formatting method, either in the initialize result or by a
///     later dynamic registration - the client honours both, so "not advertised" means neither.
/// </summary>
public static class AdvertisedMethods
{
    public const string Formatting = "textDocument/formatting";
    public const string RangeFormatting = "textDocument/rangeFormatting";

    public static bool Advertises(LanguageClient client, string method)
    {
        var caps = client.ServerSettings.Capabilities;
        var isStatic = method == Formatting
            ? Offered(caps.DocumentFormattingProvider)
            : Offered(caps.DocumentRangeFormattingProvider);
        var isDynamic = client.RegistrationManager.CurrentRegistrations.Any(r => r.Method == method);
        return isStatic || isDynamic;
    }

    /// <summary>
    ///     A dynamic registration arrives after the initialize answer, so a positive check waits
    ///     for it. A negative check instead waits for a settled server - see its caller.
    /// </summary>
    public static async Task<bool> WaitForAsync(LanguageClient client, string method, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!Advertises(client, method))
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(100);
        }

        return true;
    }

    private static bool Offered<T>(BooleanOr<T>? provider) where T : class
    {
        return provider is not null && (provider.IsBool ? provider.Bool : provider.Value is not null);
    }
}