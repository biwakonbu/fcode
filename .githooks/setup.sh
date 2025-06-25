#!/bin/bash

# Git hooks setup script
# プロジェクト参加者が実行してローカル環境にpre-commitフックを設定

echo "🔧 Setting up Git hooks for fcode project..."

# カラー出力用の定数
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Git hooks ディレクトリを .githooks に設定
echo -e "${BLUE}Configuring Git hooks directory...${NC}"
git config core.hooksPath .githooks

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ Git hooks directory configured to .githooks${NC}"
else
    echo -e "${RED}❌ Failed to configure Git hooks directory${NC}"
    exit 1
fi

# Git フックの実行権限を確認
if [ -x ".githooks/pre-commit" ] && [ -x ".githooks/prepare-commit-msg" ]; then
    echo -e "${GREEN}✅ Git hooks are executable${NC}"
else
    echo -e "${YELLOW}⚠️  Making Git hooks executable...${NC}"
    chmod +x .githooks/pre-commit .githooks/prepare-commit-msg
    echo -e "${GREEN}✅ Git hooks are now executable${NC}"
fi

# Fantomas のインストール確認
echo -e "${BLUE}Checking Fantomas installation...${NC}"
if command -v fantomas &> /dev/null; then
    echo -e "${GREEN}✅ Fantomas is already installed${NC}"
else
    echo -e "${YELLOW}⚠️  Installing Fantomas...${NC}"
    dotnet tool install -g fantomas
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✅ Fantomas installed successfully${NC}"
    else
        echo -e "${RED}❌ Failed to install Fantomas${NC}"
        exit 1
    fi
fi

echo ""
echo -e "${GREEN}🎉 Git hooks setup completed!${NC}"
echo ""
echo -e "${BLUE}Git hooks will now run automatically:${NC}"
echo ""
echo -e "${BLUE}📋 Pre-commit hook checks:${NC}"
echo "  📝 Code formatting (Fantomas)"
echo "  🔍 Linting (TreatWarningsAsErrors=true)"
echo "  🧪 Tests (dotnet test)"
echo "  🏗️  Release build"
echo ""
echo -e "${BLUE}🚨 --no-verify detection:${NC}"
echo "  prepare-commit-msg hook will warn if quality checks are skipped"
echo "  and provide instructions for manual quality verification"
echo ""
echo -e "${YELLOW}💡 Test hooks manually:${NC}"
echo "  .githooks/pre-commit          # 品質チェック実行"
echo "  make check                    # 同等の品質チェック"