ScriptPoolCount = 0

function Aet_Forms()
    local a = Find_Nearest(Object, PlayerObject)
    local b = Find_Nearest(Object, "Infantry")
    local c = Find_Nearest(Object, PlayerObject, true)
    local d = FindDeadlyEnemy(Object, 5)
    local e = FindDeadlyEnemy(Object)
    return a, b, c, d, e
end
