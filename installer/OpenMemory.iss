#define AppVersion "2.3.0"
#define Payload "..\dist\OpenMemory-v2.3.0"
[Setup]
#ifdef TestBuild
AppId={{7DE58F46-61C7-4C63-BF95-33F5191C372D}
#else
AppId={{84774D91-0ED8-4D8B-A6E8-7F407C4B2C82}
#endif
AppName=OpenMemory
AppVersion={#AppVersion}
AppVerName=OpenMemory {#AppVersion}
AppPublisher=OpenMemory
DefaultDirName={localappdata}\Programs\OpenMemory
DefaultGroupName=OpenMemory
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableWelcomePage=no
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\OpenMemory.exe
#ifdef TestBuild
OutputDir=..\test-output\installer-build
OutputBaseFilename=OpenMemory-Installer-Test
#else
OutputDir=..\release
OutputBaseFilename=OpenMemory-Setup-{#AppVersion}-x64
#endif
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes
InfoBeforeFile=release-info.txt

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Types]
Name: "standard"; Description: "기본 설치 (Revit 문서 연결 포함)"
Name: "custom"; Description: "사용자 지정 설치"; Flags: iscustom

[Components]
Name: "core"; Description: "OpenMemory 프로그램"; Types: standard custom; Flags: fixed
Name: "revit"; Description: "Revit 문서 연결 (2021·2023·2024·2026·2027, 앱 재시작 필요)"; Types: standard custom
Name: "adobe"; Description: "Adobe 문서 연결 (호환성 확인이 필요한 선택 기능, 앱 재시작 필요)"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면 바로 가기 만들기"; Flags: unchecked
Name: "startup"; Description: "Windows 로그인 시 OpenMemory를 실행하고 저장된 작업 자동 열기"; Flags: unchecked

[Files]
Source: "{#Payload}\OpenMemory.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\사용설명서.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\검증결과.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\RevitBridge\2021\OpenMemory.Revit.dll"; DestDir: "{app}\RevitBridge\2021"; Flags: ignoreversion
Source: "{#Payload}\RevitBridge\2023\OpenMemory.Revit.dll"; DestDir: "{app}\RevitBridge\2023"; Flags: ignoreversion
Source: "{#Payload}\RevitBridge\2024\OpenMemory.Revit.dll"; DestDir: "{app}\RevitBridge\2024"; Flags: ignoreversion
Source: "{#Payload}\RevitBridge\2026\OpenMemory.Revit.dll"; DestDir: "{app}\RevitBridge\2026"; Flags: ignoreversion
Source: "{#Payload}\RevitBridge\2027\OpenMemory.Revit.dll"; DestDir: "{app}\RevitBridge\2027"; Flags: ignoreversion
Source: "{#Payload}\AdobeBridge\mimetype"; DestDir: "{app}\AdobeBridge"; Flags: ignoreversion
Source: "{#Payload}\AdobeBridge\index.html"; DestDir: "{app}\AdobeBridge"; Flags: ignoreversion
Source: "{#Payload}\AdobeBridge\bridge.js"; DestDir: "{app}\AdobeBridge"; Flags: ignoreversion
Source: "{#Payload}\AdobeBridge\reader.jsx"; DestDir: "{app}\AdobeBridge"; Flags: ignoreversion
Source: "{#Payload}\AdobeBridge\CSXS\manifest.xml"; DestDir: "{app}\AdobeBridge\CSXS"; Flags: ignoreversion
Source: "{#Payload}\AdobeBridge\META-INF\signatures.xml"; DestDir: "{app}\AdobeBridge\META-INF"; Flags: ignoreversion

[Icons]
Name: "{group}\OpenMemory"; Filename: "{app}\OpenMemory.exe"
Name: "{group}\사용설명서"; Filename: "{app}\사용설명서.txt"
Name: "{userdesktop}\OpenMemory"; Filename: "{app}\OpenMemory.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenMemory.exe"; Description: "OpenMemory 실행"; Flags: nowait postinstall skipifsilent

[Code]
const RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
var Years: TArrayOfString;
    AdobeFiles: TArrayOfString;

procedure InitLists();
begin
  Years := ['2021','2023','2024','2026','2027'];
  AdobeFiles := ['mimetype','index.html','bridge.js','reader.jsx','CSXS\manifest.xml','META-INF\signatures.xml'];
end;

function XmlEscape(S: String): String;
begin
  StringChangeEx(S, '&', '&amp;', True);
  StringChangeEx(S, '<', '&lt;', True);
  StringChangeEx(S, '>', '&gt;', True);
  Result := S;
end;

function StartupCommand(): String;
begin
  Result := '"' + ExpandConstant('{app}\OpenMemory.exe') + '" --startup';
end;

function InitializeSetup(): Boolean;
var Version: Cardinal;
begin
  InitLists();
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Version) and (Version >= 528040);
  if not Result then MsgBox('OpenMemory는 .NET Framework 4.8 이상이 필요합니다. Windows 업데이트 또는 Microsoft .NET Framework 설치 후 다시 실행하세요.', mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var I: Integer; Target, LibraryPath, Backup, Content, Source, AdobeRoot, Existing: String;
    Old: AnsiString;
begin
  if CurStep <> ssPostInstall then Exit;
  if WizardIsTaskSelected('startup') then begin
    if not RegWriteStringValue(HKCU, RunKey, 'OpenMemory', StartupCommand()) then
      RaiseException('자동 시작 설정을 저장하지 못했습니다.');
  end else if RegQueryStringValue(HKCU, RunKey, 'OpenMemory', Existing) and (CompareText(Existing, StartupCommand())=0) then RegDeleteValue(HKCU, RunKey, 'OpenMemory');
  if WizardIsComponentSelected('revit') then
    for I := 0 to GetArrayLength(Years)-1 do begin
      Target := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\')+Years[I]+'\OpenMemory.addin';
      LibraryPath := ExpandConstant('{app}\RevitBridge\')+Years[I]+'\OpenMemory.Revit.dll';
      Backup := ExpandConstant('{app}\connection-backups\Revit\')+Years[I]+'.addin';
      if LoadStringFromFile(Target, Old) and (Pos(XmlEscape(ExpandConstant('{app}')), UTF8Decode(Old))=0) and not FileExists(Backup) then begin
        ForceDirectories(ExtractFileDir(Backup));
        if not FileCopy(Target, Backup, False) then RaiseException('기존 Revit 연결을 백업하지 못했습니다.');
      end;
      ForceDirectories(ExtractFileDir(Target));
      Content := '<?xml version="1.0" encoding="utf-8"?><RevitAddIns><AddIn Type="Application"><Name>OpenMemory</Name><Assembly>'+XmlEscape(LibraryPath)+'</Assembly><AddInId>E508FC7D-B986-4209-A50B-57694EA14CC8</AddInId><FullClassName>OpenMemory.RevitBridge</FullClassName><VendorId>OPMM</VendorId><VendorDescription>OpenMemory local document session helper</VendorDescription></AddIn></RevitAddIns>';
      if not SaveStringToFile(Target, UTF8Encode(Content), False) then RaiseException('Revit 문서 연결을 기록하지 못했습니다.');
    end;
  if WizardIsComponentSelected('adobe') then begin
    AdobeRoot := ExpandConstant('{userappdata}\Adobe\CEP\extensions\local.openmemory.documents\');
    for I := 0 to GetArrayLength(AdobeFiles)-1 do begin
      Source := ExpandConstant('{app}\AdobeBridge\')+AdobeFiles[I];
      Target := AdobeRoot+AdobeFiles[I];
      ForceDirectories(ExtractFileDir(Target));
      if not FileCopy(Source, Target, False) then RaiseException('Adobe 문서 연결을 설치하지 못했습니다. 앱을 닫고 다시 시도하세요.');
    end;
    SaveStringToFile(ExpandConstant('{app}\installed-adobe.flag'), 'OpenMemory', False);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  InitLists(); Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var I: Integer; Target, Backup, Value, AdobeRoot: String;
    Existing, Installed: AnsiString;
begin
  if CurUninstallStep <> usUninstall then Exit;
  if RegQueryStringValue(HKCU, RunKey, 'OpenMemory', Value) and (CompareText(Value, StartupCommand())=0) then RegDeleteValue(HKCU, RunKey, 'OpenMemory');
  for I := 0 to GetArrayLength(Years)-1 do begin
    Target := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\')+Years[I]+'\OpenMemory.addin';
    Backup := ExpandConstant('{app}\connection-backups\Revit\')+Years[I]+'.addin';
    if LoadStringFromFile(Target, Existing) and (Pos(XmlEscape(ExpandConstant('{app}')), UTF8Decode(Existing))>0) then begin
      if FileExists(Backup) then begin
        if FileCopy(Backup, Target, False) then DeleteFile(Backup);
      end else DeleteFile(Target);
    end;
  end;
  RemoveDir(ExpandConstant('{app}\connection-backups\Revit'));
  RemoveDir(ExpandConstant('{app}\connection-backups'));
  if not FileExists(ExpandConstant('{app}\installed-adobe.flag')) then Exit;
  AdobeRoot := ExpandConstant('{userappdata}\Adobe\CEP\extensions\local.openmemory.documents\');
  for I := 0 to GetArrayLength(AdobeFiles)-1 do begin
    Target := AdobeRoot+AdobeFiles[I];
    if LoadStringFromFile(Target, Existing) and LoadStringFromFile(ExpandConstant('{app}\AdobeBridge\')+AdobeFiles[I], Installed) and (Existing=Installed) then DeleteFile(Target);
  end;
  RemoveDir(AdobeRoot+'CSXS'); RemoveDir(AdobeRoot+'META-INF'); RemoveDir(AdobeRoot);
  DeleteFile(ExpandConstant('{app}\installed-adobe.flag'));
  { Saved sessions and all original business documents are deliberately preserved. }
end;
