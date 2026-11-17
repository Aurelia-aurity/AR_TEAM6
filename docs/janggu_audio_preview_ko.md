# 장구 타격음 비교 및 튜닝

장구의 덩·덕·쿵 음원을 비교하고, 덕·쿵의 강·중·약마다 음량과 피치를 조정한다. [장구 음원 프로필](../unity/ARInstruments/Assets/Performance/Audio/JangguSoundProfile.asset)은 비교 씬과 타격 씬의 기본값을 제공한다.

## 어느 씬에서든 미리듣기

1. Unity 메뉴에서 **ARInstruments → Janggu Audio Tuning**을 연다.
2. 원본 덩의 강·중·약을 비교 기준으로 듣는다.
3. 덕·쿵의 샘플별 음량과 피치를 조정하고 같은 행의 **재생** 버튼을 누른다. 음량은 `0.0–5.0` 배율, 피치는 `0.50–1.50`이다.
4. **동시 타격** 그리드에서 덕을 행, 쿵을 열로 골라 듣는다. 두 샘플은 단독 재생에 쓰는 음량과 피치를 그대로 사용한다.
5. **프로필 저장**을 눌러 다음 Play Mode 세션에서 사용할 값을 저장한다.

비교 씬의 **Janggu Audio Preview** 오브젝트를 선택하면 Inspector에서도 같은 설정과 미리듣기 버튼을 사용할 수 있다.

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
