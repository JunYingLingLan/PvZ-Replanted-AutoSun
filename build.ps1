# PvZ Replanted Auto Sun - 免 .NET SDK 构建脚本
#
# 适用：装了 Visual Studio 2022（自带 Roslyn csc.exe）与 .NET 6/8 运行时，
#       但没有安装 .NET SDK 的机器。
#
# 前置：游戏目录里已经安装好 BepInEx 6 (IL2CPP) 并至少运行过一次游戏，
#       使 BepInEx\interop\ 下的 interop 程序集生成完毕。
#
# 用法：
#   .\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Plants vs Zombies Replanted"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [string]$OutDir = "release",

    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

Write-Host "=== PvZ Replanted Auto Sun 构建 ===" -ForegroundColor Cyan
Write-Host "  游戏目录: $GameDir"

if (-not (Test-Path $GameDir)) {
    throw "游戏目录不存在: $GameDir"
}

$coreDir    = Join-Path $GameDir "BepInEx\core"
$interopDir = Join-Path $GameDir "BepInEx\interop"

if (-not (Test-Path $coreDir))    { throw "找不到 $coreDir —— 请先安装 BepInEx 6 (IL2CPP)。" }
if (-not (Test-Path $interopDir)) { throw "找不到 $interopDir —— 请先启动一次游戏，让 BepInEx 生成 interop 程序集。" }

# ---------------------------------------------------------------- 定位 csc.exe

$csc = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $candidate = Join-Path $vsPath "MSBuild\Current\Bin\Roslyn\csc.exe"
        if (Test-Path $candidate) { $csc = $candidate }
    }
}
if (-not $csc) {
    $found = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio","${env:ProgramFiles(x86)}\Microsoft Visual Studio" `
        -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*Roslyn*' } | Select-Object -First 1
    if ($found) { $csc = $found.FullName }
}
if (-not $csc) {
    throw "找不到 Roslyn csc.exe。请安装 Visual Studio 2022，或改用 .NET SDK：dotnet build -c Release -p:GameDir=`"$GameDir`""
}
Write-Host "  编译器  : $csc"

# ------------------------------------------------- 定位 .NET 运行时程序集（当引用用）

$packsRoot = "${env:ProgramFiles}\dotnet\shared\Microsoft.NETCore.App"
if (-not (Test-Path $packsRoot)) { throw "找不到 .NET 运行时目录: $packsRoot" }

$runtimeDir = Get-ChildItem $packsRoot -Directory |
    Where-Object { $_.Name -like '6.*' } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $runtimeDir) {
    $runtimeDir = Get-ChildItem $packsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
}
Write-Host "  运行时  : $($runtimeDir.FullName)"

# 该目录混有 coreclr.dll / clrjit.dll 等原生 DLL，必须过滤出真正的托管程序集
$managed = New-Object System.Collections.Generic.List[string]
foreach ($f in Get-ChildItem $runtimeDir.FullName -Filter *.dll) {
    try {
        [void][System.Reflection.AssemblyName]::GetAssemblyName($f.FullName)
        $managed.Add($f.FullName)
    } catch { }
}
Write-Host "  托管程序集: $($managed.Count) 个"

# ---------------------------------------------------------------- 生成引用列表

$refs = New-Object System.Collections.Generic.List[string]
foreach ($m in $managed) { $refs.Add('/r:"' + $m + '"') }

foreach ($d in 'BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','0Harmony.dll','Il2CppInterop.Runtime.dll') {
    $p = Join-Path $coreDir $d
    if (-not (Test-Path $p)) { throw "缺少 BepInEx 组件: $p" }
    $refs.Add('/r:"' + $p + '"')
}

Get-ChildItem $interopDir -Filter 'UnityEngine*.dll' | ForEach-Object {
    $refs.Add('/r:"' + $_.FullName + '"')
}

foreach ($d in 'Il2Cppmscorlib.dll','Assembly-CSharp.dll','Tekly.TreeState.Runtime.dll','Tekly.Common.Runtime.dll') {
    $p = Join-Path $interopDir $d
    if (Test-Path $p) { $refs.Add('/r:"' + $p + '"') }
    else { Write-Host "  [警告] 未找到 $d（若编译报类型缺失，说明游戏版本差异）" -ForegroundColor Yellow }
}

$rspPath = Join-Path $PSScriptRoot "refs.rsp"
$refs | Set-Content $rspPath -Encoding ASCII
Write-Host "  引用总数: $($refs.Count)  ->  $rspPath"

# ---------------------------------------------------------------- 编译

$srcDir = Join-Path $PSScriptRoot "src"
$sources = Get-ChildItem $srcDir -Filter *.cs | ForEach-Object { $_.FullName }
if (-not $sources) { throw "在 $srcDir 下找不到任何 .cs 源文件。" }

$outDirPath = Join-Path $PSScriptRoot $OutDir
if (-not (Test-Path $outDirPath)) { New-Item -ItemType Directory -Path $outDirPath | Out-Null }
$outFile = Join-Path $outDirPath "PvZReplantedAutoSun.dll"

if (Test-Path $outFile) { Remove-Item $outFile -Force }

Write-Host ""
Write-Host "正在编译..." -ForegroundColor Cyan
$raw = & $csc /nologo /noconfig /nostdlib+ /t:library /langversion:latest `
    /optimize+ /define:RELEASE `
    "/out:$outFile" "@$rspPath" $sources 2>&1

$errors   = @($raw | Where-Object { $_ -match '\serror\s' })
$warnings = @($raw | Where-Object { $_ -match '\swarning\s' })

if ($errors.Count -gt 0) {
    Write-Host "编译失败，$($errors.Count) 个错误：" -ForegroundColor Red
    $errors | Select-Object -First 30 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host "编译成功。" -ForegroundColor Green
if ($warnings.Count -gt 0) {
    Write-Host "警告 $($warnings.Count) 条（前 5 条）：" -ForegroundColor Yellow
    $warnings | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" }
}

$size = (Get-Item $outFile).Length
Write-Host ""
Write-Host "产物: $outFile  ($size 字节)" -ForegroundColor Green
Write-Host "把它复制到  $GameDir\BepInEx\plugins\  即可。"
