namespace CopyFullyQualifiedName.Tests;

public class CppNameSelectorTests
{
    /// <summary>テスト用のコードモデル要素。範囲が null なら「別ファイルを指す要素」。</summary>
    private sealed class Node(string fullName, int? start, int? end, params Node[] children) : ICppCodeNode
    {
        public string Name => fullName.Split("::")[^1];

        public string FullName => fullName;

        public IEnumerable<ICppCodeNode> Children => children;

        public bool TryGetRange(out int s, out int e)
        {
            s = start ?? 0;
            e = end ?? 0;
            return start.HasValue && end.HasValue;
        }
    }

    [Fact]
    public void 最も階層の深い名前を選ぶ()
    {
        Assert.Equal("ns::Widget::Draw", CppNameSelector.SelectDeepest(["ns::Widget", "ns::Widget::Draw", "ns"]));
    }

    [Fact]
    public void テンプレート引数内の区切りは階層に数えない()
    {
        Assert.Equal("ns::Widget::Draw", CppNameSelector.SelectDeepest(["ns::Repo<std::map<a::b, c::d>>", "ns::Widget::Draw"]));
    }

    [Fact]
    public void 同じ深さなら先の候補を残す()
    {
        Assert.Equal("ns::Value", CppNameSelector.SelectDeepest(["ns::Value", "ns::Other"]));
    }

    [Fact]
    public void 空やnullは無視する()
    {
        Assert.Equal("ns", CppNameSelector.SelectDeepest([null, "", "  ", "ns"]));
        Assert.Null(CppNameSelector.SelectDeepest([]));
    }

    [Fact]
    public void ツリー_カーソルを含む最も内側の要素()
    {
        var roots = new[]
        {
            new Node("a", 0, 100, new Node("a::b", 10, 90, new Node("a::b::Widget", 20, 50))),
        };

        Assert.Equal("a::b::Widget", CppNameSelector.SelectFromTree(roots, 30, identifier: null));
        Assert.Equal("a::b", CppNameSelector.SelectFromTree(roots, 60, identifier: null));
        Assert.Null(CppNameSelector.SelectFromTree(roots, 200, identifier: null));
    }

    [Fact]
    public void ツリー_定義が別ファイルにある関数のプロトタイプは識別子で探す()
    {
        // FreeFunction の範囲は .cpp 側を指すので、このファイルでは範囲を持たない
        var roots = new[]
        {
            new Node("a", 0, 100, new Node("a::b", 10, 90, new Node("a::b::FreeFunction", null, null), new Node("a::b::Widget", 20, 50))),
        };

        Assert.Equal("a::b::FreeFunction", CppNameSelector.SelectFromTree(roots, 70, identifier: "FreeFunction"));
        // 識別子が一致しなければ囲んでいる名前空間
        Assert.Equal("a::b", CppNameSelector.SelectFromTree(roots, 70, identifier: "Other"));
    }

    [Fact]
    public void ツリー_最上位の要素も識別子で探す()
    {
        var roots = new[] { new Node("Global", null, null) };

        Assert.Equal("Global", CppNameSelector.SelectFromTree(roots, 5, identifier: "Global"));
    }

    [Fact]
    public void ツリー_包含要素自身の名前()
    {
        var roots = new[] { new Node("a::Widget", 0, 100) };

        Assert.Equal("a::Widget", CppNameSelector.SelectFromTree(roots, 3, identifier: "Widget"));
    }

    [Theory]
    [InlineData("    void FreeFunction();", 9, "FreeFunction")]
    [InlineData("    void FreeFunction();", 16, "FreeFunction")]
    // 識別子の直後
    [InlineData("    void FreeFunction();", 21, "FreeFunction")]
    [InlineData("int x_1 = 0;", 4, "x_1")]
    [InlineData("    ", 2, null)]
    [InlineData("a + b", 2, null)]
    [InlineData("x = 123;", 5, null)]
    [InlineData("", 0, null)]
    [InlineData("abc", 3, "abc")]
    [InlineData("abc", 10, null)]
    public void カーソル位置の識別子(string line, int index, string? expected)
    {
        Assert.Equal(expected, CppNameSelector.GetIdentifierAt(line, index));
    }

    [Theory]
    [InlineData("::Global", "Global")]
    [InlineData("  ns::Foo ", "ns::Foo")]
    [InlineData("ns::Foo", "ns::Foo")]
    public void 名前の整形(string input, string expected)
    {
        Assert.Equal(expected, CppNameSelector.Normalize(input));
    }
}
