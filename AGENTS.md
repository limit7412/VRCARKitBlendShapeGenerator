# AGENTS

## review

Always review in Japanese.

## document / comment

コメントやドキュメンテーションなど日本語の文章を書くときは下記を読む
  - .claude/skills/japanese-tech-writing
  - .claude/skills/cognitive-rhythm-writing

## test

- 生成ロジックのテストは `Tests/Editor` へEditModeテストとして置く
  - メッシュ操作は `IMeshRepository` 越しに書き、テストは `FakeMeshRepository` へ差し替えて検証する
- `Editor/` `Runtime/` を変更したらテストを追加または更新する
- CIではテストを実行していないため、PRを出す前にUnityのTest RunnerでEditModeテストを実行する
  - 手順はREADMEの「開発」を参照
  - 実行できない環境で作業した場合はその旨をPR本文に書く

## github

- 機能実装時はデフォルトブランチへのPRを作成する
- ある程度の単位でcommit、pushしPRとissueが存在すれば更新する
- PRに付いた指摘が無いか都度確認してあれば必要な対応か検討して、必要なら修正する
  - 付いた指摘に対してはcommit idをつけて返答をしてresolveする
- PRのスコープ外の問題が発覚した場合は別途issueを作成する
- issueの解決を目的とした場合は修正中都度issueの本文を更新し必要に応じてコメントをつける
