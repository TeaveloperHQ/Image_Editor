# Image / PDF Editor

학교에서 자주 쓰는 **이미지·PDF 작업**을 위한 간단한 Windows 데스크톱 앱입니다.
Avalonia UI(.NET 8) 기반이라 Windows에서 실행되며, 개발/테스트는 Linux·macOS에서도 가능합니다.

## 기능

| 기능 | 설명 |
| --- | --- |
| 이미지 크기 변경 | 픽셀 지정 또는 백분율로 리사이즈 (비율 유지 옵션) |
| PDF 결합 | 여러 PDF를 원하는 순서로 하나로 합치기 |
| PDF 분해 | 모든 페이지를 한 장씩 개별 파일로 분리 |
| PDF 페이지 추출 | `1-3,5,8-10` 형식으로 원하는 페이지만 추출 |
| PDF 크기 변경 | 모든 페이지를 A4/A3/A5/Letter/Legal 등으로 다시 맞춤 |

## 프로젝트 구조

```
Image_Editor/
├─ src/
│  ├─ ImageEditor.Core/   # 핵심 로직 (크로스플랫폼 라이브러리)
│  │  ├─ ImageService.cs  #   이미지 리사이즈
│  │  ├─ PdfService.cs    #   PDF 결합/분해/추출/크기변경
│  │  └─ PageRange.cs     #   "1-3,5" 페이지 범위 파서
│  └─ ImageEditor.App/    # Avalonia GUI
└─ tests/
   └─ ImageEditor.Tests/  # xUnit 단위 테스트
```

로직은 모두 `ImageEditor.Core`에 있고 UI와 분리돼 있어, GUI 없이도 테스트·재사용할 수 있습니다.

## 사용 라이브러리

- [Avalonia UI](https://avaloniaui.net/) — 크로스플랫폼 데스크톱 UI
- [PDFsharp](https://www.pdfsharp.net/) — PDF 조작 (MIT)
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 3.1.x — 이미지 처리 (오픈소스/개인 사용 무료)

## 빌드 & 실행

```bash
# 전체 빌드
dotnet build

# 앱 실행
dotnet run --project src/ImageEditor.App

# 테스트
dotnet test
```

### Windows 배포용 단일 실행 파일 만들기

```bash
dotnet publish src/ImageEditor.App -c Release -r win-x64 \
    --self-contained -p:PublishSingleFile=true
```
`bin/Release/net8.0/win-x64/publish/` 에 `.exe` 가 생성됩니다.

## 메모

- PDF의 텍스트를 **새로** 그리는 기능을 추가할 경우, Linux에는 기본 폰트 리졸버가 없어
  `GlobalFontSettings.FontResolver` 설정이 필요합니다. 현재 결합/분해/추출/크기변경은
  기존 페이지를 그대로 옮기거나 이미지처럼 다시 그리므로 폰트가 필요하지 않습니다.
