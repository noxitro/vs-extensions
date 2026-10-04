<#
.SYNOPSIS
    「完全修飾名をコピー」を実験用インスタンス(/rootSuffix Exp)の Visual Studio で動かして検証する。

.DESCRIPTION
    1. 拡張機能をビルドして Exp ハイブへデプロイする
    2. samples/Samples.slnx を Exp インスタンスで開く
    3. 各ケースでカーソルを置き、右クリックメニューの項目を DTE 経由で実行し、ステータスバーの結果を照合する

    既定では、テスト実行者のクリップボードを上書きしないよう、Exp インスタンスに
    COPYFQN_E2E_SKIP_CLIPBOARD=1 を渡してクリップボードへの書き込みを省く。
    -VerifyClipboard を付けると実際にクリップボードへ書き込ませ、その中身も照合する
    (クリップボードを使う人がいない CI 向け)。
    起動中の他の Visual Studio には触れない(自分で起動したプロセスの DTE だけを使う)。

.EXAMPLE
    pwsh -File Invoke-E2E.ps1
    pwsh -File Invoke-E2E.ps1 -SkipBuild -KeepOpen
    pwsh -File Invoke-E2E.ps1 -VerifyClipboard -DiagnosticsDirectory artifacts/e2e   # CI
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$KeepOpen,
    [int]$CaseTimeoutSeconds = 60,
    # Visual Studio の起動 (DTE の登録) を待つ秒数。真新しい環境の初回起動は遅い
    [int]$StartupTimeoutSeconds = 240,
    # クリップボードに実際に書き込ませ、その中身も照合する。手元で付けると実行中はクリップボードが上書きされる
    [switch]$VerifyClipboard,
    # 指定すると、失敗時にスクリーンショット・画面上の文字・VS のログをここに保存する
    [string]$DiagnosticsDirectory
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'VsAutomation.psm1') -Force
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$extensionRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$samples = Join-Path $extensionRoot 'samples'
$csFile = Join-Path $samples 'SampleCSharp\Widgets.cs'
$hFile = Join-Path $samples 'SampleCpp\Widget.h'
$cppFile = Join-Path $samples 'SampleCpp\Widget.cpp'

# ファイル, 探す文字列, 何回目か, 列のずらし, 期待値
$cases = @(
    @($csFile, 'class Widget', 1, 7, 'Sample.Widgets.Widget'),
    @($csFile, 'Name {', 1, 0, 'Sample.Widgets.Widget.Name'),
    @($csFile, 'Draw(int', 1, 0, 'Sample.Widgets.Widget.Draw'),
    @($csFile, 'scale)', 1, 0, 'Sample.Widgets.Widget.Draw'),
    @($csFile, 'Renderer()', 1, 0, 'Sample.Widgets.Renderer'),
    @($csFile, 'Render(this)', 1, 0, 'Sample.Widgets.Renderer.Render'),
    @($csFile, 'class Part', 1, 6, 'Sample.Widgets.Widget.Part'),
    @($csFile, 'Size;', 1, 0, 'Sample.Widgets.Widget.Part.Size'),
    @($csFile, 'Repository<T>', 1, 0, 'Sample.Widgets.Repository<T>'),
    @($csFile, 'Find(', 1, 0, 'Sample.Widgets.Repository<T>.Find'),
    @($csFile, 'Red,', 1, 0, 'Sample.Widgets.Color.Red'),

    @($hFile, 'class Widget', 1, 6, 'sample::widgets::Widget'),
    @($hFile, 'Draw(int', 1, 0, 'sample::widgets::Widget::Draw'),
    @($hFile, 'size = 0', 1, 0, 'sample::widgets::Widget::size'),
    @($hFile, 'class Part', 1, 6, 'sample::widgets::Widget::Part'),
    @($hFile, 'Attach();', 1, 0, 'sample::widgets::Widget::Part::Attach'),
    @($hFile, 'enum class Color', 1, 11, 'sample::widgets::Color'),
    @($hFile, 'class Repository', 1, 6, 'sample::widgets::Repository<T>'),
    @($hFile, 'T Find', 1, 2, 'sample::widgets::Repository<T>::Find'),
    @($hFile, 'Red,', 1, 0, 'sample::widgets::Color::Red'),
    @($hFile, 'FreeFunction();', 1, 0, 'sample::widgets::FreeFunction'),
    @($hFile, 'struct Point', 1, 7, 'sample::nested::Point'),
    @($hFile, 'int x;', 1, 4, 'sample::nested::Point::x'),

    @($cppFile, 'Widget::Draw', 1, 8, 'sample::widgets::Widget::Draw'),
    @($cppFile, 'size = scale', 1, 0, 'sample::widgets::Widget::Draw'),
    @($cppFile, 'Part::Attach', 1, 6, 'sample::widgets::Widget::Part::Attach'),
    @($cppFile, 'void FreeFunction()', 1, 5, 'sample::widgets::FreeFunction'),
    # 名前空間内の空白(関数の手前)→ 名前空間
    @($cppFile, 'void Widget::Part', 1, -4, 'sample::widgets'),
    # C++ は参照先を解決しないので、関数本体内の参照箇所は囲んでいる関数になる
    @($cppFile, 'Widget widget;', 1, 0, 'sample::widgets::FreeFunction'),
    @($cppFile, 'int main', 1, 4, 'main')
)

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $path = & $vswhere -latest -prerelease -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
    if (-not $path) { throw 'Visual Studio extension development workload was not found.' }
    return $path
}

function Find-DevEnv {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $path = & $vswhere -latest -prerelease -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -property productPath | Select-Object -First 1
    if (-not $path) { throw 'devenv.exe was not found.' }
    return $path
}

function Get-ProcessElements {
    # 指定プロセスが持つ UI 要素 (トップレベルのウィンドウ以下すべて)。他のプロセスの画面は見ない
    param([int]$ProcessId)
    $A = [System.Windows.Automation.AutomationElement]
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)
    return $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Close-FirstRunDialog {
    # 新しい Exp ハイブでは初回起動の画面 (サインイン → 配色テーマ) が出るので、既定設定のまま進める。
    # 自分で起動したプロセスに限定し、名前が完全に一致する要素だけを押す
    param([int]$ProcessId)
    $names = @(
        # サインイン画面 (アカウント未登録の環境。CI など)
        'Skip and add accounts later', 'Skip and add accounts later.', 'Not now, maybe later', 'Not now, maybe later.',
        'スキップして後でアカウントを追加する', '後で行う。',
        # 配色テーマ画面
        'Start Visual Studio', 'Visual Studio の開始'
    )
    foreach ($element in (Get-ProcessElements -ProcessId $ProcessId)) {
        if ($element.Current.Name -notin $names) { continue }
        $pattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            $pattern.Invoke()
            Write-Host "First-run screen: pressed '$($element.Current.Name)'."
            return
        }
    }
}

function Save-Diagnostics {
    # 失敗の原因 (ライセンス切れ・サインイン・想定外のダイアログなど) を後から見られるように残す
    param([System.Diagnostics.Process]$Process, [string]$Reason)
    if (-not $DiagnosticsDirectory) { return }
    New-Item -ItemType Directory -Force -Path $DiagnosticsDirectory | Out-Null
    Write-Host "Saving diagnostics to $DiagnosticsDirectory ($Reason)"
    $Reason | Out-File (Join-Path $DiagnosticsDirectory 'reason.txt') -Encoding utf8

    try {
        # ランナーのコンソールなどが前面にあると VS が写らないので、先に前面へ出す
        if ($Process -and -not $Process.HasExited) {
            Add-Type -AssemblyName Microsoft.VisualBasic
            try { [Microsoft.VisualBasic.Interaction]::AppActivate($Process.Id) } catch { }
            Start-Sleep -Seconds 1
        }
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save((Join-Path $DiagnosticsDirectory 'screenshot.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    catch { Write-Warning "Screenshot failed: $_" }

    if ($Process -and -not $Process.HasExited) {
        try {
            $lines = foreach ($element in (Get-ProcessElements -ProcessId $Process.Id)) {
                if ($element.Current.Name) { "[$($element.Current.ControlType.ProgrammaticName)] $($element.Current.Name)" }
            }
            $lines | Select-Object -First 500 | Out-File (Join-Path $DiagnosticsDirectory 'ui-elements.txt') -Encoding utf8
        }
        catch { Write-Warning "UI dump failed: $_" }
    }

    # ActivityLog.xml は /log の保存先 (このフォルダー) に直接書かれる。
    # 念のため Exp ハイブ側に残ったものも集める
    Get-ChildItem (Join-Path $env:APPDATA 'Microsoft\VisualStudio') -Directory -Filter '*Exp' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Filter 'ActivityLog.xml' -ErrorAction SilentlyContinue } |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $DiagnosticsDirectory "ActivityLog-$($_.Directory.Name).xml") -Force }
}

if (-not $SkipBuild) {
    $project = Join-Path $extensionRoot 'src\CopyFullyQualifiedName\CopyFullyQualifiedName.csproj'
    & (Find-MSBuild) $project -restore -p:Configuration=Debug -p:DeployExtension=true -v:m -nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
}

# devenv は起動時の環境変数を引き継ぐので、起動の間だけ設定して元に戻す。
# 呼び出し元のシェルに残すと、そのシェルから普通に起動した VS でもコピーがスキップされてしまう
$devenvArgs = @('/rootSuffix', 'Exp', "`"$(Join-Path $samples 'Samples.slnx')`"")
if ($DiagnosticsDirectory) {
    # /log は直後の引数をログの保存先として読むので、保存先を明示して末尾に置く
    # (先頭に /log だけを置くと /rootSuffix がファイル名と見なされ、「Invalid Command Line」で止まる)
    New-Item -ItemType Directory -Force -Path $DiagnosticsDirectory | Out-Null
    $activityLog = Join-Path (Resolve-Path $DiagnosticsDirectory) 'ActivityLog.xml'
    $devenvArgs += @('/log', "`"$activityLog`"")
}
$previousSkip = $env:COPYFQN_E2E_SKIP_CLIPBOARD
$env:COPYFQN_E2E_SKIP_CLIPBOARD = if ($VerifyClipboard) { $null } else { '1' }
try {
    $process = Start-Process -FilePath (Find-DevEnv) -ArgumentList $devenvArgs -PassThru
}
finally {
    $env:COPYFQN_E2E_SKIP_CLIPBOARD = $previousSkip
}
Write-Host "Started experimental instance (PID $($process.Id)). Clipboard check: $([bool]$VerifyClipboard)"

$failures = 0
$dte = $null
$failureReason = $null
try {
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    while (-not $dte -and (Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "devenv exited during startup (exit code $($process.ExitCode))." }
        Close-FirstRunDialog -ProcessId $process.Id
        try { $dte = Get-VsDte -ProcessId $process.Id -TimeoutSeconds 5 } catch { }
    }
    if (-not $dte) { throw "Experimental instance did not become ready within $StartupTimeoutSeconds seconds." }
    Write-Host "DTE ready: Visual Studio $(Invoke-WithRetry { $dte.Version })"

    Invoke-WithRetry -TimeoutSeconds 180 {
        if (-not $dte.Solution.IsOpen -or $dte.Solution.Projects.Count -lt 2) { throw 'solution is loading' }
    }

    $menuItem = Invoke-WithRetry -TimeoutSeconds 120 {
        $item = $dte.CommandBars.Item('Code Window').Controls |
            Where-Object { $_.Caption -in @('完全修飾名をコピー', 'Copy Fully Qualified Name') } |
            Select-Object -First 1
        if (-not $item) { throw 'menu item not found' }
        $item
    }
    Write-Host "Context menu item found: $($menuItem.Caption)"

    $prefixes = @('完全修飾名をコピーしました: ', 'Copied fully qualified name: ')
    foreach ($case in $cases) {
        $file, $pattern, $occurrence, $offset, $expected = $case
        $pos = Find-TextPosition -Path $file -Pattern $pattern -Occurrence $occurrence -Offset $offset
        $label = '{0}:{1}:{2} ({3})' -f (Split-Path $file -Leaf), $pos.Line, $pos.Column, $pattern

        # 言語サービスの初期化待ちを兼ねて、期待値になるまで(またはタイムアウトまで)再実行する
        $actual = $null
        $clipboard = $null
        $caseDeadline = (Get-Date).AddSeconds($CaseTimeoutSeconds)
        do {
            Set-VsCaret -Dte $dte -Path $file -Line $pos.Line -Column $pos.Column
            if ($VerifyClipboard) { Set-Clipboard -Value '<e2e-pending>' }
            Invoke-WithRetry { $dte.StatusBar.Text = '<e2e-pending>' }
            Invoke-WithRetry { $menuItem.Execute() }
            $statusDeadline = (Get-Date).AddSeconds(5)
            do {
                Start-Sleep -Milliseconds 200
                $status = Invoke-WithRetry { $dte.StatusBar.Text }
            } while ($status -eq '<e2e-pending>' -and (Get-Date) -lt $statusDeadline)

            $actual = $status
            foreach ($prefix in $prefixes) {
                if ($status.StartsWith($prefix)) { $actual = $status.Substring($prefix.Length) }
            }
            $ok = $actual -eq $expected
            if ($VerifyClipboard) {
                $clipboard = Get-Clipboard -Raw
                $ok = $ok -and $clipboard -eq $expected
            }
            if (-not $ok) { Start-Sleep -Seconds 2 }
        } while (-not $ok -and (Get-Date) -lt $caseDeadline)

        if ($ok) {
            Write-Host "PASS $label -> $actual" -ForegroundColor Green
        }
        else {
            $failures++
            $detail = if ($VerifyClipboard) { " (clipboard: '$clipboard')" } else { '' }
            Write-Host "FAIL $label -> expected '$expected' but got '$actual'$detail" -ForegroundColor Red
        }
    }
    if ($failures -gt 0) { $failureReason = "$failures case(s) failed" }
}
catch {
    $failureReason = $_.Exception.Message
    throw
}
finally {
    if ($failureReason) { Save-Diagnostics -Process $process -Reason $failureReason }

    if (-not $KeepOpen) {
        # 正常に終われば DTE で閉じる。DTE が取れない・途中で例外が出た・保存確認で止まった
        # ときは、起動したプロセス (自分の実験用インスタンスだけ) を止めて放置しない
        $closed = $false
        if ($dte) {
            try {
                Invoke-WithRetry -TimeoutSeconds 30 { $dte.Quit() }
                $closed = $process.WaitForExit(60000)
            }
            catch { }
        }
        if (-not $closed -and -not $process.HasExited) {
            Write-Warning "Stopping experimental instance (PID $($process.Id))."
            Stop-Process -Id $process.Id -Force
        }
    }
}

Write-Host ''
Write-Host "$($cases.Count - $failures) / $($cases.Count) passed."
exit $failures
