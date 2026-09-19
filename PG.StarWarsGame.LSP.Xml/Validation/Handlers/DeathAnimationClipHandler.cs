// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports a death animation type the object's model has no clip for (#104, the model half).
/// </summary>
/// <remarks>
///     <para>
///         Measured in the 2018 build. <c>DeathBehaviorClass::Init</c> plays <c>Specific_Death_Anim_Type</c>
///         at <c>Specific_Death_Anim_Index</c> - unset is 0xffff, a random take.
///         <c>ModelClass::Set_Active_Animation_Type</c> returns false, without an assert, when the index is not
///         below the model's count of that type. Init then destroys the object at once if
///         <c>Remove_Upon_Death</c> is set and the unit is not spinning away; otherwise nothing plays and the
///         object fades after its persistence time.
///     </para>
///     <para>
///         The clips come from <c>Land_Model_Anim_Override_Name</c> when set, as in the preview. A model the
///         index has not read is not judged. The value half - that the type exists at all - is the enum check.
///         Vanilla: 2 eaw and 10 foc objects, none with <c>Remove_Upon_Death</c>, three of them the melt death
///         clones on a model with no clips at all.
///     </para>
/// </remarks>
public sealed class DeathAnimationClipHandler : XmlDiagnosticsHandler<DeathAnimationClipFact>
{
    private const string TypeTag = "Specific_Death_Anim_Type";
    private const string IndexTag = "Specific_Death_Anim_Index";

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.DeathAnimationClip;

    protected override IEnumerable<XmlDiagnosticResult> Handle(DeathAnimationClipFact fact, DiagnosticsContext ctx)
    {
        if (ctx.Objects is null) return [];

        var unit = ctx.Objects.Resolve(fact.ObjectId);
        if (!unit.Found) return [];

        var type = unit.ValueOf(TypeTag);
        if (string.IsNullOrEmpty(type)) return [];

        var model = ClipModel(unit);
        if (model is null || !ctx.Index.ModelBones.ContainsKey(ModelBoneKey.From(model))) return [];

        var takes = ModelAnimationClips.CountOfType(ModelAnimationClips.For(ctx.Index, model), model, type);

        // Unset or empty is a random take, which needs one take to exist. A value that is not a number is the
        // value validator's finding.
        var indexText = unit.ValueOf(IndexTag);
        string what;
        if (string.IsNullOrEmpty(indexText))
        {
            if (takes > 0) return [];
            what = $"'{model}' has no {type} animation";
        }
        else
        {
            if (!uint.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                return [];
            if (index < takes) return [];
            what = takes == 0
                ? $"'{model}' has no {type} animation"
                : $"'{model}' has {takes} {type} take{(takes == 1 ? "" : "s")}, so {IndexTag} {indexText} does not exist";
        }

        var consequence = !EngineBoolean.IsTrue(unit.ValueOf("Remove_Upon_Death"))
            ? "plays no death animation."
            : EngineBoolean.IsTrue(unit.ValueOf("Spin_Away_On_Death"))
                ? "plays no death animation and, with Remove_Upon_Death, is removed the moment it dies unless it spins away."
                : "plays no death animation and, with Remove_Upon_Death, is removed the moment it dies.";

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"{what}, so '{fact.ObjectId}' {consequence}", Id: DiagnosticIds.DeathAnimationClip)
        ];
    }

    /// <summary>The model the death clip is looked up on: the land pair when there is a land model, else the space pair.</summary>
    private static string? ClipModel(EffectiveObject unit)
    {
        var land = unit.ValueOf("Land_Model_Name");
        if (!string.IsNullOrEmpty(land))
            return NonEmpty(unit.ValueOf("Land_Model_Anim_Override_Name")) ?? land;

        var space = unit.ValueOf("Space_Model_Name");
        return string.IsNullOrEmpty(space) ? null : NonEmpty(unit.ValueOf("Space_Model_Anim_Override_Name")) ?? space;
    }

    private static string? NonEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}