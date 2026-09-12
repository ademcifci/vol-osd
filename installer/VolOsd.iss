; Inno Setup script for Vol OSD
; Build with:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\VolOsd.iss
; Expects a framework-dependent publish in ..\publish (see README).

#define AppName        "X3 Vol OSD"
; Passed in by build.ps1 (/DAppVersion=...); the fallback keeps a manual ISCC run working.
#ifndef AppVersion
  #define AppVersion   "1.0.0"
#endif
#define AppPublisher   "Adem Cifcioglu"
#define AppExeName     "VolOsd.exe"
#define DotNetUrl      "https://dotnet.microsoft.com/download/dotnet/8.0"

[Setup]
AppId={{8E4C1B27-95A6-4D3F-B1E8-6C2A7F0D4E93}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; Per-user install: no admin prompt, and it matches the per-user (HKCU) autostart entry
; the app writes for "Start with Windows".
PrivilegesRequired=lowest
DefaultDirName={autopf}\VolOsd
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

ArchitecturesAllowed=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\VolOsd\app.ico

OutputDir=..\dist
OutputBaseFilename=VolOsd-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

{ The app is framework-dependent, so it needs the .NET 8 Desktop Runtime. Without this check
  it would install fine and then simply fail to start, with no useful explanation. }
function IsDotNet8DesktopInstalled(): Boolean;
var
  BasePath: String;
  FindRec: TFindRec;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.WindowsDesktop.App';
  if not DirExists(BasePath) then
    Exit;

  if FindFirst(BasePath + '\8.*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

{ The exe is locked while the tray app is running, which would make an upgrade fail. }
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExeName}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if IsDotNet8DesktopInstalled() then
    Exit;

  if MsgBox('{#AppName} needs the .NET 8 Desktop Runtime, which does not appear to be installed.'#13#10#13#10 +
            'Open the download page now? (Setup will close - run it again after installing the runtime.)'#13#10#13#10 +
            'Choose No to install anyway.',
            mbConfirmation, MB_YESNO) = IDYES then
  begin
    ShellExec('open', '{#DotNetUrl}', '', '', SW_SHOW, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRunningApp();
    { Otherwise a stale "Start with Windows" entry keeps pointing at the deleted exe. }
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'VolOsd');
  end;
end;
