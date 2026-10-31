require("PGStateMachine")

function Definitions()
    Define_State("State_Init", State_Init)
    Aet_Setup()
end

function Aet_Setup()
    aet_owner = Object
end

function State_Init(message)
    if message == OnEnter then
        aet_hits = aet_hits + 1
    end
end
