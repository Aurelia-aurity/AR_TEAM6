# B 작업용 카메라 표시 씬

## 실행

1. Unity Hub에서 `unity/ARInstruments`를 Unity **6000.0.25f1**으로 엽니다.
2. `Assets/Presentation/Scenes/CameraDisplayPreview.unity`를 엽니다.
3. Play를 누르면 네 가지 색상의 테스트 영상과 `Test pattern preview` 상태 문구가 표시됩니다. 카메라 장치나 촬영 권한은 사용하지 않습니다.
4. Hierarchy의 `TestVideoInput`을 선택하고 `CameraDisplayPreviewSource`의 Inspector 값을 바꿔 표시 동작을 확인합니다.
   - `Input Mode`: `Test Pattern` / `Waiting` / `Simulated Error`
   - `Frame Size`: 입력 영상 크기. 예: 640×360, 640×480, 360×640
   - `Clockwise Rotation`: 0 / 90 / 180 / 270도
   - `Vertically Mirrored`: 입력 영상의 상하 방향 보정

이 씬은 FR-002의 **영상 표시 컴포넌트 검증 단계**입니다. 실제 카메라 입력 연결, 사용자 거울 모드, 월드 좌표 투영은 후속 작업입니다. 거울 모드 Toggle은 아직 비활성입니다. 채 커서는 추가하지 않았습니다.

## 에셋 구성

- 작업 씬: `Assets/Presentation/Scenes/CameraDisplayPreview.unity`
- 재사용 프리팹: `Assets/Presentation/Prefabs/CameraDisplay.prefab`
- 씬의 `CameraDisplay`는 위 프리팹의 인스턴스입니다.
- 기존 `Assets/Shared/Scenes/Main.unity`와 빌드 씬 목록은 수정하지 않았습니다.
- 씬에는 UI 조작을 위한 EventSystem과 InputSystemUIInputModule이 있습니다. 프리팹에는 EventSystem을 포함하지 않아 다른 씬에 배치할 때 중복 생성을 피할 수 있습니다.
- 프리팹에는 `CameraDisplayView`가 연결되어 있습니다. 테스트 입력을 생성하는 `CameraDisplayPreviewSource`는 작업 씬의 별도 `TestVideoInput` 오브젝트에만 있습니다.

```text
CameraDisplay (Canvas, CanvasScaler, GraphicRaycaster, CameraDisplayView)
├─ Background
├─ Header
│  ├─ ProjectLabel
│  └─ Title
├─ PreviewFrame
│  └─ VideoViewport (AspectRatioFitter)
│     ├─ VideoBackground
│     ├─ CameraImage (RawImage)
│     ├─ OverlayRoot
│     └─ WaitingPlaceholder
└─ Footer
   ├─ StatusIndicator
   ├─ StatusText
   ├─ StatusDetail
   └─ MirrorToggle
```

## 영상 표시 동작

| 오브젝트 | 현재 동작 | 후속 작업 |
| --- | --- | --- |
| `CameraImage` | 유효한 Texture 입력 시 활성화, 회전·상하 보정 적용 | A의 기준 영상 제공자 연결 |
| `VideoViewport` | 회전 후 영상 비율을 유지하며 부모 안에 맞춤, 남는 공간은 배경 표시 | 실제 영상 메타데이터 연결 |
| `WaitingPlaceholder` | 수신 중에는 숨김, 대기·오류 시 안내 표시 | 입력 상태 제공자 연결 |
| `StatusText` / `StatusDetail` | 대기·입력 정상·오류 상태에 따라 문구·색상 갱신 | 실제 입력 상태 연결 |
| `MirrorToggle` | 꺼짐, 조작 비활성, 콜백 없음 | 영상과 투영 결과를 함께 반전하는 동작 연결 후 조작 활성화 |
| `OverlayRoot` | 빈 표시용 영역 | 개발·보정용 커서 등 월드 좌표의 투영 결과 표시 |

Canvas 기준 해상도는 1280×720이며 화면 크기에 따라 스케일을 조정합니다. 영상 영역은 Texture의 실제 크기를 사용하며 90·270도 회전 시 가로·세로 비율을 교환합니다. 대기·오류 상태에서는 16:9 안내 영역으로 돌아갑니다. UI 문구는 Unity 기본 폰트에서 표시 가능한 영문을 사용합니다.

테스트 영상의 기본 방향은 왼쪽 위 빨강, 오른쪽 위 초록, 왼쪽 아래 파랑, 오른쪽 아래 노랑입니다. 입력 상하 보정을 먼저 적용한 뒤 시계 방향으로 회전합니다. 이 보정은 입력 장치의 방향 보정이며 사용자가 선택하는 좌우 거울 모드와 별개입니다.

## A 입력과 연결할 API

실제 입력을 연결하기 전에는 작업 씬의 `TestVideoInput`을 비활성화합니다. 그렇지 않으면 테스트 입력이 매 프레임 화면을 덮어씁니다. 프리팹 자체에는 테스트 입력이 없습니다.

```csharp
// Unity 메인 스레드에서, 사용 가능한 프레임을 수신했을 때 호출합니다.
// Texture2D, RenderTexture, 준비가 끝난 WebCamTexture 등을 받을 수 있습니다.
display.PresentFrame(texture, clockwiseRotation: 90, verticallyMirrored: false);

// 입력이 중단되거나 오류가 발생하면 제공자가 상태를 전달합니다.
display.ShowWaiting();
display.ShowError("The camera input was disconnected.");
```

- `PresentFrame`은 표시 성공 여부를 반환합니다. 회전은 90도의 배수이며 음수와 360도 이상의 값도 같은 방향으로 정규화합니다. 지원하지 않는 회전값은 오류 화면으로 전환합니다.
- 영상 크기·방향이 바뀌면 새 정보로 `PresentFrame`을 다시 호출합니다. 화면 크기 변경은 표시 컴포넌트가 반영합니다.
- null 입력, 입력 대기, 오류, 컴포넌트 비활성화 시 이전 Texture 참조와 영상 표시를 제거합니다. 새 유효 프레임이 들어오면 정상 표시로 복구합니다.
- Texture 생성·촬영·종료·해제는 제공자가 담당합니다. `CameraDisplayView`는 외부 Texture를 해제하지 않습니다.
- 카메라 연결 해제·프레임 지연 감지는 입력 제공자 책임입니다. 표시 컴포넌트는 Texture가 남아 있다는 이유만으로 연결 상태를 추정하지 않습니다.

## 담당 경계

- A는 카메라 입력과 추적을 담당합니다. B는 기준 영상 Texture, 해상도·회전·반전 정보, 입력 상태, 카메라 보정 정보의 전달 방식을 A와 합의합니다. 현재 GameEventBus에는 영상과 카메라 보정 정보를 전달하는 계약이 없습니다.
- B는 영상 표시·거울 모드·월드 좌표 투영을 구현합니다. 거울 모드는 표시 결과에 적용하며 추적·타격용 월드 좌표는 변경하지 않습니다.
- 타격 판정과 사운드는 C가 담당합니다.

## 검증

- Unity Test Runner의 EditMode에서 `CameraDisplay` 테스트를 실행합니다.
- 실제 렌더링 결과의 네 모서리 색상으로 0·90·180·270도 회전과 입력 상하 보정을 검증합니다.
- 16:9·4:3·세로 영상과 회전 영상의 비율 유지·잘림 여부, 오류·입력 중단 시 이전 영상 제거와 재입력 복구를 검증합니다.
- 작업용 씬을 Play 모드로 실행하여 테스트 영상 → 오류 → 대기 → 정상 복구와 표시 컴포넌트 비활성화·재활성화를 검증합니다.
- 실제 카메라 수신, 사용자 좌우 거울 모드, 영상과 채·악기의 정합 검증은 포함하지 않습니다. FR-002 완료 여부는 해당 후속 작업까지 검증한 뒤 판단합니다.
