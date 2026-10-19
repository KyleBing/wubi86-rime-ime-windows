# 下载 librime 官方 Windows 预编译包，把 rime.dll 及其依赖放到 third_party/librime/<arch>
# 用法：pwsh scripts/fetch-librime.ps1 [-Tag 1.13.1] [-Arch x64]
param(
    [string]$Tag = "latest",
    [ValidateSet("x64", "x86")]
    [string]$Arch = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root "third_party/librime/$Arch"
$work = Join-Path ([IO.Path]::GetTempPath()) "meifeng-librime"

# 查 GitHub 发布，找 Windows MSVC 对应架构的 7z 包
$api = if ($Tag -eq "latest") { "https://api.github.com/repos/rime/librime/releases/latest" } else { "https://api.github.com/repos/rime/librime/releases/tags/$Tag" }
Write-Host "查询 librime 发布：$api"
$release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "meifeng-wubi-build" }
$pattern = if ($Arch -eq "x64") { "Windows-msvc-x64\.7z$" } else { "Windows-msvc-x86\.7z$" }
$asset = $release.assets | Where-Object { $_.name -match $pattern -and $_.name -notmatch "deps" } | Select-Object -First 1
if (-not $asset) { throw "在 $($release.tag_name) 中没找到匹配 $pattern 的发布包" }

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archive = Join-Path $work $asset.name
Write-Host "下载 $($asset.name)（$($release.tag_name)）"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive

# Windows 10 起自带的 tar（bsdtar）能解 7z；装了 7-Zip 时优先用它
$sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
if ($sevenZip) {
    & $sevenZip.Source x $archive "-o$work/x" -y | Out-Null
} else {
    New-Item -ItemType Directory -Force -Path "$work/x" | Out-Null
    tar -xf $archive -C "$work/x"
}

$dlls = Get-ChildItem "$work/x" -Recurse -Filter *.dll | Where-Object { $_.FullName -match "[\\/](dist[\\/])?lib[\\/]" -or $_.Name -eq "rime.dll" }
if (-not ($dlls | Where-Object Name -eq "rime.dll")) { throw "发布包里没有 rime.dll" }

New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($dll in $dlls) {
    Copy-Item $dll.FullName -Destination $dest -Force
    Write-Host "  $($dll.Name)"
}
Set-Content -Path (Join-Path $dest "VERSION.txt") -Value "$($release.tag_name) $($asset.name)"
Write-Host "librime 已放到 $dest"
