# B 작업용 카메라 표시 씬

## 실행

1. Unity Hub에서 `unity/ARInstruments`를 Unity **6000.0.25f1**으로 엽니다.
2. `Assets/Presentation/Scenes/CameraDisplayPreview.unity`를 엽니다.
3. Play를 누르면 영상 대기 화면, 상태 안내, 거울 모드 Toggle을 확인할 수 있습니다.

이 씬은 FR-002 구현을 시작하기 위한 **UI 구성 단계**입니다. 카메라를 연결해도 이번 단계에서는 영상이 자동으로 표시되지 않습니다. 실제 입력 연결, 거울 모드 동작, 월드 좌표 투영은 후속 작업입니다.

## 에셋 구성

- 작업 씬: `Assets/Presentation/Scenes/CameraDisplayPreview.unity`
- 재사용 프리팹: `Assets/Presentation/Prefabs/CameraDisplay.prefab`
- 씬의 `CameraDisplay`는 위 프리팹의 인스턴스입니다.
- 기존 `Assets/Shared/Scenes/Main.unity`와 빌드 씬 목록은 수정하지 않았습니다.
- 씬에는 UI 조작을 위한 EventSystem과 InputSystemUIInputModule이 있습니다. 프리팹에는 EventSystem을 포함하지 않아 다른 씬에 배치할 때 중복 생성을 피할 수 있습니다.

```text
CameraDisplay (Canvas, CanvasScaler, GraphicRaycaster)
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

## 초기 상태와 다음 연결 지점

| 오브젝트 | 초기 상태 | 후속 작업 |
| --- | --- | --- |
| `CameraImage` | Texture 없음, RawImage 컴포넌트 비활성 | A가 제공하는 기준 영상 Texture를 연결하고 컴포넌트 활성화 |
| `VideoViewport` | 16:9 비율로 부모 안에 맞춤 | 실제 영상 해상도와 회전에 따라 표시 비율 갱신 |
| `WaitingPlaceholder` | 입력 대기 문구 표시 | 영상 수신 시 숨기고 입력 중단 시 다시 표시 |
| `StatusText` / `StatusDetail` | `Waiting for camera` / `No video feed is available.` | 입력 대기·정상·오류 상태를 받아 문구와 표시 갱신 |
| `MirrorToggle` | 꺼짐, 조작 비활성, 콜백 없음 | 영상과 투영 결과를 함께 반전하는 동작 연결 후 조작 활성화 |
| `OverlayRoot` | 빈 표시용 영역 | 채 커서 등 월드 좌표의 투영 결과 표시 |

Canvas 기준 해상도는 1280×720이며 화면 크기에 따라 스케일을 조정합니다. 영상 영역은 초기 16:9 비율을 유지하며 남는 공간을 배경으로 채웁니다. UI 문구는 Unity 기본 폰트에서 표시 가능한 영문을 사용합니다.

## 담당 경계

- A는 카메라 입력과 추적을 담당합니다. B는 기준 영상 Texture, 해상도·회전·반전 정보, 입력 상태, 카메라 보정 정보의 전달 방식을 A와 합의합니다. 현재 GameEventBus에는 영상과 카메라 보정 정보를 전달하는 계약이 없습니다.
- B는 영상 표시·거울 모드·월드 좌표 투영을 구현합니다. 거울 모드는 표시 결과에 적용하며 추적·타격용 월드 좌표는 변경하지 않습니다.
- 타격 판정과 사운드는 C가 담당합니다.

## 이번 단계 확인 항목

- 씬이 재사용 프리팹을 참조하고 필수 UI 컴포넌트·EventSystem 입력 액션에 누락이 없는지 확인합니다.
- 1280×720과 1024×768에서 대기 화면, 상태 안내, Toggle 배치가 겹치거나 잘리지 않는지 확인합니다.
- 이번 단계는 실제 카메라 수신, Toggle 조작에 따른 반전, 영상과 채·악기의 정합 검증을 포함하지 않습니다. FR-002 완료 여부는 해당 후속 작업까지 검증한 뒤 판단합니다.
