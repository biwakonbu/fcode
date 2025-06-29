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

# Run linting
echo "🔧 Running F# linting..."
echo "Linting src/..."
dotnet fsharplint lint src/fcode.fsproj --lint-config .fsharplint.json

echo "Linting tests/..."
dotnet fsharplint lint tests/fcode.Tests.fsproj --lint-config .fsharplint.json

echo "✅ Code quality check complete!"