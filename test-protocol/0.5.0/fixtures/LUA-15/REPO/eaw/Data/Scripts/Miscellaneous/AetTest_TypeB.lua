ScriptPoolCount = 0
require("PGBase")

function Aet_Set_B()
    aet_shared_value = 5
end

function Aet_Read_B()
    local copy = aet_shared_value
    local ok = TestValid(Object)
    GlobalValue.Set("aet_key", 1)
    return copy, ok
end
