// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;

namespace PG.StarWarsGame.LSP.Preview;

/// <summary>
///     One stat an ability multiplies while it is active.
/// </summary>
/// <remarks>
///     <c>&lt;Mod_Multiplier&gt; SPEED_MULTIPLIER, 0.8f &lt;/...&gt;</c> - 316 uses over 9 kinds in
///     foc, led by SPEED, WEAPON_DELAY, ENERGY_REGEN and SHIELD_REGEN. This is the whole of what a
///     stat-only ability does: DEFEND declares no proxy, no bone, no particle and no clip, so
///     without these its row is a switch with nothing behind it.
/// </remarks>
public sealed record PreviewAbilityModifier(string Stat, float Factor);

/// <summary>
///     One ability the subject declares, and what it drives on the model.
/// </summary>
/// <remarks>
///     <para>
///         68 ability types exist over the two trees and MOST DRIVE NOTHING on the model - SPREAD_OUT
///         and HUNT are orders, not effects. So an ability with no bone, no particle and no clip is
///         the common case and is reported plainly rather than hidden: what a unit can do is worth
///         reading even when the answer is "nothing you can see here".
///     </para>
///     <para>
///         <paramref name="ProxyNames" /> are the particle proxies BOUND to this ability by their
///         name prefix - see <c>AbilityProxyPrefix</c>. The prefix is a hint, so a proxy is bound
///         only when the object also declares the type it names.
///     </para>
/// </remarks>
/// <param name="DeployClip">
///     The clip this ability plays when it activates, when the model ships one. 25 models in foc
///     carry a <c>_deploy_00</c>; 23 carry the matching <c>_undeploy_00</c>, and the X-Wing is one of
///     the two that ship a deploy with NO undeploy - so the pair is genuinely optional on both sides.
/// </param>
/// <param name="GuiName">
///     The ability's <c>GUI_Activated_Ability_Name</c>: NOT display text, but the name of a
///     <c>SpecialAbility</c> block defined elsewhere in the XML. Carried so the lens can offer a jump
///     to that definition; 39% of shipped abilities declare one, and an ability without it never
///     reaches the command bar at all.
/// </param>
/// <param name="Name">
///     What the command bar calls this ability, already localised. From
///     <c>Alternate_Name_Text</c> when the instance overrides it, otherwise the
///     <c>TEXT_TOOLTIP_ABILITY_&lt;TYPE&gt;_NAME</c> convention. Null when neither resolves, which
///     for a shipped ability means the mod has replaced the text without providing it.
/// </param>
/// <param name="Description">
///     The tooltip text, already localised, resolved the same way from
///     <c>Alternate_Description_Text</c> or <c>TEXT_TOOLTIP_ABILITY_&lt;TYPE&gt;_DESCRIPTION</c>.
/// </param>
/// <param name="IconDataUri">
///     The command-bar icon as a <c>data:image/png;base64,...</c> URI, or null when nothing could be
///     asserted about it. A declared-but-missing icon carries the missing-art placeholder here AND
///     raises a problem, because that is what the engine would draw. Same shape as the reticle icons
///     beside it. The atlas slot is 26x26 - draw it near native size rather than blown up, which is
///     how the game itself shows it.
/// </param>
[PublicAPI]
public sealed record PreviewAbility(
    string Type,
    string? GuiName,
    string? OwnerAttachmentBone,
    string? ParticleEffect,
    float? RechargeSeconds,
    float? ExpirationSeconds,
    IReadOnlyList<string> ProxyNames,
    string? DeployClip,
    string? UndeployClip,
    IReadOnlyList<PreviewAbilityModifier> Modifiers,
    string? Name = null,
    string? Description = null,
    string? IconDataUri = null);

/// <summary>A faction's colours, for the colourisation controls.</summary>
/// <param name="Color">Team tint, applied to <c>Colorization</c> and <c>FC_</c>-prefixed meshes.</param>
/// <param name="NoColorizationColor">
///     Blended into white pixels of the hull texture's alpha channel - a different mechanism from the
///     team tint, and the reason both travel.
/// </param>
[PublicAPI]
public sealed record PreviewFaction(
    string Name,
    PreviewRgba? Color,
    PreviewRgba? NoColorizationColor,
    PreviewRgba? DisplayFontColor);