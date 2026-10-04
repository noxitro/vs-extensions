using System.Diagnostics;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.VSSdkCompatibility;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Threading;
using Command = Microsoft.VisualStudio.Extensibility.Commands.Command;

namespace CopyFullyQualifiedName;

/// <summary>
/// コードエディターの右クリックメニュー「完全修飾名をコピー」。
/// </summary>
[VisualStudioContribution]
internal sealed class CopyFullyQualifiedNameCommand : Command
{
    /// <summary>
    /// C++ コードモデルに問い合わせる要素の種類。同じ深さの候補が並んだときに優先したい具体的なものから並べる。
    /// </summary>
    private static readonly vsCMElement[] CppElementKinds =
    [
        vsCMElement.vsCMElementFunction,
        vsCMElement.vsCMElementVariable,
        vsCMElement.vsCMElementProperty,
        vsCMElement.vsCMElementEvent,
        vsCMElement.vsCMElementDelegate,
        vsCMElement.vsCMElementTypeDef,
        vsCMElement.vsCMElementEnum,
        vsCMElement.vsCMElementUnion,
        vsCMElement.vsCMElementStruct,
        vsCMElement.vsCMElementClass,
        vsCMElement.vsCMElementInterface,
        vsCMElement.vsCMElementNamespace,
    ];

    private readonly TraceSource logger;
    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly MefInjection<VisualStudioWorkspace> workspace;
    private readonly AsyncServiceProviderInjection<DTE, DTE2> dte;

    public CopyFullyQualifiedNameCommand(
        VisualStudioExtensibility extensibility,
        TraceSource traceSource,
        JoinableTaskFactory joinableTaskFactory,
        MefInjection<VisualStudioWorkspace> workspace,
        AsyncServiceProviderInjection<DTE, DTE2> dte)
        : base(extensibility)
    {
        this.logger = Requires.NotNull(traceSource, nameof(traceSource));
        this.joinableTaskFactory = Requires.NotNull(joinableTaskFactory, nameof(joinableTaskFactory));
        this.workspace = Requires.NotNull(workspace, nameof(workspace));
        this.dte = Requires.NotNull(dte, nameof(dte));
    }

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%CopyFullyQualifiedName.Command.DisplayName%")
    {
        TooltipText = "%CopyFullyQualifiedName.Command.ToolTipText%",
        Icon = new(ImageMoniker.KnownValues.Copy, IconSettings.IconAndText),
        Placements = [ExtensionEntrypoint.CodeWindowContextMenuPlacement],
        VisibleWhen = ActivationConstraint.EditorContentType("CSharp") | ActivationConstraint.EditorContentType("C/C++"),
    };

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        string? name;
        try
        {
            name = await this.ResolveNameAsync(context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger.TraceEvent(TraceEventType.Error, 0, $"Failed to resolve fully qualified name: {ex}");
            await this.SetStatusAsync(Messages.Failed(ex.Message), cancellationToken);
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            await this.SetStatusAsync(Messages.NotFound, cancellationToken);
            return;
        }

        await this.joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        if (!TrySetClipboard(name!))
        {
            await this.SetStatusAsync(Messages.ClipboardBusy, cancellationToken);
            return;
        }

        await this.SetStatusAsync(Messages.Copied(name!), cancellationToken);
    }

    private async Task<string?> ResolveNameAsync(IClientContext context, CancellationToken cancellationToken)
    {
        var textView = await context.GetActiveTextViewAsync(cancellationToken);
        if (textView is null)
        {
            return null;
        }

        var filePath = textView.Document.Uri.LocalPath;
        var position = textView.Selection.ActivePosition.Offset;

        // Roslyn のワークスペースに載っているファイル(C#)なら、参照箇所も含めてシンボルを解決できる
        var roslynWorkspace = await this.workspace.GetServiceAsync();
        var documentId = roslynWorkspace.CurrentSolution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
        var document = documentId is null ? null : roslynWorkspace.CurrentSolution.GetDocument(documentId);
        if (document is not null)
        {
            return await CSharpNameResolver.ResolveAsync(document, position, cancellationToken);
        }

        // それ以外(C++)は DTE のコードモデルで、カーソルを含む最も内側の宣言を取る
        return await this.ResolveCppNameAsync(filePath, cancellationToken);
    }

    private async Task<string?> ResolveCppNameAsync(string filePath, CancellationToken cancellationToken)
    {
        var dte = await this.dte.GetServiceAsync();
        await this.joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var document = dte.ActiveDocument;
        if (document is null || !string.Equals(document.FullName, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (document.Selection is not TextSelection selection)
        {
            return null;
        }

        TextPoint caret = selection.ActivePoint;
        var fileCodeModel = document.ProjectItem?.FileCodeModel;

        // 1. カーソル位置を含む要素をコードモデルに種類ごとに問い合わせ、最も内側を選ぶ
        var names = new List<string?>();
        foreach (var kind in CppElementKinds)
        {
            var element = GetCodeElement(fileCodeModel, caret, kind);
            if (element is null)
            {
                continue;
            }

            try
            {
                names.Add(element.FullName);
            }
            catch (COMException ex)
            {
                this.logger.TraceEvent(TraceEventType.Warning, 0, $"Skipped code element ({kind}): {ex.Message}");
            }
        }

        var name = CppNameSelector.SelectDeepest(names);
        if (name is not null || fileCodeModel is null)
        {
            return name;
        }

        // 2. 見つからない場合(名前空間内の関数プロトタイプ宣言、名前空間内の空白など)は、
        //    このファイル内の範囲でツリーをたどり、カーソル位置の識別子と同名の要素か、囲んでいる要素を使う
        var lineText = caret.CreateEditPoint().GetLines(caret.Line, caret.Line + 1);
        var identifier = CppNameSelector.GetIdentifierAt(lineText, caret.LineCharOffset - 1);
        var roots = DteCppCodeNode.Wrap(fileCodeModel.CodeElements, filePath);
        return CppNameSelector.SelectFromTree(roots, caret.AbsoluteCharOffset, identifier);
    }

    private static CodeElement? GetCodeElement(FileCodeModel? fileCodeModel, TextPoint point, vsCMElement kind)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            // 該当する種類の要素が無いと null を返すか COMException を投げる(言語サービスによって異なる)
            return fileCodeModel is not null
                ? fileCodeModel.CodeElementFromPoint(point, kind)
                : point.CodeElement[kind];
        }
        catch (COMException)
        {
            return null;
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    private static bool TrySetClipboard(string text)
    {
        // E2E テストで、テストを走らせている人のクリップボードを上書きしないための抜け道。
        // test/e2e のスクリプトが実験用インスタンスを起動するときにだけ設定する
        if (Environment.GetEnvironmentVariable("COPYFQN_E2E_SKIP_CLIPBOARD") == "1")
        {
            return true;
        }

        // 他プロセスがクリップボードを開いていると一時的に失敗するので、少し待って再試行する
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        return false;
    }

    private async Task SetStatusAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            var dte = await this.dte.GetServiceAsync();
            await this.joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            dte.StatusBar.Text = message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.logger.TraceEvent(TraceEventType.Warning, 0, $"Failed to update status bar: {ex.Message}");
        }
    }
}
