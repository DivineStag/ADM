#ifndef Payload
  #error Payload must name the self-contained publish directory
#endif
#ifndef Version
  #define Version "0.8.0-beta.8"
#endif
#ifndef Output
  #define Output "."
#endif
#ifndef Repository
  #define Repository ""
#endif
[Setup]
#ifdef TestBuild
AppId=ADM.IsolatedInstallerQualification
CreateUninstallRegKey=no
#else
AppId={{E0FA1639-463E-435D-A668-98D4382D42B2}
#endif
AppName=ADM - Aircraft Dispatch Manager
AppVersion={#Version}
AppPublisher=Aircraft Dispatch Manager
DefaultDirName={localappdata}\Programs\ADM
DefaultGroupName=Aircraft Dispatch Manager
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\src\ADM.Desktop\Assets\ADM.ico
UninstallDisplayIcon={app}\ADM.Desktop.exe
OutputDir={#Output}
OutputBaseFilename=ADM-Setup
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
#ifdef TestBuild
; Test-mode ADM deliberately has no production instance mutex. Keep qualification
; isolated from the installed application and its setup coordination objects.
SetupMutex=ADM.IsolatedInstallerQualification.Setup
#else
AppMutex=ADM.Desktop.SingleInstance
SetupMutex=ADM.Desktop.Setup
#endif
DisableWelcomePage=no
#ifdef ReleaseBuild
InfoBeforeFile=release-notice.txt
#else
InfoBeforeFile=beta-notice.txt
#endif
LicenseFile=..\LICENSE
#ifdef SignCommand
SignTool=ADMSign
SignedUninstaller=yes
#endif
#if Repository != ""
AppSupportURL=https://github.com/{#Repository}/issues
AppUpdatesURL=https://github.com/{#Repository}/releases
#endif
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifndef TestBuild
[Icons]
Name: "{userprograms}\Aircraft Dispatch Manager"; Filename: "{app}\ADM.Desktop.exe"
Name: "{userdesktop}\Aircraft Dispatch Manager"; Filename: "{app}\ADM.Desktop.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\ADM.Desktop.exe"; Description: "Open Aircraft Dispatch Manager"; Flags: nowait postinstall skipifsilent
#endif
[UninstallDelete]
Type: files; Name: "{app}\Maintenance\ADM-Setup.exe"
Type: files; Name: "{app}\Maintenance\ADM-Setup.exe.sha256"
Type: dirifempty; Name: "{app}\Maintenance"
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Source, Cached, Hash: String;
begin
  if CurStep = ssPostInstall then begin
    Source := ExpandConstant('{srcexe}');
    Cached := ExpandConstant('{app}\Maintenance\ADM-Setup.exe');
    ForceDirectories(ExpandConstant('{app}\Maintenance'));
    if CompareText(Source, Cached) <> 0 then begin
      if not FileCopy(Source, Cached, False) then begin
        Log('Could not cache setup; downloaded setup can still be used for repair.');
        Exit;
      end;
    end;
    Hash := GetSHA256OfFile(Cached);
    SaveStringToFile(Cached + '.sha256', Hash, False);
  end;
end;
