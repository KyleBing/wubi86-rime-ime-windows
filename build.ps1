# 一键构建：librime → 服务进程（WPF）→ TSF 文本服务（x64 + x86）→ 安装包
# 用法：pwsh ./build.ps1 [-Configuration Release] [-SkipInstaller] [-Register]
#   -Register  构建后直接在本机注册文本服务（需管理员），便于调试
param(
    [string]$Configuration = "Release",
    [switch]$SkipInstaller,
    [switch]$Register
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "out/app"

# 1. librime
if (-not (Test-Path (Join-Path $root "third_party/librime/x64/rime.dll"))) {
    & (Join-Path $root "scripts/fetch-librime.ps1") -Arch x64
}

# 2. 服务进程：自包含发布，用户机器无需安装 .NET
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root "src/MeifengWubi/MeifengWubi.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

# 3. TSF 文本服务：64 位与 32 位程序各加载对应位数的 DLL
foreach ($platform in @("x64", "Win32")) {
    $buildDir = Join-Path $root "out/tsf-$platform"
    cmake -S (Join-Path $root "src/TextService") -B $buildDir -A $platform
    if ($LASTEXITCODE -ne 0) { throw "cmake 配置失败（$platform）" }
    cmake --build $buildDir --config $Configuration
    if ($LASTEXITCODE -ne 0) { throw "cmake 构建失败（$platform）" }
    Get-ChildItem (Join-Path $buildDir $Configuration) -Filter "MeifengWubiTSF*.dll" | Copy-Item -Destination $out -Force
}

if ($Register) {
    # 先停掉旧服务进程，再注册两份 DLL
    Get-Process MeifengWubi -ErrorAction SilentlyContinue | Stop-Process -Force
    & "$env:SystemRoot\System32\regsvr32.exe" /s (Join-Path $out "MeifengWubiTSF64.dll")
    & "$env:SystemRoot\SysWOW64\regsvr32.exe" /s (Join-Path $out "MeifengWubiTSF32.dll")
    Start-Process (Join-Path $out "MeifengWubi.exe") -ArgumentList "--background"
    Write-Host "已注册文本服务。到 设置 › 时间和语言 › 语言 › 中文 › 键盘 中添加「玫枫五笔」"
}

# 4. 安装包（需要 Inno Setup 6）
if (-not $SkipInstaller) {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $candidate = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        if (Test-Path $candidate) { $iscc = Get-Item $candidate }
    }
    if ($iscc) {
        $version = ([xml](Get-Content (Join-Path $root "src/MeifengWubi/MeifengWubi.csproj"))).Project.PropertyGroup.Version | Select-Object -First 1
        & $iscc.FullName "/DAppVersion=$version" "/DSourceDir=$out" (Join-Path $root "installer/MeifengWubi.iss")
        if ($LASTEXITCODE -ne 0) { throw "安装包编译失败" }
    } else {
        Write-Warning "未找到 Inno Setup（ISCC.exe），跳过安装包。程序文件在 $out"
    }
}

Write-Host "完成：$out"
