# 장구 타격음 비교 및 튜닝

장구의 덩·덕·쿵 음원을 비교하고, 덕·쿵의 강·중·약마다 음량과 피치를 조정한다. [장구 음원 프로필](../unity/ARInstruments/Assets/Performance/Audio/JangguSoundProfile.asset)은 비교 씬과 타격 씬의 기본값을 제공한다.

## 어느 씬에서든 미리듣기

1. Unity 메뉴에서 **ARInstruments → Audio Tuning → Janggu**을 연다.
2. 원본 덩의 강·중·약을 비교 기준으로 듣는다.
3. 덕·쿵의 샘플별 음량과 피치를 조정하고 같은 행의 **재생** 버튼을 누른다. 음량은 `0.0–5.0` 배율, 피치는 `0.50–1.50`이다.
4. **동시 타격** 그리드에서 덕을 행, 쿵을 열로 골라 듣는다. 두 샘플은 단독 재생에 쓰는 음량과 피치를 그대로 사용한다.
5. **프로필 저장**을 눌러 다음 Play Mode 세션에서 사용할 값을 저장한다.

비교 씬의 **Janggu Audio Preview** 오브젝트를 선택하면 Inspector에서도 같은 설정과 미리듣기 버튼을 사용할 수 있다.

편집 모드 미리듣기는 튜닝 창과 Inspector가 하나의 재생 소유권을 공유한다. 새 소리를 재생하면 이전 재생의 타이머와 임시 WAV를 정리하고 소유권을 넘긴다. 타이머 만료나 창 종료 시 전체 재생 중지는 현재 소유자만 수행한다.

## 기본 음량과 출력 보호

프로필에 저장된 기본값은 다음과 같다. 강·중·약은 각 주법의 원본 샘플 선택을 뜻한다.

| 설정 | 강 | 중 | 약 |
| --- | --- | --- | --- |
| 덕 음량 | `1.0` | `1.0` | `1.0` |
| 쿵 음량 | `1.7` | `1.7` | `3.1` |
| 덕 피치 | `1.0` | `1.0` | `1.0` |
| 쿵 피치 | `0.9` | `0.9` | `0.9` |

재생할 때 개별 음량에 공통 감쇠 `0.35`배를 곱한다. 예를 들어 쿵 약의 음량 `3.1`은 리미터 처리 전 배율 `1.085`가 된다. 원본 덩도 공통 감쇠를 적용해 같은 출력 기준으로 비교한다. 공통 감쇠는 [JangguSoundProfile.PlaybackGain](../unity/ARInstruments/Assets/Performance/Scripts/JangguSoundProfile.cs)에서 조정한다.

공통 감쇠 후 합쳐진 신호에는 [JangguPeakLimiter](../unity/ARInstruments/Assets/Performance/Scripts/JangguPeakLimiter.cs)를 적용한다. 샘플 피크의 상한은 `0.95`이며, 상한을 넘는 순간 모든 채널에 같은 감쇠를 적용한다. 피크가 지나가면 감쇠를 서서히 해제한다. 회복 시정수는 `50ms`로, 완전히 복원되는 데 걸리는 시간과는 다르다. 리미터가 작동하는 구간에서는 타격의 세기 차이와 음색이 달라질 수 있다.

- 편집 모드에서는 미리듣기 버튼을 누르면 음량·피치를 반영해 샘플을 합친 뒤, 전체 배열을 리미터로 처리하고 16비트 PCM WAV로 변환한다.
- Play Mode에서는 프리뷰가 활성화될 때 활성 AudioListener에 [JangguOutputLimiter](../unity/ARInstruments/Assets/Performance/Scripts/JangguOutputLimiter.cs)가 없으면 추가한다. 프리뷰가 추가한 리미터는 마지막 프리뷰를 비활성화하거나 닫을 때 제거한다. Unity의 오디오 스레드가 `OnAudioFilterRead()`를 호출할 때마다 샘플 묶음을 처리하며, 감쇠 상태를 다음 묶음으로 이어 간다.
- Play Mode의 리미터는 해당 AudioListener의 최종 출력에 적용된다. 같은 리스너로 들리는 음악과 다른 효과음도 함께 제한된다.

## Game 탭에서 비교하기

1. [JangguAudioPreview 씬](../unity/ARInstruments/Assets/Performance/Scenes/JangguAudioPreview.unity)을 열고 Play를 누른다.
2. Game 탭에서 슬라이더나 숫자 칸으로 값을 바꾼다. 덕·쿵 단독 버튼과 3×3 동시 타격 그리드에서 같은 값이 적용된다.
3. 시험한 값을 프로필에 반영하려면 **Save tuning on Play Mode exit**를 켜고 Play Mode를 종료한다. 체크박스는 매 세션 기본으로 꺼져 있다. 켰을 때는 Game 탭에서 수정한 음량·피치 항목만 저장된다.

Play Mode에서는 타격마다 재생용 AudioSource를 할당하므로 연타 시 앞 소리의 잔향이 이어진다. **Stop playback**은 재생 중인 소리를 모두 중지한다.

## 타격 판정으로 재생하기

1. [JangguHitPreview 씬](../unity/ARInstruments/Assets/Performance/Scenes/JangguHitPreview.unity)을 열고 Play를 누른다.
2. **Hit and stay behind** 버튼을 눌러 A면의 덕을 듣는다. **Rotation Y**를 `180°`로 돌려 B면의 쿵을 듣는다.
3. **Lift and hit again**을 눌러 연속 타격을 확인한다. **Cross outside pad**와 **Stop before face**에서는 타격음이 나지 않는다.
4. 직접 시험하려면 Game 탭의 말렛 십자선을 드래그한다. 화면의 마지막 타격 강도와 선택된 샘플을 확인할 수 있다.

| 정규화한 타격 강도 | 선택하는 샘플 |
| --- | --- |
| `0.33` 미만 | 약 |
| `0.33` 이상, `0.66` 미만 | 중 |
| `0.66` 이상 | 강 |

양쪽 면이 맞으면 각 타격을 받아 덕과 쿵을 각각 재생한다. 원본 덩은 비교 기준으로 사용한다. 타격 프리뷰의 입력과 판정은 [장구 타격 판정 안내](janggu_hit_detection_ko.md)를 참고한다.

현재 검증은 합성 말렛을 쓰는 프리뷰 기준이다. 실제 카메라 추적과 Main 씬에서의 연주는 후속 검증 대상이다. 장구 음원의 출처와 이용 조건은 [저작물 이용 고지](../NOTICE.md)에 있다.

## 오디오 검증 결과

2026-11-19 Unity `6000.0.25f1`에서 관련 소스와 테스트를 복사한 임시 프로젝트로 검증했다.

- 편집 모드의 실제 WAV 생성 메서드로 기본 합타 9개를 생성했다. 최대 피크는 약 `0.784`였고, 16비트 PCM 범위를 넘는 샘플은 없었다. 덕·쿵 음량을 각각 `5.0`으로 높인 시험 조합도 변환 후 피크가 약 `0.95`로 제한됐다.
- Play Mode에서 쿵 약 `3.1`, 공통 감쇠 `0.35`, 쿵 피치 `0.9`로 단타·합타와 덕 강+쿵 약의 `0.1초` 간격 10회 연타를 확인했다. 음량을 각각 `5.0`으로 높인 연타 시험에서도 리미터 처리 직후 피크가 약 `0.95`였고, 출력 범위를 넘는 샘플은 없었다.
- [리미터 단위테스트](../unity/ARInstruments/Assets/Performance/Tests/Editor/JangguPeakLimiterTests.cs) 11개와 기존 프로필 테스트 14개, 총 25개가 EditMode에서 통과했다. 작은 신호 통과, 양수·음수 피크 제한, 좌우 채널 균형, 버퍼 간 연속성, `44.1kHz`·`48kHz` 회복 동작, 무음·빈 배열 처리를 검사한다.

위 결과는 임시 프로젝트의 오디오 처리 및 관련 테스트 범위다. 원본 프로젝트 전체 테스트, 실제 출력 장치의 청취 품질과 타격감은 추가 확인 대상이다.
