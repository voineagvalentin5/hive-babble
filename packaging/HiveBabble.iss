; Hive-Babble per-user installer (no admin). Built by packaging\Build-Installer.ps1.
#ifndef Suffix
  #define Suffix ""
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
[Setup]
AppId={{60A73F87-84F6-4AEC-926F-53F1B2F3F421}
AppName=Hive-Babble
AppVersion={#AppVersion}
AppPublisher=Codex Hive Labs LLC
DefaultDirName={localappdata}\Programs\Hive-Babble
DefaultGroupName=Hive-Babble
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputBaseFilename=HiveBabble-Setup-{#AppVersion}{#Suffix}
SetupIconFile={#Root}\HiveBabble\app.ico
UninstallDisplayIcon={app}\HiveBabble.exe
LicenseFile={#Stage}\LICENSE.txt
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
DiskSpanning=no
WizardStyle=modern
WizardImageFile={#Root}\packaging\wizard\wizard-100.bmp,{#Root}\packaging\wizard\wizard-200.bmp
WizardSmallImageFile={#Root}\packaging\wizard\wizard-small-100.bmp,{#Root}\packaging\wizard\wizard-small-200.bmp
WizardImageStretch=no
WizardImageBackColor=$0C1014
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Hive-Babble"; Filename: "{app}\HiveBabble.exe"
Name: "{autodesktop}\Hive-Babble"; Filename: "{app}\HiveBabble.exe"; Tasks: desktopicon
Name: "{autoprograms}\Hive-Babble README"; Filename: "{app}\README.txt"

[Run]
Filename: "{app}\README.txt"; Description: "View the README"; Flags: postinstall shellexec skipifsilent unchecked
Filename: "{app}\HiveBabble.exe"; Description: "Launch Hive-Babble"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /f /im HiveBabble.exe"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\translator"
Type: filesandordirs; Name: "{localappdata}\Hive-Babble"
