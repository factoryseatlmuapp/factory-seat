; Installer for Factory Seat. Build it with installer\build.ps1, which publishes the exe first and
; passes the version in.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Factory Seat"
#define AppExe "FactorySeat.exe"

[Setup]
; Never change the AppId: it's how Windows knows a new version is an upgrade of this app. (The app was
; called Endurance Career before 1.0; the same AppId lets 1.0 replace those installs.)
AppId={{6B0D4C1E-5E57-4C2B-9C61-2F8E4A7D3B19}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppComments=Factory Seat: a career mode for Le Mans Ultimate. Unofficial; not affiliated with Studio 397 or Motorsport Games.
; Installs for the current user without asking for admin rights; the first page offers all users instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
; Pre-1.0 installs live in an "Endurance Career" folder; 1.0 moves to its own.
UsePreviousAppDir=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\publish\installer
OutputBaseFilename=FactorySeat-Setup-{#AppVersion}
SetupIconFile=..\src\LmuCareer.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
InfoBeforeFile=about.txt
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; An upgrade closes the running app first.
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; What a pre-1.0 "Endurance Career" install left behind.
Type: filesandordirs; Name: "{autopf}\Endurance Career"
Type: files; Name: "{autoprograms}\Endurance Career.lnk"
Type: files; Name: "{autodesktop}\Endurance Career.lnk"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The app's browser cache. Career saves live elsewhere and are only removed if the player says so.
Type: filesandordirs; Name: "{localappdata}\LmuCareer\WebView2"

[Code]
const
  WebView2ClientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Download = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

{ The app runs on the Edge WebView2 Runtime, which Windows 11 and most Windows 10 PCs already have. }
function HasWebView2: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\' + WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'))
    or (RegQueryStringValue(HKLM, 'SOFTWARE\' + WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'))
    or (RegQueryStringValue(HKCU, 'Software\' + WebView2ClientKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and not HasWebView2 and not WizardSilent then
  begin
    if MsgBox('Factory Seat needs the Microsoft Edge WebView2 Runtime, which isn''t on this PC yet.' + #13#10#13#10 +
      'Open Microsoft''s download page now? It''s a small, free install.', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', WebView2Download, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Saves: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Saves := ExpandConstant('{userappdata}\LmuCareer');
  if not DirExists(Saves) then Exit;
  { A silent uninstall (an upgrade script, say) always keeps them. }
  if SuppressibleMsgBox('Also delete your careers and settings?' + #13#10#13#10 +
    'Choose No to keep them, so a reinstall picks up where you left off. They''re in:' + #13#10 + Saves,
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(Saves, True, True, True);
end;
