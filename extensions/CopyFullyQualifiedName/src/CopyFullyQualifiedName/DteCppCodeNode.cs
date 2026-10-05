using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace CopyFullyQualifiedName;

/// <summary>
/// DTE のコードモデル要素を <see cref="ICppCodeNode"/> として見せるアダプター。UI スレッドでのみ使う。
/// </summary>
internal sealed class DteCppCodeNode(CodeElement element, string filePath) : ICppCodeNode
{
    /// <summary>
    /// コードモデルが「該当なし・取得できない」の意味で返す例外か。
    /// COM の HRESULT は .NET の例外型に写される(E_INVALIDARG → ArgumentException、
    /// E_NOINTERFACE → InvalidCastException、E_ACCESSDENIED → UnauthorizedAccessException)ので、
    /// COMException だけでは拾いきれない。
    /// </summary>
    public static bool IsCodeModelError(Exception ex) =>
        ex is COMException or NotImplementedException or ArgumentException or InvalidCastException or UnauthorizedAccessException;

    public string Name
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return element.Name ?? "";
            }
            catch (Exception ex) when (IsCodeModelError(ex))
            {
                return "";
            }
        }
    }

    public string FullName
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return element.FullName ?? "";
            }
            catch (Exception ex) when (IsCodeModelError(ex))
            {
                return "";
            }
        }
    }

    public IEnumerable<ICppCodeNode> Children
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return Wrap(element.Children, filePath);
            }
            catch (Exception ex) when (IsCodeModelError(ex))
            {
                return [];
            }
        }
    }

    public bool TryGetRange(out int start, out int end)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        start = end = 0;
        try
        {
            // 宣言と定義が別ファイルの要素は、位置が定義側(別ファイル)を指すことがある
            var startPoint = element.StartPoint;
            if (!string.Equals(startPoint.Parent?.Parent?.FullName, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            start = startPoint.AbsoluteCharOffset;
            end = element.EndPoint.AbsoluteCharOffset;
            return true;
        }
        catch (Exception ex) when (IsCodeModelError(ex))
        {
            return false;
        }
    }

    public static IEnumerable<ICppCodeNode> Wrap(CodeElements? elements, string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var nodes = new List<ICppCodeNode>();
        if (elements is null)
        {
            return nodes;
        }

        foreach (CodeElement child in elements)
        {
            nodes.Add(new DteCppCodeNode(child, filePath));
        }

        return nodes;
    }
}
