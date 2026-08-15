// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using PG.StarWarsGame.LSP.Assets.Models;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     Writes the preview protocol's enums as their names.
/// </summary>
/// <remarks>
///     <para>
///         Newtonsoft serialises a bare enum as its ordinal, and the hand-written TS mirror declares
///         these as string unions - so <c>shape: 3</c> arrives where <c>"Sphere"</c> was expected. That
///         does not fail loudly: every <c>switch</c> in the simulator falls through to its default, and
///         the visible symptom is particles quietly spawning at the origin with no speed. The same trap
///         is already recorded on <c>EncyclopediaTextAlignment</c>, which sidesteps it by not using an
///         enum at all. These are real domain enums with sixty-odd usage sites in the readers, so they
///         keep their type and the transport adapts instead.
///     </para>
///     <para>
///         An explicit list, never "every enum": the LSP spec's own enums are numeric by definition,
///         and converting <c>DiagnosticSeverity</c> to <c>"Error"</c> would break every client on the
///         protocol.
///     </para>
/// </remarks>
public sealed class PreviewEnumConverter : JsonConverter
{
    /// <summary>
    ///     The enums the preview endpoints put on the wire.
    /// </summary>
    /// <remarks>
    ///     An enum added to a preview DTO and forgotten here goes out as an ordinal.
    ///     <c>PreviewWireShapeTest.EveryEnumOnTheWire_IsHandledByTheConverter</c> walks the result
    ///     types and fails with the missing name, which is what caught <see cref="PreviewParticleGate" />
    ///     after this exact mistake had already been made once with <see cref="PreviewSceneKind" />.
    /// </remarks>
    private static readonly HashSet<Type> Handled =
    [
        typeof(PreviewSceneKind),
        typeof(PreviewPartOrigin),
        typeof(PreviewParticleGate),
        typeof(AlamoSpawnShape),
        typeof(AlamoTrackInterpolation),
        typeof(AlamoTrackChannel),
        typeof(AlamoParticleBlendMode),
        typeof(AlamoGroundBehavior),
        typeof(AlamoEmitFromMesh)
    ];

    /// <summary>The preview protocol is server-to-client only; nothing sends these back.</summary>
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType)
    {
        return Handled.Contains(Nullable.GetUnderlyingType(objectType) ?? objectType);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
            writer.WriteNull();
        else
            writer.WriteValue(value.ToString());
    }

    public override object ReadJson(
        JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotSupportedException("The preview protocol only writes these enums.");
    }
}

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
