require("PGStateMachine")

function Definitions()
    Define_State("State_Init", State_Init)
    Define_State("State_Wait", State_Wait)
    Define_State("State_Done", State_Done)
    Define_State("State_Done", State_Done)
    Define_State("State_Bad", 5)
end

function State_Init(message)
    if message == OnEnter then
        Set_Next_State("State_Wait")
    elseif message == "OnUpdate" then
        Set_Next_State("State_Nowhere")
    end
end

function State_Wait(message, extra)
end

function State_Done(message)
end
