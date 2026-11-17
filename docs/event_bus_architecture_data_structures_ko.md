# 가상 타악기 프로젝트 - 핵심 데이터 버스 및 인터페이스 규약 (V1.0)

> **LLM 협업자 필독 (Instruction for LLMs):**
> 이 프로젝트는 Unity(C#) 기반의 실시간 가상 타악기 프로젝트입니다. 프로젝트는 CV 추적(Module A), AR/UI(Module B), 판정 및 오디오(Module C)의 세 부분으로 구성됩니다.
> 어떤 모듈의 개발을 지원하든 아래에 정의된 데이터 구조와 이벤트 인터페이스를 **반드시 엄격히 준수해야 합니다.** 핵심 구조체(Struct)의 필드를 임의로 변경해서는 안 됩니다. 모듈 간 모든 통신은 **반드시 `EventBus`를 통해 이루어져야 하며**, 모듈 간 강한 결합을 유발하는 직접 호출은 엄격히 금지됩니다.

## 1. 핵심 데이터 구조 (Core Data Structures)

2026.11.09 개정 명세에 따라 모듈 간 추적·타격 데이터는 **공통 Unity 월드 좌표계의 3차원 좌표(World Space, Unity Vector3)**로 통일합니다. `MalletState.Position`과 `HitEvent.HitPosition`은 미터 단위 위치, `MalletState.Velocity`는 미터/초 단위 속도입니다. 공통 원점·축 방향·스케일은 보정으로 정합니다. 화면 정규화 좌표는 B 모듈이 표시용으로 투영할 때 사용하며, 거울 모드는 영상과 투영 결과에 적용합니다. 필요에 따라 카메라를 추가할 수 있고, A 모듈은 관측 시각 정렬과 공간 보정을 거친 유효한 3차원 위치를 발행합니다. 아래 구조체 필드와 이벤트 시그니처는 유지합니다.

```csharp
using UnityEngine;

// 1.1 말렛의 실시간 상태 (Module A: CV 추적에서 매 프레임 생성)
public struct MalletState
{
    public int MalletId;         // 말렛 식별자 (0 = 왼손, 1 = 오른손)
    public Vector3 Position;     // 말렛 끝부분의 좌표 (X, Y, Z 깊이)
    public Vector3 Velocity;     // 현재 운동 벡터 (방향 및 속도)
    public float Confidence;     // 추적 신뢰도 (0.0f ~ 1.0f)
    public double TimestampMs;   // 수집 시각의 타임스탬프 (지연 시간 보정 및 속도 계산에 사용)
}

// 1.2 물리적 타격 이벤트 (Module C: 판정 로직에서 생성하며, 오디오 및 시각 효과를 실행)
public struct HitEvent
{
    public string InstrumentId;  // 타격된 악기 영역의 ID (예: "Snare_1", "Cymbal_Left")
    public Vector3 HitPosition;  // 충돌이 발생한 정확한 좌표
    public float HitForce;       // 타격 강도 (0.0f ~ 1.0f, 오디오 음량에 매핑)
    public int MalletId;         // 타격을 발생시킨 말렛의 식별자
}
```

장구의 `InstrumentId`는 현재 GLB의 `extras.zone_id`인 `janggu_head_a_inner`, `janggu_head_a_outer`, `janggu_head_b_inner`, `janggu_head_b_outer`를 사용합니다. C는 같은 ID로 `InstrumentPad.PadId`를 등록합니다. 표시 대상 매핑과 ID 변경 절차는 [장구 타격 영역 진동](janggu_hit_vibration_ko.md)에 정리되어 있습니다.

## 2. 전역 이벤트 버스 (Global Event Bus)

정적 `Action` 델리게이트를 사용하여 가볍고 지연 없는 메모리 내 이벤트 버스를 구현합니다.

```csharp
using System;

public static class GameEventBus
{
    // ==========================================
    // Module A (CV) 발행 -> Module B, C 구독
    // ==========================================
    
    /// <summary>
    /// CV 모듈이 한 프레임의 분석을 완료하고 말렛 상태를 추출할 때마다 발생합니다.
    /// C 모듈은 이를 기반으로 충돌을 감지하고, B 모듈은 커서/AR 효과를 렌더링합니다.
    /// </summary>
    public static Action<MalletState, MalletState> OnMalletsTracked;
    
    /// <summary>
    /// 카메라 추적이 끊기면 발생하며, UI가 사용자에게 안내하도록 알립니다.
    /// </summary>
    public static Action<int> OnTrackingLost; // int는 MalletId를 의미합니다.

    // 순서 검사가 필요한 구독자는 관측 시각이 포함된 이 이벤트를 사용합니다.
    public static Action<int, double> OnTrackingLostAt;

    public static void PublishTrackingLost(int malletId, double timestampMs)
    {
        OnTrackingLostAt?.Invoke(malletId, timestampMs);
        OnTrackingLost?.Invoke(malletId); // 기존 구독자 호환
    }

    // A -> calibration: 월드 좌표 변환 전의 픽셀 중심과 색상 마스크 면적입니다.
    public static Action<RawMalletSample> OnRawMalletSample;

    // ==========================================
    // Module C (Logic) 발행 -> Module B (UI/AR) 구독
    // ==========================================
    
    /// <summary>
    /// 판정 로직이 유효한 타격을 확인할 때마다 발생합니다.
    /// B 모듈은 이 이벤트를 구독하여 물결, 파티클 등의 시각 효과를 재생합니다.
    /// 오디오 모듈도 이 이벤트를 수신하여 소리를 재생합니다.
    /// </summary>
    public static Action<HitEvent> OnValidHit;
}
```

## 3. 카메라 영상·상태 계약

이 절은 실제 입력 연결의 2번 작업에서 확정한 추가 계약입니다. 기존 `MalletState`, `HitEvent` 및 추적 이벤트는 유지합니다. 구현은 `Shared/Scripts/CameraFrameData.cs`, `CameraInputStateData.cs`, `GameEventBus.cs`를 기준으로 합니다. A 발행기·B 연결 컴포넌트·씬 구성은 후속 작업입니다.

### 데이터와 식별

| 데이터 | 필드 | 의미 |
| --- | --- | --- |
| `CameraFrameData` | `string CameraId`, `Guid SessionId` | 논리 카메라 ID와 시작 시도 식별자 |
| | `Texture Texture` | A 소유의 사용 가능한 영상 Texture 참조 |
| | `int ClockwiseRotation`, `bool VerticallyMirrored` | 시계 방향 회전각(0/90/180/270), 원본 상하 반전 보정 |
| | `double ReceivedAtMs` | A가 새 프레임을 확인한 시각 |
| `CameraInputStateData` | `string CameraId`, `Guid SessionId` | 영상과 동일한 식별자 |
| | `string DeviceName` | 선택된 실제 장치 이름. 선택 전에는 빈 문자열 |
| | `CameraInputState State`, `double ChangedAtMs` | 입력 상태와 상태 변경 시각 |
| | `CameraInputError Error`, `string Description` | 기계 판독용 오류와 선택적 진단 설명 |

- `CameraId`는 Inspector에서 지정할 비어 있지 않은 논리 ID(예: `primary`)입니다. 대소문자를 구분하며, 동시에 동작하는 발행기마다 고유해야 합니다. 실제 장치 이름과 분리합니다.
- A는 실패한 시도를 포함해 시작 시도마다 `Guid.NewGuid()`로 새 `SessionId`를 만들고, 해당 시도의 모든 영상·상태에 같은 값을 사용합니다. `Guid.Empty`는 유효한 세션이 아닙니다. GUID는 순서 비교용 숫자가 아닙니다.
- 두 시각은 `Time.realtimeSinceStartupAsDouble * 1000` 기준 밀리초이며, 실제 촬영 시각이나 UTC가 아닙니다. 수신 시각으로 센서 촬영 지연을 계산하지 않습니다.
- 상하 반전은 Texture 공간에서 먼저 보정하고, 그 뒤 시계 방향으로 회전합니다. B의 사용자 거울 모드는 그와 별개이며 추적 좌표를 변경하지 않습니다.
- Texture는 변경 가능한 실시간 참조이며 프레임 이미지의 복사본이 아닙니다. B는 표시하는 동안만 참조하며 생성·촬영·정지·해제하지 않습니다.

### 상태와 발행 순서

| 상태 | 의미 | B의 후속 연결 |
| --- | --- | --- |
| `Waiting` | 새 시작 시도, 첫 사용 가능 프레임 대기 | `ShowWaiting()` |
| `Streaming` | 실제 사용 가능 프레임을 수신함 | 다음 영상 이벤트에서 `PresentFrame()` |
| `Error` | 장치 없음, 시작 실패 또는 수신 중단 | `ShowError()` |
| `Stopped` | 명시적 종료·비활성화 | `ShowWaiting()` |

`CameraInputError`는 `None`, `DeviceNotFound`, `StartFailed`, `FrameTimeout`입니다. 정상 상태는 `None`을 사용합니다. `Description`은 진단용이며 B의 동작 분기는 문자열 대신 상태·오류 코드로 결정합니다.

1. A는 새 세션의 `Waiting`을 먼저 발행합니다. 첫 프레임 수신 시 `Streaming` 상태를 발행한 뒤 해당 영상 이벤트를 보냅니다. 이후 새 프레임마다 영상 이벤트를 보내며 동일 상태를 매 프레임 반복 발행하지 않습니다.
2. 오류·종료 시 상태를 먼저 동기 발행하여 B가 이전 영상과 오버레이를 제거하게 한 뒤 A가 Texture를 정지·해제합니다. 종료된 세션에서는 프레임을 발행하지 않습니다. 재시작은 새 세션의 `Waiting`부터 시작합니다.
3. 영상 수신은 채 검출, 공간 보정, GPU 추적 분석 성공 여부와 독립적입니다. 채가 안 보인다는 이유로 영상 입력을 `Error`로 바꾸지 않습니다.
4. 발행·구독·조회는 Unity 메인 스레드에서만 수행합니다. A는 이전 세션의 지연 콜백을 발행 전에 버리고, B도 현재 `CameraId`·`SessionId`와 일치하는 영상만 사용합니다. 이벤트 버스 자체는 입력 유효성 검사, 세션 순서 판정, 타임아웃 감지나 재시도를 수행하지 않습니다.

### 이벤트 버스와 늦은 구독

```csharp
// Subscribe with +=, unsubscribe with -=.
public static event Action<CameraFrameData> OnCameraFrameReceived;
public static event Action<CameraInputStateData> OnCameraInputStateChanged;

public static void PublishCameraFrame(CameraFrameData frame);
public static void PublishCameraInputState(CameraInputStateData state);
public static bool TryGetCameraInputState(string cameraId, out CameraInputStateData state);
```

새 이벤트는 `event Action<T>`로 제공하며 A는 반드시 발행 메서드를 사용합니다. 기존 공개 `Action`들은 변경하지 않습니다. 상태는 카메라 ID별로 저장한 뒤 구독자에게 동기 전달합니다. 조회는 마지막으로 발행된 상태의 복사본을 반환하며 추가 이벤트를 발생시키지 않습니다. Texture나 영상 데이터는 보관·재전송하지 않습니다.

B는 `OnEnable`에서 구독한 다음 지정 ID의 상태를 조회합니다. 상태가 없으면 대기하고, 저장된 상태가 `Streaming`이어도 새 프레임을 받기 전에는 영상을 표시하지 않습니다. `OnDisable`에서 구독을 해제하고 표시 중인 참조를 제거합니다. 오류·중지 상태는 늦은 구독자를 위해 남겨두되, 새 상태가 덮어씁니다. Play 시작의 `SubsystemRegistration`에서 카메라 상태 저장소와 새 카메라 이벤트 구독을 초기화합니다.

수신 중단의 제한 시간, 재시작 실행 방법, 실제 장치 선택 설정의 연결은 3번 이후 작업에서 구현합니다. 이 계약은 깊이 추정·공간 정렬·타격 판정의 완료를 의미하지 않습니다.

## 4. 모듈별 개발 범위 및 책임 (Module Responsibilities)

실패 발행자는 `PublishTrackingLost(id, observationTimestampMs)`를 호출합니다. 시각은 같은 관측의 `MalletState.TimestampMs`와 동일한 `Time.realtimeSinceStartupAsDouble * 1000` 기준이며, 지연된 콜백의 현재 시각으로 바꾸지 않습니다. 기존 `OnTrackingLost(int)` 시그니처는 유지합니다. B의 커서는 `OnTrackingLostAt`에 순서 검사를 적용하며 시각 없는 호환 알림을 상태 변경 근거로 사용하지 않습니다.

- **Module A (CV — 사용자가 현재 담당):** 위 계약의 영상·입력 상태를 발행하고, 추적 분석 결과는 `GameEventBus.OnMalletsTracked?.Invoke(leftMallet, rightMallet);`로 전달합니다. CV 코드에는 어떠한 충돌 감지 코드도 **작성하지 마세요.**

- **Module B (AR/UI):** 이벤트 버스를 구독하는 역할만 담당합니다. `OnMalletsTracked`를 수신하여 화면의 커서 위치를 갱신하고, `OnValidHit`를 수신하여 시각 효과를 재생합니다.

- **Module C (Logic/Audio):** `OnMalletsTracked`를 구독합니다. 가상 악기의 `Collider` 목록을 관리하며, 이전 프레임과 현재 프레임의 위치를 잇는 선분이 `Collider`를 통과하는지 계산합니다(레이캐스트). 통과했다면 타격 강도를 계산하고 `GameEventBus.OnValidHit?.Invoke(hitData);`를 호출합니다.
