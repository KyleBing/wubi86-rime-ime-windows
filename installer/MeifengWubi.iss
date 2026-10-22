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

[Languages]
; 语言文件随仓库提供：Inno Setup 默认不带简体中文，只靠 {pf}\...\Languages 里的副本
; 在 CI 上会缺文件。路径相对本 .iss 所在目录。
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "autostart"; Description: "登录 Windows 时在后台启动玫枫五笔（推荐）"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "MeifengWubiTSF*.dll"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\MeifengWubiTSF64.dll"; DestDir: "{app}"; Flags: ignoreversion regserver 64bit restartreplace uninsrestartdelete
Source: "{#SourceDir}\MeifengWubiTSF32.dll"; DestDir: "{app}"; Flags: ignoreversion regserver 32bit restartreplace uninsrestartdelete

[Icons]
Name: "{group}\玫枫五笔设置"; Filename: "{app}\MeifengWubi.exe"
Name: "{group}\卸载玫枫五笔"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MeifengWubi"; ValueData: """{app}\MeifengWubi.exe"" --background"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MeifengWubi.exe"; Description: "打开玫枫五笔设置"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM MeifengWubi.exe"; Flags: runhidden; RunOnceId: "StopServer"

[Code]
// 安装前结束旧的服务进程，否则 exe 被占用无法覆盖
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM MeifengWubi.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
