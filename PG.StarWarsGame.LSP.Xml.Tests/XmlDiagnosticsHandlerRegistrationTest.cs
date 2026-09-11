// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

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
        // lists (EnumValueList) against the GalacticVictoryCondition enum (A5).
        // 106 → 107: CampaignStoryAttachmentHandler added - validates how a <Campaign> attaches plot
        // manifests to factions across both *_Story_Name authoring forms.
        // 107 → 108: IconAwaitingRepackHandler added - an Icon_Name whose art exists as a raw source
        // but is missing from the workspace mega texture, i.e. drawn but never repacked. Kept apart
        // from TextureFileExistenceHandler because the fix is a rebuild, not a drawing.
        // 108 → 109: ModelTextureExistenceHandler added - the textures a model names INSIDE itself,
        // which no XML tag mentions and nothing therefore validated. Reported against the tag that
        // pulls the model in, because that is the only place in the document it can be anchored.
        // 109 -> 110: DamageStageNotOnModelHandler added - Land_Damage_Alternates naming a damage
        // stage nothing in the object's model is tagged _ALT<n> for, so the unit reaches that state
        // and does not change. One direction only: a model staging MORE than the XML uses is an
        // asset carrying more than this object asks of it, and is never reported.
        // 110 -> 111: UnnamedObjectHandler added - an object element whose Name attribute is empty
        // or absent. The parser skips it with a debug log, so it becomes no symbol at all: nothing
        // can reference or override it and it shows up in no list. Error rather than warning, and
        // safe at that severity - of the 44 shipped files containing the text Name="", every one is
        // inside a comment block, so the live count across foc/ and eaw/ is zero.
        // 111 -> 112: DamageAbsorbsNothingHandler added - both terms of the absorb formula at zero,
        // so the ability triggers and heals nothing. A cross-tag rule because neither value is
        // wrong alone: zero percentage with a flat amount, or the reverse, are both normal.
        // 112 -> 113: SpecialWeaponBehaviorHandler added - the first handler to ask about an object
        // OTHER than the one being edited, via DiagnosticsContext.Objects. Checks the behaviour
        // rather than the name, because vanilla's own special weapons (Ground_Ion_Cannon,
        // Ground_Empire_Hypervelocity_Gun) would fail the name rule the issue originally asked for.
        // 113 -> 114: VehicleThiefCloneHandler added - a capture clone with no EJECT_VEHICLE_THIEF
        // ability, so the thief can never get out. Second user of the cross-object seam. Only the
        // ability half of the tag's stated rule is enforced; the GARRISON_VEHICLE half would warn
        // on two shipped objects with nothing but a description to justify it.
        // 114 -> 115: LandDamageTableMismatchHandler added - Land_Damage_Thresholds and
        // Land_Damage_Alternates are one positional table and must be the same length. Two of the
        // three columns the issue named: Land_Damage_SFX disagrees with the alternates on 42 of
        // foc's 219 objects and 37 of eaw's 161, so enforcing the stated three-column rule would
        // fire on the base game.
        // 115 -> 117: NonNegativeValueHandler and PositiveValueHandler added - two range rules the
        // engine states in its own error messages about 62 tags between them ("cannot be less than
        // zero", "must be greater than zero"), harvested from the 2018 binary. Opt-in by
        // validationId, so no XmlValueType member was invented and each tag keeps the numeric type
        // the engine actually parses.
        // 117 -> 118: BonusPercentageHandler added - nine *_Bonus_Percentage tags are multipliers
        // the engine adds to 1.0, so it rejects -1.0 or below. The bound is exclusive, which is
        // what separates this from a plain lower-bound check.
        // 118 -> 123: the remaining ranges the engine states, on the shared NumericRangeHandlerBase
        // (min, max, and whether each bound is inclusive). AngleDegreesHalfTurn, AngleDegreesFullTurn,
        // NegativeFraction, FractionBelowOne and BelowOne. The three existing range handlers moved
        // onto the same base rather than keeping their own copies of parse-and-compare.
        const int expectedHandlerCount = 123;

        Assert.Equal(expectedHandlerCount, RegisteredHandlerTypes().Count);
    }

    /// <summary>
    ///     The cross-object seam has to survive DI, not just compile.
    /// </summary>
    /// <remarks>
    ///     <c>IVariantTagSource</c> is a REQUIRED constructor parameter, and this test is why it
    ///     became one. As an optional it was the single dependency shape a container is free to
    ///     skip, and skipping it is silent: the publisher still builds, still publishes, and every
    ///     cross-object rule returns nothing forever - invisible to the handler tests, which pass
    ///     the dependency in by hand. So it is asserted against a container built exactly the way
    ///     the server builds one.
    /// </remarks>
    [Fact]
    public void The_publisher_gets_an_object_source_from_the_real_container()
    {
        var services = new ServiceCollection();
        services.AddXmlLanguageServices();

        var registered = services.Any(d => d.ServiceType == typeof(IVariantTagSource));
        Assert.True(registered, "IVariantTagSource must be registered for cross-object rules to run.");

        // The publisher itself cannot be resolved here - it needs ILanguageServerFacade, which the
        // server supplies - so this guards the two halves that CAN be checked without it: the
        // service is registered, and the constructor still asks for it. Whether the container
        // actually fills the optional parameter is a runtime property of the live graph; the
        // publisher exposes HasObjectSource for that, and the E2E run is what exercises it.
        var ctor = Assert.Single(typeof(XmlDiagnosticsPublisher).GetConstructors());
        Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(IVariantTagSource));
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