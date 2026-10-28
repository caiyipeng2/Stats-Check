param(
    [switch]$SkipTests,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

try {
    if (-not $SkipTests) {
        Write-Host "==> 运行离线测试" -ForegroundColor Cyan
        dotnet run --project "tests/LolStatsCheck.Tests/LolStatsCheck.Tests.csproj" -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "离线测试失败，已中止发布。"
        }
    }

    if (Test-Path $OutputDirectory) {
        try {
            Remove-Item $OutputDirectory -Recurse -Force
        }
        catch {
            throw "无法清理输出目录（请先退出 LolStatsCheck 与 LeagueAkari）：$($_.Exception.Message)"
        }
    }
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

    Write-Host "==> 发布 Windows x64 单文件 exe" -ForegroundColor Cyan
    dotnet publish "src/LolStatsCheck.App/LolStatsCheck.App.csproj" `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "发布失败。"
    }

    $exe = Join-Path $OutputDirectory "LolStatsCheck.exe"
    if (-not (Test-Path $exe)) {
        throw "未生成 LolStatsCheck.exe。"
    }

    $extra = Get-ChildItem $OutputDirectory -File | Where-Object { $_.Name -ne "LolStatsCheck.exe" }
    if ($extra) {
        Write-Warning ("输出目录包含额外文件：" + ($extra.Name -join "，"))
    }

    $fallbackSource = Join-Path $PSScriptRoot "vendor\LeagueAkari"
    $fallbackExe = Join-Path $fallbackSource "LeagueAkari.exe"
    if (Test-Path $fallbackExe) {
        Write-Host "==> 打包 LeagueAkari 兜底工具" -ForegroundColor Cyan
        $fallbackTarget = Join-Path $OutputDirectory "fallback\LeagueAkari"
        New-Item -ItemType Directory -Path $fallbackTarget -Force | Out-Null
        try {
            Copy-Item (Join-Path $fallbackSource "*") $fallbackTarget -Recurse -Force
            Write-Host ("    兜底目录：{0}" -f $fallbackTarget)
        }
        catch {
            Write-Warning ("复制兜底工具失败（请退出正在运行的 LeagueAkari 后重试）：" + $_.Exception.Message)
        }
    }
    else {
        Write-Warning "未找到 vendor\LeagueAkari\LeagueAkari.exe，发布包不包含兜底工具。"
    }

    $item = Get-Item $exe
    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash

    Write-Host "==> 发布完成" -ForegroundColor Green
    Write-Host ("    路径：{0}" -f $exe)
    Write-Host ("    大小：{0:N1} MB" -f ($item.Length / 1MB))
    Write-Host ("    SHA256：{0}" -f $hash)
}
finally {
    Pop-Location
}
