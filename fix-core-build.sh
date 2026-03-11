#!/bin/bash
echo "1. Running build to capture the raw error log..."
dotnet build Chummer.CoreEngine.sln > build_error_log.txt || true

echo "2. Gathering remaining project files..."
PROJECT_FILES=$(find . -name "*.csproj" | tr '\n' ' ')

echo "3. Launching Aider to repair the architecture..."
aider --model gemini/gemini-3.1-pro-preview \
  $PROJECT_FILES \
  build_error_log.txt \
  --message "Read build_error_log.txt. The UI directories have been completely deleted. If there are MSBuild errors complaining about missing .csproj files, surgically remove the broken <ProjectReference> tags from the loaded .csproj files. If there are CS compiler errors, stub out the missing UI interfaces in Chummer.Contracts. Do whatever it takes to get the build green."
