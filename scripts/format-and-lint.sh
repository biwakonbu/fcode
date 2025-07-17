#!/bin/bash

# F# formatting and linting script

echo "🔍 F# Code Quality Check"
echo "======================="

# Install tools if needed
if ! command -v fantomas &> /dev/null; then
    echo "⚠️  Installing Fantomas..."
    dotnet tool install -g fantomas
fi

if ! command -v dotnet-fsharplint &> /dev/null; then
    echo "⚠️  Installing FSharpLint..."
    dotnet tool install -g dotnet-fsharplint
fi

# Format all F# files
echo "📐 Formatting F# files..."
fantomas src/ tests/
echo "✅ Formatting complete"

# Run linting (using build-based approach due to FSharpLint compatibility issues)
echo "🔧 Running F# linting..."
{ echo "Linting src/..."; dotnet build src/fcode.fsproj --configuration Debug --verbosity normal --no-restore; echo "Linting tests/..."; dotnet build tests/fcode.Tests.fsproj --configuration Debug --verbosity normal --no-restore; } >> lint-output.log 2>&1

echo "✅ Code quality check complete!"