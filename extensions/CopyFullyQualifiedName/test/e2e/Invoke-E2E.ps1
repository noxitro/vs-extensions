<#
.SYNOPSIS
    「完全修飾名をコピー」を実験用インスタンス(/rootSuffix Exp)の Visual Studio で動かして検証する。

.DESCRIPTION
    1. 拡張機能をビルドして Exp ハイブへデプロイする
    2. samples/Samples.slnx を Exp インスタンスで開く
    3. 各ケースでカーソルを置き、右クリックメニューの項目を DTE 経由で実行し、ステータスバーの結果を照合する

    テスト実行者のクリップボードを上書きしないよう、Exp インスタンスには COPYFQN_E2E_SKIP_CLIPBOARD=1 を渡す。
    起動中の他の Visual Studio には触れない(自分で起動したプロセスの DTE だけを使う)。

.EXAMPLE
    pwsh -File Invoke-E2E.ps1
    pwsh -File Invoke-E2E.ps1 -SkipBuild -KeepOpen
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$KeepOpen,
    [int]$CaseTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'VsAutomation.psm1') -Force

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

function Close-FirstRunDialog {
    # 新しい Exp ハイブでは初回起動ダイアログが出るので、既定設定のまま閉じる(自分で起動したプロセスに限定)
    param([int]$ProcessId)
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $A = [System.Windows.Automation.AutomationElement]
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $buttons = $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($button in $buttons) {
        if ($button.Current.Name -in @('Visual Studio の開始', 'Start Visual Studio')) {
            $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Write-Host 'Closed first-run dialog.'
        }
    }
}

if (-not $SkipBuild) {
    $project = Join-Path $extensionRoot 'src\CopyFullyQualifiedName\CopyFullyQualifiedName.csproj'
    & (Find-MSBuild) $project -restore -p:Configuration=Debug -p:DeployExtension=true -v:m -nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
}

$env:COPYFQN_E2E_SKIP_CLIPBOARD = '1'
$process = Start-Process -FilePath (Find-DevEnv) -ArgumentList @('/rootSuffix', 'Exp', "`"$(Join-Path $samples 'Samples.slnx')`"") -PassThru
Write-Host "Started experimental instance (PID $($process.Id))."

$dte = $null
$deadline = (Get-Date).AddSeconds(240)
while (-not $dte -and (Get-Date) -lt $deadline) {
    Close-FirstRunDialog -ProcessId $process.Id
    try { $dte = Get-VsDte -ProcessId $process.Id -TimeoutSeconds 5 } catch { }
}
if (-not $dte) { throw 'Experimental instance did not become ready.' }

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
$failures = 0
foreach ($case in $cases) {
    $file, $pattern, $occurrence, $offset, $expected = $case
    $pos = Find-TextPosition -Path $file -Pattern $pattern -Occurrence $occurrence -Offset $offset
    $label = '{0}:{1}:{2} ({3})' -f (Split-Path $file -Leaf), $pos.Line, $pos.Column, $pattern

    # 言語サービスの初期化待ちを兼ねて、期待値になるまで(またはタイムアウトまで)再実行する
    $actual = $null
    $caseDeadline = (Get-Date).AddSeconds($CaseTimeoutSeconds)
    do {
        Set-VsCaret -Dte $dte -Path $file -Line $pos.Line -Column $pos.Column
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
        if ($actual -ne $expected) { Start-Sleep -Seconds 2 }
    } while ($actual -ne $expected -and (Get-Date) -lt $caseDeadline)

    if ($actual -eq $expected) {
        Write-Host "PASS $label -> $actual" -ForegroundColor Green
    }
    else {
        $failures++
        Write-Host "FAIL $label -> expected '$expected' but got '$actual'" -ForegroundColor Red
    }
}

if (-not $KeepOpen) {
    Invoke-WithRetry { $dte.Quit() }
}

Write-Host ''
Write-Host "$($cases.Count - $failures) / $($cases.Count) passed."
exit $failures
