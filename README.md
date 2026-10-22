# 玫枫五笔 · Windows

基于 [librime](https://github.com/rime/librime) 的五笔 86 输入法，Windows 版。功能与 macOS 版（`wubi86-rime-ime-macos`）对齐：极点五笔方案、自造词与分组、删除候选、候选排序、云同步、扩展词库、皮肤编辑与分享、标点设置等。

## 结构

```
src/
  Meifeng.Core/     核心逻辑（net10.0）：配置、Rime 引擎封装、输入控制、词库、云同步、管道服务
  MeifengWubi/      服务进程（WPF）：候选窗、托盘、全局造词快捷键、设置窗口、各类弹窗
  TextService/      TSF 文本服务（C++ DLL）：只转发按键、写回上屏与编码
tests/
  Meifeng.Core.Tests/  核心逻辑单元测试（跨平台可跑）
Resources/          Rime 方案与词库、共享数据、品牌图标
installer/          Inno Setup 安装脚本
scripts/            fetch-librime.ps1：下载 librime 预编译包
```

各程序进程里加载的 TSF DLL 很薄：按键经命名管道（`MeifengWubi-<会话>-<用户>`）发给常驻的 `MeifengWubi.exe`，由它驱动 librime、显示候选窗，再把上屏文字与编码回给 DLL 写进文档。librime 只在服务进程里加载一份，部署、词库重载、云同步都不影响各程序。

## 构建

需要 Windows 10/11 x64，安装：

- .NET 10 SDK
- Visual Studio 2022（“使用 C++ 的桌面开发”）与 CMake
- Inno Setup 6（可选，打安装包）

```powershell
pwsh ./build.ps1            # librime → 服务进程 → TSF（x64 + x86）→ 安装包
pwsh ./build.ps1 -Register  # 额外在本机注册文本服务，便于调试（需管理员）
```

产物在 `out/`：`out/app` 为程序目录，`out/MeifengWubi-<版本>-setup.exe` 为安装包。

## 打包与发布

推送 `v1.0.2` 这类标签时，`.github/workflows/release.yml` 会在 Windows runner 上装好 .NET SDK 与 Inno Setup，跑 `build.ps1` 打出安装包，再发布到**本仓库**的 Releases，更新说明取自 `CHANGELOG.md` 对应版本。发版前先把 `src/MeifengWubi/MeifengWubi.csproj` 里的 `<Version>` 改好并提交；标签与版本不一致时工作流会直接失败。

附件名固定为 `MeifengWubi-Windows-setup.exe`，所以说明站只用一条不变的地址：

```
https://github.com/KyleBing/wubi86-rime-ime-windows/releases/latest/download/MeifengWubi-Windows-setup.exe
```

注意：仓库必须是 public，未登录的人才能下载 Release 附件。私有仓库的附件只有登录且有权限的账号能取。

安装后到「设置 › 时间和语言 › 语言和区域 › 中文（简体） › 键盘」添加「玫枫五笔」。

## 程序更新

「帮助 › 版本与更新」可以手动检查，也会在启动后静默检查一次（24 小时内不重复，可在卡片里关闭）。
客户端只读 Release 附件里的清单，固定地址：

```
https://github.com/KyleBing/wubi86-rime-ime-windows/releases/latest/download/update.json
```

`release.yml` 打 tag 时会生成清单，带 `sha256` 和 `size`。点「下载并安装」后先下载到
`%APPDATA%\MeifengWubi\update`，校验通过再启动 Inno 安装器
（`/VERYSILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`）。安装脚本会先结束服务进程、
重新注册两份 TSF DLL；升级需要管理员权限，被其他程序占用的 DLL 会排到注销或重启时替换。

清单和安装包都只放在本仓库的 Releases 里，不在别的服务器上托管文件。`download_url`
就是 `releases/latest/download/<固定附件名>` 这一条，客户端也只请求这个地址。

注意：仓库必须先有 Release，`releases/latest/download/...` 才会有内容。
第一个带「下载并安装」的版本发布后，老版本用户仍要手动更新一次；之后才能从应用内升级。

### 只改服务进程时

```powershell
dotnet build src/MeifengWubi
```

macOS / Linux 上也能编译检查（不能运行）：`dotnet build src/MeifengWubi -p:EnableWindowsTargeting=true`，核心测试可直接 `dotnet test tests/Meifeng.Core.Tests`。

## 与 macOS 版的差异

- 输入法框架：TSF 取代 InputMethodKit；中 / 英状态显示在任务栏输入指示器与托盘
- 快捷键默认 `Ctrl+Alt+N`（造词）、`Ctrl+Alt+1–9`（删除候选）、`Ctrl+Alt+0`（候选排序），可在「输入习惯」页修改
- 「切到 Rime 目录」指向小狼毫的 `%APPDATA%\Rime`
- 没有 macOS 特有的部分：输入源安装 / 注册（TIS）、App Group 容器、访达集成、DMG 打包

## 许可

内置组件许可见 `Resources/SharedSupport/NOTICE`。
