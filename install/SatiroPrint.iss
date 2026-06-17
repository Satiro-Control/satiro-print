[Setup]
AppId={{9A1B2C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D}
AppName=Satiro Print
AppVersion=1.3
AppPublisher=Satiro Solucoes
AppComments=Instalador Satiro Print, necessario para imprimir etiquetas em impressoras termicas.
DefaultDirName={autopf}\Satiro Print
DefaultGroupName=Satiro Print
OutputBaseFilename=SatiroPrint_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ShowLanguageDialog=no
UninstallDisplayName=Satiro Print
UninstallDisplayIcon={app}\SatiroPrint.exe
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
SetupIconFile="C:\projetos\SatiroPrint\assets\icone_programa.ico"
WizardImageFile="C:\projetos\SatiroPrint\assets\banner_lateral.bmp"
WizardSmallImageFile="C:\projetos\SatiroPrint\assets\logo_pequeno.bmp"

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "C:\projetos\SatiroPrint\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Satiro Print"; Filename: "{app}\SatiroPrint.exe"; IconFilename: "{app}\SatiroPrint.exe"
Name: "{autodesktop}\Satiro Print"; Filename: "{app}\SatiroPrint.exe"; IconFilename: "{app}\SatiroPrint.exe"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SatiroPrint"; ValueData: """{app}\SatiroPrint.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\SatiroPrint.exe"; Description: "Iniciar Satiro Print"; Flags: nowait postinstall skipifsilent runhidden

[Code]
function GetInstalledVersion(): String;
var
  Version: String;
begin
  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A1B2C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D}_is1',
    'DisplayVersion', Version) then
    Result := Version
  else
    Result := '';
end;

function CompareVersions(V1, V2: String): Integer;
var
  P1, P2, N1, N2: Integer;
begin
  P1 := StrToIntDef(Copy(V1, 1, Pos('.', V1 + '.') - 1), 0);
  P2 := StrToIntDef(Copy(V2, 1, Pos('.', V2 + '.') - 1), 0);
  if P1 < P2 then begin Result := -1; Exit; end;
  if P1 > P2 then begin Result := 1; Exit; end;
  Delete(V1, 1, Pos('.', V1 + '.'));
  Delete(V2, 1, Pos('.', V2 + '.'));
  N1 := StrToIntDef(V1, 0);
  N2 := StrToIntDef(V2, 0);
  if N1 < N2 then Result := -1
  else if N1 > N2 then Result := 1
  else Result := 0;
end;

function InitializeSetup(): Boolean;
var
  InstalledVer, NewVer: String;
  Msg: String;
  Choice: Integer;
begin
  Result := True;
  InstalledVer := GetInstalledVersion();
  NewVer := '{#SetupSetting("AppVersion")}';

  if InstalledVer <> '' then
  begin
    if CompareVersions(NewVer, InstalledVer) > 0 then
    begin
      Msg := 'O Satiro Print versão ' + InstalledVer + ' está instalado.' + #13#10 +
             'Deseja atualizar para a versão ' + NewVer + '?';
      Choice := MsgBox(Msg, mbConfirmation, MB_YESNO);
      if Choice = IDNO then
        Result := False;
    end
    else if CompareVersions(NewVer, InstalledVer) = 0 then
    begin
      Msg := 'O Satiro Print versão ' + InstalledVer + ' já está instalado.' + #13#10 +
             'Deseja reparar a instalação?';
      Choice := MsgBox(Msg, mbConfirmation, MB_YESNO);
      if Choice = IDNO then
        Result := False;
    end
    else
    begin
      Msg := 'Já existe uma versão mais recente instalada (v' + InstalledVer + ').' + #13#10 +
             'Este instalador é da versão ' + NewVer + ' e não pode prosseguir.';
      MsgBox(Msg, mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec('taskkill.exe', '/F /IM SatiroPrint.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM SatiroPrint.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;