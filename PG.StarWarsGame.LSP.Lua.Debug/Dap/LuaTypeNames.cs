// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Debug.Dap;

/// <summary>The Lua type codes the game sends with a value, as the language names them.</summary>
public static class LuaTypeNames
{
    public const int Nil = 0;
    public const int Boolean = 1;
    public const int LightUserdata = 2;
    public const int Number = 3;
    public const int String = 4;
    public const int Table = 5;
    public const int Function = 6;
    public const int Userdata = 7;
    public const int Thread = 8;

    /// <summary>The code the game answers with when it does not have the script asked about.</summary>
    public const int UnknownScript = -1;

    public static string Of(int typeCode)
    {
        return typeCode switch
        {
            Nil => "nil",
            Boolean => "boolean",
            LightUserdata => "userdata",
            Number => "number",
            String => "string",
            Table => "table",
            Function => "function",
            Userdata => "userdata",
            Thread => "thread",
            UnknownScript => "unknown script",
            _ => $"type {typeCode}"
        };
    }

    public static bool IsTable(int typeCode)
    {
        return typeCode == Table;
    }
}