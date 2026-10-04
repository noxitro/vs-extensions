namespace CopyFullyQualifiedName;

/// <summary>
/// C++ コードモデルの要素を、VS に依存しない形で表したもの。
/// </summary>
public interface ICppCodeNode
{
    /// <summary>単純名(例: <c>Draw</c>)。</summary>
    string Name { get; }

    /// <summary>コードモデルが返す完全修飾名(例: <c>ns::Widget::Draw</c>)。</summary>
    string FullName { get; }

    /// <summary>
    /// 対象ファイル内での範囲(ファイル先頭からの絶対オフセット)を返す。
    /// 要素の位置が別ファイル(.h の宣言に対する .cpp の定義など)を指すときは false。
    /// </summary>
    bool TryGetRange(out int start, out int end);

    IEnumerable<ICppCodeNode> Children { get; }
}

/// <summary>
/// C++ の完全修飾名の選び方。
/// </summary>
/// <remarks>
/// VC のコードモデルは宣言と定義を 1 つの要素にまとめ、位置は定義側を返すことがある。
/// そのため「範囲の広さ」はファイルをまたいで比べられないので、入れ子の深さ(名前の階層)で内側を判断する。
/// </remarks>
public static class CppNameSelector
{
    /// <summary>
    /// カーソル位置を含むとコードモデルが答えた要素名の中から、最も内側(階層が深い)ものを選ぶ。
    /// 同じ深さなら先に渡されたものを残す(呼び出し側で具体的な種類から順に渡す)。
    /// </summary>
    public static string? SelectDeepest(IEnumerable<string?> fullNames)
    {
        string? best = null;
        var bestDepth = -1;
        foreach (var fullName in fullNames)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var name = Normalize(fullName!);
            var depth = GetDepth(name);
            if (depth > bestDepth)
            {
                best = name;
                bestDepth = depth;
            }
        }

        return best;
    }

    /// <summary>
    /// コードモデルのツリーを、このファイル内の範囲だけを頼りにたどって名前を決める。
    /// </summary>
    /// <param name="roots">ファイルの最上位の要素。</param>
    /// <param name="caretOffset">カーソル位置(<see cref="ICppCodeNode.TryGetRange"/> と同じ基準のオフセット)。</param>
    /// <param name="identifier">カーソル位置の識別子。無ければ null。</param>
    /// <returns>
    /// カーソルを含む最も内側の要素の直下に <paramref name="identifier"/> と同名の要素があればその名前
    /// (名前空間内の関数プロトタイプ宣言など)。無ければカーソルを含む最も内側の要素の名前。
    /// </returns>
    public static string? SelectFromTree(IEnumerable<ICppCodeNode> roots, int caretOffset, string? identifier)
    {
        ICppCodeNode? container = null;
        var level = roots;
        while (true)
        {
            var next = level.FirstOrDefault(node => node.TryGetRange(out var start, out var end) && start <= caretOffset && caretOffset <= end);
            if (next is null)
            {
                break;
            }

            container = next;
            level = next.Children;
        }

        if (!string.IsNullOrEmpty(identifier))
        {
            if (container is not null && container.Name == identifier)
            {
                return Normalize(container.FullName);
            }

            var match = level.FirstOrDefault(node => node.Name == identifier);
            if (match is not null)
            {
                return Normalize(match.FullName);
            }
        }

        return container is null ? null : Normalize(container.FullName);
    }

    /// <summary>
    /// 行テキスト中の <paramref name="index"/>(0 始まり)にある識別子を返す。
    /// 識別子の直後(<c>Foo|(</c>)にカーソルがある場合も、その識別子を返す。
    /// </summary>
    public static string? GetIdentifierAt(string lineText, int index)
    {
        if (index < 0 || index > lineText.Length)
        {
            return null;
        }

        var start = index;
        if (start == lineText.Length || !IsIdentifierChar(lineText[start]))
        {
            // 識別子の末尾の直後にいる場合は 1 文字戻る
            if (start == 0 || !IsIdentifierChar(lineText[start - 1]))
            {
                return null;
            }

            start--;
        }

        while (start > 0 && IsIdentifierChar(lineText[start - 1]))
        {
            start--;
        }

        var end = start;
        while (end < lineText.Length && IsIdentifierChar(lineText[end]))
        {
            end++;
        }

        // 数字始まりは識別子ではない(数値リテラル)
        return char.IsDigit(lineText[start]) ? null : lineText.Substring(start, end - start);
    }

    /// <summary>
    /// コードモデルの FullName を表示用に整える。先頭の <c>::</c>(グローバル修飾)と余分な空白を除く。
    /// </summary>
    public static string Normalize(string fullName)
    {
        var name = fullName.Trim();
        if (name.StartsWith("::", StringComparison.Ordinal))
        {
            name = name.Substring(2);
        }

        return name;
    }

    /// <summary>
    /// <c>::</c> で区切られた階層の数。テンプレート引数(<c>&lt;...&gt;</c>)の中の <c>::</c> は数えない。
    /// </summary>
    internal static int GetDepth(string fullName)
    {
        var depth = 0;
        var angle = 0;
        for (var i = 0; i < fullName.Length; i++)
        {
            switch (fullName[i])
            {
                case '<':
                    angle++;
                    break;
                case '>':
                    angle = Math.Max(0, angle - 1);
                    break;
                case ':' when angle == 0 && i + 1 < fullName.Length && fullName[i + 1] == ':':
                    depth++;
                    i++;
                    break;
            }
        }

        return depth;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
