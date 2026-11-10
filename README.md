# AR_TEAM6

고정 카메라 영상 위의 가상 타악기를 실제 채로 연주하는 Unity 프로젝트입니다.

## 시작하기

1. Unity Hub에서 **Add → Add project from disk**를 선택합니다.
2. 이 저장소의 `unity/ARInstruments` 폴더를 추가합니다.
3. Unity **6000.0.25f1**으로 프로젝트를 엽니다.
4. `Assets/Shared/Scenes/Main.unity`를 엽니다.

설치된 에디터의 공식 **3D URP** 템플릿으로 초기화했습니다. Unity Hub에서 Unity Personal 등의 유효한 라이선스를 활성화해야 에디터를 실행할 수 있습니다. 2026.11.09 사용자 확인 기준, 라이선스 활성화 후 프로젝트를 열고 기본 씬을 Play 모드에서 오류 없이 실행했습니다.

현재 단계는 프로젝트와 협업 디렉토리 초기화입니다. 카메라 입력, 추적, 타격 판정, 오디오 및 UI 기능은 이후 구현합니다.

## 디렉토리

```text
AR_TEAM6/                         # 현재 저장소 폴더: TEAM6
├─ assets/                       # 로컬 전용 모델 원본 (Git 공유 제외)
│  ├─ janggu/
│  └─ buk/
├─ unity/
│  └─ ARInstruments/             # Unity Hub에서 여는 프로젝트
│     ├─ Assets/
│     │  ├─ Tracking/            # A: 카메라 입력·채 추적
│     │  ├─ Presentation/        # B: 영상·악기 표시·UI·시각 효과
│     │  ├─ Performance/         # C: 타격 판정·강도·오디오
│     │  ├─ Shared/              # 공통 데이터·이벤트·통합 씬
│     │  └─ Models/              # Unity 런타임용 모델
│     ├─ Packages/
│     └─ ProjectSettings/
└─ docs/
```

루트 `assets/`는 로컬 작업용이며 `.gitignore`로 공유 대상에서 제외합니다. 저장소를 새로 받으면 이 폴더는 포함되지 않습니다. Unity에서 사용하는 `unity/ARInstruments/Assets/Models`는 팀 공유 대상으로 유지합니다.

- [기능 명세서](docs/AR_가상악기_기능명세_표정리.md)
- [공통 데이터·이벤트 인터페이스 규약](docs/event_bus_architecture_data_structures_ko.md)
- [Unity 프로젝트 협업 안내](docs/unity_project_setup_ko.md)

## 현재 구현

- `Tracking/Scripts/GpuMalletTracker.cs`와 `Tracking/Shaders/MalletTracker.compute`는
  WebCamTexture를 Compute Shader로 처리하여 좌우 말렛의 색상 영역을 추적합니다.
- `Tracking/Scripts/KalmanFilter2D.cs`는 추적 좌표와 속도를 평활화합니다.
- `Tracking/Scripts/CoordinateMapper.cs`는 카메라 픽셀·색상 영역을 Unity 월드 좌표로 변환하는
  보정 유틸리티입니다.
- `Performance/Scripts/HitDetectionCore.cs`는 이벤트 버스를 구독하여 히트박스와 속도,
  깊이 임계값을 기준으로 중복 타격을 억제하고 `OnValidHit`를 발행합니다.
- OpenCVForUnity에 의존하는 이전 CPU 추적 경로는 현재 Unity 패키지 구성에 포함하지 않았습니다.
  모바일 기본 경로는 Compute Shader 구현을 사용합니다.

상세한 데이터 구조와 이벤트 계약은
[영문 계약 문서](docs/event_bus_architecture_data_structures_en.md)와
[한국어 계약 문서](docs/event_bus_architecture_data_structures_ko.md)를 참고하세요.
