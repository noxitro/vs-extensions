# Copy Fully Qualified Name(完全修飾名をコピー)

C# / C++ のコードエディターでクラスや関数などを右クリックし、**名前空間を含む完全修飾名**をクリップボードにコピーする Visual Studio 拡張機能です。

| 言語 | 右クリックした場所 | コピーされる文字列 |
| --- | --- | --- |
| C# | `class Widget` の `Widget` | `Sample.Widgets.Widget` |
| C# | `helper.Render(this)` の `Render`(呼び出し側) | `Sample.Widgets.Renderer.Render` |
| C# | `Repository<T>` のメソッド `Find` | `Sample.Widgets.Repository<T>.Find` |
| C++ | `class Widget` の `Widget` | `sample::widgets::Widget` |
| C++ | `void Widget::Draw(int scale)` の本体の中 | `sample::widgets::Widget::Draw` |
| C++ | `enum class Color` の `Red` | `sample::widgets::Color::Red` |

## 使い方

1. C# または C++ のファイルを開く
2. コピーしたいクラス・関数・プロパティなどの名前の上で右クリックする
3. メニューの「切り取り / コピー / 貼り付け」の下にある **「完全修飾名をコピー」** を選ぶ
4. ステータスバー(ウィンドウ左下)に「完全修飾名をコピーしました: …」と表示され、クリップボードに入る

右クリックするとカーソルがその位置へ移動するので、「右クリックした位置」=「カーソル位置」として扱われます。メニュー項目は C# と C++ のエディターでだけ表示されます。Visual Studio の表示言語が日本語以外なら、メニューは「Copy Fully Qualified Name」と英語で表示されます。

## インストール

1. リポジトリ直下の `build.bat` をダブルクリックしてビルドする(詳しくは[リポジトリの README](../../README.md))
2. `artifacts\vsix\CopyFullyQualifiedName.vsix` をダブルクリックしてインストールする
3. Visual Studio を再起動する

対応バージョン: Visual Studio 2022 (17.14 以降) / Visual Studio 2026(Community / Professional / Enterprise)。動作確認は Visual Studio Community 2026 (18.10, x64) で行っています。Arm64 版もインストール対象に含めていますが未検証です。

## 仕様

### 出力形式

- **C#**: 区切りは `.`。名前空間と外側の型で修飾し、ジェネリックの型パラメーターは残す(`Repository<T>`)。メソッドの引数リストは含めない(オーバーロードは区別しない)。
- **C++**: 区切りは `::`。Visual Studio の C++ コードモデルが返す名前をそのまま使う。テンプレートは `Repository<T>` のように型パラメーター付きになる。

### C# で何がコピーされるか

カーソル位置のシンボルを Roslyn(C# コンパイラー)で解決します。宣言だけでなく、**使っている側(参照箇所)を右クリックしても、その参照先の名前**になります。

| 右クリックした場所 | 結果 |
| --- | --- |
| 型・メソッド・プロパティ・フィールド・イベント・列挙値・名前空間(宣言でも参照でも) | そのシンボル |
| `List<int>` のような構築済みジェネリック | 定義側(`System.Collections.Generic.List<T>`) |
| `int` `string` などの組み込み型キーワード | .NET の型名(`System.Int32`) |
| 拡張メソッドの呼び出し(`xs.Any()`) | 静的メソッドとしての名前(`System.Linq.Enumerable.Any<TSource>`) |
| `using` エイリアス | エイリアスの実体 |
| ローカル変数・引数・ラムダ・ローカル関数・型パラメーター | それを含むメソッドや型(これらには完全修飾名が無いため) |
| プロパティやイベントのアクセサー(`get` / `set` / `add` / `remove`)の中 | そのプロパティ・イベント |
| メソッド本体やクラス本体の空白 | それを含むメソッドやクラス |
| トップレベルステートメントの中(参照箇所以外) | 取得できない(ステータスバーにその旨を表示) |

### C++ で何がコピーされるか

Visual Studio の C++ コードモデルを使い、**カーソル位置を含む最も内側の宣言**の名前になります。

| 右クリックした場所 | 結果 |
| --- | --- |
| クラス・構造体・共用体・列挙型・関数・メンバー変数・列挙子の宣言 | その宣言 |
| ヘッダーのメンバー関数宣言 / .cpp のクラス外定義(`void Widget::Draw() {}`) | どちらもそのメンバー関数 |
| 名前空間スコープの関数プロトタイプ宣言(`void FreeFunction();`) | その関数 |
| 関数本体の中 | その関数(**中で使っている別のクラスや関数の名前にはならない**) |
| 名前空間の中の空白 | その名前空間 |

## 制限事項

- **C++ では参照箇所の解決をしません。** 関数本体の中で別のクラス名や関数呼び出しを右クリックしても、コピーされるのは囲んでいる関数の名前です。参照先の名前が欲しい場合は、定義へ移動(F12)してから右クリックしてください。C++ 側は宣言の構造を返す API(コードモデル)を使っていて、C# のようなシンボル解決をしていないためです。
- C++ のコードモデルは **`.vcxproj` のプロジェクトに含まれるファイル**で使えます。CMake やフォルダーを開く(Open Folder)形式のプロジェクトでは取得できないことがあります。
- C# でも、プロジェクトに属さない単独の `.cs` ファイル(ソリューションエクスプローラーの「その他のファイル」)は Roslyn のワークスペースに載らないため、C++ と同じコードモデルによる方式になります。この場合は参照箇所を解決せず、取得できないこともあります。
- C++ のファイルを開いた直後は、IntelliSense の解析が終わるまで結果が得られないことがあります。少し待ってから再実行してください。
- Razor(`.razor` / `.cshtml`)や VB は対象外です。

## 開発

### 構成

| パス | 内容 |
| --- | --- |
| `src/CopyFullyQualifiedName/` | VSIX 本体。コマンド定義(`CopyFullyQualifiedNameCommand.cs`)、メニュー配置(`ExtensionEntrypoint.cs`)、表示文字列(`.vsextension/`) |
| `src/CopyFullyQualifiedName.Core/` | VS に依存しないロジック。C# のシンボル解決(`CSharpNameResolver`)、C++ の名前選択(`CppNameSelector`) |
| `test/CopyFullyQualifiedName.Core.Tests/` | 単体テスト(xUnit) |
| `test/e2e/` | 実験用インスタンスの Visual Studio を DTE 経由で操作する E2E テスト |
| `samples/` | 手動確認・E2E 用の C# / C++ サンプル(`Samples.slnx`) |

### 単体テスト

```bash
dotnet test extensions/CopyFullyQualifiedName/test/CopyFullyQualifiedName.Core.Tests
```

### E2E テスト

拡張機能をビルドして実験用インスタンス(`/rootSuffix Exp`)にデプロイし、`samples/Samples.slnx` を開いて、各ケースでカーソルを置いて右クリックメニューの項目を実行し、結果を照合します。普段使いの Visual Studio が起動していても、それには触れません。

```bash
pwsh -File extensions/CopyFullyQualifiedName/test/e2e/Invoke-E2E.ps1
```

- 実験用インスタンスを初めて起動したときに出る初回設定ダイアログは、既定の設定のまま自動で閉じます。
- テスト中は、テストを実行している人のクリップボードを上書きしないよう、環境変数 `COPYFQN_E2E_SKIP_CLIPBOARD=1` を付けて起動し、ステータスバーの表示で結果を確認します(この環境変数は通常の利用では設定されません)。
- `-KeepOpen` を付けると、終了後も実験用インスタンスを閉じません。
- `-VerifyClipboard` を付けると、クリップボードへの書き込みを省かず、実際にコピーされた文字列も照合します(実行中はクリップボードが上書きされます)。CI ではこれを付けて走らせています。
- `-DiagnosticsDirectory <フォルダー>` を付けると、失敗時にスクリーンショット・画面上の文字・VS のログを保存します(スクリーンショットには画面全体が写ります)。

### 手動での動作確認

`vs-extensions.slnx` を開き、`CopyFullyQualifiedName` をスタートアッププロジェクトにして `F5` を押します。起動した実験用インスタンスで `samples/Samples.slnx` を開き、右クリックして確認してください。
