; Inno Setup script for DB Inventory Scanner.
; Built by .github/workflows/build.yml after `dotnet build ... -o out/app`:
;   iscc /DAppVersion=1.0.0 installer\DbScanner.iss
; Installs for the current user only, so no administrator rights are needed.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "DB Inventory Scanner"
#define AppExe "DbScanner.exe"
#define AppUrl "https://github.com/killssingkurisu/db-inventory-scanner"

[Setup]
AppId={{6E0C5A2B-3F1D-4C8E-9B7A-DB1A5C0FFEE1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=killssingkurisu
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
MinVersion=10.0
OutputDir=..\out
OutputBaseFilename=DbScanner-Setup-{#AppVersion}
SetupIconFile=..\src\DbScanner\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\out\app\*"; Excludes: "*.pdb,*.xml"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remembered options. Saved scans in Documents are left alone.
Type: filesandordirs; Name: "{userappdata}\DbScanner"
