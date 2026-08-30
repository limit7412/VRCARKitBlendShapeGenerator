#!/usr/bin/env bash
# 配布済みの.metaのGUIDが変わっていないことを確かめる。
#
# GUIDはbooth版の利用者のシーンやプレハブから参照されている。変えてしまうと、
# 更新した瞬間にアバターへ付けたコンポーネントがMissingになる。
# .metaを作り直したときや、コピーして書き換えたときに気付けるようにする。
set -euo pipefail

BASE="${1:?usage: $0 <base-ref>}"

guid_of() {
  sed -n 's/^guid: \([0-9a-f]\{32\}\)$/\1/p' | head -n 1
}

STATUS=0
while IFS=$'\t' read -r CHANGE OLD NEW; do
  case "$CHANGE" in
    M*) NEW="$OLD" ;;
    R*) ;;
    *) continue ;;
  esac

  BEFORE=$(git show "$BASE:$OLD" | guid_of)
  AFTER=$(guid_of < "$NEW")

  if [ -z "$AFTER" ]; then
    echo "::error::guidを読み取れません: $NEW"
    STATUS=1
    continue
  fi

  if [ "$BEFORE" != "$AFTER" ]; then
    echo "::error::GUIDが変わっています: $NEW ($BEFORE -> $AFTER)"
    STATUS=1
  fi
done < <(git diff --name-status -M --diff-filter=MR "$BASE" -- '*.meta')

if [ "$STATUS" -eq 0 ]; then
  echo "既存の.metaのGUIDは変わっていません"
fi
exit "$STATUS"
