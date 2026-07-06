# Python Package Downloader

Windows Online PC에서 Offline Windows/Linux PC용 Python wheel bundle을 만드는 WPF desktop tool이다.

## Scope

- 사용자는 package requirement를 직접 입력한다.
- target OS, Python version, architecture, platform tag를 선택한다.
- 앱은 bundled Python/pip를 우선 사용해 `pip download`를 실행한다.
- 1차 정책은 wheel-only download다.
- 실패 package가 있어도 나머지는 계속 진행하고 `Partial Success`로 보고한다.

## Runtime

배포본은 다음 위치에 Python runtime을 포함해야 한다.

```text
runtime/python/python.exe
```

개발 중에는 bundled runtime이 없으면 `python` command를 fallback으로 시도한다. Release package에서는 bundled runtime을 포함해야 하며, 사용자가 Online PC에 Python을 별도로 설치하지 않아도 되는 것이 목표다.

## Output

```text
output/
  packages/
    *.whl
  requirements.lock.txt
  download-report.csv
  install-offline.ps1 또는 install-offline.sh
```

## Build

```powershell
dotnet restore .\PythonPackageDownloaderWPF.csproj
dotnet build .\PythonPackageDownloaderWPF.csproj -c Release
```
