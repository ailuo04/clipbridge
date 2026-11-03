#ifndef AppVersion
  #define AppVersion "0.3.7"
#endif
#ifndef BuildDir
  #error BuildDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifdef TestRuntimeDownload
  #ifndef TestMode
    #error TestRuntimeDownload is only allowed with TestMode
  #endif
#endif
#define RuntimeUrl "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.14/windowsdesktop-runtime-10.0.14-win-x64.exe"
#define RuntimeName "windowsdesktop-runtime-10.0.14-win-x64.exe"
#define RuntimeSha256 "91c2795a7b1e2480caa83fce9514dc2e2d21e277d9a297311faba6040bad7330"

[Setup]
#ifdef TestMode
AppId={{E245AEEB-4F2F-4B78-89D9-D4290AF6B087}
AppName=ClipBridge Installation Test
Uninstallable=no
CreateUninstallRegKey=no
OutputBaseFilename=ClipBridge-{#AppVersion}-test-setup
#else
AppId={{B04D96BB-781E-456F-B491-0EE5F6E74344}
AppName=互传 ClipBridge
OutputBaseFilename=ClipBridge-{#AppVersion}-win-x64-Setup
#endif
AppVersion={#AppVersion}
AppPublisher=ClipBridge
DefaultDirName={code:GetDefaultInstallDir}
DefaultGroupName=ClipBridge
UsePreviousAppDir=yes
DisableDirPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
SetupIconFile=..\ClipBridge.ico
UninstallDisplayIcon={app}\ClipBridge.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他选项："

[Files]
Source: "{#BuildDir}\ClipBridge.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\ClipBridge.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\ClipBridge.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\ClipBridge.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\QRCoder.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\QRCoder-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}"; Permissions: users-modify

#ifndef TestMode
[Icons]
Name: "{autoprograms}\ClipBridge"; Filename: "{app}\ClipBridge.exe"
Name: "{autodesktop}\ClipBridge"; Filename: "{app}\ClipBridge.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ClipBridge.exe"; Description: "启动 ClipBridge"; Flags: nowait postinstall skipifsilent
#endif

[Code]
var
  RuntimeDownloadPage: TDownloadWizardPage;

function GetDefaultInstallDir(Param: String): String;
begin
  if DirExists('D:\own\app') then
    Result := AddBackslash('D:\own\app') + 'ClipBridge'
  else
    Result := ExpandConstant('{localappdata}\Programs\ClipBridge');
end;

procedure InitializeWizard;
begin
  RuntimeDownloadPage := CreateDownloadPage('准备运行环境', '正在从 Microsoft 下载并校验 .NET 10 Desktop Runtime。', nil);
end;

function DownloadRuntime: String;
begin
  Result := '';
  RuntimeDownloadPage.Clear;
  RuntimeDownloadPage.Add('{#RuntimeUrl}', '{#RuntimeName}', '{#RuntimeSha256}');
  RuntimeDownloadPage.Show;
  try
    try
      RuntimeDownloadPage.Download;
      Log('Runtime download and pinned SHA-256 verification succeeded');
    except
      if RuntimeDownloadPage.AbortedByUser then
        Result := '已取消运行环境下载。安装尚未完成，可以稍后重试。'
      else
        Result := '无法下载或校验 .NET 10 运行环境。请检查网络后重试。' + #13#10 + GetExceptionMessage;
    end;
  finally
    RuntimeDownloadPage.Hide;
  end;
end;

function DesktopRuntimeIn(const Root: String): Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := AddBackslash(Root) + 'shared\Microsoft.WindowsDesktop.App\';
  if FindFirst(Base + '10.0.*', FindRec) then
  try
    repeat
      if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
         FileExists(Base + FindRec.Name + '\System.Windows.Forms.dll') and
         (StrToIntDef(Copy(FindRec.Name, 6, Length(FindRec.Name)), -1) >= 14) then
      begin
        Result := True;
        exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function DesktopRuntimeInstalled: Boolean;
var
  Root: String;
begin
  Root := GetEnv('DOTNET_ROOT');
  if (Root <> '') and DesktopRuntimeIn(Root) then begin Result := True; exit; end;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost', 'Path', Root) and DesktopRuntimeIn(Root) then begin Result := True; exit; end;
  Result := DesktopRuntimeIn(ExpandConstant('{autopf64}\dotnet'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RuntimePath: String;
  ResultCode: Integer;
begin
  Result := '';
#ifndef TestRuntimeDownload
  if DesktopRuntimeInstalled then exit;
#endif
  Result := DownloadRuntime;
  if Result <> '' then exit;
#ifdef TestRuntimeDownload
  Log('TestMode runtime download verified; runtime execution intentionally skipped');
  exit;
#endif
  RuntimePath := ExpandConstant('{tmp}\{#RuntimeName}');
  if not ShellExec('runas', RuntimePath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := '无法启动 .NET 10 Desktop Runtime 安装。请允许管理员授权后重试。';
    exit;
  end;
  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    Result := '.NET 10 Desktop Runtime 安装失败，退出代码：' + IntToStr(ResultCode);
    exit;
  end;
  if ResultCode = 3010 then NeedsRestart := True;
  if not DesktopRuntimeInstalled then Result := '未能检测到 .NET 10 Desktop Runtime，请重启后重试。';
end;
