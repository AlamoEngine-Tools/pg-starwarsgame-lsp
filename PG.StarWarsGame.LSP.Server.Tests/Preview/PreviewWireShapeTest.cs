// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     What the preview endpoints actually put on the wire.
/// </summary>
/// <remarks>
///     The TS mirror in <c>src/protocol/modelPreview.ts</c> is hand-written, so nothing but a test
///     stops the two drifting. Field names are the whole contract here: a vector that serialises under
///     names the client does not read arrives as three undefined numbers, and the first sign of it is
///     NaN positions in the renderer - a long way from the cause.
/// </remarks>
public sealed class PreviewWireShapeTest
{
    /// <summary>Serialises the way the server's OUTPUT pipeline does, not a convenient stand-in.</summary>
    private static JObject SerializeAsResponse(object value)
    {
        return JObject.Parse(PreviewSerialization.Create().SerializeObject(value));
    }

    [Fact]
    public void TheResponsePipeline_UsesTheConverterToo()
    {
        // The converter was installed on the serializer's cached JsonSerializer, which every test
        // here used - but OmniSharp writes responses through Settings, so the live server kept
        // sending ordinals while the whole suite passed. Assert the path that actually ships.
        var json = SerializeAsResponse(Volume() with { Shape = AlamoSpawnShape.Sphere });

        Assert.Equal("Sphere", json["shape"]?.Value<string>());
    }

    private static JObject Serialize(object value)
    {
        // The server's own serializer, not a fresh one - a test that builds its own proves only what
        // that one does.
        return JObject.FromObject(value, PreviewSerialization.Create().JsonSerializer);
    }

    [Fact]
    public void Vectors_SerialiseWithTheComponentNamesTheClientReads()
    {
        var emitter = new AlamoEmitter("smoke", "p_particle_master.tga", null, Volume(), Volume(),
            Volume(), [], 0, 0,
            new AlamoEmitterProperties { Acceleration = new Vector3(1, 2, 3) });

        var json = Serialize(emitter);
        var acceleration = json["properties"]?["acceleration"];

        Assert.NotNull(acceleration);
        Assert.Equal(1d, acceleration["x"]?.Value<double>());
        Assert.Equal(2d, acceleration["y"]?.Value<double>());
        Assert.Equal(3d, acceleration["z"]?.Value<double>());
    }

    [Fact]
    public void SpawnVolumes_SerialiseTheirVectorsToo()
    {
        var json = Serialize(Volume() with { ExactValue = new Vector3(4, 5, 6) });

        Assert.Equal(4d, json["exactValue"]?["x"]?.Value<double>());
        Assert.Equal(6d, json["exactValue"]?["z"]?.Value<double>());
    }

    [Fact]
    public void RandomColors_KeepsItsFourthComponent()
    {
        // Vector4, not Vector3 - the alpha channel is a real per-particle randomisation.
        var properties = new AlamoEmitterProperties { RandomColors = new Vector4(0.1f, 0.2f, 0.3f, 0.4f) };

        var json = Serialize(properties)["randomColors"];

        Assert.NotNull(json);
        Assert.Equal(0.4d, json["w"]?.Value<double>() ?? 0d, 5);
    }

    [Fact]
    public void Enums_SerialiseAsTheNamesTheClientSwitchesOn()
    {
        // The TS mirror declares these as string unions, and the simulator switches on them by name.
        // An integer here does not fail loudly: every switch falls through to its default, so every
        // particle quietly spawns at the origin with no speed.
        var json = Serialize(Volume() with { Shape = AlamoSpawnShape.Sphere });

        Assert.Equal("Sphere", json["shape"]?.Value<string>());
    }

    [Fact]
    public void SceneKindAndPartOrigin_SerialiseAsNamesToo()
    {
        var scene = new PreviewScene(PreviewSceneKind.Particle, "p_smoke.alo",
            [new PreviewPart("hull", "hull.alo", null, null, PreviewPartOrigin.Hardpoint, null, true)],
            [], [], [], new GameAssetTiers(1, false, false, 0));

        var json = Serialize(scene);

        Assert.Equal("Particle", json["kind"]?.Value<string>());
        Assert.Equal("Hardpoint", json["parts"]?[0]?["origin"]?.Value<string>());
    }

    [Fact]
    public void EveryWayOfSerialising_AgreesOnTheShape()
    {
        // A converter added to the serializer has to apply however the value reaches it. These two
        // paths are both in use - the handlers take one, this test file the other - and a test that
        // only exercised the second would prove nothing about what the server sends.
        var serializer = PreviewSerialization.Create().JsonSerializer;
        var volume = Volume() with { Shape = AlamoSpawnShape.Sphere };

        var viaFromObject = JObject.FromObject(volume, serializer)["shape"]?.ToString();

        using var writer = new StringWriter();
        serializer.Serialize(writer, volume);
        var viaSerialize = JObject.Parse(writer.ToString())["shape"]?.ToString();

        Assert.Equal(viaFromObject, viaSerialize);
    }

    [Fact]
    public void EveryEnumOnTheWire_IsHandledByTheConverter()
    {
        // A structural guard, not a list to remember. The converter carries an explicit set of types,
        // and adding a DTO enum without adding it there ships an ordinal where the hand-written TS
        // mirror expects a name - silent, because every switch just falls through to its default.
        // That has now happened twice: PreviewSceneKind, then PreviewParticleGate.
        var converter = new PreviewEnumConverter();

        var missing = ReachableEnums(
                typeof(GetPreviewSceneResult),
                typeof(GetModelGlbResult),
                typeof(GetParticleSystemResult),
                typeof(GetModelTextureResult))
            .Where(type => !converter.CanConvert(type))
            .Select(type => type.Name)
            .Order()
            .ToList();

        Assert.True(missing.Count == 0,
            "These enums cross the wire but PreviewEnumConverter does not handle them, so they "
            + $"serialise as ordinals: {string.Join(", ", missing)}");
    }

    [Fact]
    public void DictionaryKeys_CrossTheWireExactlyAsAuthored()
    {
        // MEASURED against a live server 2026-08-23: the reticle map arrived keyed
        // `harD_POINT_WEAPON_LASER`, because the LSP serializer's camel-case naming strategy
        // processes dictionary KEYS as well as property names. The client looks the type up by the
        // hardpoint's own `HARD_POINT_WEAPON_LASER`, so every lookup missed and no reticle could
        // ever be drawn - silently, since an absent mapping is a legitimate state.
        //
        // The icon names are the same defect one level down: `byType` names an icon VERBATIM while
        // `icons` is keyed by the mangled form, so the two halves of the same reply disagree.
        var reticles = new PreviewReticles(
            new Dictionary<string, PreviewReticleStates>
            {
                ["HARD_POINT_WEAPON_LASER"] = new("I_Hard_Point_Reticle_Weapons",
                    null, null, null, null, null, null)
            },
            new Dictionary<string, string> { ["I_Hard_Point_Reticle_Weapons"] = "data:image/png;base64,AA" },
            0.03f, 0.03f);

        var json = SerializeAsResponse(reticles);

        Assert.NotNull(json["byType"]?["HARD_POINT_WEAPON_LASER"]);
        Assert.NotNull(json["icons"]?["I_Hard_Point_Reticle_Weapons"]);
    }

    [Fact]
    public void EveryDictionaryOnTheWire_KeepsItsKeys()
    {
        // Structural, like the enum guard above and for the same reason: the next dictionary added
        // to a preview DTO would be mangled in exactly the same way, and nothing about the reply
        // would look wrong.
        var unguarded = ReachableDictionaries(
                typeof(GetPreviewSceneResult),
                typeof(GetModelGlbResult),
                typeof(GetParticleSystemResult),
                typeof(GetModelTextureResult))
            .Where(property => property.GetCustomAttributes(typeof(JsonConverterAttribute), true)
                .OfType<JsonConverterAttribute>()
                .All(attribute => attribute.ConverterType != typeof(VerbatimKeyDictionaryConverter)))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .Order()
            .ToList();

        Assert.True(unguarded.Count == 0,
            "These dictionaries cross the wire without [JsonConverter(typeof("
            + "VerbatimKeyDictionaryConverter))], so the camel-case naming strategy rewrites their "
            + $"keys: {string.Join(", ", unguarded)}");
    }

    /// <summary>Every dictionary-typed property reachable from these result types.</summary>
    private static HashSet<PropertyInfo> ReachableDictionaries(params Type[] roots)
    {
        var seen = new HashSet<Type>();
        var found = new HashSet<PropertyInfo>();
        var queue = new Queue<Type>(roots);

        while (queue.Count > 0)
        {
            var type = Unwrap(queue.Dequeue());

            if (!seen.Add(type)
                || type.Namespace?.StartsWith("PG.StarWarsGame", StringComparison.Ordinal) != true)
                continue;

            foreach (var property in type.GetProperties())
            {
                if (IsDictionary(property.PropertyType))
                    found.Add(property);

                queue.Enqueue(property.PropertyType);
            }
        }

        return found;
    }

    private static bool IsDictionary(Type type)
    {
        return type.IsGenericType
               && (type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                   || type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                   || type.GetGenericTypeDefinition() == typeof(Dictionary<,>));
    }

    /// <summary>Every enum reachable from these result types by public property or record field.</summary>
    private static HashSet<Type> ReachableEnums(params Type[] roots)
    {
        var seen = new HashSet<Type>();
        var enums = new HashSet<Type>();
        var queue = new Queue<Type>(roots);

        while (queue.Count > 0)
        {
            var type = Unwrap(queue.Dequeue());

            if (type.IsEnum)
            {
                enums.Add(type);
                continue;
            }

            // Only our own types; walking into the BCL finds nothing that crosses this protocol.
            if (!seen.Add(type) || type.Namespace?.StartsWith("PG.StarWarsGame", StringComparison.Ordinal) != true)
                continue;

            foreach (var property in type.GetProperties())
                queue.Enqueue(property.PropertyType);
        }

        return enums;
    }

    /// <summary>Looks through nullables and collections to the type actually carried.</summary>
    private static Type Unwrap(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return Unwrap(underlying);

        if (type.IsArray)
            return Unwrap(type.GetElementType()!);

        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            return Unwrap(type.GetGenericArguments()[0]);

        return type;
    }

    private static AlamoSpawnVolume Volume()
    {
        return new AlamoSpawnVolume(AlamoSpawnShape.Point, Vector3.Zero, Vector3.Zero, 0, 0, false, 0,
            false, 0, Vector3.Zero);
    }
}
