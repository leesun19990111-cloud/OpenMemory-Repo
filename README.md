# OpenMemory 2.3.0

Windows에서 열린 프로그램, 탐색기 폴더, 감지 가능한 문서를 기록하고 다시 여는 한국어 WinForms 앱입니다. 창 위치·크기·최대화·최소화 상태를 추가했습니다. 배포 파일은 release/OpenMemory-Setup-2.3.0-x64.exe입니다. 사용 범위는 사용설명서.txt, 실제 검증 범위는 검증결과.md에 구분해 기록합니다.

## 배포

[OpenMemory 2.3 설치 파일](release/OpenMemory-Setup-2.3.0-x64.exe) · [사용설명서](사용설명서.txt) · [검증 결과](검증결과.md)

Windows 10/11 x64, .NET Framework 4.8 이상. 한국어 Inno Setup 6.4.3 마법사로 사용자별 설치·업데이트·제거를 지원합니다. 기본 설치는 Revit 연결을 포함합니다. Adobe 연결은 호환성 검증이 제한된 선택 기능입니다. 설치 EXE는 공인 Authenticode 서명이 없습니다. 개인 세션, 업무 문서, 시험 데이터와 서명 개인 키는 설치 파일에 포함하지 않습니다.

## 복원 방식

- Office/AutoCAD/한글/Adobe COM, Revit 추가 기능, 선택 Adobe CEP를 통해 감지된 파일 경로를 보관합니다. 연결되지 않은 앱은 제목 보관과 프로그램 실행으로 처리합니다.
- Revit 연결 DLL은 설치 경로에 두고 사용자별 .addin으로 연결합니다. 이미 실행 중인 Revit은 재시작이 필요합니다.
- Adobe CEP는 문서 목록 조회만 수행합니다. 실제 Adobe 앱들의 파일 재열기는 전체 검증하지 않았습니다. Figma 파일 링크 수집은 구현하지 않았습니다.
- 브라우저는 마지막 종료 세션 복원을 요청합니다. 저장 시점 탭 목록은 아닙니다.
- GetWindowPlacement/SetWindowPlacement로 최상위 창을 기록·복원합니다. 작업 영역 좌표를 일관되게 사용하고 모니터가 달라지면 화면 안으로 조정합니다.
- 창 연결은 앱 경로/프로필, 창 클래스, 제목, 폴더 경로를 사용합니다. 같은 실행 중 프로세스는 시작 시각과 HWND도 확인합니다. 애매한 복수 창을 임의로 연결하지 않고 적용 후 위치·크기·상태를 확인합니다.
- 창 배치가 없는 이전 v1/v2 기록도 읽습니다. 새로 저장해야 창 배치가 생깁니다. 앱 내부 탭·도킹 패널·겹침 순서는 대상이 아닙니다.
- 원본 문서를 Save/SaveAs/삭제하지 않습니다. 미저장 내용은 각 앱에서 먼저 저장해야 합니다.

## 구성

| 파일 | 역할 |
|---|---|
| src/Shared.cs | 데이터와 경로 검증, XML, 원자적 기록 |
| src/Desktop.cs / Layout.cs | 앱·폴더 수집, 창 연결·배치 복원 |
| src/Collector.cs / Adobe.cs / RevitBridge.cs | 앱별 문서 연결 |
| src/Engine.cs | 제한 시간 있는 수집, 저장·열기 |
| src/Program.cs / Startup.cs | 목록·트레이·자동 시작 |
| src/Tests.cs / IntegrationTests.cs / LayoutTests.cs | 단위·실제 UI·창 배치 검사 |
| installer/OpenMemory.iss | 한국어 설치·제거·연결 설정 |

## 빌드와 검사

build.ps1은 .NET Framework 컴파일러와 설치된 Revit API/최신 .NET SDK로 dist/OpenMemory-v2.3.0을 만듭니다. Autodesk DLL은 배포하지 않습니다. build-release.ps1은 빌드, Adobe 로컬 서명 검증, 설명서 복사, Inno Setup 6.4.3 컴파일, SHA-256 기록을 수행합니다. 공인 Windows 서명을 대신하지는 않습니다.

- OpenMemory.exe --self-test <새 폴더>
- OpenMemory.exe --layout-test <새 폴더> <Fixture.cs를 컴파일한 전용 EXE>
- OpenMemory.exe --integration <새 폴더> <Fixture.cs를 컴파일한 전용 EXE>
- tests/excel-integration.ps1
- tests/office-documents.ps1 -Kind PowerPoint
- Inno /DTestBuild로 별도 AppId 설치판을 만든 뒤 tests/installer-roundtrip.ps1 -TestBuild

통합 검사는 임시 문서와 별도 세션을 사용합니다. 자동 시작·연결 설정 검사는 현재 사용자 설정을 잠시 바꾸고 복구하므로 운영 중 반복 실행하지 마세요. Word COM 시험은 현재 PC에서 응답 대기가 발생하여 완료하지 못했습니다. 모든 앱 검증이 끝났다는 의미가 아닙니다.

## 창 배치 참고

[Microsoft WINDOWPLACEMENT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement), [SetWindowPlacement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowplacement)
