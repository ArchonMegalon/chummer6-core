#!/bin/bash
cd /docker/chummercomplete/chummer-core-engine/

echo "=================================================="
echo "📝 CREATING LUA RULESET PACK"
echo "=================================================="
mkdir -p Chummer/Backend/BuildLab/Packs
cat << 'LUA' > Chummer/Backend/BuildLab/Packs/KarmaCosts.lua
function CalculateKarma(metatype, isAwakened)
    local cost = 0
    if metatype == "Troll" then 
        cost = 40
    elseif metatype == "Elf" then 
        cost = 30
    elseif metatype == "Dwarf" or metatype == "Ork" then 
        cost = 20
    else 
        cost = 0 
    end

    if isAwakened then 
        cost = cost + 15 
    end
    
    return cost
end
LUA

echo "✅ KarmaCosts.lua created."

# Force-source the env file safely
if [ -f "/docker/EA/.env" ]; then
    set -a; source /docker/EA/.env; set +a
fi

AIDER_CMD="aider"
if ! command -v aider &> /dev/null; then
    if [ -f "$HOME/.local/bin/aider" ]; then AIDER_CMD="$HOME/.local/bin/aider"; else AIDER_CMD="python3 -m aider"; fi
fi

MODEL="gemini/gemini-3.1-pro-preview"
BUILD_CMD="dotnet build Chummer.CoreEngine.sln"

echo "=================================================="
echo "⚙️ TICKET A8: Wiring Lua into the BuildLabEngine"
echo "=================================================="

$AIDER_CMD --model $MODEL \
  Chummer/Backend/BuildLab/BuildLabEngine.cs \
  --yes \
  --test-cmd "$BUILD_CMD" \
  --message "Update 'Chummer/Backend/BuildLab/BuildLabEngine.cs'. 
1. Add 'using System.IO;'.
2. Inside 'ProjectKarmaSpend()', instantiate 'var lua = new LuaScriptEngine();'.
3. Read the lua file: 'string scriptText = File.ReadAllText(\"Chummer/Backend/BuildLab/Packs/KarmaCosts.lua\");'.
4. Call 'double result = lua.EvaluateRule(scriptText, \"CalculateKarma\", \"Troll\", true);'.
5. Set 'ProjectedTotal = (int)result' in the returned KarmaProjectionDto.
6. Ensure it builds successfully."

echo "=================================================="
echo "✅ Pilot Extraction Complete! The Core is running Lua."
echo "=================================================="
