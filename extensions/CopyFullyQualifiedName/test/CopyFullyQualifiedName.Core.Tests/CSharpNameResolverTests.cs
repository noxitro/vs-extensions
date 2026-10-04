using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CopyFullyQualifiedName.Tests;

public class CSharpNameResolverTests
{
    private const string Caret = "$$";

    /// <summary>
    /// <paramref name="markup"/> 内の <c>$$</c> をカーソル位置として解決する。
    /// </summary>
    private static async Task<string?> ResolveAsync(string markup)
    {
        var position = markup.IndexOf(Caret, StringComparison.Ordinal);
        Assert.True(position >= 0, "markup に $$ がありません");
        var source = markup.Remove(position, Caret.Length);

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
            .WithMetadataReferences(
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
            ]);
        var document = project.AddDocument("Test.cs", SourceText.From(source));
        return await CSharpNameResolver.ResolveAsync(document, position, CancellationToken.None);
    }

    [Theory]
    // クラス宣言の名前
    [InlineData("namespace A.B { class $$Foo { } }", "A.B.Foo")]
    // 名前の末尾にカーソル
    [InlineData("namespace A.B { class Foo$$ { } }", "A.B.Foo")]
    // file-scoped namespace
    [InlineData("namespace A.B; class $$Foo { }", "A.B.Foo")]
    // 入れ子クラス
    [InlineData("namespace A { class Outer { class $$Inner { } } }", "A.Outer.Inner")]
    // ジェネリック型は型パラメーター付き
    [InlineData("namespace A { class $$Repo<T> { } }", "A.Repo<T>")]
    // メソッドは引数リストを含めない
    [InlineData("namespace A { class Foo { void $$Bar(int x) { } } }", "A.Foo.Bar")]
    [InlineData("namespace A { class Foo<T> { void $$Bar<U>() { } } }", "A.Foo<T>.Bar<U>")]
    // プロパティ・フィールド・イベント・列挙値
    [InlineData("namespace A { class Foo { int $$Prop { get; set; } } }", "A.Foo.Prop")]
    [InlineData("namespace A { class Foo { int $$field; } }", "A.Foo.field")]
    [InlineData("namespace A { class Foo { event System.Action $$Changed; } }", "A.Foo.Changed")]
    [InlineData("namespace A { enum Color { $$Red, Green } }", "A.Color.Red")]
    // コンストラクター
    [InlineData("namespace A { class Foo { $$Foo() { } } }", "A.Foo.Foo")]
    // 名前空間宣言そのもの
    [InlineData("namespace A.$$B { class Foo { } }", "A.B")]
    // グローバル名前空間の型
    [InlineData("class $$Foo { }", "Foo")]
    // record / struct / interface
    [InlineData("namespace A { record $$Person(string Name); }", "A.Person")]
    [InlineData("namespace A { struct $$Point { } }", "A.Point")]
    [InlineData("namespace A { interface $$IFoo { } }", "A.IFoo")]
    public async Task 宣言の名前(string markup, string expected)
    {
        Assert.Equal(expected, await ResolveAsync(markup));
    }

    [Theory]
    // 参照箇所(型の使用)
    [InlineData("namespace A { class Foo { } class Bar { $$Foo f; } }", "A.Foo")]
    // 参照箇所(メソッド呼び出し)
    [InlineData("namespace A { class Foo { static void Run() { } void M() { Foo.$$Run(); } } }", "A.Foo.Run")]
    // 構築済みジェネリックは定義側の名前にする
    [InlineData("using System.Collections.Generic; class C { $$List<int> xs; }", "System.Collections.Generic.List<T>")]
    // 組み込み型のキーワードは .NET の型名
    [InlineData("class C { $$int x; }", "System.Int32")]
    [InlineData("class C { $$string s; }", "System.String")]
    // using エイリアスは実体
    [InlineData("using L = System.Collections.Generic.List<int>; class C { $$L xs; }", "System.Collections.Generic.List<T>")]
    // 拡張メソッド呼び出しは静的メソッドとしての名前
    [InlineData("using System.Linq; class C { void M(int[] a) { a.$$Any(); } }", "System.Linq.Enumerable.Any<TSource>")]
    // using ディレクティブの名前空間
    [InlineData("using System.Collections.$$Generic; class C { }", "System.Collections.Generic")]
    // 配列型は要素型
    [InlineData("namespace A { class Foo { } class C { $$Foo[] xs; } }", "A.Foo")]
    // 暗黙のコンストラクターによる生成
    [InlineData("namespace A { class Foo { } class C { object o = new $$Foo(); } }", "A.Foo")]
    // トップレベルステートメントからの参照
    [InlineData("A.Foo.$$Run(); namespace A { static class Foo { public static void Run() { } } }", "A.Foo.Run")]
    public async Task 参照箇所(string markup, string expected)
    {
        Assert.Equal(expected, await ResolveAsync(markup));
    }

    [Theory]
    // メソッド本体の空白 → 包含するメソッド
    [InlineData("namespace A { class Foo { void Bar() { $$ } } }", "A.Foo.Bar")]
    // ローカル変数 → 包含するメソッド
    [InlineData("namespace A { class Foo { void Bar() { int $$x = 0; } } }", "A.Foo.Bar")]
    // 引数 → 包含するメソッド
    [InlineData("namespace A { class Foo { void Bar(int $$x) { } } }", "A.Foo.Bar")]
    // ラムダ内 → 包含するメソッド
    [InlineData("namespace A { class Foo { void Bar() { System.Action a = () => { $$ }; } } }", "A.Foo.Bar")]
    // ローカル関数 → 包含するメソッド
    [InlineData("namespace A { class Foo { void Bar() { void $$Local() { } } } }", "A.Foo.Bar")]
    // 型パラメーター → それを宣言した型
    [InlineData("namespace A { class Foo<$$T> { } }", "A.Foo<T>")]
    // クラス本体の空白 → クラス
    [InlineData("namespace A { class Foo { $$ } }", "A.Foo")]
    // プロパティのアクセサー内 → プロパティ
    [InlineData("namespace A { class Foo { int P { get { $$return 0; } } } }", "A.Foo.P")]
    [InlineData("namespace A { class Foo { int P { $$set { } } } }", "A.Foo.P")]
    // イベントのアクセサー内 → イベント
    [InlineData("namespace A { class Foo { event System.Action E { add { $$ } remove { } } } }", "A.Foo.E")]
    public async Task 包含する宣言へのフォールバック(string markup, string expected)
    {
        Assert.Equal(expected, await ResolveAsync(markup));
    }

    [Theory]
    // ファイル先頭の空白(どの宣言の中でもない)
    [InlineData("$$\nclass Foo { }")]
    // 解決できない識別子のみ
    [InlineData("$$Unknown x;")]
    public async Task 解決できない場合はnull(string markup)
    {
        Assert.Null(await ResolveAsync(markup));
    }
}
