<#
.SYNOPSIS
    リポジトリ内のすべての拡張機能をビルドし、単体テストを実行して、.vsix を artifacts/vsix/ に集める。

.EXAMPLE
    pwsh -File build.ps1
    pwsh -File build.ps1 -Configuration Debug -SkipTests
    pwsh -File build.ps1 -Extension CopyFullyQualifiedName
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    # 特定の拡張機能だけビルドする(extensions/ 直下のフォルダー名)
    [string]$Extension
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio が見つかりません(vswhere.exe がありません)。' }
$msbuild = & $vswhere -latest -prerelease -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw '「Visual Studio 拡張機能の開発」ワークロードが入った Visual Studio が見つかりません。' }

$extensionDirs = Get-ChildItem (Join-Path $root 'extensions') -Directory
if ($Extension) { $extensionDirs = $extensionDirs | Where-Object Name -eq $Extension }
if (-not $extensionDirs) { throw "拡張機能が見つかりません: $Extension" }

$outDir = Join-Path $root 'artifacts\vsix'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

foreach ($dir in $extensionDirs) {
    Write-Host "==> $($dir.Name)" -ForegroundColor Cyan

    # source.extension.vsixmanifest を持つプロジェクトが VSIX 本体
    $vsixProjects = Get-ChildItem (Join-Path $dir.FullName 'src') -Recurse -Filter 'source.extension.vsixmanifest' |
        ForEach-Object { Get-ChildItem $_.DirectoryName -Filter '*.csproj' }
    foreach ($project in $vsixProjects) {
        & $msbuild $project.FullName -restore "-p:Configuration=$Configuration" -v:m -nologo
        if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました: $($project.Name)" }

        $vsix = Get-ChildItem (Join-Path $root "artifacts\bin\$($project.BaseName)\$($Configuration.ToLowerInvariant())") -Filter '*.vsix' | Select-Object -First 1
        Copy-Item $vsix.FullName $outDir -Force
        Write-Host "    -> $(Join-Path $outDir $vsix.Name)" -ForegroundColor Green
    }

    if (-not $SkipTests) {
        $testProjects = Get-ChildItem (Join-Path $dir.FullName 'test') -Recurse -Filter '*.Tests.csproj' -ErrorAction SilentlyContinue
        foreach ($project in $testProjects) {
            dotnet test $project.FullName -c $Configuration --nologo
            if ($LASTEXITCODE -ne 0) { throw "テストに失敗しました: $($project.Name)" }
        }
    }
}

Write-Host ''
Write-Host "完了。VSIX は $outDir にあります。" -ForegroundColor Green
