require("PGStateMachine")

function Definitions()
    Define_State("State_Init", State_Init)
end

function State_Init(message)
    Set_Next_State("State_Nowhere")
end
