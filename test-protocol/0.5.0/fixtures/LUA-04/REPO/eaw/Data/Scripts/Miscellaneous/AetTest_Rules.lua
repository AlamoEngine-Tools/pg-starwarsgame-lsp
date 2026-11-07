ScriptPoolCount = 0

function Aet_Two(a)
    return a
end

---@param nothing string
function Aet_Doc(a)
    return a
end

function Aet_Rules()
    local unused_value = 1
    local t = { a = 1, a = 2 }
    for i = 1, 3 do
        i = 5
    end
    local r = 1
    local r = 2
    Aet_Two(1, 2)
    Sleep(1, 2)
    local p = Find_Player("Empire")
    p.No_Such_Method()
    return t, r
end
