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

`build.ps1` は `extensions/*/src/**/source.extension.vsixmanifest` を持つプロジェクトを VSIX として、`extensions/*/test/**/*.Tests.csproj` を単体テストとして自動で拾います。

## ライセンス

[MIT](LICENSE)
