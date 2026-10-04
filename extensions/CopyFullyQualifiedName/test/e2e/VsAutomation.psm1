# 実験用インスタンス(/rootSuffix Exp)の Visual Studio を DTE 経由で操作するための補助関数。
# 起動中の他の Visual Studio には触れないよう、自分で起動したプロセスの DTE だけを ROT から取得する。

if (-not ('VsE2E.Rot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace VsE2E
{
    public static class Rot
    {
        [DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable rot);

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(int reserved, out IBindCtx ctx);

        public static object Get(string displayName)
        {
            IRunningObjectTable rot;
            IEnumMoniker monikers;
            IBindCtx ctx;
            GetRunningObjectTable(0, out rot);
            CreateBindCtx(0, out ctx);
            rot.EnumRunning(out monikers);
            var moniker = new IMoniker[1];
            while (monikers.Next(1, moniker, IntPtr.Zero) == 0)
            {
                string name;
                moniker[0].GetDisplayName(ctx, null, out name);
                if (string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    object value;
                    rot.GetObject(moniker[0], out value);
                    return value;
                }
            }

            return null;
        }
    }
}
'@
}

function Invoke-WithRetry {
    # VS がビジー中は COM 呼び出しが RPC_E_CALL_REJECTED 等で失敗するので再試行する
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [int]$TimeoutSeconds = 60
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        try {
            return & $Action
        }
        catch {
            if ((Get-Date) -gt $deadline) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Get-VsDte {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [string]$DteVersion = '18.0',
        [int]$TimeoutSeconds = 180
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $dte = [VsE2E.Rot]::Get("!VisualStudio.DTE.$($DteVersion):$ProcessId")
        if ($null -ne $dte) { return $dte }
        Start-Sleep -Seconds 2
    }
    throw "DTE for process $ProcessId was not registered within $TimeoutSeconds seconds."
}

function Set-VsCaret {
    # 指定ファイルを開き、行・列(どちらも 1 始まり)へカーソルを置く
    param(
        [Parameter(Mandatory)]$Dte,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Line,
        [Parameter(Mandatory)][int]$Column
    )
    Invoke-WithRetry { $null = $Dte.ItemOperations.OpenFile($Path) }
    Invoke-WithRetry {
        $doc = $Dte.ActiveDocument
        if ($doc.FullName -ne $Path) { throw "active document is $($doc.FullName)" }
        $doc.Activate()
        $doc.Selection.MoveToLineAndOffset($Line, $Column)
    }
}

function Find-TextPosition {
    # ファイル内で $Pattern が $Occurrence 回目に現れる位置(1 始まりの行・列)を返す。列は $Offset 文字ずらせる
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Pattern,
        [int]$Occurrence = 1,
        [int]$Offset = 0
    )
    $lines = Get-Content -LiteralPath $Path -Encoding utf8
    $count = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $index = $lines[$i].IndexOf($Pattern, [StringComparison]::Ordinal)
        while ($index -ge 0) {
            $count++
            if ($count -eq $Occurrence) {
                return [pscustomobject]@{ Line = $i + 1; Column = $index + 1 + $Offset }
            }
            $index = $lines[$i].IndexOf($Pattern, $index + 1, [StringComparison]::Ordinal)
        }
    }
    throw "'$Pattern' (#$Occurrence) not found in $Path"
}

Export-ModuleMember -Function Invoke-WithRetry, Get-VsDte, Set-VsCaret, Find-TextPosition
