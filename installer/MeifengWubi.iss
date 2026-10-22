; 玫枫五笔 Windows 安装包（Inno Setup 6）
; 由 build.ps1 调用：ISCC /DAppVersion=x.y.z /DSourceDir=<out/app> installer\MeifengWubi.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\app"
#endif

[Setup]
AppId={{55C8F83B-3F8E-46AF-BFED-5B224BB9381A}
AppName=玫枫五笔
AppVersion={#AppVersion}
AppPublisher=KyleBing
AppPublisherURL=https://github.com/KyleBing
DefaultDirName={autopf}\MeifengWubi
DefaultGroupName=玫枫五笔
DisableProgramGroupPage=yes
OutputDir=..\out
OutputBaseFilename=MeifengWubi-{#AppVersion}-setup
SetupIconFile=..\Resources\Brand\AppIcon.ico
UninstallDisplayIcon={app}\MeifengWubi.exe
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
; 文本服务 DLL 被各程序加载着，升级时可能要重启才能替换
CloseApplications=yes
RestartIfNeededByRun=no
WizardStyle=modern
; 安装失败时把日志留在 %TEMP%\Setup Log*.txt，方便排查“拒绝访问”这类问题
SetupLogging=yes

[Languages]
; 语言文件随仓库提供：Inno Setup 默认不带简体中文，只靠 {pf}\...\Languages 里的副本
; 在 CI 上会缺文件。路径相对本 .iss 所在目录。
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "autostart"; Description: "登录 Windows 时在后台启动玫枫五笔（推荐）"; Flags: checkedonce

[Files]
; 运行库（clrjit.dll 等）会被正在运行的服务进程映射，被占用时改为重启后替换，
; 而不是弹出“重试 / 跳过”的死胡同——跳过会导致新旧 dll 版本不一致
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "MeifengWubiTSF*.dll"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace uninsrestartdelete
Source: "{#SourceDir}\MeifengWubiTSF64.dll"; DestDir: "{app}"; Flags: ignoreversion regserver 64bit restartreplace uninsrestartdelete
Source: "{#SourceDir}\MeifengWubiTSF32.dll"; DestDir: "{app}"; Flags: ignoreversion regserver 32bit restartreplace uninsrestartdelete

[Icons]
Name: "{group}\玫枫五笔设置"; Filename: "{app}\MeifengWubi.exe"
Name: "{group}\卸载玫枫五笔"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MeifengWubi"; ValueData: """{app}\MeifengWubi.exe"" --background"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; 服务进程没能退出时文件要等重启才替换，这时不要拉起半新半旧的程序
Filename: "{app}\MeifengWubi.exe"; Description: "打开玫枫五笔设置"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: ShouldRunApp

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM MeifengWubi.exe"; Flags: runhidden; RunOnceId: "StopServer"

[Code]
const
  // 与 App.cs 里的 MutexName 保持一致：服务进程活着时互斥体一定存在
  ServerMutex = 'Local\MeifengWubi.Server';
  ServerExe = 'MeifengWubi.exe';

var
  // 服务进程始终没退出时为 True，用来跳过安装结束后的自动启动
  ServerStillRunning: Boolean;

// 服务进程是否还在运行（靠它自己创建的互斥体判断，不依赖进程枚举）
function IsServerRunning: Boolean;
begin
  Result := CheckForMutexes(ServerMutex);
end;

// 安装前结束旧的服务进程：exe 及它映射的运行库 dll（clrjit.dll 等）被锁住时
// 覆盖会报“错误代码 5 拒绝访问”。TSF 管道客户端在输入时会按需把服务重新拉起，
// 自动更新时本进程也可能还没退干净，所以最多等 10 秒，确认进程真的退出再复制文件。
procedure StopServerProcess;
var
  I, ResultCode: Integer;
  Taskkill: String;
begin
  ServerStillRunning := False;
  Taskkill := ExpandConstant('{sys}\taskkill.exe');
  for I := 1 to 20 do
  begin
    if not IsServerRunning then
      Exit;
    Log('停止服务进程 ' + ServerExe + '（第 ' + IntToStr(I) + ' 次）');
    Exec(Taskkill, '/F /IM ' + ServerExe, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
  // 最后一轮 taskkill 可能刚好生效，再确认一次
  ServerStillRunning := IsServerRunning;
  // 仍没退出：交给 restartreplace 在重启后替换，不再弹“重试 / 跳过”
  if ServerStillRunning then
    Log('服务进程未能在安装前退出，文件将在重启后替换');
end;

// 安装结束是否拉起程序：文件要等重启替换时先别启动，避免新旧文件混用
function ShouldRunApp: Boolean;
begin
  Result := not ServerStillRunning;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServerProcess;
  Result := '';
end;
