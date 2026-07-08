# TLE_Download

`TLE_Download`는 `Sat_List.xlsx`를 읽어 `Space-Track`을 1순위, `CelesTrak`을 2순위로 사용해 TLE를 수집하고 `TLE_{YYMMDD_HHmmss}.txt` 형식의 출력 파일을 생성하는 Python 도구다.

## 파일 구성

- `Sat_List.xlsx`: 입력 파일
- `profile.xml`: 선택 입력 파일. `Space-Track` 계정 및 실행 옵션
- `TLE_{YYMMDD_HHmmss}.txt`: 실행 시각 기준으로 생성되는 출력 파일
- `logs/`: 일별 실행 로그
- `main.py`: 실행 진입점
- `src/`: 구현 코드
- `samples/`: Git에 보관하는 샘플 출력

## 권장 Python 설치

현재 PC에 CPython이 없다면 아래 예시처럼 설치한 뒤 사용한다.

```powershell
winget install --id Python.Python.3.12 --exact --scope user
```

## 의존 패키지 설치

```powershell
python -m pip install -r requirements.txt
```

## 실행 방법

1. `Sat_List.xlsx`가 프로젝트 루트에 있는지 확인한다.
2. `Space-Track`을 사용하려면 `profile.template.xml`을 `profile.xml`로 복사하고 계정 정보를 입력한다.
3. 아래 명령으로 실행한다.

```powershell
python main.py
```

`profile.xml`이 없거나 `ID`, `PW`가 placeholder 상태면 로그에 경고를 남긴 뒤 `Space-Track`은 건너뛰고 `CelesTrak`만으로 실행된다.

```powershell
Copy-Item .\profile.template.xml .\profile.xml
```

## SAT_Name 보정 fallback

- `NORAD ID`로 `Space-Track` 조회
- 실패 시 `NORAD ID`로 `CelesTrak` 조회
- 그래도 실패하면 `SAT_Name`으로 `CelesTrak NAME` 검색
- 검색 결과에서 `OBJECT_NAME`이 `SAT_Name`과 정확히 일치하는 후보가 1건일 때만 해당 `NORAD ID`로 보정

보정이 일어나면 원래 입력 `NORAD ID`와 보정된 `NORAD ID`가 로그에 남는다.

## EXE 패키징

패키징 의존성 설치:

```powershell
python -m pip install -r requirements-packaging.txt
```

빌드 실행:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build_exe.ps1
```

빌드 결과:

- `dist\TLE_Download.exe`
- `dist\README.md`
- `dist\profile.template.xml`

`exe`는 자기 자신이 있는 폴더를 기준으로 `profile.xml`, `Sat_List.xlsx`, `TLE_{YYMMDD_HHmmss}.txt`, `logs\`를 찾고 생성한다.
따라서 `dist` 폴더에서 실행하려면 최소한 `Sat_List.xlsx`를 `TLE_Download.exe`와 같은 폴더에 둬야 한다.
`Space-Track`을 쓰려면 `profile.xml`도 같은 폴더에 둔다.

`build_exe.ps1`은 빌드 전에 `build/`, `dist/`를 제거하고 새로 생성한다.
이 정책은 오래된 `profile.xml`, TLE 결과 파일, 로그가 배포 폴더에 섞이는 일을 막기 위한 것이다.

## 로그 정책

- 로그 파일은 `logs/tle_download_YYYY-MM-DD.log` 형식이다.
- 같은 날짜의 재실행 로그는 기존 파일 하단에 append 된다.
- 따라서 파일 내부 로그는 시간 오름차순으로 누적된다.

## 입력 계약

- 파일명: `Sat_List.xlsx`
- 시트명: `Sheet1`
- 필수 헤더:
  - `SAT_Name`
  - `NORAD ID`

## CSV 입력 지원

- 기본 우선순위는 `Sat_List.xlsx`가 1순위, `Sat_List.csv`가 2순위다.
- 두 파일이 모두 있으면 `python main.py`는 `Sat_List.xlsx`를 사용한다.
- CSV를 강제로 사용하려면 `python main.py --input .\Sat_List.csv`로 실행한다.
- 패키징된 실행 파일도 같은 규칙을 사용하며, `TLE_Download.exe --input .\Sat_List.csv`로 CSV를 명시할 수 있다.
- CSV 입력 파일도 `SAT_Name`, `NORAD ID` 헤더가 필요하다.
- CSV 구분자는 쉼표, 탭, 세미콜론을 자동 감지한다.

추가 열이 있어도 무시된다.

## Git 관리 기준

- 실제 계정 정보가 들어 있는 `profile.xml`은 commit하지 않는다.
- 실행 결과인 `TLE_*.txt`, `TLE_Data.txt`, `logs/`, `build/`, `dist/`는 commit하지 않는다.
- 보관할 출력 예시는 `samples/` 아래에 둔다.
