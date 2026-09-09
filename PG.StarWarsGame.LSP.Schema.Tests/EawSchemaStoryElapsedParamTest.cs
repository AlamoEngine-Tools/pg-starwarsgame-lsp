// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     <c>STORY_ELAPSED</c> takes exactly ONE parameter, and this pins it so nobody adds a second.
/// </summary>
/// <remarks>
///     <para>
///         It was reported (issue #134) that the event also supports <c>Event_Param2</c>. It does
///         not, and the engine source settles it rather than leaving it to inference:
///         <c>StoryEvent.cpp</c> dispatches <c>Event_Param1..7</c> straight to
///         <c>Set_Param(0..6)</c>, and <c>StoryEventElapsedClass::Set_Param</c> handles
///         <c>index == 0</c> only - it reads <c>TriggerTime</c> and returns, never calling the
///         base. Every other index is silently dropped.
///     </para>
///     <para>
///         The report almost certainly came from vanilla:
///         <c>foc/Data/Xml/Story_rebel_actiii_m08_space.xml</c> has a <c>STORY_ELAPSED</c> event
///         carrying <c>Event_Param2 = 4</c> and NO <c>Event_Param1</c>. That is an authoring
///         mistake rather than evidence of support - with param 0 never set, <c>TriggerTime</c>
///         keeps its constructor default of <c>-1</c>, so <c>Elapsed &gt;= TriggerTime</c> is true
///         at once and the event fires on the next frame instead of after four seconds. The 4 is
///         dead data.
///     </para>
///     <para>
///         Our two existing diagnostics already say exactly that, together: "STORY_ELAPSED requires
///         Event_Param1" and "'Event_Param2' is not used by STORY_ELAPSED". Declaring a second
///         param would silence both and bless the typo, so the schema deliberately stays as it is.
///         The near-identical <c>STORY_OBJECTIVE_TIMEOUT</c> is the event that really does take two
///         (time, then objective name) - that is the one to reach for.
///     </para>
/// </remarks>
public sealed class EawSchemaStoryElapsedParamTest
{
    [Fact]
    public void StoryElapsed_TakesExactlyOneParam_TheTriggerTime()
    {
        var elapsed = EventValue("STORY_ELAPSED");

        Assert.NotNull(elapsed.Params);
        var param = Assert.Single(elapsed.Params!);
        Assert.Equal(0, param.Position);
        Assert.Equal(XmlValueType.Float, param.ValueType);
    }

    // The contrast case, kept beside it: this one is genuinely two-parameter, so a future reader
    // comparing the pair sees the difference is deliberate rather than an oversight.
    [Fact]
    public void StoryObjectiveTimeout_IsTheTwoParamOne()
    {
        var timeout = EventValue("STORY_OBJECTIVE_TIMEOUT");

        Assert.NotNull(timeout.Params);
        Assert.Equal(2, timeout.Params!.Count);
        Assert.Equal(XmlValueType.Float, timeout.Params.Single(p => p.Position == 0).ValueType);
        Assert.Equal(XmlValueType.NameReference, timeout.Params.Single(p => p.Position == 1).ValueType);
    }

    private static RawEnumValueDefinition EventValue(string name)
    {
        return StoryEventSchema.Value(name);
    }
}
