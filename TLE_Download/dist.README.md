# TLE_Download 실행 가이드

이 폴더는 `TLE_Download.exe` 실행용 배포 폴더다.
`Python`이나 별도 패키지를 설치하지 않아도 `exe` 실행만으로 동작한다.

## 실행 전 준비

이 폴더 안에 아래 파일이 있어야 한다.

- `TLE_Download.exe`
- `Sat_List.xlsx` 또는 `Sat_List.csv`

`profile.xml`은 선택 사항이다.
`Space-Track`을 사용하려면 `profile.template.xml`을 복사해 `profile.xml`로 이름을 바꾼 뒤 계정 정보를 채운다.
`profile.xml`이 없거나 계정 정보가 placeholder 상태면 로그에 경고를 남기고 `CelesTrak`만 사용한다.

## profile.xml 설정

`profile.xml`의 `ID`, `PW`에는 `Space-Track` 계정 정보를 입력한다.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Profile>
  <SpaceTrack>
    <ID>YOUR_SPACE_TRACK_ID</ID>
    <PW>YOUR_SPACE_TRACK_PASSWORD</PW>
  </SpaceTrack>
  <Options>
    <RequestTimeoutSeconds>30</RequestTimeoutSeconds>
    <UserAgent>TLE_Download/1.0</UserAgent>
  </Options>
</Profile>
```

## 입력 파일 규칙

- 파일명: `Sat_List.xlsx`
- 시트명: `Sheet1`
- 필수 헤더:
  - `SAT_Name`
  - `NORAD ID`

## CSV 입력 지원

- 기본 우선순위는 `Sat_List.xlsx`가 1순위, `Sat_List.csv`가 2순위다.
- 두 파일이 모두 있으면 `TLE_Download.exe`는 `Sat_List.xlsx`를 사용한다.
- CSV를 강제로 사용하려면 `TLE_Download.exe --input .\Sat_List.csv`로 실행한다.
- CSV 입력 파일도 `SAT_Name`, `NORAD ID` 헤더가 필요하다.
- CSV 구분자는 쉼표, 탭, 세미콜론을 자동 감지한다.

추가 열이 있어도 무시된다.

## 실행 방법

1. `Sat_List.xlsx`를 `TLE_Download.exe`와 같은 폴더에 둔다.
2. `Space-Track`을 사용하려면 `profile.xml`도 같은 폴더에 둔다.
3. `TLE_Download.exe`를 실행한다.

## 실행 결과

실행이 끝나면 이 폴더 기준으로 아래가 생성된다.

- `TLE_{YYMMDD_HHmmss}.txt`
- `logs\tle_download_YYYY-MM-DD.log`

## 조회 순서

1. `Space-Track`
2. `CelesTrak`
3. `SAT_Name` exact match 기반 보정 fallback

즉, 입력 `NORAD ID`가 잘못되어도 `SAT_Name`이 정확히 일치하면 일부 항목은 자동 복구될 수 있다.

## 주의사항

- `profile.xml`에는 실제 계정 정보가 들어가므로 외부 공유 시 주의한다.
- `profile.template.xml`은 예시 파일이므로 그대로는 로그인되지 않는다.
- `profile.xml`이 없어도 실행은 가능하지만, 이 경우 `Space-Track`은 사용하지 않는다.
- `Sat_List.xlsx`의 시트명이나 헤더명이 바뀌면 실행이 실패할 수 있다.
