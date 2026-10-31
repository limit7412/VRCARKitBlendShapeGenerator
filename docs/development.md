# 開発環境とテストの実行

生成ロジックのテストは `Tests/Editor` にEditModeテストとして置いている。
メッシュの読み書きは `IMeshRepository` 越しに行っているため、テストでは `UnityEngine.Mesh` を使わずインメモリ実装（`FakeMeshRepository`）へ差し替えて検証する。

`Editor/` `Runtime/` を変更したときは、PRを出す前に手元で実行する。
CIでも実行されるが（[ci.md](ci.md)）、Unityライセンスのsecretが未設定のリポジトリやフォークではスキップされる。

1. このリポジトリをUnityプロジェクトの `Packages/` 以下へ配置する（VCC/ALCOM経由で導入したものは書き換えられないため、開発時はクローンを直接置く）
2. `Packages/manifest.json` の `testables` にパッケージ名を追加する

    ```json
    {
      "dependencies": { },
      "testables": [ "com.qazx7412.kx-vrc-arkit-blendshape-generator" ]
    }
    ```

3. Unityメニュー > Window > General > Test Runner を開く
4. EditModeタブで `ARKitBlendShapeGenerator.Editor.Tests` を実行する

`testables` へ追加しないとテストアセンブリがコンパイルされず、Test Runnerの一覧にも現れない。
テストは配布物には含まない（配布物の中身は [packaging.md](packaging.md) に書いている）。
