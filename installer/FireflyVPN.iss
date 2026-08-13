#ifndef SourceDir
  #error SourceDir must point to a published FireflyVPN directory.
#endif
#ifndef OutputDir
  #define OutputDir "artifacts"
#endif
#ifndef BuildArch
  #define BuildArch "x64"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "流萤加速器"
#define AppPublisher "FireflyVPN"
#define AppWebsite "https://vpn.202132.xyz"
#define AppExeName "流萤加速器.exe"
#define AppId "{{9C461F44-1EA6-4BA7-97D0-C17A301042C2}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppWebsite}
AppSupportURL={#AppWebsite}
AppUpdatesURL={#AppWebsite}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile=License.zh-CN.txt
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-win-{#BuildArch}
SetupIconFile=..\v2rayN\v2rayN.Desktop\Assets\Firefly.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
#if BuildArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x86compatible
#endif

[Languages]
; Keep the installer self-contained: Inno Setup 6 does not bundle its
; community Chinese translation in every installation.
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Lets the desktop client identify this installed copy without placing a marker
; file in either installer or application directory.
Root: HKCU; Subkey: "Software\FireflyVPN\Desktop"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue

[InstallDelete]
; Clean the marker created by older FireflyVPN installer builds on upgrade.
Type: files; Name: "{app}\NotStoreConfigHere.txt"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\guiTemps"

[Code]
var
  DeletePersonalData: Boolean;

procedure DeletePersonalDataDirectories();
begin
  { Keep this explicit rather than relying on conditional [UninstallDelete]
    entries. The choice is made by InitializeUninstall, and at this point the
    application has already been shut down by the uninstaller. }
  DelTree(ExpandConstant('{localappdata}\fireflyVPN'), True, True, True);
  DelTree(ExpandConstant('{localappdata}\firefly-accelerator'), True, True, True);
end;

function InitializeUninstall(): Boolean;
var
  DataForm: TSetupForm;
  MessageLabel: TNewStaticText;
  DeleteDataCheckBox: TNewCheckBox;
  UninstallButton: TNewButton;
  CancelButton: TNewButton;
begin
  DeletePersonalData := False;
  if UninstallSilent then
  begin
    Result := True;
    Exit;
  end;

  DataForm := CreateCustomForm(ScaleX(360), ScaleY(160), True, True);
  try
    DataForm.Caption := '卸载 流萤加速器';

    MessageLabel := TNewStaticText.Create(DataForm);
    MessageLabel.Parent := DataForm;
    MessageLabel.Left := ScaleX(16);
    MessageLabel.Top := ScaleY(16);
    MessageLabel.Width := DataForm.ClientWidth - ScaleX(32);
    MessageLabel.Height := ScaleY(42);
    MessageLabel.AutoSize := False;
    MessageLabel.WordWrap := True;
    MessageLabel.Caption := '默认会保留节点、订阅和设置，方便以后重新安装。';

    DeleteDataCheckBox := TNewCheckBox.Create(DataForm);
    DeleteDataCheckBox.Parent := DataForm;
    DeleteDataCheckBox.Left := ScaleX(16);
    DeleteDataCheckBox.Top := ScaleY(68);
    DeleteDataCheckBox.Width := DataForm.ClientWidth - ScaleX(32);
    DeleteDataCheckBox.Height := ScaleY(20);
    DeleteDataCheckBox.Caption := '同时删除个人数据（节点、订阅、设置和缓存）';
    DeleteDataCheckBox.Checked := False;

    UninstallButton := TNewButton.Create(DataForm);
    UninstallButton.Parent := DataForm;
    UninstallButton.Caption := '卸载';
    UninstallButton.ModalResult := mrOk;
    UninstallButton.Width := ScaleX(88);
    UninstallButton.Height := ScaleY(26);
    UninstallButton.Left := DataForm.ClientWidth - ScaleX(192);
    UninstallButton.Top := DataForm.ClientHeight - ScaleY(42);

    CancelButton := TNewButton.Create(DataForm);
    CancelButton.Parent := DataForm;
    CancelButton.Caption := '取消';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Width := ScaleX(88);
    CancelButton.Height := ScaleY(26);
    CancelButton.Left := DataForm.ClientWidth - ScaleX(96);
    CancelButton.Top := DataForm.ClientHeight - ScaleY(42);

    Result := DataForm.ShowModal = mrOk;
    DeletePersonalData := Result and DeleteDataCheckBox.Checked;
  finally
    DataForm.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeletePersonalData then
  begin
    DeletePersonalDataDirectories();
  end;
end;
