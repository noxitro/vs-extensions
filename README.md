# vs-extensions

Visual Studio の拡張機能をまとめたリポジトリです。拡張機能ごとにフォルダーを分け、ビルド設定・パッケージのバージョン・ビルドスクリプトはリポジトリ全体で共有します。

## 拡張機能一覧

| 拡張機能 | 概要 | 対応 VS |
| --- | --- | --- |
| [CopyFullyQualifiedName](extensions/CopyFullyQualifiedName/README.md) | C# / C++ のクラス・関数などを右クリックして、名前空間を含む完全修飾名をコピーする | 2022 (17.14 以降) / 2026 |

## フォルダー構成

```
vs-extensions/
├─ build.bat / build.ps1        全拡張機能のビルド + 単体テスト → artifacts/vsix/ に .vsix を集める
├─ vs-extensions.slnx           全プロジェクトを含むソリューション
├─ Directory.Build.props        全プロジェクト共通のビルド設定(言語バージョン、出力先など)
├─ Directory.Packages.props     NuGet パッケージのバージョンを一元管理(Central Package Management)
├─ artifacts/                   ビルド出力(git 管理外)
├─ .github/                     CI(workflows/)、CI 用スクリプト(scripts/)、Dependabot
└─ extensions/
   └─ <拡張機能名>/
      ├─ README.md              その拡張機能の使い方・仕様
      ├─ src/
      │  ├─ <拡張機能名>/          VSIX 本体(source.extension.vsixmanifest を持つ)
      │  └─ <拡張機能名>.Core/     VS に依存しないロジック(単体テストの対象)
      ├─ test/
      │  ├─ <拡張機能名>.Core.Tests/  単体テスト(xUnit)
      │  └─ e2e/                   実験用インスタンスの VS を使った E2E テスト
      └─ samples/               手動確認・E2E 用のサンプルコード
```

方針:

- **VS に依存するコードは VSIX 本体に、純粋なロジックは `.Core` に分ける。** `.Core` は `dotnet test` だけで検証できる。
- **パッケージのバージョンは `Directory.Packages.props` にだけ書く。** 各 `.csproj` には `PackageReference` の名前だけを書く。
- **拡張機能は VisualStudio.Extensibility(VSSDK 互換のインプロセス拡張)で作る。** コマンドやメニュー配置はコードで宣言し、Roslyn や DTE など VS プロセス内の API が要る部分は VSSDK のサービスを注入して使う([公式の推奨構成](https://learn.microsoft.com/visualstudio/extensibility/visualstudio.extensibility/get-started/in-proc-extensions))。

## 必要なもの

- Visual Studio 2022 (17.14 以降) または 2026
  - ワークロード「**Visual Studio 拡張機能の開発**」
  - C++ の拡張機能を試すなら「C++ によるデスクトップ開発」
- .NET SDK 10(単体テストの実行に使用)

## ビルド

`build.bat` をダブルクリックすると、すべての拡張機能を Release でビルドし、単体テストを実行して、`artifacts\vsix\` に `.vsix` を集めます。

コマンドラインからは次のように実行します。

```bash
pwsh -File build.ps1
```

```bash
pwsh -File build.ps1 -Extension CopyFullyQualifiedName -Configuration Debug -SkipTests
```

> VSIX のビルドには Visual Studio 付属の MSBuild が必要です(`dotnet build` では VSIX を作れません)。`build.ps1` は `vswhere` で自動的に探します。

## インストール

`artifacts\vsix\<拡張機能名>.vsix` をダブルクリックし、表示されるインストーラーに従ってください。インストール後に Visual Studio を再起動すると有効になります。

アンインストールは Visual Studio の「拡張機能 > 拡張機能の管理」から行います。

## 開発・デバッグ

1. `vs-extensions.slnx` を Visual Studio で開く
2. デバッグしたい拡張機能の VSIX 本体プロジェクト(例: `CopyFullyQualifiedName`)をスタートアッププロジェクトにする
3. `F5` で実験用インスタンス(`/rootSuffix Exp`)の Visual Studio が起動し、拡張機能が読み込まれる

実験用インスタンスは普段使いの Visual Studio とは設定・拡張機能が分かれているので、壊しても普段の環境には影響しません。

## 新しい拡張機能を追加する

1. `extensions/<新しい名前>/` を作り、既存の拡張機能(`CopyFullyQualifiedName`)と同じ形で `src/` `test/` `README.md` を置く
   - VSIX 本体の `.csproj` は `CopyFullyQualifiedName.csproj` をコピーして名前を変える
   - `source.extension.vsixmanifest` の `Identity Id` は**新しい一意な値**にする(GUID を新しく生成する)
2. 新しく使う NuGet パッケージは `Directory.Packages.props` にバージョンを追加する
3. ソリューションにプロジェクトを追加する

   ```bash
   dotnet sln vs-extensions.slnx add --solution-folder extensions/<新しい名前> extensions/<新しい名前>/src/<新しい名前>/<新しい名前>.csproj
   ```

4. この README の「拡張機能一覧」に 1 行追加する

`build.ps1` は `extensions/*/src/**/source.extension.vsixmanifest` を持つプロジェクトを VSIX として、`extensions/*/test/**/*.Tests.csproj` を単体テストとして自動で拾います。CI も同じ `build.ps1` を通すので、追加した拡張機能は CI でも自動でビルド・テストされます。

## CI

GitHub Actions で次のワークフローが動きます(公開リポジトリなので無料枠の範囲)。構成は [Nox](https://github.com/noxitro/Nox) の CI に合わせています。

| ワークフロー | きっかけ | 内容 |
| --- | --- | --- |
| [CI](.github/workflows/ci.yml) | 全ブランチへの push(文書だけの変更は除く) | `build.ps1` で Release(単体テスト込み)と Debug をビルド。`.vsix` をアーティファクト `vsix` に保存。実験用 Visual Studio を起動する E2E テスト。失敗時と復旧時に Discord へ通知 |
| [CodeQL](.github/workflows/codeql.yml) | main への push・PR、毎週 | C# の静的解析 |
| [File format](.github/workflows/file-format.yml) | 全ブランチへの push | BOM の検査(下の「ファイル形式」) |
| [Secret scan](.github/workflows/secret-scan.yml) | 全ブランチへの push・PR | 秘密情報・個人情報・ライセンス文言の混入検査([noxitro/github-templates](https://github.com/noxitro/github-templates) の共通ワークフロー) |
| [Workflow lint](.github/workflows/workflow-lint.yml) | `.github/` を変えた push | actionlint と ruff で CI 自身を検査 |

補足:

- 同じリポジトリのブランチからの PR では、CI は push 側だけで走ります(二重起動を避けるため)。PR のチェック欄には push 側の結果が出ます。
- E2E テスト(`extensions/*/test/e2e`)は CI でも実験用の Visual Studio を起動して走らせ、クリップボードに実際に入った文字列まで確かめます。落ちたときはアーティファクト `e2e-diagnostics` にスクリーンショット・画面上の文字・VS のログが残ります。
- Discord 通知は secret `DISCORD_WEBHOOK_URL` を使います。未設定ならスキップするだけで、CI は落ちません。
- 依存の更新は Dependabot が週 1 回 PR を出します。Roslyn と VS SDK は対応する最も古い VS に合わせているので、自動更新の対象から外しています。

## ファイル形式

- テキストファイルは **BOM なしの UTF-8**。ただし PowerShell スクリプト(`*.ps1` `*.psm1` `*.psd1`)だけは **BOM 付き**(Windows PowerShell 5.1 が BOM の無いスクリプトを Shift-JIS として読むため。決まりは [.github/scripts/bom_policy.py](.github/scripts/bom_policy.py))。
- 改行コードは git が管理します。`.gitattributes` の `* text=auto` により、テキストファイルはリポジトリ内では LF で格納され、チェックアウト時に各環境の改行(Windows では CRLF)に変換されます。`.bat` `.cmd` `.ps1` `.psm1` は常に CRLF でチェックアウトされます。そのため改行の混入や一括変換はコミットに残りません。
- File format ワークフローが、作業ブランチの main との分岐点からの変更で BOM を検査します(改行は上のとおり git が正規化するので、この検査では実質見ていません)。意図して BOM を付け外しするときは、コミットメッセージに `Format-Change: <パス or glob>` の行を書きます。BOM が付いてしまったファイルは `python .github/scripts/strip-bom.py <パス>` で外せます。push 前に手元で確かめるには次を実行します。

  ```bash
  python .github/scripts/check-file-format.py --base origin/main
  ```

## ライセンス

[MIT](LICENSE)
