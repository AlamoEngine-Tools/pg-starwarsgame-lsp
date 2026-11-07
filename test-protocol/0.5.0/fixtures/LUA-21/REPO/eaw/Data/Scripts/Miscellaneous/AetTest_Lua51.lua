ScriptPoolCount = 0

function Aet_Count(list)
    local n = #list
    local odd = n % 2
    return n, odd
end

function Aet_Pass(...)
    local first = ...
    return first, arg
end

aet_text = [==[
long string
]==]

--[=[
first comment line
second comment line
]=]

--[=[ one line ]=]

--[[ a Lua 5.0
long comment ]]
