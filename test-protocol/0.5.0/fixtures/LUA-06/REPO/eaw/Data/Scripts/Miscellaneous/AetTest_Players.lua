ScriptPoolCount = 0

function Aet_Players()
    local me = Find_Player("local")
    local them = Find_Player("Aet_No_Faction")
    local empire = Find_Player("Empire")
    return me, them, empire
end
