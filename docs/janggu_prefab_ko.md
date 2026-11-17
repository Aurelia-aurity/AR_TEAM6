# 장구 표시용 Prefab

- 경로: `Assets/Presentation/Prefabs/Janggu.prefab`
- 원본 연결: `Assets/Models/Janggu/janggu.glb`를 `Visual` 자식으로 참조합니다.
- 기본 위치와 회전은 0, Scale은 `(1, 1, 1)`입니다. 길이 방향은 X축이며 전체 크기는 0.53 × 0.45 × 0.45m입니다.
- 원본의 메시 이름과 계층을 유지합니다. `Head_A_Inner`, `Head_A_Outer`, `Head_B_Inner`, `Head_B_Outer`가 타격 영역입니다. 원본 폴더의 `janggu-zones.png`에서 청록색은 안쪽 원판, 주황색은 바깥 고리입니다.
- Collider, Rigidbody, 타격 판정 및 오디오 스크립트는 포함하지 않습니다. C 담당이 후속 연결합니다.
- `HitRegionVibration`은 유효한 타격 이벤트를 받아 해당 표시 영역을 1~3mm 진동시킵니다. [영역 ID·진동 효과 안내](janggu_hit_vibration_ko.md)를 참고하세요.
- 씬 배치는 별도입니다. Project 창에서 Prefab을 씬으로 드래그해 위치와 방향을 설정합니다.
- 테스트 영상 위에서 위치·회전·크기를 조절하려면 [장구 배치 미리보기](janggu_placement_preview_ko.md)의 `JangguPlacementPreview.unity`를 사용합니다.

## 임포터

Unity 6000.0.25f1에서는 glTFast 6.12.1과 `com.unity.modules.imageconversion`을 사용합니다. glTFast 6.21.0과 6.19.0은 이미지 모듈 활성화 시 `Texture2D.LoadImage(ReadOnlySpan<byte>, ...)` 호출로 컴파일 오류가 발생했습니다. 패키지 목록과 잠금 파일을 함께 공유해야 합니다.

`KHR_materials_specular` 확장은 이 임포터에서 지원되지 않을 수 있으므로 Blender의 반사광 표현과 차이가 날 수 있습니다. 기본 재질과 내장 텍스처는 GLB에서 가져옵니다.

`ARInstruments/Create Janggu presentation prefab` 메뉴는 Prefab이 없을 때만 생성합니다. 기존 Prefab의 수동 편집을 덮어쓰지 않습니다.
