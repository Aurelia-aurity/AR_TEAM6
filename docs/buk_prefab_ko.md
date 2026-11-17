# 북 표시용 Prefab

- 경로: `Assets/Presentation/Prefabs/Buk.prefab`
- `Visual` 자식은 `Assets/Models/Buk/buk.glb`를 참조하며 원본 메시와 재질을 유지합니다.
- 위치와 회전은 0, Scale은 `(1, 1, 1)`입니다. X축은 양쪽 북면을 연결하는 방향입니다.
- 끈과 손잡이를 포함한 전체 크기는 약 0.252 × 0.49732 × 0.48060m입니다.
- Collider, Rigidbody, 타격 및 오디오 스크립트는 추가하지 않습니다. C 담당이 후속 설정합니다. 씬 배치는 별도입니다.

## 원본 타격 영역

로컬 원본 `assets/buk/buk-zones.png`의 양면 각각 세 영역을 그대로 유지합니다.

| 구분 | 북면 A | 북면 B | 지름 |
| --- | --- | --- | --- |
| 중심 가죽 | `Head_A_Inner` | `Head_B_Inner` | 210mm |
| 바깥 가죽 | `Head_A_Outer` | `Head_B_Outer` | 외경 420mm |
| 테두리 | `Head_A_Rim` | `Head_B_Rim` | 외경 약 472.27mm |

테두리가 북통 중심 쪽으로 기울어진 원본 형상도 유지합니다. 구역을 단순한 평면으로 교체하지 않습니다.

장구와 동일하게 glTFast 6.12.1과 이미지 디코딩 모듈을 사용합니다. `ARInstruments/Create Buk presentation prefab` 메뉴는 기존 Prefab을 덮어쓰지 않습니다.
