# OpenMemory for macOS — 3.0 미리보기

macOS 13 이상용 메뉴 막대 앱입니다. Apple Silicon과 Intel용 실행 파일을 하나의 Universal 앱으로 패키징합니다. Windows판과 같은 파란색 겹친 창 아이콘, 작업 목록, 저장·열기·제외 버튼을 사용합니다.

## 설치

PKG를 실행해 설치마법사를 따르거나, DMG를 열고 OpenMemory를 Applications로 옮기세요. 이후 응용 프로그램에서 OpenMemory를 실행합니다. 앱 창을 닫아도 메뉴 막대에 상주합니다.

이 미리보기는 개발용 로컬 서명이며 Apple Developer ID 서명·공증이 없습니다. macOS가 실행을 막으면 시스템 설정의 개인정보 보호 및 보안에서 해당 앱의 실행 허용 여부를 확인해야 합니다. Gatekeeper를 전체 해제하는 명령은 사용하지 마세요. 회사 관리 Mac에서는 조직 정책에 따라 실행이 제한될 수 있습니다.

## 첫 사용

1. 권한 설정을 눌러 손쉬운 사용에서 OpenMemory를 허용합니다. 열린 창의 문서 경로와 위치를 읽고 이동할 때 필요합니다.
2. Finder 접근 확인이 나오면 폴더 경로 수집을 허용합니다. 거절해도 앱 목록은 기록하며 안내를 표시합니다.
3. 각 업무 앱에서 파일을 저장하고 원하는 창 배치를 만든 뒤 현재 작업 저장을 누릅니다.
4. 저장된 파일 열기로 기록된 앱·파일·폴더·감지된 웹 주소를 열고 구분 가능한 창의 위치·크기·최소화 상태를 적용합니다.
5. 로그인 시 자동 복원을 체크하면 macOS 로그인 항목으로 등록합니다. 시스템에서 추가 승인을 요구하면 로그인 항목 설정을 확인하세요.

목록의 숫자는 실행순번입니다. 선택한 행만 제외하며 모두 제외한 뒤에도 새 저장으로 다시 수집할 수 있습니다. 초기화는 앱 기록만 백업으로 이동합니다.

## 지원 범위

- Dock에 나타나는 일반 앱의 번들 식별자와 실행 경로를 기록합니다.
- 앱이 손쉬운 사용 API에 제공한 문서 경로를 기록합니다. Office·Adobe 등 모든 버전의 내부 문서를 일괄 보장하지 않습니다.
- Finder는 별도의 읽기 전용 자동화 요청으로 열린 창의 폴더 경로를 수집합니다. Finder 탭 전체 구성을 재현하지는 않습니다.
- 브라우저/Figma는 앱이 공개한 현재 창의 URL만 기록할 수 있습니다. 전체 내부 탭 목록을 백업하지 않습니다.
- 경로를 읽을 수 없는 문서는 확인 가능한 제목을 기록하고 앱만 실행합니다. 원본 문서를 자동 저장하지 않습니다.
- 모니터 구성이 바뀌면 현재 화면 안으로 조정합니다. 전체 화면은 해당 앱이 상태 변경을 허용할 때 적용을 시도하며 macOS Spaces·Stage Manager 그룹·도킹 패널 배치는 재현하지 않습니다.
- Windows 세션 XML과 Mac JSON 기록은 서로 가져오지 않습니다. 서로 다른 OS의 파일 경로와 앱은 자동 변환하지 않습니다.
- Revit은 macOS용 네이티브 앱이 없습니다. Windows 가상환경 내 Revit은 Windows판 OpenMemory로 처리하세요.

기록은 이 Mac의 ~/Library/Application Support/OpenMemoryMac/session.json에 저장합니다. 제목·파일 경로·감지된 URL이 포함되므로 개인 기록을 다른 사람에게 배포하지 마세요. Mac 로그인 전에 파일을 열거나 NAS/VPN 연결을 대신 설정하지 않습니다.

## 검사와 빌드

Xcode Command Line Tools가 설치된 Mac에서 bash macos/build.sh를 실행합니다. Swift 단위 검사, 두 아키텍처 컴파일, Universal 바이너리 확인, 앱 실행 검사, PKG·DMG 생성을 수행합니다. GitHub Actions에서도 동일한 작업을 수행합니다.

자동 검사는 실제 사용자 Mac에서 손쉬운 사용·Finder 자동화·로그인 권한을 허용한 실사용 검증과 다릅니다. 아직 해당 권한을 사용한 Office·Adobe·Finder 문서/창 복원과 실제 재부팅은 실사용 Mac에서 확인해야 합니다.

## 제거

먼저 메뉴에서 로그인 시 자동 복원을 해제하고 OpenMemory 종료를 선택한 뒤 응용 프로그램의 OpenMemory를 휴지통으로 옮기세요. 작업 기록과 원본 업무 파일은 그대로 남습니다.

참고: [Apple 손쉬운 사용 API](https://developer.apple.com/documentation/applicationservices/axuielement_h), [로그인 항목 API](https://developer.apple.com/documentation/servicemanagement/smappservice), [Autodesk Mac 지원](https://www.autodesk.com/solutions/mac-compatible-software).
