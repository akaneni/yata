; Yata installer (Inno Setup 6). Build with tools/build-installer.ps1.
; Per-user install, no admin rights — except installing the .NET runtime when it's missing.

#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{8F3C2A51-6D7E-4B9A-9C1D-2E5F7A8B9C0D}
AppName=Yata
AppVersion={#AppVersion}
AppVerName=Yata {#AppVersion}
AppPublisher=akaneni
AppPublisherURL=https://github.com/akaneni/yata
AppSupportURL=https://github.com/akaneni/yata/issues
AppUpdatesURL=https://github.com/akaneni/yata/releases
DefaultDirName={localappdata}\Programs\Yata
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Yata-Setup-{#AppVersion}
SetupIconFile=..\src\Yata\Assets\yata.ico
UninstallDisplayIcon={app}\Yata.exe
UninstallDisplayName=Yata
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Yata in 1-bit pixel art (tools/make-art.ps1). One file per display scale, each exactly the
; size of the image area, so nothing is ever stretched and blurred; any spare room is black.
WizardImageFile=wizard-image-100.png,wizard-image-125.png,wizard-image-150.png,wizard-image-175.png,wizard-image-200.png
WizardSmallImageFile=wizard-small-100.png,wizard-small-125.png,wizard-small-150.png,wizard-small-175.png,wizard-small-200.png
WizardImageStretch=no
WizardImageBackColor=black
WizardSmallImageBackColor=black
; Greet with Yata up front; Inno skips the Welcome page by default, leaving the art for the end.
DisableWelcomePage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Close a running Yata so the exe can be replaced on upgrade.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.AutoStart=Iniciar o Yata com o Windows
english.AutoStart=Start Yata with Windows
brazilianportuguese.RuntimeInstalling=Instalando o .NET 10 Desktop Runtime (Microsoft)...
english.RuntimeInstalling=Installing the .NET 10 Desktop Runtime (Microsoft)...
brazilianportuguese.RuntimeFailed=Não foi possível instalar o .NET 10 Desktop Runtime. Instale-o manualmente em https://dotnet.microsoft.com/download/dotnet/10.0 e rode este instalador de novo.
english.RuntimeFailed=Could not install the .NET 10 Desktop Runtime. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run this setup again.

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Yata.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Yata"; Filename: "{app}\Yata.exe"
Name: "{autodesktop}\Yata"; Filename: "{app}\Yata.exe"; Tasks: desktopicon

[Registry]
; Same value format the app's own "Start with Windows" switch writes, so the two stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Yata"; ValueData: """{app}\Yata.exe"" --autostart"; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Yata"; Flags: deletevalue; Tasks: not autostart
; Remove it on uninstall even if it was turned on later from the app's settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Yata"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Yata.exe"; Description: "{cm:LaunchProgram,Yata}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Yata.exe /F"; Flags: runhidden; RunOnceId: "StopYata"

; Note: %LOCALAPPDATA%\Yata (tasks, water log, shelf, lyrics cache) is kept on uninstall on purpose.

[Code]
function IsDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
  Root: String;
begin
  Result := False;
  Root := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  if FindFirst(Root + '10.*', FindRec) then
  try
    repeat
      if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        Result := FileExists(Root + FindRec.Name + '\PresentationFramework.dll');
    until Result or not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// Runs for both interactive and silent installs, right before files are copied.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Installer: String;
begin
  Result := '';
  if IsDesktopRuntimeInstalled then
    Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('RuntimeInstalling');
  try
    // Straight from Microsoft over HTTPS; the runtime installer is Microsoft-signed.
    DownloadTemporaryFile('{#RuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '', nil);
    Installer := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
    // ShellExec (not Exec) so Windows can show the UAC prompt the runtime installer needs.
    if not ShellExec('', Installer, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) or
       ((ResultCode <> 0) and (ResultCode <> 3010)) or not IsDesktopRuntimeInstalled then
      Result := CustomMessage('RuntimeFailed');
  except
    Result := CustomMessage('RuntimeFailed') + #13#10#13#10 + GetExceptionMessage;
  end;
end;
