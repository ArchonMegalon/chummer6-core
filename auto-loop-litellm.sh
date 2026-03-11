#!/bin/bash
MAX_RETRIES=5
ATTEMPT=1

while [ $ATTEMPT -le $MAX_RETRIES ]; do
    echo "=========================================="
    echo "⚙️  BUILD ATTEMPT $ATTEMPT / $MAX_RETRIES"
    echo "=========================================="
    
    dotnet build Chummer.CoreEngine.sln > build_log.txt || true

    if grep -q "Build succeeded." build_log.txt; then
        echo "✅ SUCCESS! The build is completely GREEN!"
        exit 0
    fi

    echo "❌ Build failed. Extracting broken files..."
    BROKEN_FILES=$(grep -oP '^.*\.cs(?=\(\d+,\d+\): error)' build_log.txt | sort -u | tr '\n' ' ')

    if [ -z "$BROKEN_FILES" ]; then
        BROKEN_FILES="Chummer.CoreEngine.sln"
    fi

    echo "Handing broken files to Aider via local EA Proxy (Port 8090)..."
    
    aider --model openai/gpt-4o \
      $BROKEN_FILES \
      build_log.txt \
      --yes \
      --message "The dotnet build failed. Read the compiler errors in build_log.txt. I have passed you the exact files that are failing. Fix the syntax errors, missing references, or method signatures. Do not implement complex logic; just satisfy the compiler so the build passes."

    ATTEMPT=$((ATTEMPT+1))
done

echo "🛑 Max retries reached. Manual review required."
