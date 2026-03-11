#!/bin/bash

# Configuration
cd /docker/chummercomplete/chummer-presentation/
if [ -f "/docker/EA/.env" ]; then
    export $(grep -v '^#' /docker/EA/.env | xargs)
fi

MODEL="gemini/gemini-3.1-pro-preview"
BUILD_CMD="dotnet build Chummer.Presentation.sln"
BUDGET_LIMIT=200
CURRENT_COST=0

# Helper function to run Aider and track costs
run_ticket() {
    local TICKET_NAME=$1
    shift
    local AIDER_ARGS=("$@")

    echo "=================================================="
    echo "🎫 $TICKET_NAME"
    echo "=================================================="
    
    # We pipe the output to tee so we can see it AND capture it to scrape the cost
    aider --model $MODEL --yes --test-cmd "$BUILD_CMD" "${AIDER_ARGS[@]}" | tee aider_output.txt
    
    # Scrape the cost line (e.g., "Cost: $0.05 message, $0.15 session.")
    # We extract the session cost, remove the dollar sign, and add it to our running total using awk
    SESSION_COST=$(grep -oP 'Cost: \$\d+\.\d+ message, \$\K\d+\.\d+(?= session\.)' aider_output.txt | tail -n 1)
    
    if [ ! -z "$SESSION_COST" ]; then
        CURRENT_COST=$(awk "BEGIN {print $CURRENT_COST + $SESSION_COST}")
        echo "💰 Accumulated Cost: $$CURRENT_COST / $$BUDGET_LIMIT"
        
        # Check if we blew the budget
        if (( $(echo "$CURRENT_COST >= $BUDGET_LIMIT" | bc -l) )); then
            echo "🛑 KILLS-WITCH ACTIVATED: Budget limit of $$BUDGET_LIMIT reached."
            exit 1
        fi
    fi
}

echo "🚀 Starting Autonomous Project Manager for Instance B..."

# Ticket B1: Create the new Avalonia/Blazor presentation solution
run_ticket "TICKET B1: Initializing Presentation Solution" \
  --message "Create a new Chummer.Presentation.sln file. Add the existing Chummer.Avalonia, Chummer.Avalonia.Browser, Chummer.Blazor, and Chummer.Blazor.Desktop projects to it. Add a project reference in all of them pointing to the Chummer.Contracts.csproj file in the adjacent chummer-core-engine folder. Do not implement any logic yet."

# Ticket B2: Stub the UI Services
run_ticket "TICKET B2: Wiring UI Services to Contracts" \
  Chummer.Avalonia/Services/EngineClient.cs \
  --message "Create an 'EngineClient' class in the Avalonia project that implements a basic interface to call the IBuildLabEngine and IDeriveInitiativeCapability contracts over a mocked asynchronous boundary. Ensure the build passes."

echo "=================================================="
echo "🎉 Instance B Milestones Complete! Final Cost: $$CURRENT_COST"
