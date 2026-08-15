// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Tests;

/// <summary>
///     Guards that every concrete <see cref="IXmlDiagnosticsHandler" /> in the Xml assembly is
///     registered exactly once in <c>AddXmlLanguageServices</c>, so additions and deletions
///     cannot silently drift apart from the DI list.
/// </summary>
public sealed class XmlDiagnosticsHandlerRegistrationTest
{
    private static IReadOnlyCollection<Type> AllConcreteHandlerTypes()
    {
        return typeof(XmlLanguageServiceExtensions).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IXmlDiagnosticsHandler).IsAssignableFrom(t))
            .ToList();
    }

    private static IReadOnlyCollection<Type> RegisteredHandlerTypes()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();
        return services
            .Where(d => d.ServiceType == typeof(IXmlDiagnosticsHandler))
            .Select(d => d.ImplementationType)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
    }

    [Fact]
    public void Every_concrete_handler_is_registered()
    {
        var expected = AllConcreteHandlerTypes().ToHashSet();
        var registered = RegisteredHandlerTypes().ToHashSet();

        var missing = expected.Except(registered).ToList();
        var extra = registered.Except(expected).ToList();

        Assert.True(missing.Count == 0,
            "Concrete handlers not registered in AddXmlLanguageServices: " +
            string.Join(", ", missing.Select(t => t.Name)));
        Assert.True(extra.Count == 0,
            "Registered handler types that do not exist as concrete handlers: " +
            string.Join(", ", extra.Select(t => t.Name)));
    }

    [Fact]
    public void Registered_handler_count_is_locked()
    {
        // Authoritative count of concrete IXmlDiagnosticsHandler registrations. Update this
        // deliberately whenever a handler is added or removed so the change is reviewed.
        // 100 → 99: StoryParamReferenceHandler retired - story object params are validated by
        // the generic reference pipeline since the collector emits GameReferences for them.
        // 99 → 100: VariantAdditiveMergeHandler added - reports that an additive tag set on both a
        // variant and its base accumulates rather than replaces (#63).
        // 100 → 101: PlanetModeExclusionListHandler added - validates the mode half and the pairing
        // of Autoresolve_Exclusion_Locations.
        // 101 → 102: HardpointMissingAttachmentBoneHandler added - a destroyable hardpoint with no
        // Attachment_Bone is indestructible (#53).
        // 102 → 104: HardpointBoneNotOnModelHandler + HardpointModelBonesUnavailableHandler added -
        // hardpoint bones cross-checked against the models of the objects mounting them (#53).
        // 104 → 105: HardpointAbilityNotOnOwnerHandler added - Special_Ability_Name must name an
        // ability the mounting object actually has (#53).
        // 105 → 106: VictoryConditionListHandler added - validates the Campaign victory-condition
        // lists (Type69) against the GalacticVictoryCondition enum (A5).
        // 106 → 107: CampaignStoryAttachmentHandler added - validates how a <Campaign> attaches plot
        // manifests to factions across both *_Story_Name authoring forms.
        // 107 → 108: IconAwaitingRepackHandler added - an Icon_Name whose art exists as a raw source
        // but is missing from the workspace mega texture, i.e. drawn but never repacked. Kept apart
        // from TextureFileExistenceHandler because the fix is a rebuild, not a drawing.
        // 108 → 109: ModelTextureExistenceHandler added - the textures a model names INSIDE itself,
        // which no XML tag mentions and nothing therefore validated. Reported against the tag that
        // pulls the model in, because that is the only place in the document it can be anchored.
        const int expectedHandlerCount = 109;

        Assert.Equal(expectedHandlerCount, RegisteredHandlerTypes().Count);
    }

    [Fact]
    public void Each_handler_is_registered_exactly_once()
    {
        var duplicates = RegisteredHandlerTypes()
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Handlers registered more than once: " + string.Join(", ", duplicates));
    }
}