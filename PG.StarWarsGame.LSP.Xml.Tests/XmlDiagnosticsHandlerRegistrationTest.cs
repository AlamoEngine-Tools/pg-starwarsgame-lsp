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
        // 123 -> 124: MissingRequiredTagHandler added - seven tags a Leech_Shields_Ability cannot
        // run without, each with its own "has not been set" message in the binary. Scoped to the
        // element rather than the tag, since Beam_Texture_Name and friends appear on other
        // abilities where the rule does not apply.
        // 124 -> 125: ControlPointCurveHandler added, on a ListLengthHandlerBase that counts
        // ENTRIES rather than values (a curve point is an x,y pair). Three Cost_Mod_By_* tags need
        // at least two points; shape and per-token typing stay with the value type's own handler.
        // 125 -> 126: TagComparisonHandler added, fed by a TagComparisonRuleBase that relates one
        // numeric tag to another - Damage_Radius against Chase_Radius, Min_Respawn_Time against
        // Max_Respawn_Time. Requiring both tags scopes each rule without naming an element.
        // 126 -> 127: UnknownTagHandler added - an element written where a tag belongs that the
        // schema has no tag by that name for, which the engine reads and discards. Scoped to the
        // direct children of an object element: below that an element is the CONTENT of a tag and
        // is described by its value type, not by the tag table. Measured at that scope over foc/
        // and eaw/ it fires 3800 times on 458 distinct names, every one of which was checked
        // against the engine's parser table and has no row there - loud, but no false positives.
        // 127 -> 129: VariantBaseUnresolvedHandler and VariantChainTooDeepHandler added, the two
        // things a variant's base CHAIN can be wrong about as opposed to its tags. Unresolvable is
        // an error - it is the engine's only variant failure with no assert and no log line, so the
        // author gets no other signal. Too-deep is a warning worded as MAY fail, because the same
        // eleven-link chain resolves or does not depending on declaration order.
        // 129 -> 130: VariantTagNotSupportedHandler added - Variant_Of_Existing_Type on a type with
        // no variant machinery, which the engine logs as an unprocessed entry and then ignores.
        // Needs its own rule rather than falling out of UnknownTagHandler, because the tag resolver
        // falls back to a flat lookup across every type: a real tag on the wrong element resolves,
        // so the unknown-tag rule never sees it.
        // 130 -> 131: AtLeastNegativeOneHandler added - a floor of -1.0 INCLUSIVE, one boundary away
        // from BonusPercentageHandler and deliberately not it. Percentage_Income_Modifier's engine
        // test is x <= -1.0 && x != -1.0, i.e. x < -1.0, so -1.0 passes; the eight
        // *_Bonus_Percentage tags reject it. The messages differ by two words, so only the
        // decompiled comparison separates them.
        // 131 -> 132: BooleanGatedRequirementHandler added, fed by six rules on a shared base - the
        // engine's "If you set A to true you must also set B" family. Five pair a System_Spy_Ability
        // detail flag with the summary flag it reads from; the sixth gates a DURATION rather than a
        // flag (Can_Halt_Credit_Production needs Duration_Of_Credit_Halt > 0). Element-scoped,
        // because the rule has to fire when the required tag is absent and so cannot use the pair's
        // presence to scope itself the way TagComparisonRuleBase does.
        // 132 -> 133: AllowedValuesHandler added, and the allowedValues check MOVED out of
        // DynamicEnumValueHandler into it. The restriction lives on the tag, so it has to apply
        // whatever the tag's value type: Causes_Despawn is a Boolean that GalacticSabotageAbility
        // demands be Yes, and an enum-only check did nothing there. Booleans compare by MEANING,
        // since Yes/True/1 are interchangeable to the engine.
        // 133 -> 134: ProjectileCategoryListHandler added - Projectile_Types_Targeted on
        // Laser_Defense_Ability was the one tag with value type 81 and nothing validated it. The
        // tag also had no enum wired, so it is now referenceKind: enum + ProjectileCategory. A
        // misspelt category does not fail the load, it never matches - the point defence quietly
        // stops intercepting that projectile.
        // 134 -> 135: EitherOrRequirementHandler added, fed by three rules on a shared base - the
        // engine's "you should set either A or B to Yes, otherwise this ability won't do anything"
        // family. Its own id rather than BooleanGatedRequirement's: that one reports a value the
        // engine is about to OVERWRITE, this one an ability the engine leaves inert, and a modder
        // silencing "this ability deliberately covers neither case" must not lose the other.
        // Reported once against the object, because neither flag is wrong alone - a No is the
        // ordinary value for whichever half an ability does not cover.
        // The third rule was not in the harvested list: the harvest keyed on the "Error: (%s) "
        // prefix and NeutralizeHeroAbilityClass labels its copy "Warning" while stating the same
        // consequence, with the sentence split across two constants by an embedded newline.
        // 135 -> 136: AutomaticDespawnHandler added - an automatic activation style with
        // Causes_Despawn on, which SpecialAbilityClass::Validate_Data reports and then turns off
        // itself. Scoped by the two TAGS rather than by an element: the rule is stated on the base
        // class every ability type calls first, so naming elements would mean listing all of them
        // and missing whichever one a mod reaches for. Which six styles are automatic is measured
        // from that function's switch, not inferred from the names - and the switch is also where
        // Global_Automatic turned up, a style our enum did not carry.
        // No collision with the three classes that DEMAND Causes_Despawn=Yes: each of those accepts
        // only Ground_Activated, which is not an automatic style.
        // 136 -> 137: AtLeastOneSecondHandler added - LeechShieldsAbilityClass tests Duration < 1.0
        // and then assigns 1.0, so the bound is inclusive and the repair value is the bound itself.
        // Its own id and handler rather than a shared minimum: Duration_In_Secs appears on three
        // ability types and only this one's validator mentions it, so the rule opts in per owner.
        // 137 -> 139: the income-stream pair. OwnerIncomeShareHandler reports a split share outside
        // [0,1) - a CONDITIONAL range, checked by the engine only while Split_Favors_Owner and
        // Split_Income_With_Allies are both on, which is why it is a rule and not a validationId on
        // the tag. IncomeSplitConflictHandler covers the two flag combinations the engine clears,
        // under one id because they are one concern: that flag set where it cannot mean anything.
        // Two things here are measured against the code rather than the messages. Zero passes the
        // share check despite the message saying "greater than zero", and the conflict rule needs
        // the allies split ON - the ignored-flag branch runs first and has already cleared
        // Split_Favors_Owner when it is off, so the engine never reaches the second complaint.
        // 139 -> 140: RespawnTimeListHandler added - the per-tech-level respawn table, as
        // Validate_Respawn_Times checks it for both the Slicer and the Black Market: exactly five
        // entries, none negative, zeros all-or-nothing. One handler and one id for all three,
        // because the engine gates them together and returns at the first failure, after which the
        // caller CLEARS both lists - so any of them costs the same thing and suppressing one while
        // the table stays unusable would help nobody. No quick fix: the engine's repair is that
        // deletion, which is not something to put one keystroke away.
        // 140 -> 141: HardpointUnhittableHandler added - a destroyable hardpoint nothing can hit,
        // either because it names no Collision_Mesh or because another destroyable hardpoint on the
        // same object already claims that mesh and wins the first-match lookup. Both shapes come
        // from the damage-routing pass rather than from an engine message: the game says nothing,
        // and the hardpoint still shows in the UI and still takes its share of the object's health.
        // Its own id for that reason - someone who disagrees with our routing model should not have
        // to silence the rules the engine itself states.
        // 141 -> 142: EngineTextLimitHandler added, and it is the only rule here whose consequence
        // is a game that will not start. DatabaseMapClass::Map_Data_Of_Type strcpy's a tag's value
        // into strtok_string_buffer[8192] with no length check - the size test beside it is an
        // assert, so it exists in the build Petroglyph tested with and not in the one anyone plays.
        // Produced in the fact producer rather than by a value handler because it is a property of
        // the TEXT, true of every tag whatever the schema says it holds.
        // Reached through long list values - a station's HardPoints, a campaign's trade routes or
        // planets - once names get descriptive. Vanilla's longest HardPoints is 869 characters and
        // its longest value of any kind 2,535, so the headroom is real but spendable.
        // 142 -> 143: CaseSensitiveTagHandler added - a tag spelled in a casing its own parser will
        // not accept. Rare by design: DatabaseMapClass uppercases every key it handles, so casing is
        // free for all but the tags with a hand-rolled parser. StoryModeClass::Load_Plots is one,
        // comparing with std::operator==, and the result of its Active_Plot test becomes the
        // is_active argument to Load_Single_Plot - so <active_plot> loads SUSPENDED and the campaign
        // never starts, with nothing said at runtime.
        // Severity follows the consequence, not the rule: that case is an Error, while a mis-cased
        // Suspended_Plot reaches the same outcome it would have anyway and is a Warning.
        // Checked against the authored text rather than the node, because HAP lower-cases names and
        // the casing is gone everywhere else in the walk.
        // 143 -> 144: SystemSpyDurationHandler added - Duration_In_Secs on a System_Spy_Ability,
        // whose legal range flips with Activation_Style. SystemSpyAbilityClass::Validate_Data
        // (0101dcdf) demands the OPPOSITE sign in each arm: Galactic_Automatic complains when
        // 0.0 <= duration and writes -1.0, Ground_Activated complains when duration <= 0.0 and
        // writes 30.0. Zero is refused by both.
        // Its own handler rather than a range on the tag, because no range could say it - the same
        // value is correct under one style and overwritten under the other.
        // The branch's third arm, an unsupported style, is NOT reported here: allowedValues on that
        // owner's Activation_Style reports it where it is written, and picking a bound for a style
        // the class refuses would invent one.
        // 144 -> 145: RequiredFirstEntryHandler added - the first entry of Damage_Types must be
        // Damage_Default and of Armor_Types must be Armor_Default, because index 0 is the fallback
        // the engine returns when a lookup misses. Prepending a type silently repoints every
        // default; appending is safe.
        // The first rule taken from the ASSERT seam rather than from an engine message - these two
        // have no message at all (GameConstants.cpp:1170 and :1180), so neither message harvest
        // could ever have found them, and no shipped build reports them.
        // Additive, not replace: the tag's own NameReferenceList handling still has to run.
        // Keyed on (owner, tag) in code beside the handler rather than carried in the schema - the
        // required name is an engine fact with an address behind it, not something a schema author
        // could author correctly. Same reasoning as EngineValueRepairs.
        // 145 -> 146: GrenadeProjectileHandler added - a Grenade_Attack_Ability's Grenade_Type and a
        // Remote_Bomb_Ability's Bomb_Type must name a projectile whose Projectile_Category is
        // GRENADE. From the assert seam, so there is no engine message: GrenadeAttackAbility.cpp:440
        // and RemoteBombAbility.cpp:388 both read !type->Is_Projectile_Grenade(), and the BRANCH
        // settles it - the assert fires when the call returns false, so the text states the failure
        // and the rule is its opposite. Is_Projectile_Grenade is one comparison, ProjCategory ==
        // PROJECTILE_CATEGORY_GRENADE.
        // Reads the EFFECTIVE object, not the node: four of the ten shipped declarations name a
        // projectile that inherits its category through Variant_Of_Existing_Type, and a node-level
        // check would report every one of them.
        // Keyed on (owner, tag) because Bomb_Type is declared on three ability types and only
        // RemoteBombAbility asserts this - attribution follows the xref, never the schema.
        const int expectedHandlerCount = 146;

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