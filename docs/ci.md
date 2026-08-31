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

## Unityライセンスの設定

実行にはリポジトリのsecretsへUnityライセンスの登録が必要である。
ライセンスの種類で使うsecretが違う。取得手順は[game-ciのドキュメント](https://game.ci/docs/github/activation)を参照。

| secret | 内容 |
| ---- | ---- |
| `UNITY_EMAIL` | Unityアカウントのメールアドレス（どちらの種類でも必要） |
| `UNITY_PASSWORD` | Unityアカウントのパスワード（どちらの種類でも必要） |
| `UNITY_LICENSE` | Personalの場合。ライセンスファイル（`.ulf`）の中身 |
| `UNITY_SERIAL` | Pro/Plusの場合。シリアル |

`UNITY_LICENSE` を登録するとそちらで認証するため、シリアルを使う場合は登録しないこと。

`.ulf` は認証ファイル（`.alf`）を作った**Unityのバージョンに紐づく**。
別のバージョンで作ったものを登録すると、ログインには成功したうえで `Code 20110 (serial invalid)` で認証に失敗する。
このリポジトリのCIは 2022.3.6f1 で動かすため、`.alf` も同じバージョンで作ること。

どちらのsecretも未設定のときはテストジョブがスキップされ、ワークフローは失敗しない。
フォークからのPRはsecretsを受け取れないため、同様にスキップされる。

なお、テストの実行はPRのコードをUnityで動かすことであり、そのUnityはアカウントの認証情報を持つ。
同一リポジトリのブランチから作ったPRではsecretsが読めるため、**このリポジトリへの書き込み権限は認証情報へのアクセスと同義**である。
外部のコントリビューターを迎える場合は、secretsを承認必須のGitHub Environmentへ移すことを検討すること。
