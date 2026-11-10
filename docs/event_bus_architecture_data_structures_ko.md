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

## 3. 모듈별 개발 범위 및 책임 (Module Responsibilities)

실패 발행자는 `PublishTrackingLost(id, observationTimestampMs)`를 호출합니다. 시각은 같은 관측의 `MalletState.TimestampMs`와 동일한 `Time.realtimeSinceStartupAsDouble * 1000` 기준이며, 지연된 콜백의 현재 시각으로 바꾸지 않습니다. 기존 `OnTrackingLost(int)` 시그니처는 유지합니다. B의 커서는 `OnTrackingLostAt`에 순서 검사를 적용하며 시각 없는 호환 알림을 상태 변경 근거로 사용하지 않습니다.

- **Module A (CV — 사용자가 현재 담당):** 카메라 영상을 입력받아 `MalletState`를 계산하고, 매 프레임 `GameEventBus.OnMalletsTracked?.Invoke(leftMallet, rightMallet);`를 호출하는 역할만 담당합니다. CV 코드에는 어떠한 충돌 감지 코드도 **작성하지 마세요.**

- **Module B (AR/UI):** 이벤트 버스를 구독하는 역할만 담당합니다. `OnMalletsTracked`를 수신하여 화면의 커서 위치를 갱신하고, `OnValidHit`를 수신하여 시각 효과를 재생합니다.

- **Module C (Logic/Audio):** `OnMalletsTracked`를 구독합니다. 가상 악기의 `Collider` 목록을 관리하며, 이전 프레임과 현재 프레임의 위치를 잇는 선분이 `Collider`를 통과하는지 계산합니다(레이캐스트). 통과했다면 타격 강도를 계산하고 `GameEventBus.OnValidHit?.Invoke(hitData);`를 호출합니다.
