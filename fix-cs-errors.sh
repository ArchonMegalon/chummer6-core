#!/bin/bash
echo "1. Building solution..."
dotnet build Chummer.CoreEngine.sln > build_error_log.txt || true

echo "2. Finding broken C# files..."
# Extract exact file paths from the compiler errors
BROKEN_FILES=$(grep -oP '^.*\.cs(?=\(\d+,\d+\): error)' build_error_log.txt | sort -u | tr '\n' ' ')

if [ -z "$BROKEN_FILES" ]; then
    echo "No C# errors found! The build might be green."
    exit 0
fi

echo "3. Sending ONLY the broken files to Aider..."
aider --model gemini/gemini-3.1-pro-preview \
  $BROKEN_FILES \
  build_error_log.txt \
  --message "Read build_error_log.txt. I have only passed you the specific .cs files that are failing. Create empty interface or class stubs in the Chummer.Contracts project to resolve these missing UI references so the build passes."
