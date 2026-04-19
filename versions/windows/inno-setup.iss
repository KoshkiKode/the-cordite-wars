; Inno Setup 6 installer script for Cordite Wars: Six Fronts
; Version strings are kept in sync by bump-version.py.
; Requires: Inno Setup 6.x (https://jrsoftware.org/isdl.php)
; Compile: iscc versions/windows/inno-setup.iss

#define MyAppName "Cordite Wars: Six Fronts"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Cordite Wars Team"
#define MyAppURL "https://koshkikode.com"
#define MyAppExeName "CorditeWars.exe"
#define MyAppId "{{8F3A6D4E-B2C1-4E7F-9A5D-3B8C12E4F6A2}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Cordite Wars Six Fronts
DefaultGroupName=Cordite Wars Six Fronts
DisableProgramGroupPage=yes
OutputDir=..\..\dist\windows
OutputBaseFilename=CorditeWars_Setup_{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LicenseFile=EULA.txt
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Include the full Godot export tree so launch-critical runtime files
; (e.g., .pck, GodotSharp/, managed assemblies, data/assets) are always packaged.
Source: "..\..\build\windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.dbg,*.ilk,*.exp,*.lib,*.iobj,*.ipdb,*.tmp,*.log"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; Store install path for launchers / updaters
Root: HKLM; Subkey: "Software\CorditeWarsTeam\CorditeWarsSixFronts"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\CorditeWarsTeam\CorditeWarsSixFronts"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"

[Code]
{ ──────────────────────────────────────────────────────────────────
   .NET 9 Runtime detection
   Checks HKLM for the shared framework registration that the
   .NET 9 installer writes on Windows.
────────────────────────────────────────────────────────────────── }

function HasDotNet9SharedFramework(const Architecture, FrameworkName: String): Boolean;
var
  KeyPath: String;
  Names: TArrayOfString;
  i: Integer;
begin
  Result := False;
  KeyPath := 'SOFTWARE\dotnet\Setup\InstalledVersions\' + Architecture + '\sharedfx\' + FrameworkName;
  if RegGetSubkeyNames(HKLM, KeyPath, Names) then
  begin
    for i := 0 to GetArrayLength(Names) - 1 do
    begin
      // Accept any 9.x version
      if Pos('9.', Names[i]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function IsDotNet9Installed: Boolean;
begin
  Result :=
    HasDotNet9SharedFramework('x64', 'Microsoft.NETCore.App') or
    HasDotNet9SharedFramework('x64', 'Microsoft.WindowsDesktop.App') or
    HasDotNet9SharedFramework('arm64', 'Microsoft.NETCore.App') or
    HasDotNet9SharedFramework('arm64', 'Microsoft.WindowsDesktop.App');
end;

procedure InitializeWizard;
begin
  if not IsDotNet9Installed then
    MsgBox(
      '.NET 9 Runtime is required to run ' + ExpandConstant('{#MyAppName}') + '.' + #13#10 +
      'After installation, you will be prompted to download it from Microsoft.' + #13#10 +
      'Alternatively, install it from: https://dotnet.microsoft.com/download/dotnet/9.0',
      mbInformation, MB_OK
    );
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  DotNetURL: String;
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and not IsDotNet9Installed then
  begin
    DotNetURL := 'https://aka.ms/dotnet/9.0/dotnet-runtime-win-x64.exe';
    if MsgBox(
      '.NET 9 Runtime was not found on your system.' + #13#10 +
      'Would you like to download and install it now?',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', DotNetURL, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  end;
end;
