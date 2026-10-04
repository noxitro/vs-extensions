using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace CopyFullyQualifiedName;

/// <summary>
/// 拡張機能のエントリポイント。
/// </summary>
[VisualStudioContribution]
internal sealed class ExtensionEntrypoint : Extension
{
    /// <summary>VS 標準メニューの GUID(guidSHLMainMenu)。</summary>
    private static readonly Guid GuidShlMainMenu = new("d309f791-903f-11d0-9efc-00a0c911004f");

    /// <summary>コードエディターの右クリックメニュー内、切り取り/コピー/貼り付けのグループ(IDG_VS_CODEWIN_TEXTEDIT)。</summary>
    private const uint IdgVsCodeWinTextEdit = 0x01C0;

    /// <inheritdoc />
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        RequiresInProcessHosting = true,
    };

    /// <summary>
    /// コードエディターの右クリックメニューの「コピー」の並びに置く。
    /// </summary>
    internal static CommandPlacement CodeWindowContextMenuPlacement =>
        CommandPlacement.VsctParent(GuidShlMainMenu, IdgVsCodeWinTextEdit, priority: 0x0700);
}
