# B 작업용 카메라 표시 씬

## 실행

1. Unity Hub에서 `unity/ARInstruments`를 Unity **6000.0.25f1**으로 엽니다.
2. `Assets/Presentation/Scenes/CameraDisplayPreview.unity`를 엽니다.
3. Play를 누르면 네 가지 색상의 테스트 영상과 `Test pattern preview` 상태 문구가 표시됩니다. 카메라 장치나 촬영 권한은 사용하지 않습니다.
4. 하단 `Mirror view` 체크박스 또는 라벨을 클릭하면 영상이 화면 기준으로 좌우 반전됩니다. 다시 클릭하면 원래 표시로 돌아갑니다.
5. `Cursor debug`를 켜면 분홍색 왼쪽 채 커서와 초록색 오른쪽 채 커서가 나타납니다. 기본값은 꺼짐이며, 일반 표시에서는 두 커서를 숨깁니다.
6. Hierarchy의 `TestVideoInput`을 선택하고 `CameraDisplayPreviewSource`의 Inspector 값을 바꿔 표시 동작을 확인합니다.
   - `Input Mode`: `Test Pattern` / `Waiting` / `Simulated Error`
   - `Frame Size`: 입력 영상 크기. 예: 640×360, 640×480, 360×640
   - `Clockwise Rotation`: 0 / 90 / 180 / 270도
   - `Vertically Mirrored`: 입력 영상의 상하 방향 보정
7. `TestTrackingInput`의 `MalletCursorPreviewSource`에서 `Animate`, `Left Tracked`, `Right Tracked`, 양쪽 `Position`을 변경하여 정지·이동·한쪽 추적 실패를 시험합니다.

이 씬은 FR-002의 **영상 표시·거울 모드·개발용 커서 검증 단계**입니다. 테스트용 투영 카메라로 월드 좌표를 화면에 투영합니다. 실제 카메라 영상과 보정 정보의 연결·정합 검증은 후속 작업이며, 테스트 카메라 설정은 실제 공간 보정 결과가 아닙니다.

## 에셋 구성

- 작업 씬: `Assets/Presentation/Scenes/CameraDisplayPreview.unity`
- 재사용 프리팹: `Assets/Presentation/Prefabs/CameraDisplay.prefab`
- 씬의 `CameraDisplay`는 위 프리팹의 인스턴스입니다.
- 기존 `Assets/Shared/Scenes/Main.unity`와 빌드 씬 목록은 수정하지 않았습니다.
- 씬에는 UI 조작을 위한 EventSystem과 InputSystemUIInputModule이 있습니다. 프리팹에는 EventSystem을 포함하지 않아 다른 씬에 배치할 때 중복 생성을 피할 수 있습니다.
- 프리팹에는 `CameraDisplayView`가 연결되어 있습니다. 테스트 입력을 생성하는 `CameraDisplayPreviewSource`는 작업 씬의 별도 `TestVideoInput` 오브젝트에만 있습니다.
- 프리팹의 `MalletCursorPresenter`는 기본적으로 투영 카메라가 비어 있습니다. 작업 씬에서만 렌더링을 끈 `TestProjectionCamera`와 추적 시뮬레이터 `TestTrackingInput`을 연결합니다. 실제 연동 시 두 테스트 입력을 모두 비활성화합니다.

```text
CameraDisplay (Canvas, CanvasScaler, GraphicRaycaster, CameraDisplayView, MalletCursorPresenter)
├─ Background
├─ Header
│  ├─ ProjectLabel
│  └─ Title
├─ PreviewFrame
│  └─ VideoViewport (AspectRatioFitter, RectMask2D)
│     ├─ VideoBackground
│     ├─ MirrorRoot
│     │  ├─ CameraImage (RawImage)
│     │  └─ OverlayRoot
│     │     ├─ LeftCursor
│     │     └─ RightCursor
│     └─ WaitingPlaceholder
└─ Footer
   ├─ StatusIndicator
   ├─ StatusText
   ├─ StatusDetail
   ├─ MirrorToggle
   └─ CursorDebugToggle
```

## 영상 표시 동작

| 오브젝트 | 현재 동작 | 후속 작업 |
| --- | --- | --- |
| `CameraImage` | 유효한 Texture 입력 시 활성화, 회전·상하 보정 적용 | A의 기준 영상 제공자 연결 |
| `VideoViewport` | 회전 후 영상 비율을 유지하며 부모 안에 맞춤, 남는 공간은 배경 표시 | 실제 영상 메타데이터 연결 |
| `WaitingPlaceholder` | 수신 중에는 숨김, 대기·오류 시 안내 표시 | 입력 상태 제공자 연결 |
| `StatusText` / `StatusDetail` | 대기·입력 정상·오류 상태에 따라 문구·색상 갱신 | 실제 입력 상태 연결 |
| `MirrorToggle` | 기본 꺼짐. 클릭으로 거울 모드 전환. 표시 컴포넌트 비활성화 시 조작 비활성 | 실제 영상과의 통합 검증 |
| `MirrorRoot` | 영상과 표시용 오버레이를 화면 기준으로 함께 좌우 반전. 대기·오류 상태에서는 숨김 | 실제 투영 결과 연결 |
| `OverlayRoot` | 채 커서 등 표시물을 영상과 함께 반전. 바깥쪽 VideoViewport 마스크가 영상 영역 밖 표시를 잘라냄 | 실제 보정 기준 연결 |
| `CursorDebugToggle` | 기본 꺼짐. 디버그·보정용 커서 표시만 전환 | 보정 절차 UI와 연결 가능 |

Canvas 기준 해상도는 1280×720이며 화면 크기에 따라 스케일을 조정합니다. 영상 영역은 Texture의 실제 크기를 사용하며 90·270도 회전 시 가로·세로 비율을 교환합니다. 대기·오류 상태에서는 16:9 안내 영역으로 돌아갑니다. UI 문구는 Unity 기본 폰트에서 표시 가능한 영문을 사용합니다.

테스트 영상의 기본 방향은 왼쪽 위 빨강, 오른쪽 위 초록, 왼쪽 아래 파랑, 오른쪽 아래 노랑입니다. 입력 상하 보정을 먼저 적용한 뒤 시계 방향으로 회전합니다. 이 보정은 입력 장치의 방향 보정이며 사용자가 선택하는 좌우 거울 모드와 별개입니다.

## 거울 모드와 표시 좌표

- 거울 모드는 입력의 회전·상하 보정이 끝난 결과를 화면 기준으로 좌우 반전합니다. 90·270도 회전 영상에서도 좌우 반전으로 동작합니다.
- `MirrorRoot` 안의 영상과 오버레이만 반전합니다. 제목, 상태 안내, 대기·오류 문구, Toggle은 반전하지 않습니다.
- 투영된 표시물은 `VideoViewport/MirrorRoot/OverlayRoot` 아래에 배치합니다. 이 영역에는 좌우 거울 반전을 적용하기 전의 표시 좌표를 전달합니다. 별도로 좌우 반전 좌표를 계산하면 두 번 반전됩니다.
- 실제 월드 오브젝트, 추적 위치·속도, 타격 판정 데이터와 입력 Texture는 수정하지 않습니다. 커서는 제공된 투영 카메라를 사용해 월드 위치를 표시 좌표로 변환합니다.
- 선택한 모드는 새 영상 입력, 방향 변경, 입력 대기·오류, 표시 컴포넌트 비활성화·재활성화 중에도 유지됩니다. 대기·오류 상태에서도 미리 선택할 수 있으며 다음 유효 영상에 적용됩니다. 앱을 다시 실행하면 기본 꺼짐으로 시작합니다.
- 프로그램에서 선택하려면 Unity 메인 스레드에서 `display.SetMirrorMode(true)` 또는 `false`를 호출합니다. `display.MirrorEnabled`로 현재 선택을 읽을 수 있습니다. Toggle 상태도 함께 갱신됩니다.

## 개발·보정용 채 커서

- `MalletCursorPresenter`는 `OnEnable`에서 `GameEventBus.OnMalletsTracked`와 시각이 포함된 `OnTrackingLostAt`를 구독하고 `OnDisable`에서 해제합니다. 두 입력 인자의 순서가 바뀌어도 `MalletId` 0/1을 기준으로 각각 갱신합니다.
- 커서는 기본적으로 숨겨집니다. `Cursor debug` 또는 `SetDebugCursorsEnabled(true)`로 켭니다. 껐다가 다시 켜면 새 추적 샘플을 받아야 표시됩니다. 이 설정은 타격 판정·오디오에 관여하지 않습니다.
- 투영 카메라의 `WorldToViewportPoint` 결과를 `OverlayRoot` 안의 정규화된 표시 위치로 사용합니다. 좌우 반전은 기존 `MirrorRoot`에서 한 번만 적용합니다.
- 신뢰도 0 이하·비정상 수치, 카메라 앞뒤 클리핑 범위 밖·화면 밖 좌표, 추적 실패 이벤트는 해당 커서를 숨깁니다. 한쪽 실패는 다른 쪽 커서에 영향을 주지 않습니다.
- 기본 0.5초 동안 새 샘플이 없거나 샘플 자체가 오래되면 숨깁니다. `TimestampMs`는 기존 추적기의 기준과 같은 `Time.realtimeSinceStartupAsDouble * 1000` 밀리초를 사용해야 합니다. 이전 시각의 재전송 샘플은 무시하며, 50ms 넘게 미래인 시각은 유효한 샘플로 사용하지 않습니다.
- 영상 입력 중단은 `CameraDisplayView.InputInvalidated`로 즉시 전달해 이전 샘플을 폐기합니다. 같은 프레임에 영상이 재시작되어도 이전 커서가 복원되지 않으며, 중단 전 관측이 비동기로 늦게 도착해도 버립니다. 표시 컴포넌트 비활성화, 디버그 전환, 투영 카메라 변경도 새 샘플을 요구합니다.
- 실패 발행자는 `GameEventBus.PublishTrackingLost(malletId, observationTimestampMs)`를 사용합니다. 시각은 실패를 알리는 순간이 아닌 해당 관측의 `MalletState.TimestampMs`입니다. 기존 `OnTrackingLost(int)`도 호환용으로 발행되지만 커서는 그 알림만으로 상태를 바꾸지 않습니다. 구형 실패는 무시하고, 최신 또는 같은 관측 시각의 실패는 즉시 숨깁니다.
- 시뮬레이터는 `Cursor debug`가 켜지고 테스트 영상이 정상 표시될 때만 테스트 추적 이벤트를 발행합니다. 자체 테스트 카메라의 종횡비만 영상에 맞추며, 실제 보정 카메라는 수정하지 않습니다.

실제 연동에서는 A와 합의한 **방향 보정 후 기준 영상에 대응하는 월드→영상 투영 카메라**를 연결해야 합니다. 카메라의 내부 파라미터·위치·자세·왜곡 처리와 영상 방향의 일치는 제공자와 통합 검증할 항목입니다. 임의의 테스트 카메라를 연결한 상태로 실제 채와 커서가 정합된다고 판단하지 않습니다.

```csharp
// 실제 영상·추적을 연결하기 전에 TestVideoInput과 TestTrackingInput을 비활성화합니다.
cursors.SetProjectionCamera(calibratedReferenceCamera);
cursors.SetDebugCursorsEnabled(true); // 개발·보정 화면에서만 사용
// A의 OnMalletsTracked / OnTrackingLostAt 데이터를 표시합니다.
// A는 실패 관측 시 PublishTrackingLost(id, observationTimestampMs)를 호출합니다.
```

## A 입력과 연결할 API

실제 입력을 연결하기 전에는 작업 씬의 `TestVideoInput`과 `TestTrackingInput`을 비활성화합니다. 그렇지 않으면 테스트 영상과 테스트 추적 이벤트가 실제 입력과 섞일 수 있습니다. 프리팹 자체에는 테스트 입력이 없습니다.

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
- 실제 렌더링 결과의 네 모서리 색상으로 0·90·180·270도 회전, 입력 상하 보정, 거울 모드의 16가지 조합을 검증합니다.
- 테스트용 오버레이의 화면 좌우 반전·복원, 원본 투영 좌표·월드 위치·입력 Texture 보존, 제목 위치 유지를 검증합니다. 테스트 마커는 테스트 실행 중에만 생성합니다.
- 16:9·4:3·세로 영상과 회전 영상의 비율 유지·잘림 여부, 오류·입력 중단 시 이전 영상 제거와 재입력 복구를 검증합니다.
- 작업용 씬을 Play 모드로 실행하여 Toggle 라벨의 UI 레이캐스트·클릭, 거울 모드 전환, 테스트 영상 → 오류 → 대기 → 정상 복구와 표시 컴포넌트 비활성화·재활성화 후 선택 유지를 검증합니다.
- 커서 테스트는 기본 숨김, 디버그 Toggle 클릭, ID별 독립 갱신, 투영 카메라의 위치·자세 반영, 거울 모드, 신뢰도·가림·화면 밖·비정상 수치·오래된/역순 샘플, 입력 중단·재활성화와 새 샘플 복구를 검증합니다.
- 회귀 테스트는 같은 프레임의 대기·오류→재시작, 중단 전 샘플의 지연 도착, 역순 실패 알림, 같은 시각의 실패 후 재전송, 정상 복구와 기존 실패 알림 구독자 호환을 검증합니다.
- 실제 카메라 수신, 실제 영상과 채·악기의 정합 검증은 포함하지 않습니다. FR-002 완료 여부는 해당 후속 작업까지 검증한 뒤 판단합니다.
