# VS Code Extension Downloader

Windows Online PC에서 Offline Windows/Linux PC용 VS Code extension VSIX bundle을 만드는 WPF desktop tool이다.

## Scope

- 사용자는 VS Code extension 검색어를 입력한다.
- 검색 결과는 VS Code Extensions Marketplace와 비슷한 compact list로 표시한다.
- 검색 결과를 `Add to Bundle`로 queue에 추가한다.
- 1차 target platform은 `win32-x64`, `linux-x64`다.
- 기본은 stable latest version과 dependency 포함이다.

## Output

```text
VSCodeExtensionBundle/
  extensions/
    *.vsix
  vscode-extensions.lock.json
  download-report.csv
  install-offline.ps1
  install-offline.sh
```

## Build

```powershell
dotnet restore .\VSCodeExtensionDownloaderWPF.csproj
dotnet build .\VSCodeExtensionDownloaderWPF.csproj -c Release
```
