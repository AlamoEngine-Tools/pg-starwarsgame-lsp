// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Hardcoded allowlist of concrete ability type names that count as a <c>SpecialAbility</c> for
///     reference type-checking purposes (e.g. <c>GUI_Activated_Ability_Name</c>). The schema has no
///     type-hierarchy concept — <c>types.yaml</c> lists these as flat siblings of
///     <c>SpecialAbility</c> — so <see cref="ReferenceResolutionEvaluator" /> consults this set
///     directly instead of doing exact string equality. Deliberately excludes <c>UnitAbility</c>,
///     which is a structurally different construct (a runtime ability-activation record inside a
///     Unit_Abilities_Data sub-object list), not a kind of ability.
/// </summary>
internal static class SpecialAbilityTypeFamily
{
    public static readonly HashSet<string> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpecialAbility",
        "SiphonCreditsAbility", "GalacticStealthAbility", "DetectHeroAbility",
        "SabotagePoliticalControlAbility", "SystemSpyAbility", "SlicerAbility", "BlackMarketAbility",
        "EnhanceDefenseAbility", "CombatBonusAbility", "ConcentrateFireAttackAbility",
        "LuckyShotAttackAbility", "TractorBeamAttackAbility", "IonCannonShotAttackAbility",
        "MaximumFirepowerAttackAbility", "EnergyWeaponAttackAbility", "ReduceProductionTimeAbility",
        "ReduceProductionPriceAbility", "ReduceTechnologyPriceAbility", "PoliticalControlBonusAbility",
        "PoliticalTransitionBonusAbility", "PlanetIncomeBonusAbility", "PlanetIncomeGamblingAbility",
        "FindWeaknessAbility", "GalacticSabotageAbility", "HeroAssassinAbility", "RedirectBlasterAbility",
        "ForceHealingAbility", "AbsorbBlasterAbility", "ForceWhirlwindAbility", "ForceTelekinesisAbility",
        "ForceLightningAbility", "EarthquakeAttackAbility", "HackAbility", "RepairAbility",
        "VehicleThiefAbility", "PersonalFlameThrowerAbility", "CableAttackAbility", "GrenadeAttackAbility",
        "BaseDestructionAbility", "BattlefieldModifierAbility", "DemolitionAbility",
        "RetreatProtectionAbility", "NeutralizeHeroAbility", "HeroProtectionAbility",
        "GenericAttackAbility", "ArcSweepAttackAbility", "EatAttackAbility", "IncomeStreamAbility",
        "IncomeStreamModAbility", "EnableRadarAbility", "GarrisonUpgradeAbility", "EnableAbilityAbility",
        "GalaxyWideUpgradeAbility", "WeatherproofAbility", "StealthAbility", "CorruptPlanetAbility",
        "RemoveCorruptionAbility", "StunAbility", "RadioactiveContaminateAbility", "BerserkerAbility",
        "ForceSightAbility", "SaberThrowAbility", "ForceCloakAbility", "BountyOnFactionAbility",
        "SuperLaserAbility", "LaserDefenseAbility", "ForceConfuseAbility", "LeechShieldsAbility",
        "TacticalBribeAbility", "ClusterBombAbility", "SensorJammingAbility", "RemoteBombAbility",
        "InfectionAbility", "ProximityMinesAbility", "DrainLifeAbility", "BlastAbility",
        "BuzzDroidsAbility", "ShieldFlareAbility", "SummonAbility", "CorruptSystemsAbility",
        "SpawnAbility", "CorruptionAbility", "PermanentWeaponSwapAbility"
    };
}