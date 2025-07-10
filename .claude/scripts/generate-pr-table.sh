#!/bin/bash

# PR Description Table Generator
# Markdownテーブルを正しく生成し、制御文字を除去

generate_effect_table() {
    local title="$1"
    shift
    local -a metrics=("$@")
    
    cat << 'EOF'
## 📊 修正効果（実測値）

| 指標 | 修正前 | 修正後 |
|------|--------|--------|
EOF
    
    # メトリクス行を生成（制御文字を除去）
    for metric in "${metrics[@]}"; do
        # パイプで区切られた値を分割（内部スペースを保持）
        IFS='|' read -ra VALUES <<< "$metric"
        local name="${VALUES[0]}"
        local before="${VALUES[1]}"
        local after="${VALUES[2]}"
        
        # 先頭・末尾のスペースのみ除去
        name="${name#"${name%%[![:space:]]*}"}"
        name="${name%"${name##*[![:space:]]}"}"
        before="${before#"${before%%[![:space:]]*}"}"
        before="${before%"${before##*[![:space:]]}"}"
        after="${after#"${after%%[![:space:]]*}"}"
        after="${after%"${after##*[![:space:]]}"}"
        
        # 制御文字・リダイレクト記号を除去
        name=$(echo "$name" | sed 's/[<>]//g' | sed 's/\/dev\/null//g')
        before=$(echo "$before" | sed 's/[<>]//g' | sed 's/\/dev\/null//g')
        after=$(echo "$after" | sed 's/[<>]//g' | sed 's/\/dev\/null//g')
        
        echo "| **$name** | $before | $after |"
    done
}

# HTMLテーブル生成（Markdownが破綻する場合の代替）
generate_html_table() {
    local title="$1"
    shift
    local -a metrics=("$@")
    
    cat << 'EOF'
## 📊 修正効果（実測値）

<table>
<tr>
<th>指標</th>
<th>修正前</th>
<th>修正後</th>
</tr>
EOF
    
    for metric in "${metrics[@]}"; do
        IFS='|' read -ra VALUES <<< "$metric"
        local name="${VALUES[0]}"
        local before="${VALUES[1]}"
        local after="${VALUES[2]}"
        
        # 先頭・末尾のスペースのみ除去
        name="${name#"${name%%[![:space:]]*}"}"
        name="${name%"${name##*[![:space:]]}"}"
        before="${before#"${before%%[![:space:]]*}"}"
        before="${before%"${before##*[![:space:]]}"}"
        after="${after#"${after%%[![:space:]]*}"}"
        after="${after%"${after##*[![:space:]]}"}"
        
        # 制御文字・HTML特殊文字をエスケープ
        name=$(echo "$name" | sed 's/</\&lt;/g' | sed 's/>/\&gt;/g' | sed 's/\/dev\/null//g')
        before=$(echo "$before" | sed 's/</\&lt;/g' | sed 's/>/\&gt;/g' | sed 's/\/dev\/null//g')
        after=$(echo "$after" | sed 's/</\&lt;/g' | sed 's/>/\&gt;/g' | sed 's/\/dev\/null//g')
        
        echo "<tr>"
        echo "<td><strong>$name</strong></td>"
        echo "<td>$before</td>"
        echo "<td>$after</td>"
        echo "</tr>"
    done
    
    echo "</table>"
}

# テキスト内の制御文字・問題文字列を除去
sanitize_pr_text() {
    local input="$1"
    
    # 制御文字・リダイレクト記号を除去
    echo "$input" | \
        sed 's/< \/dev\/null//g' | \
        sed 's/<\/dev\/null//g' | \
        sed 's/< *\/dev\/null *//g' | \
        sed 's/>>>.*<<<//g' | \
        sed 's/<<<.*>>>//g' | \
        tr -d '\000-\010\013\014\016-\037\177' | \
        sed 's/\$([^)]*)//g' | \
        sed 's/`[^`]*`//g'
}

# 使用例
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    # テスト用のメトリクス
    metrics=(
        "テスト実行時間|2分タイムアウト|36秒完了 ✅"
        "テスト成功率|不安定|464/464 (100%) ✅"
        "JSON解析エラー|頻発|0件 ✅"
        "Terminal.Guiハング|CI環境で発生|完全回避 ✅"
        "CI成功率|不安定|100%安定実行 ✅"
    )
    
    echo "# Markdownテーブル生成テスト"
    generate_effect_table "修正効果" "${metrics[@]}"
    
    echo -e "\n# HTMLテーブル生成テスト"
    generate_html_table "修正効果" "${metrics[@]}"
fi