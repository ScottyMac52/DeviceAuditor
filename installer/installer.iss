; Inno Setup script for DeviceAuditor

#define AppName "DeviceAuditor"
#define AppVersion "0.0.0"
#define AppPublisher "ScottyMac52"
#define PublishDir "..\publish"

[Setup]
AppName={#AppName}
AppPublisher={#AppPublisher}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\DeviceAuditor
DefaultGroupName=DeviceAuditor
PrivilegesRequired=admin
OutputDir=..\_setup
OutputBaseFilename=DeviceAuditor-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
DisableDirPage=no
DisableProgramGroupPage=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Installer

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\DeviceAuditor"; Filename: "{app}\DeviceAuditor.exe"
Name: "{commondesktop}\DeviceAuditor"; Filename: "{app}\DeviceAuditor.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    ForceDirectories(ExpandConstant('{userappdata}\DeviceAuditor'));
end;