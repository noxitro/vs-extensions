using System.Globalization;

namespace CopyFullyQualifiedName;

/// <summary>
/// ステータスバーに出す文言。VS の表示言語が日本語なら日本語、それ以外は英語。
/// </summary>
internal static class Messages
{
    private static bool IsJapanese => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";

    public static string Copied(string name) => IsJapanese
        ? $"完全修飾名をコピーしました: {name}"
        : $"Copied fully qualified name: {name}";

    public static string NotFound => IsJapanese
        ? "完全修飾名を取得できませんでした(カーソル位置にクラス・関数などの宣言が見つかりません)"
        : "Could not get a fully qualified name (no class, function, or other declaration found at the caret)";

    public static string ClipboardBusy => IsJapanese
        ? "完全修飾名をコピーできませんでした(クリップボードが他のアプリで使用中です)。もう一度お試しください"
        : "Could not copy the fully qualified name (the clipboard is in use by another application). Please try again";

    public static string Failed(string reason) => IsJapanese
        ? $"完全修飾名のコピーに失敗しました: {reason}"
        : $"Failed to copy fully qualified name: {reason}";
}
