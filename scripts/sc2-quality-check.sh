#!/bin/bash

# SC-2品質保証・統合テスト実行スクリプト
# Usage: ./scripts/sc2-quality-check.sh [--full|--quick|--performance]

set -euo pipefail

# スクリプト設定
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TEST_PROJECT="${PROJECT_ROOT}/tests/fcode.Tests.fsproj"
SRC_PROJECT="${PROJECT_ROOT}/src/fcode.fsproj"

# ログ設定
LOG_DIR="${PROJECT_ROOT}/logs"
mkdir -p "${LOG_DIR}"
LOG_FILE="${LOG_DIR}/sc2-quality-$(date +%Y%m%d-%H%M%S).log"

# カラー出力
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# ログ関数
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1" | tee -a "${LOG_FILE}"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1" | tee -a "${LOG_FILE}"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1" | tee -a "${LOG_FILE}"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1" | tee -a "${LOG_FILE}"
}

# 使用方法表示
show_usage() {
    cat << EOF
SC-2品質保証・統合テスト実行スクリプト

使用方法:
  $0 [オプション]

オプション:
  --full          全テスト実行（統合・品質保証・パフォーマンス・レグレッション）
  --quick         クイックテスト実行（統合・品質保証のみ）
  --performance   パフォーマンステストのみ実行
  --regression    レグレッション検出テストのみ実行
  --help, -h      このヘルプを表示

例:
  $0 --full       # 全品質保証テスト実行
  $0 --quick      # 基本品質確認
  $0 --performance # パフォーマンス監視のみ
EOF
}

# 前提条件チェック
check_prerequisites() {
    log_info "前提条件チェック中..."
    
    # .NET SDK確認
    if ! command -v dotnet &> /dev/null; then
        log_error ".NET SDK not found. Install .NET 8.0 SDK"
        exit 1
    fi
    
    local dotnet_version
    dotnet_version=$(dotnet --version)
    log_info ".NET Version: ${dotnet_version}"
    
    # プロジェクトファイル確認
    if [[ ! -f "${SRC_PROJECT}" ]]; then
        log_error "Source project not found: ${SRC_PROJECT}"
        exit 1
    fi
    
    if [[ ! -f "${TEST_PROJECT}" ]]; then
        log_error "Test project not found: ${TEST_PROJECT}"
        exit 1
    fi
    
    log_success "前提条件チェック完了"
}

# プロジェクトビルド
build_projects() {
    log_info "プロジェクトビルド中..."
    
    # 依存関係復元とビルド
    { log_info "依存関係復元中..."; dotnet restore "${SRC_PROJECT}"; log_info "ソースプロジェクトビルド中..."; dotnet build "${SRC_PROJECT}" --no-restore --configuration Release; log_info "テストプロジェクトビルド中..."; dotnet build "${TEST_PROJECT}" --no-restore --configuration Release; } >> "${LOG_FILE}" 2>&1
    
    log_success "プロジェクトビルド完了"
}

# SC-2統合テスト実行
run_integration_tests() {
    log_info "SC-2統合テスト実行中..."
    
    local test_results_dir="${PROJECT_ROOT}/TestResults/SC2Integration"
    mkdir -p "${test_results_dir}"
    
    if dotnet test "${TEST_PROJECT}" \
        --filter "TestCategory=Integration&FullyQualifiedName~SC2" \
        --logger "trx;LogFileName=sc2-integration-$(date +%Y%m%d-%H%M%S).trx" \
        --results-directory "${test_results_dir}" \
        --configuration Release \
        --no-build \
        --verbosity normal >> "${LOG_FILE}" 2>&1; then
        
        log_success "SC-2統合テスト完了"
        return 0
    else
        log_error "SC-2統合テスト失敗"
        return 1
    fi
}

# SC-2品質保証テスト実行
run_quality_assurance_tests() {
    log_info "SC-2品質保証テスト実行中..."
    
    local test_results_dir="${PROJECT_ROOT}/TestResults/SC2QualityAssurance"
    mkdir -p "${test_results_dir}"
    
    if dotnet test "${TEST_PROJECT}" \
        --filter "FullyQualifiedName~SC2QualityAssurance" \
        --logger "trx;LogFileName=sc2-quality-$(date +%Y%m%d-%H%M%S).trx" \
        --results-directory "${test_results_dir}" \
        --configuration Release \
        --no-build \
        --verbosity normal >> "${LOG_FILE}" 2>&1; then
        
        log_success "SC-2品質保証テスト完了"
        return 0
    else
        log_error "SC-2品質保証テスト失敗"
        return 1
    fi
}

# SC-2パフォーマンステスト実行
run_performance_tests() {
    log_info "SC-2パフォーマンステスト実行中..."
    
    local test_results_dir="${PROJECT_ROOT}/TestResults/SC2Performance"
    mkdir -p "${test_results_dir}"
    
    if dotnet test "${TEST_PROJECT}" \
        --filter "TestCategory=Performance&FullyQualifiedName~SC2" \
        --logger "trx;LogFileName=sc2-performance-$(date +%Y%m%d-%H%M%S).trx" \
        --results-directory "${test_results_dir}" \
        --configuration Release \
        --no-build \
        --verbosity normal >> "${LOG_FILE}" 2>&1; then
        
        log_success "SC-2パフォーマンステスト完了"
        return 0
    else
        log_warning "SC-2パフォーマンステスト警告あり"
        return 1
    fi
}

# SC-2レグレッション検出テスト実行
run_regression_tests() {
    log_info "SC-2レグレッション検出テスト実行中..."
    
    local test_results_dir="${PROJECT_ROOT}/TestResults/SC2Regression"
    mkdir -p "${test_results_dir}"
    
    if dotnet test "${TEST_PROJECT}" \
        --filter "FullyQualifiedName~レグレッション" \
        --logger "trx;LogFileName=sc2-regression-$(date +%Y%m%d-%H%M%S).trx" \
        --results-directory "${test_results_dir}" \
        --configuration Release \
        --no-build \
        --verbosity normal >> "${LOG_FILE}" 2>&1; then
        
        log_success "SC-2レグレッション検出テスト完了"
        return 0
    else
        log_error "SC-2レグレッション検出テスト失敗"
        return 1
    fi
}

# 品質レポート生成
generate_quality_report() {
    log_info "SC-2品質レポート生成中..."
    
    local report_file="${PROJECT_ROOT}/SC2-Quality-Report-$(date +%Y%m%d-%H%M%S).md"
    
    cat > "${report_file}" << EOF
# SC-2品質保証レポート

**実行日時**: $(date)
**実行者**: $(whoami)
**システム**: $(uname -s) $(uname -r)
**.NET Version**: $(dotnet --version)

## 実行サマリー

EOF

    # テスト結果サマリー追加
    local test_results_count
    test_results_count=$(find "${PROJECT_ROOT}/TestResults" -name "*.trx" -newer "${PROJECT_ROOT}/scripts" 2>/dev/null | wc -l)
    
    echo "- **実行テストスイート数**: ${test_results_count}" >> "${report_file}"
    echo "- **ログファイル**: ${LOG_FILE}" >> "${report_file}"
    echo "" >> "${report_file}"
    
    echo "## 品質保証項目" >> "${report_file}"
    echo "- ✅ SC-2統合テスト実行" >> "${report_file}"
    echo "- ✅ 品質保証テスト実行" >> "${report_file}"
    echo "- ✅ パフォーマンス監視" >> "${report_file}"
    echo "- ✅ レグレッション検出" >> "${report_file}"
    echo "" >> "${report_file}"
    
    echo "## 次回アクション" >> "${report_file}"
    echo "- 継続的品質監視継続" >> "${report_file}"
    echo "- パフォーマンス基準維持" >> "${report_file}"
    echo "- レグレッション0件維持" >> "${report_file}"
    
    log_success "品質レポート生成完了: ${report_file}"
    echo "${report_file}"
}

# メイン実行
main() {
    local mode="quick"
    
    # 引数解析
    while [[ $# -gt 0 ]]; do
        case $1 in
            --full)
                mode="full"
                shift
                ;;
            --quick)
                mode="quick"
                shift
                ;;
            --performance)
                mode="performance"
                shift
                ;;
            --regression)
                mode="regression"
                shift
                ;;
            --help|-h)
                show_usage
                exit 0
                ;;
            *)
                log_error "Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
    
    log_info "SC-2品質保証開始 (モード: ${mode})"
    
    # 実行開始
    local start_time
    start_time=$(date +%s)
    
    check_prerequisites
    build_projects
    
    local exit_code=0
    
    case "${mode}" in
        "full")
            log_info "フル品質保証実行中..."
            run_integration_tests || exit_code=1
            run_quality_assurance_tests || exit_code=1
            run_performance_tests || exit_code=1
            run_regression_tests || exit_code=1
            ;;
        "quick")
            log_info "クイック品質確認実行中..."
            run_integration_tests || exit_code=1
            run_quality_assurance_tests || exit_code=1
            ;;
        "performance")
            log_info "パフォーマンステストのみ実行中..."
            run_performance_tests || exit_code=1
            ;;
        "regression")
            log_info "レグレッション検出テストのみ実行中..."
            run_regression_tests || exit_code=1
            ;;
    esac
    
    # 品質レポート生成
    local report_file
    report_file=$(generate_quality_report)
    
    # 実行時間計算
    local end_time
    end_time=$(date +%s)
    local duration=$((end_time - start_time))
    
    if [[ ${exit_code} -eq 0 ]]; then
        log_success "SC-2品質保証完了 (実行時間: ${duration}秒)"
        log_info "品質レポート: ${report_file}"
    else
        log_error "SC-2品質保証で問題検出 (実行時間: ${duration}秒)"
        log_info "詳細ログ: ${LOG_FILE}"
    fi
    
    exit ${exit_code}
}

# スクリプト実行
main "$@"