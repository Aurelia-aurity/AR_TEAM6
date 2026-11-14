# 장구 타격 영역 진동

`Janggu.prefab`의 `HitRegionVibration`이 `GameEventBus.OnValidHit`를 구독해 타격된 표시 메시만 진동시킵니다. 배치 미리보기 씬의 장구에도 프리팹을 통해 적용됩니다.

## C와 공유하는 영역 ID

현재 GLB 노드의 `extras.zone_id`를 `HitEvent.InstrumentId`로 사용합니다. 대소문자를 구분하며 오브젝트 이름과 ID는 서로 다릅니다.

| `HitEvent.InstrumentId` / `InstrumentPad.PadId` | 표시 대상 | Unity 메시 로컬 바깥쪽 법선 |
| --- | --- | --- |
| `janggu_head_a_inner` | `Head_A_Inner` | +X |
| `janggu_head_a_outer` | `Head_A_Outer` | +X |
| `janggu_head_b_inner` | `Head_B_Inner` | -X |
| `janggu_head_b_outer` | `Head_B_Outer` | -X |

방향은 Unity 임포트 후 좌표계 기준입니다. 각 원판·고리는 장구 면의 공통 법선 축으로 움직입니다. 테두리, 몸통, 다른 영역은 움직이지 않습니다.

```csharp
// C가 유효한 타격을 판정한 뒤 Unity 메인 스레드에서 발행합니다.
GameEventBus.OnValidHit?.Invoke(new HitEvent
{
    InstrumentId = "janggu_head_a_inner",
    HitPosition = hitPosition,
    HitForce = 0.75f, // 기존 계약: 0~1
    MalletId = 0
});
```

기존 `HitDetectionCore`는 등록된 `InstrumentPad.PadId`를 그대로 `InstrumentId`에 넣습니다. 이 문서의 ID로 C의 영역을 등록하면 됩니다. 이번 변경은 타격 판정용 영역·Collider·오디오를 생성하지 않습니다.

## 진동 동작

- 강도 0/0.5/1에 최대 변위 1/2/3mm를 선형 대응합니다. 유한한 범위 밖 강도는 0~1로 제한하고 NaN·무한대와 알 수 없는 ID는 무시합니다.
- 최초에는 표면 안쪽으로 움직입니다. 이후 `-진폭 × (1-t)² × cos(2π × 진동횟수 × t)`로 왕복하며 감쇠합니다. `t`는 재생 시간의 0~1 비율입니다.
- 기본 재생 시간은 0.15초, 진동 횟수는 3회입니다. Inspector에서 시간은 0.1~0.2초, 횟수는 1~4회로 설정할 수 있습니다.
- 이동량은 월드 미터 단위입니다. 부모의 회전·균일/비균일 배율을 반영해 법선을 계산하므로 장구 배치 크기에 따라 진폭이 확대되지 않습니다.
- 영역마다 독립적으로 재생합니다. 같은 영역을 다시 치면 마지막 타격 강도로 다시 시작하며, 이전 변위를 누적하지 않습니다.
- 종료 시 저장된 `localPosition`을 직접 대입해 정확히 복원합니다. 컴포넌트·오브젝트를 끄거나 제거할 때도 복원하고 이벤트 구독을 해제합니다.
- 게임 시간 배율과 무관한 시간을 사용하므로 `Time.timeScale = 0`에서도 효과가 끝나면 복귀합니다. 렌더링은 프레임 단위이므로 실제 화면의 종료는 다음 업데이트 시점입니다.

## ID 또는 모델 변경

1. Play를 종료하고 `Janggu.prefab`을 엽니다.
2. `HitRegionVibration > Regions`에서 `Region Id`, `Target`, `Local Normal`을 변경합니다. ID와 대상 Transform은 중복 없이 지정합니다.
3. C의 `InstrumentPad.PadId`도 같은 ID로 변경합니다. 이벤트 구조나 진동 알고리즘을 수정할 필요는 없습니다.
4. 새 프리팹을 생성하는 기본값도 바꾸려면 `JangguPrefabBuilder.ConfigureHitFeedback`의 매핑을 함께 수정합니다. 이 메서드는 기존 컴포넌트 설정을 덮어쓰지 않습니다.

바인딩과 기준 위치는 컴포넌트를 활성화할 때 저장합니다. 진동 대상은 표시 전용 자식 Transform이어야 하며 같은 Transform의 위치를 다른 애니메이터가 동시에 제어하지 않도록 합니다. C의 판정용 Collider는 이 진동 메시와 분리해야 합니다. 현재 전역 이벤트에는 장구 인스턴스 ID가 없으므로 같은 영역 ID를 사용하는 여러 장구는 함께 반응합니다.

## 직접 확인

1. `JangguPlacementPreview.unity`를 열고 Play를 실행합니다.
2. Hierarchy에서 `Janggu`를 선택합니다.
3. `HitRegionVibration` Inspector의 `Local vibration preview`에서 강도를 조절하고 영역별 `Hit ...` 버튼을 누릅니다. 작은 효과이므로 필요하면 Scene 뷰에서 해당 면을 확대합니다.
4. 미리보기 버튼은 해당 장구의 표시만 재생하며 타격 이벤트·오디오·점수를 발행하지 않습니다. 실제 C 연결은 `OnValidHit`를 통해 동작합니다.

`HitRegionVibrationTests`는 GLB의 실제 ID와 프리팹 연결, 영역별 강도·독립성, 감쇠·종료 복원, 비균일 배율에서의 수직 방향과 이동량, 연타, 비활성화·파괴, ID 변경 및 비정상 입력을 확인합니다. 실제 장치의 타격 검출·공간 보정·오디오 통합은 별도 검증 대상입니다.
