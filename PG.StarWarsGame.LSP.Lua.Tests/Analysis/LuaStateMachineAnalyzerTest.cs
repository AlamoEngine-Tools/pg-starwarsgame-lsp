// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis.Lua;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Lua.Tests.Analysis;

/// <summary>
///     The game's state machine library holds scripts to a fixed contract (#252): a state is a name
///     registered with a function, transitions name registered states, and a handler receives one
///     message - OnEnter, OnUpdate or OnExit. Story mode registers the entries of StoryModeEvents.
/// </summary>
public sealed class LuaStateMachineAnalyzerTest
{
    private const string ScriptUri = "file:///mod/data/scripts/gameobject/interdictor.lua";

    private static readonly LuaStateMachineContract Vanilla = new(true, true, new HashSet<string>());

    private static IReadOnlyList<Diagnostic> Analyze(string text, LuaStateMachineContract? contract = null,
        string uri = ScriptUri)
    {
        var tree = LuaSyntaxTree.ParseText(text, new LuaParseOptions(LuaSyntaxOptions.Lua51));
        return LuaStateMachineAnalyzer.Analyze(uri, tree, contract ?? Vanilla, TestCatalogues.English);
    }

    private static Diagnostic Single(string text, DiagnosticId id, LuaStateMachineContract? contract = null)
    {
        return Assert.Single(Analyze(text, contract), d => d.Code!.Value.String == id.ToString());
    }

    private const string WellFormed = """
                                      function Definitions()
                                          Define_State("State_Init", State_Init)
                                          Define_State("State_Attack", State_Attack)
                                      end
                                      function State_Init(message)
                                          if message == OnEnter then
                                              Set_Next_State("State_Attack")
                                          elseif message == OnUpdate then
                                          elseif message == OnExit then
                                          end
                                      end
                                      function State_Attack(message)
                                          if message ~= OnEnter then return end
                                      end
                                      """;

    [Fact]
    public void AWellFormedMachine_HasNoFindings()
    {
        Assert.Empty(Analyze(WellFormed));
    }

    // The library is a mod's own and may not keep the contract: nothing is judged.
    [Fact]
    public void WithoutTheLibrarysContract_NothingIsJudged()
    {
        Assert.Empty(Analyze("Set_Next_State(\"Nowhere\")\nDefine_State(\"A\", 5)",
            new LuaStateMachineContract(false, false, new HashSet<string>())));
    }

    [Fact]
    public void ATransitionToAnUnregisteredState_IsAnError_AtTheName()
    {
        var diagnostic = Single("Define_State(\"State_Init\", State_Init)\nfunction State_Init(message) end\n" +
                                "Set_Next_State(\"State_Atack\")",
            DiagnosticIds.LuaStateTransitionUnregistered);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal((2, 16, 27), (diagnostic.Range.Start.Line, diagnostic.Range.Start.Character,
            diagnostic.Range.End.Character));
        Assert.Contains("State_Atack", diagnostic.Message);
    }

    // A state registered under a computed name could be any name: transitions are not judged then.
    [Fact]
    public void AComputedRegistration_SilencesTheTransitionRule()
    {
        Assert.Empty(Analyze("Define_State(name, State_Init)\nfunction State_Init(message) end\n" +
                             "Set_Next_State(\"Anything\")"));
    }

    [Fact]
    public void ARegistrationOfANonFunction_IsAnError()
    {
        var diagnostic = Single("Define_State(\"State_Init\", State_Iint)\nfunction State_Init(message) end",
            DiagnosticIds.LuaStateHandlerNotFunction);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("State_Iint", diagnostic.Message);
    }

    // A handler defined in a file of the script's state is a function at run time.
    [Fact]
    public void AHandlerDefinedInAnotherFileOfTheState_IsAFunction()
    {
        Assert.Empty(Analyze("Define_State(\"State_Init\", Shared_Init)",
            new LuaStateMachineContract(true, true, new HashSet<string> { "Shared_Init" })));
    }

    // Measured over the vanilla scripts: 8 EaW and 202 FoC entries name a function defined nowhere.
    [Fact]
    public void AStoryEventNamingAnUndefinedFunction_IsAWarning_TheEventDoesNothing()
    {
        var diagnostic = Single("function Definitions()\n  StoryModeEvents = { Demo_Start = State_Demo_Start }\nend",
            DiagnosticIds.LuaStoryEventHandlerMissing);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Demo_Start", diagnostic.Message);
    }

    [Fact]
    public void StoryEvents_WithoutStoryMode_AreNotJudged()
    {
        Assert.Empty(Analyze("StoryModeEvents = { Demo_Start = State_Demo_Start }",
            new LuaStateMachineContract(true, false, new HashSet<string>())));
    }

    // Story mode registers its own first state and every StoryModeEvents entry.
    [Fact]
    public void ATransitionToAStoryEventOrTheStoryInitState_IsRegistered()
    {
        Assert.Empty(Analyze("StoryModeEvents = { Demo_Start = State_Demo_Start }\n" +
                             "function State_Demo_Start(message) end\n" +
                             "Set_Next_State(\"Demo_Start\")\nSet_Next_State(\"PG_Story_State_Init\")"));
    }

    [Fact]
    public void AStateRegisteredTwice_IsAWarning()
    {
        var diagnostic = Single("Define_State(\"A\", F)\nDefine_State(\"A\", G)\nfunction F(message) end\n" +
                                "function G(message) end", DiagnosticIds.LuaStateRegisteredTwice);

        Assert.Equal(1, diagnostic.Range.Start.Line);
    }

    [Fact]
    public void AHandlerNotTakingOneParameter_IsAWarning()
    {
        var diagnostic = Single("Define_State(\"A\", F)\nfunction F(message, extra) end",
            DiagnosticIds.LuaStateHandlerParameters);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("2", diagnostic.Message);
    }

    // The parameter list of a handler taking none is "()", too small to see: the name is marked.
    [Fact]
    public void AHandlerNotTakingOneParameter_IsMarkedAtItsName()
    {
        var diagnostic = Single("Define_State(\"A\", State_A)\nfunction State_A() end",
            DiagnosticIds.LuaStateHandlerParameters);

        Assert.Equal((1, 9, 16), (diagnostic.Range.Start.Line, diagnostic.Range.Start.Character,
            diagnostic.Range.End.Character));
    }

    // An inline handler has no name: its parameter list is marked, not its whole body.
    [Fact]
    public void AnInlineHandlerNotTakingOneParameter_IsMarkedAtItsParameterList()
    {
        var diagnostic = Single("Define_State(\"A\", function() end)", DiagnosticIds.LuaStateHandlerParameters);

        Assert.Equal((0, 26, 28), (diagnostic.Range.Start.Line, diagnostic.Range.Start.Character,
            diagnostic.Range.End.Character));
    }

    [Fact]
    public void TheMessageComparedWithAnythingElse_IsAWarning()
    {
        var diagnostic = Single("Define_State(\"A\", F)\nfunction F(message)\n  if message == OnEntre then end\nend",
            DiagnosticIds.LuaStateMessageComparison);

        Assert.Equal(2, diagnostic.Range.Start.Line);
        Assert.Contains("OnEntre", diagnostic.Message);
    }

    // The handler's parameter is the message whatever the author called it.
    [Fact]
    public void TheMessage_IsTheFirstParameterWhateverItsName()
    {
        Assert.Single(Analyze("Define_State(\"A\", F)\nfunction F(msg)\n  if msg == 1 then end\nend"),
            d => d.Code!.Value.String == DiagnosticIds.LuaStateMessageComparison.ToString());
    }

    // A library's machine runs in the scripts that require it; the file alone cannot be judged.
    [Fact]
    public void ALibrary_IsNotJudged()
    {
        Assert.Empty(Analyze("Set_Next_State(\"Nowhere\")", uri: "file:///mod/data/scripts/library/mylib.lua"));
    }
}