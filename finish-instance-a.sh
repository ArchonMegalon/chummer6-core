#!/bin/bash
cd /docker/chummercomplete/chummer-core-engine/

# Load keys
if [ -f "/docker/EA/.env" ]; then
    export $(grep -v '^#' /docker/EA/.env | xargs)
fi

MODEL="gemini/gemini-3.1-pro-preview"
BUILD_CMD="dotnet build Chummer.CoreEngine.sln"

echo "🚀 Starting Autonomous Project Manager..."

echo "=================================================="
echo "🎫 TICKET A4: Build Lab Engine"
echo "=================================================="
aider --model $MODEL \
  Chummer.Contracts/BuildLab/IBuildLabEngine.cs \
  Chummer.Contracts/BuildLab/BuildVariantDto.cs \
  Chummer.Contracts/BuildLab/ProgressionSimulationDto.cs \
  Chummer.Contracts/BuildLab/TrapChoiceWarningDto.cs \
  --yes \
  --test-cmd "$BUILD_CMD" \
  --message "Execute Milestone A4. Create an 'IBuildLabEngine' interface with GenerateBuildVariants, ProjectKarmaSpend, and DetectTrapChoices. Create immutable records BuildVariantDto, ProgressionSimulationDto, and TrapChoiceWarningDto. Ensure all properties use standard C# types. Do not modify any .csproj files. Satisfy the test command."

echo "=================================================="
echo "🎫 TICKET A5: Aesthetic and Dossier Seeds"
echo "=================================================="
aider --model $MODEL \
  Chummer.Contracts/Exports/CharacterDossierSeed.cs \
  Chummer.Contracts/Exports/NpcDossierSeed.cs \
  Chummer.Contracts/Exports/RunSummarySeed.cs \
  --yes \
  --test-cmd "$BUILD_CMD" \
  --message "Execute Milestone A5. Create immutable semantic seeds for media generation in the Chummer.Contracts.Exports namespace: CharacterDossierSeed, NpcDossierSeed, and RunSummarySeed. They should contain properties like string Metatype, IReadOnlyList<string> RoleTags, string MoodTags, etc. Do not modify any .csproj files. Satisfy the test command."

echo "=================================================="
echo "🎉 Instance A (Core Engine) Milestones Complete!"
