// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using PG.StarWarsGame.LSP.Preview;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>The serializer the language server uses, with the preview converter installed.</summary>
public static class PreviewSerialization
{
    /// <summary>
    ///     Builds the serializer.
    /// </summary>
    /// <remarks>
    ///     Shared with the tests deliberately: a wire-shape test that builds its own serializer proves
    ///     what that serializer does, not what the server sends.
    /// </remarks>
    public static LspSerializer Create()
    {
        var serializer = new LspSerializer();
        var converter = new PreviewEnumConverter();

        // BOTH, and Settings is the one that matters. OmniSharp writes responses through
        // Settings.Converters; the cached JsonSerializer is what JObject.FromObject and the tests
        // reach for. Installing on the latter alone left the live server sending ordinals while the
        // whole suite passed - a false green that survived a server launch, because the handshake
        // this changes nothing about looked healthy.
        serializer.Settings.Converters.Add(converter);
        serializer.JsonSerializer.Converters.Add(converter);

        return serializer;
    }
}