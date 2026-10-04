using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CopyFullyQualifiedName;

/// <summary>
/// C# のカーソル位置にあるシンボルの完全修飾名を求める。
/// </summary>
/// <remarks>
/// 解決順:
/// 1. カーソル位置のトークンが指すシンボル(宣言でも参照でもよい)。
/// 2. それが完全修飾名を持たないもの(ローカル変数・引数・ラムダ等)なら、それを含む最も内側の宣言。
/// 3. トークンがシンボルを指さない(空白・キーワード等)なら、カーソルを含む最も内側の宣言。
/// </remarks>
public static class CSharpNameResolver
{
    /// <summary>
    /// 出力形式。名前空間・外側の型で修飾し、型引数は残し、メソッドの引数リストは含めない。
    /// 例: <c>MyApp.Models.Repository&lt;T&gt;.Find</c>
    /// </summary>
    internal static readonly SymbolDisplayFormat Format = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType | SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public static async Task<string?> ResolveAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return null;
        }

        // 編集直後はワークスペース側の文書がエディターより短いことがあるので、範囲内に収める
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        position = Math.Max(0, Math.Min(position, text.Length));

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace, cancellationToken).ConfigureAwait(false);
        var target = Normalize(symbol) ?? Normalize(semanticModel.GetEnclosingSymbol(position, cancellationToken));
        return target is null ? null : target.ToDisplayString(Format);
    }

    /// <summary>
    /// 表示対象として意味のあるシンボルに寄せる。完全修飾名を持たないものは外側へたどる。
    /// </summary>
    internal static ISymbol? Normalize(ISymbol? symbol)
    {
        while (symbol is not null)
        {
            // トップレベルステートメントの中は、ソース上に名前を持つ宣言が無い
            if (symbol is IMethodSymbol { Name: WellKnownMemberNames.TopLevelStatementsEntryPointMethodName })
            {
                return null;
            }

            // 暗黙のコンストラクターなど、ソースに名前が無いもの
            if (symbol.IsImplicitlyDeclared && symbol is not INamespaceSymbol)
            {
                symbol = symbol.ContainingSymbol;
                continue;
            }

            switch (symbol)
            {
                case IAliasSymbol alias:
                    symbol = alias.Target;
                    continue;
                case INamespaceSymbol { IsGlobalNamespace: true }:
                    return null;
                case IMethodSymbol { MethodKind: MethodKind.ReducedExtension } reduced:
                    symbol = reduced.ReducedFrom;
                    continue;
                case IMethodSymbol { AssociatedSymbol: { } associated }:
                    // get/set/add/remove アクセサーはプロパティ・イベント側の名前にする
                    symbol = associated;
                    continue;
                case IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction }:
                case ILocalSymbol:
                case IParameterSymbol:
                case IRangeVariableSymbol:
                case ILabelSymbol:
                case IDiscardSymbol:
                case ITypeParameterSymbol:
                    symbol = symbol.ContainingSymbol;
                    continue;
                case IArrayTypeSymbol array:
                    symbol = array.ElementType;
                    continue;
                case IPointerTypeSymbol pointer:
                    symbol = pointer.PointedAtType;
                    continue;
                case INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying }:
                    symbol = underlying.OriginalDefinition;
                    return symbol;
                case INamedTypeSymbol { IsAnonymousType: true }:
                case IDynamicTypeSymbol:
                case IFunctionPointerTypeSymbol:
                case IErrorTypeSymbol:
                    return null;
                case INamespaceOrTypeSymbol or IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol:
                    // List<int> → List<T> のように、構築済みジェネリックは定義側に戻す
                    return symbol.OriginalDefinition;
                default:
                    symbol = symbol.ContainingSymbol;
                    continue;
            }
        }

        return null;
    }
}
