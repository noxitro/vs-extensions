"""どのファイルに UTF-8 BOM を付けるかの決まり。check-file-format.py が使う。

リポジトリのテキストファイルは BOM なしの UTF-8 に統一してある (README.md の「ファイル形式」)。
BOM が要るのは、BOM が無いとシステムのコードページ (日本語 Windows では Shift-JIS) で
読まれてしまう次のものだけ。.editorconfig の charset もこれと揃えること。
"""

import fnmatch

# BOM が必要なもの
REQUIRED = [
    # Windows PowerShell 5.1 は BOM の無いスクリプトを Shift-JIS として読む
    "*.ps1",
    "*.psm1",
    "*.psd1",
]

# どちらでもよいもの
ANY = []

REQUIRED_POLICY = "required"
FORBIDDEN_POLICY = "forbidden"
ANY_POLICY = "any"


def policy(path):
    """path (リポジトリのルートからの / 区切りのパス) の BOM の決まりを返す。"""
    if any(fnmatch.fnmatch(path, p) for p in REQUIRED):
        return REQUIRED_POLICY
    if any(fnmatch.fnmatch(path, p) for p in ANY):
        return ANY_POLICY
    return FORBIDDEN_POLICY
