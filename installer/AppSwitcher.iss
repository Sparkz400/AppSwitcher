#ifndef PackageRoot
  #error PackageRoot must identify the verified offline package
#endif
#ifndef ProductVersion
  #error ProductVersion is required
#endif
#ifndef ProductId
  #define ProductId "Sparkz400.AppSwitcher"
#endif
#ifndef InstanceMutex
  #define InstanceMutex "AppSwitcher_SingleInstance_SCADA"
#endif

[Setup]
AppId={#ProductId}
AppName=AppSwitcher
AppVersion={#ProductVersion}
AppPublisher=AppSwitcher contributors
DefaultDirName={localappdata}\Programs\AppSwitcher
DefaultGroupName=AppSwitcher
UninstallDisplayIcon={app}\app\AppSwitcher.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
DisableProgramGroupPage=yes
AllowNoIcons=yes
Compression=lzma2
SolidCompression=yes
AppMutex={#InstanceMutex}
CloseApplications=no
RestartApplications=no
SetupLogging=yes
#if FileExists(PackageRoot + "\LICENSE")
LicenseFile={#PackageRoot}\LICENSE
#endif
InfoBeforeFile=beta-installation.txt

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "{#PackageRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AppSwitcher"; Filename: "{app}\app\AppSwitcher.exe"; WorkingDir: "{app}\app"
Name: "{autoprograms}\AppSwitcher guide"; Filename: "{app}\README.md"

[CustomMessages]
en.HostInUse=AppSwitcher or its browser connection is still in use, or its files are not writable. Close AppSwitcher and the paired browser profiles, then retry. Setup will not close applications for you.
de.HostInUse=AppSwitcher oder seine Browser-Verbindung wird noch verwendet, oder die Dateien sind nicht beschreibbar. Schließen Sie AppSwitcher und die gekoppelten Browser-Profile und versuchen Sie es erneut. Die Installation schließt keine Anwendungen automatisch.

[Code]
function CreateFile(FileName: String; DesiredAccess, ShareMode: Cardinal; Security: NativeUInt;
  CreationDisposition, Flags: Cardinal; TemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CanReplaceFile(FileName: String): Boolean;
var
  Handle: THandle;
begin
  Result := True;
  if not FileExists(FileName) then Exit;
  Handle := CreateFile(FileName, $C0000000, 0, 0, 3, $80, 0);
  Result := Handle <> THandle(-1);
  if Result then CloseHandle(Handle);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CanReplaceFile(ExpandConstant('{app}\app\AppSwitcher.exe')) or
     not CanReplaceFile(ExpandConstant('{app}\app\browser-setup\host\AppSwitcher.BrowserHost.exe')) or
     not CanReplaceFile(ExpandConstant('{app}\host\AppSwitcher.BrowserHost.exe')) then
    Result := CustomMessage('HostInUse');
end;

function InitializeUninstall: Boolean;
var
  NeedsRestart: Boolean;
  Error: String;
begin
  NeedsRestart := False;
  Error := PrepareToInstall(NeedsRestart);
  Result := Error = '';
  if not Result then SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
end;
