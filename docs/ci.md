# CIでのテスト実行

`.github/workflows/test.yml` が、PRごとにEditModeテストを実行する。
結果は「EditMode Test Results」チェックとしてPRに出る。

テストが読むのは `Editor/` `Runtime/` `Tests/` `package.json` とワークフロー自身だけなので、これらに触れないPRではテストジョブをスキップする。
スキップの判定はワークフローの `changes` ジョブが行い、テストジョブはskippedとして完了する。
`on` の `paths` フィルタを使っていないのは、そちらで起動を止めるとチェック自体が作られず、テストをrequired status checkに指定したときにPRをマージできなくなるためである。
判定に使うパスの一覧は `changes` ジョブにのみ書かれている。テストが読むファイルを増やしたときは、この一覧にも追加すること。

同じワークフローの `packaging` ジョブが、配布パッケージ（[packaging.md](packaging.md)）の組み立てを検証する。
こちらはUnityを使わないため、ライセンスの有無にも変更パスの判定にも関わらず常に実行される。
検証するのは、同梱対象すべてに `.meta` があること、GUIDが重複していないこと、配布済みのGUID（`ARKitBlendShapeGeneratorComponent`）が変わっていないこと、取り込み先が `Assets/AtelierKairox/VRCARKitBlendShapeGenerator/` の外へ出ていないこと、booth用zipの直下にVPM用の `package.json` が居ないこと、同じ入力から2回作って同一のバイト列になることである。

このリポジトリはUnityプロジェクトではないため、ワークフローは実行のたびに最小のUnityプロジェクトを組み立て、その `Packages/` へこのリポジトリを置く。
VPM依存（VRChat SDK / NDMF）はUnity Package Managerでは解決できないので、[vrc-get](https://github.com/vrc-get/vrc-get)で先に導入してから[game-ci](https://game.ci/)のテストランナーを回す。
game-ciのCLIはテストでもプロジェクトでgitを呼んでバージョンを決めるため、組み立てたプロジェクトは空のコミットを1つ持つgitリポジトリにしてから渡す（詳細はワークフロー内のコメントに書いている）。
CLIの版はワークフローの `UNITY_TEST_RUNNER_CLI_VERSION` で固定している。既定の `latest` は版の解決にGitHub APIを使い、共有ランナーでは無認証の上限に達して失敗することがある。

## Unityライセンスの設定

実行にはリポジトリのsecretsへUnityアカウントの登録が必要である。
ライセンスの種類で使うsecretが違う。取得手順は[game-ciのドキュメント](https://game.ci/docs/github/activation)を参照。

| secret | 内容 |
| ---- | ---- |
| `UNITY_EMAIL` | Unityアカウントのメールアドレス（どちらの種類でも必要） |
| `UNITY_PASSWORD` | Unityアカウントのパスワード（どちらの種類でも必要） |
| `UNITY_SERIAL` | Pro/Plusの場合。シリアル |

Personalの場合はメールアドレスとパスワードだけで認証する。
`UNITY_SERIAL` を登録するとそちらで認証する。

ライセンスファイル（`.ulf`）を登録する `UNITY_LICENSE` は使わない。
Personalの `.ulf` は認証ファイルを要求した機械の識別子に紐付いており、GitHubのランナー側でその識別子が変わると `Machine bindings don't match` で退けられる（2026年9月に実際に起きた。詳細は[issue #96](https://github.com/limit7412/VRCARKitBlendShapeGenerator/issues/96)）。
作り直そうにも、UnityはPersonalの手動認証を廃止しているため、新しい `.ulf` は発行できない。
登録が残っていても読まれないので、削除してよい。

Personalのアカウント認証には、Unityに同梱されるライセンスクライアントが `--include-personal` に対応している必要がある。
`package.json` が最小版とする 2022.3.6f1 のクライアント（1.12.1）は対応していないため、CIは 2022.3.62f3 で動かしている。
game-ciがこの版でメールアドレスとパスワードだけの認証を確認している。
UNITY_EMAIL と UNITY_PASSWORD が未設定のときはテストジョブがスキップされ、ワークフローは失敗しない。
フォークからのPRはsecretsを受け取れないため、同様にスキップされる。

なお、テストの実行はPRのコードをUnityで動かすことであり、そのUnityはアカウントの認証情報を持つ。
同一リポジトリのブランチから作ったPRではsecretsが読めるため、**このリポジトリへの書き込み権限は認証情報へのアクセスと同義**である。
外部のコントリビューターを迎える場合は、secretsを承認必須のGitHub Environmentへ移すことを検討すること。
