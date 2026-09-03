#ifndef AppVersion
  #define AppVersion "0.4.4"
#endif

[Setup]
AppId={{B7A4D4EC-79DF-49CD-A11E-8C3C65DB90EE}
AppName=FreeFlight Cabin Control
AppVersion={#AppVersion}
AppPublisher=FreeFlight LLC
AppPublisherURL=https://github.com/TofferAviation/FreeFlight-Cabin-Controls
AppSupportURL=https://github.com/TofferAviation/FreeFlight-Cabin-Controls/issues
AppUpdatesURL=https://github.com/TofferAviation/FreeFlight-Cabin-Controls/releases
DefaultDirName={localappdata}\Programs\FreeFlight Cabin Control
DefaultGroupName=FreeFlight Cabin Control
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=FreeFlight-Cabin-Control-v{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\FreeFlight.CabinControl.App\Assets\FreeFlight.ico
UninstallDisplayIcon={app}\FreeFlight.CabinControl.exe
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=FreeFlight Cabin Control
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FreeFlight Cabin Control"; Filename: "{app}\FreeFlight.CabinControl.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\FreeFlight Cabin Control"; Filename: "{app}\FreeFlight.CabinControl.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FreeFlight.CabinControl.exe"; Description: "Launch FreeFlight Cabin Control"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\updates"
