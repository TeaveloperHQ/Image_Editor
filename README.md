# Teaveloper 이미지 / PDF 편집기

학교·사무에서 자주 쓰는 **이미지·PDF 작업**을 한 곳에서 처리하는 Windows 데스크톱 앱입니다.
Avalonia UI(.NET 8) 기반이라 Windows에서 실행되며, 개발/테스트는 Linux·macOS에서도 가능합니다.

📖 **사용법은 [사용 설명서(MANUAL.md)](MANUAL.md)** 를 참고하세요.

## 기능

| 기능 | 설명 |
| --- | --- |
| 이미지 크기 변경 | 픽셀 지정 또는 백분율로 리사이즈 (비율 유지 옵션) |
| 이미지 편집 | 자르기(드래그 선택)·회전/반전·텍스트/그림을 **객체로 올려 이동·크기조절 후 적용** |
| 도장 만들기 | 글자로 도장 생성 또는 스캔 직인 배경 투명화 (별도 탭, PNG로 저장해 편집 탭에서 사용) |
| PDF 결합 | 여러 PDF를 원하는 순서로 하나로 합치기 |
| PDF 분해 | 모든 페이지를 한 장씩 개별 파일로 분리 |
| PDF 페이지 추출 | `1-3,5,8-10` 형식으로 원하는 페이지만 추출 |
| PDF 크기/용량 | ① 용량 줄이기(% 슬라이더 하나로 간단하게) ② 용지 크기 변경(A4/A3/A5/Letter/Legal) |
| PDF 편집 | 페이지를 보면서 텍스트·이미지·서명을 **객체로 올려 이동·크기조절 후 적용** (페이지 넘기기 지원) |

> **객체 편집 방식:** 텍스트/이미지/도장/서명은 클릭하면 미리보기 위에 객체로 떠서,
> 드래그로 이동하고 파란 모서리 핸들로 크기를 조절할 수 있습니다. 위치가 정해지면
> **`적용(굽기)`** 으로 확정합니다. (PDF는 페이지를 넘기거나 저장하면 자동 확정)
> 선택한 객체는 **`선택 삭제`** 로 지울 수 있습니다.

### 이미지 편집 사용법

1. **이미지 편집** 탭에서 `불러오기`
2. 작업 모드 선택:
   - **자르기** — 미리보기에서 드래그로 영역 선택 후 `선택 영역 자르기`
   - **텍스트** — 내용·폰트·크기·색상을 정하고 미리보기를 클릭한 위치에 글자 삽입
   - **그림** — 얹을 이미지를 고르고 미리보기를 클릭한 위치에 합성
3. 회전(↺/↻)·좌우/상하 반전은 버튼으로 즉시 적용
4. `저장…` 으로 내보내기 (PNG/JPG 등)

#### 도장 만들기 (별도 탭)

- **글자로** — 한글/한자를 입력하면 배경 투명 도장(원형/사각, 붉은색·푸른색)으로 생성 (막도장 수준, 4자 이상은 사각 직인)
- **스캔 투명화** — 스캔한 직인 이미지의 흰 배경을 투명하게 변환 (실제 직인용 핵심 기능, 색 통일 옵션)
- 미리보기로 결과를 확인하고 `PNG로 저장` → **이미지/PDF 편집 탭의 '이미지/서명 선택'으로 불러와 찍기**

> **폰트:** 실행 중인 OS의 시스템 폰트를 그대로 사용합니다(Windows에서는 맑은 고딕 등).
> `폰트 추가…` 로 `.ttf/.otf` 파일을 직접 불러와 원하는 글씨체를 쓸 수도 있습니다.
> 단, 이미 그려진(래스터) 글자나 PDF 안의 기존 텍스트를 **수정**하는 기능은 없습니다 — 새 텍스트를 얹는 방식입니다.

## 프로젝트 구조

```
Image_Editor/
├─ src/
│  ├─ ImageEditor.Core/      # 핵심 로직 (크로스플랫폼 라이브러리)
│  │  ├─ ImageService.cs     #   이미지 리사이즈
│  │  ├─ ImageEditSession.cs #   자르기/회전/텍스트·그림 추가 (편집 세션)
│  │  ├─ FontCatalog.cs      #   시스템·사용자 폰트 관리
│  │  ├─ StampMaker.cs       #   도장 만들기(글자→PNG, 스캔→투명화)
│  │  ├─ PdfService.cs       #   PDF 결합/분해/추출/용지크기
│  │  ├─ PdfCompressor.cs    #   PDF 용량 줄이기 (이미지 재압축)
│  │  ├─ PdfRenderer.cs      #   PDF 페이지 → 이미지 렌더 (PDFium/Docnet)
│  │  ├─ PdfEditSession.cs   #   PDF에 텍스트·이미지·서명 얹기
│  │  ├─ PdfFontResolver.cs  #   PdfSharp 텍스트용 폰트 공급
│  │  └─ PageRange.cs        #   "1-3,5" 페이지 범위 파서
│  └─ ImageEditor.App/    # Avalonia GUI
└─ tests/
   └─ ImageEditor.Tests/  # xUnit 단위 테스트
```

로직은 모두 `ImageEditor.Core`에 있고 UI와 분리돼 있어, GUI 없이도 테스트·재사용할 수 있습니다.

## 사용 라이브러리

- [Avalonia UI](https://avaloniaui.net/) — 크로스플랫폼 데스크톱 UI
- [PDFsharp](https://www.pdfsharp.net/) — PDF 조작 (MIT)
- [Docnet.Core](https://github.com/GowenGit/docnet) — PDF 페이지 렌더링(PDFium 래퍼, MIT)
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
dotnet publish src/ImageEditor.App -c Release -r win-x64
```
단일 파일·self-contained 옵션은 csproj에 win-x64 조건으로 들어 있어 별도 플래그가 필요 없습니다.
`bin/Release/net8.0/win-x64/publish/` 에 아이콘이 적용된 `TeaveloperImageEditor.exe` (설치 불필요) 가 생성됩니다.

> 아이콘 소스는 [`assets/icon.svg`](assets/icon.svg) (teaveloper 브랜드 그라데이션). 수정 시:
> ```bash
> magick -background none assets/icon.svg -define icon:auto-resize=256,128,64,48,32,16 assets/app.ico
> magick -background none assets/icon.svg -resize 256x256 assets/icon-256.png
> ```

## 메모

- PDF의 텍스트를 **새로** 그리는 기능을 추가할 경우, Linux에는 기본 폰트 리졸버가 없어
  `GlobalFontSettings.FontResolver` 설정이 필요합니다. 현재 결합/분해/추출/크기변경은
  기존 페이지를 그대로 옮기거나 이미지처럼 다시 그리므로 폰트가 필요하지 않습니다.

## 라이선스

[MIT License](LICENSE) © 2026 TeaveloperHQ

사용 라이브러리는 각자의 라이선스를 따릅니다 (PDFsharp·Docnet MIT, SixLabors.ImageSharp 3.1.x는 오픈소스/개인 사용 무료).
