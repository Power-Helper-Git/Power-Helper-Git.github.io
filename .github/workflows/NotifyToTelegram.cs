name: Notify Telegram on Commit
on: [push]

jobs:
  notify:
    runs-on: ubuntu-latest
    steps:
      - name: Send TG notify
        env:
          CHAT_ID: ${{ secrets.TELEGRAM_CHAT_ID }}
          BOT_TOKEN: ${{ secrets.TELEGRAM_BOT_TOKEN }}
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          REPO: ${{ github.repository }}
        run: |
          REPO_URL="https://github.com/$REPO"
          BEFORE=$(jq -r '.before' "$GITHUB_EVENT_PATH")
          AFTER=$(jq -r '.after' "$GITHUB_EVENT_PATH")

          send_tg_msg() {
            jq -n --arg chat_id "$CHAT_ID" --arg text "$1" '
            {
              "chat_id": $chat_id,
              "text": $text,
              "parse_mode": "HTML",
              "disable_web_page_preview": true
            }' | curl -s -X POST -H "Content-Type: application/json" -d @- "https://api.telegram.org/bot$BOT_TOKEN/sendMessage" > /dev/null
          }


          if [[ "$BEFORE" == "0000000000000000000000000000000000000000" ]]; then
            VALID_SHAS=$(jq -r '.commits | reverse | .[] | select((.author.username // .author.name // "") | test("bot"; "i") | not) | .id' "$GITHUB_EVENT_PATH")
          else
            VALID_SHAS=$(curl -s -H "Authorization: Bearer $GH_TOKEN" "https://api.github.com/repos/$REPO/compare/$BEFORE...$AFTER" | jq -r '.commits | reverse | .[] | select((.author.login // .commit.author.name // "") | test("bot"; "i") | not) | .sha')
          fi

          if [[ -z "$VALID_SHAS" ]]; then exit 0; fi

          VALID_COUNT=$(echo "$VALID_SHAS" | wc -w)


          if [[ "$VALID_COUNT" -eq 1 ]]; then
            COMMIT_WORD="Новый коммит"
          else
            COMMIT_WORD="Новые коммиты"
          fi

          CURRENT_MSG="<tg-emoji emoji-id=\"5303382121967001310\">💻</tg-emoji> <b>$COMMIT_WORD в <a href=\"$REPO_URL\">$REPO</a></b>"

          TOTAL_FILES_ADDED=0; TOTAL_FILES_REMOVED=0; TOTAL_FILES_MODIFIED=0
          TOTAL_LINES_ADDED=0; TOTAL_LINES_REMOVED=0

          for SHA in $VALID_SHAS; do
            COMMIT_JSON=$(curl -s -H "Authorization: Bearer $GH_TOKEN" "https://api.github.com/repos/$REPO/commits/$SHA")

            AUTHOR_LOGIN=$(echo "$COMMIT_JSON" | jq -r '.author.login // .commit.author.name // ""')
            TIMESTAMP=$(echo "$COMMIT_JSON" | jq -r '.commit.author.date // ""')
            LINES_ADDED=$(echo "$COMMIT_JSON" | jq -r '.stats.additions // 0')
            LINES_REMOVED=$(echo "$COMMIT_JSON" | jq -r '.stats.deletions // 0')
            FILES_ADDED=$(echo "$COMMIT_JSON" | jq -r '[.files // [] | .[] | select(.status == "added")] | length')
            FILES_REMOVED=$(echo "$COMMIT_JSON" | jq -r '[.files // [] | .[] | select(.status == "removed")] | length')
            FILES_MODIFIED=$(echo "$COMMIT_JSON" | jq -r '[.files // [] | .[] | select(.status == "modified" or .status == "renamed")] | length')
            FILES_STR=$(echo "$COMMIT_JSON" | jq -r '
              [.files // [] | .[] |
                (if .status == "added"   then "<tg-emoji emoji-id=\"5886306834410640699\">🆕</tg-emoji>"
                 elif .status == "removed" then "<tg-emoji emoji-id=\"5841541824803509441\">🗑</tg-emoji>"
                 elif .status == "renamed" then "<tg-emoji emoji-id=\"5877410604225924969\">😀</tg-emoji>"
                 else "<tg-emoji emoji-id=\"5879841310902324730\">✏️</tg-emoji>" end
                 + " " + (.filename | split("/") | last))
              ] | join(" | ")')
            COMMIT_MSG=$(echo "$COMMIT_JSON" | jq -r '.commit.message // "" | gsub("&";"&amp;") | gsub("<";"&lt;") | gsub(">";"&gt;")')

            TOTAL_LINES_ADDED=$((TOTAL_LINES_ADDED + LINES_ADDED))
            TOTAL_LINES_REMOVED=$((TOTAL_LINES_REMOVED + LINES_REMOVED))
            TOTAL_FILES_ADDED=$((TOTAL_FILES_ADDED + FILES_ADDED))
            TOTAL_FILES_REMOVED=$((TOTAL_FILES_REMOVED + FILES_REMOVED))
            TOTAL_FILES_MODIFIED=$((TOTAL_FILES_MODIFIED + FILES_MODIFIED))

            UNIX_TIME=$(date -d "$TIMESTAMP" +%s)
            AUTHOR_URL="https://github.com/$AUTHOR_LOGIN"


            if [[ -n "$FILES_STR" ]]; then
              FILE_LINE="• ${FILES_STR}"$'\n'
            else
              FILE_LINE=""
            fi

            BLOCK="${FILE_LINE}<blockquote expandable>${COMMIT_MSG}</blockquote>"$'\n'"👤 <a href=\"$AUTHOR_URL\">$AUTHOR_LOGIN</a> | 🕒 <tg-time unix=\"$UNIX_TIME\" format=\"dt\">$UNIX_TIME</tg-time>"$'\n'

            if [[ $(( ${#CURRENT_MSG} + ${#BLOCK} )) -gt 3800 ]]; then
              send_tg_msg "$CURRENT_MSG"
              CURRENT_MSG="<b>Продолжение коммитов <a href=\"$REPO_URL\">$REPO</a></b>"$'\n\n'"$BLOCK"
            else
              CURRENT_MSG="${CURRENT_MSG}"$'\n\n'"$BLOCK"
            fi
          done

          TOTALS_BLOCK="<tg-emoji emoji-id=\"6007817446398890097\">📊</tg-emoji> <i>+$TOTAL_FILES_ADDED/-$TOTAL_FILES_REMOVED/~$TOTAL_FILES_MODIFIED файлов | +$TOTAL_LINES_ADDED/-$TOTAL_LINES_REMOVED строк</i>"

          if [[ $(( ${#CURRENT_MSG} + ${#TOTALS_BLOCK} )) -gt 4000 ]]; then
            send_tg_msg "$CURRENT_MSG"
            send_tg_msg "$TOTALS_BLOCK"
          else
            send_tg_msg "${CURRENT_MSG}"$'\n\n'"$TOTALS_BLOCK"
          fi