# 장구 배치 미리보기

FR-003의 장구 생성·위치·회전·크기 설정과 FR-004의 합성 구조를 준비하는 B 전용 테스트 씬입니다. 기존 장구 GLB 및 `Janggu.prefab`을 재사용합니다.

## 실행

1. Unity 6000.0.25f1에서 `Assets/Presentation/Scenes/JangguPlacementPreview.unity`를 엽니다.
2. Play를 누릅니다. 실제 카메라를 열지 않고 테스트 영상 위에 장구를 표시합니다.
3. 왼쪽 `INSTRUMENT POSE` 패널의 슬라이더로 위치 X/Y/Z(미터), 회전 X/Y/Z(도), 균일 크기를 조절합니다.
4. `Reset pose`를 누르면 실행 시작 시의 위치·회전·크기로 돌아갑니다.
5. 기존 `Mirror view`는 영상과 장구·디버그 커서의 표시 결과를 함께 좌우 반전합니다. 왼쪽 설정 패널과 안내 문구는 반전하지 않습니다.
6. `Cursor debug`로 기존 테스트 커서를 확인할 수 있습니다. 테스트 추적 좌표는 실제 장구 타격 판정을 의미하지 않습니다.

Play 중 조절한 값은 실행을 종료하면 저장되지 않습니다. 기본 배치를 저장하려면 Play를 끈 상태에서 씬의 `Janggu` 오브젝트 Transform을 수정하고 씬을 저장합니다. `Reset pose`는 해당 실행 시작 시의 Transform으로 복귀합니다.

## 구조와 좌표

- 장구는 UI 밖의 월드 공간에 있는 기존 프리팹 인스턴스입니다. 원본 프리팹과 GLB를 변경하지 않습니다.
- 기본 월드 위치는 `(-0.08, 0, 1.4)m`, 회전은 `(8, -28, 0)도`, 크기는 원본의 1배입니다. 원본의 전체 크기는 약 `0.53 × 0.45 × 0.45m`입니다.
- 기본 위치 범위는 X/Y `-0.6~0.6m`, Z `0.7~3m`, 회전 범위는 각 축 `-180~180도`, 크기는 `0.25~2배`입니다. 씬에 저장된 초기 위치가 기본 범위를 벗어나면 해당 축의 슬라이더 범위를 초기 위치까지 확장합니다. 일부 극단 배치에서는 모델이 영상 영역 밖으로 나갈 수 있습니다.
- 네 영역 `Head_A_Inner`, `Head_A_Outer`, `Head_B_Inner`, `Head_B_Outer`와 원본 계층·재질을 유지합니다. 물리 Collider·타격 판정·오디오 연결은 C와 후속 작업으로 정합니다.
- 테스트 씬에서만 31번 레이어를 장구 렌더링에 사용합니다. ProjectSettings에 새 레이어 이름을 추가하거나 원본 모델의 레이어를 바꾸지 않습니다. 다른 기능과 합칠 때 카메라의 레이어 마스크를 다시 합의합니다.
- 전용 `TestProjectionCamera`가 투명 배경의 RenderTexture로 장구를 렌더링합니다. 기존 디버그 커서도 같은 카메라를 사용합니다.
- `MirrorRoot/InstrumentImage`는 카메라 영상 위, 디버그 커서 아래에 배치됩니다. 영상의 회전·상하 방향 보정이 끝난 표시 공간에 맞춰 렌더링하며, 거울 반전은 MirrorRoot에서 한 번 적용합니다.
- 입력 비율 변경 시 장구 렌더링 비율도 갱신합니다. 입력 대기·오류에서는 장구 표시를 숨기고, 컴포넌트 비활성화 시 소유한 RenderTexture를 해제합니다.

## 코드와 에셋

| 경로 | 역할 |
| --- | --- |
| `Assets/Presentation/Scenes/JangguPlacementPreview.unity` | 장구와 조절 패널을 연결한 작업 씬 |
| `Assets/Presentation/Scripts/InstrumentPlacementControls.cs` | 월드 위치·회전·균일 크기와 초기화 |
| `Assets/Presentation/Scripts/InstrumentPreviewRenderer.cs` | 투명 배경 렌더링·화면 합성·렌더 타깃 수명 관리 |
| `Assets/Editor/JangguPlacementPreviewBuilder.cs` | 기존 표시 씬을 기반으로 새 배치 씬 생성. 기존 배치 씬이 있으면 덮어쓰지 않음 |
| `Assets/Presentation/Tests/Editor/JangguPlacementTests.cs` | 프리팹 재사용, 실제 렌더링·조절·거울 모드·입력 복구·자원 해제 검증 |

기존 `CameraDisplayPreview.unity`, `CameraDisplay.prefab`, `Janggu.prefab`, `Main.unity`와 빌드 씬 목록은 유지합니다.

## 완료 범위와 후속 작업

이 씬은 테스트 영상 위에 장구를 생성·배치하고 표시하는 개발 단계입니다. 실제 카메라 보정값으로 정렬된 AR 완료 상태는 아닙니다. 테스트 투영 카메라와 원본 모델의 미터 단위 크기가 실제 공간 정합을 보장하지는 않습니다.

후속 작업은 A의 기준 영상·카메라 보정 정보 연결, C와의 타격 영역 ID·3차원 Collider 및 사운드 대응 합의, 실제 공간에서의 영상·장구·채 정렬 검증입니다. 기존 이벤트 버스와 추적·타격 데이터 구조를 변경하지 않습니다.

## 검증 결과

2026.11.13 Unity 6000.0.25f1에서 컴파일과 EditMode 테스트 **33개 전체 통과**를 확인했습니다. 장구 테스트 3개와 기존 표시·거울 모드·커서 테스트 30개를 포함합니다. Play 진입 검증과 실제 GPU 렌더링을 사용했으며, 정상·거울 모드 이미지를 직접 확인했습니다.

- 기존 장구 프리팹 참조와 네 타격 영역 보존, 원본 프리팹의 위치·크기 유지.
- 모델이 투명 배경으로 실제 렌더링되는지, 슬라이더와 Reset이 월드 Transform을 갱신하는지 확인.
- 거울 모드에서 영상·장구가 함께 반전되고 원래 월드 위치·회전·크기는 유지되는지 확인.
- 입력 오류 시 숨김, 세로 영상 재입력 시 비율 갱신, 비활성화 시 렌더 타깃 해제 확인.

로컬 결과는 `unity/ARInstruments/Logs/janggu-placement-final-results.xml`, 검토 이미지는 같은 폴더의 `janggu-placement-normal.png`, `janggu-placement-mirrored.png`입니다. Logs는 Git 공유 대상이 아닙니다. 실제 휴대폰 입력·공간 보정·타격·음원 연동은 이번 검증 범위에 포함하지 않았습니다.
