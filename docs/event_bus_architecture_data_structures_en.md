# 虚拟打击乐器项目 - 核心数据总线与接口契约 (V1.0)

> **LLM 协作者必读 (Instruction for LLMs):**
> 这是一个基于 Unity (C#) 的实时虚拟打击乐器项目。本项目由三部分组成：CV追踪 (Module A)、AR/UI (Module B)、判定与音频 (Module C)。
> 当你协助任何一个模块的开发时，**必须绝对遵守**以下定义的数据结构和事件接口。严禁擅自修改核心 Struct 的字段。所有模块间的通信**必须**通过 `EventBus` 进行，严禁模块间的强耦合直接调用。

## 1. 核心数据结构 (Core Data Structures)

所有的坐标均统一映射为**屏幕标准化坐标 (Normalized Screen Coordinates, 0.0 ~ 1.0)** 或 **世界坐标 (World Space, Unity Vector3)**。

```csharp
using UnityEngine;

// 1.1 琴槌实时状态 (由 Module A: CV 追踪 每帧产生)
public struct MalletState
{
    public int MalletId;         // 琴槌标识符 (0 = 左手, 1 = 右手)
    public Vector3 Position;     // 琴槌端点坐标 (X, Y, Z深度)
    public Vector3 Velocity;     // 当前运动矢量 (方向与速度)
    public float Confidence;     // 追踪置信度 (0.0f ~ 1.0f)
    public double TimestampMs;   // 采集时间戳 (用于延迟对齐与速度计算)
}

// 1.2 物理打击事件 (由 Module C: 逻辑判定 产生，触发音频与特效)
public struct HitEvent
{
    public string InstrumentId;  // 被击中的乐器区域ID (e.g., "Snare_1", "Cymbal_Left")
    public Vector3 HitPosition;  // 发生碰撞的精确坐标
    public float HitForce;       // 打击力度 (0.0f ~ 1.0f，映射到音频音量)
    public int MalletId;         // 哪个琴槌造成的击打
}
```

## 2. 全局事件总线 (Global Event Bus)

采用静态 Action 委托实现轻量级、无延迟的内存事件总线。

```csharp
using System;

public static class GameEventBus
{
    // ==========================================
    // Module A (CV) 发布 -> Module B, C 订阅
    // ==========================================
    
    /// <summary>
    /// 每当 CV 模块完成一帧解析，提取出琴槌状态时触发。
    /// C模块基于此进行碰撞检测；B模块基于此渲染光标/AR特效。
    /// </summary>
    public static Action<MalletState, MalletState> OnMalletsTracked;

    /// <summary>
    /// 当摄像头追踪丢失时触发，通知 UI 提示用户。
    /// </summary>
    public static Action<int> OnTrackingLost; // int 为 MalletId

    // Ordered consumers use the failed observation's TimestampMs.
    public static Action<int, double> OnTrackingLostAt;

    public static void PublishTrackingLost(int malletId, double timestampMs)
    {
        OnTrackingLostAt?.Invoke(malletId, timestampMs);
        OnTrackingLost?.Invoke(malletId); // Preserve existing subscribers.
    }

    // A -> calibration: raw pixel centroid and color-mask area before world mapping.
    public static Action<RawMalletSample> OnRawMalletSample;

    // ==========================================
    // Module C (Logic) 发布 -> Module B (UI/AR) 订阅
    // ==========================================
    
    /// <summary>
    /// 每当判定逻辑确认一次有效打击时触发。
    /// B模块订阅此事件播放水波纹、粒子特效等；音频模块也会监听此事件播放声音。
    /// </summary>
    public static Action<HitEvent> OnValidHit;
}
```

## 3. Camera frame and input state contract (step 2)

This additive contract leaves `MalletState`, `HitEvent` and existing tracking events unchanged. Sources: `Shared/Scripts/CameraFrameData.cs`, `CameraInputStateData.cs`, and `GameEventBus.cs`. See also the [Korean contract](event_bus_architecture_data_structures_ko.md). Publisher integration, B's adapter and scene wiring are subsequent steps.

| Payload | Fields |
| --- | --- |
| `CameraFrameData` | `string CameraId`, `Guid SessionId`, `Texture Texture`, `int ClockwiseRotation`, `bool VerticallyMirrored`, `double ReceivedAtMs` |
| `CameraInputStateData` | `string CameraId`, `Guid SessionId`, `string DeviceName`, `CameraInputState State`, `double ChangedAtMs`, `CameraInputError Error`, `string Description` |

- `CameraId` is a nonempty, case-sensitive logical Inspector ID such as `primary`, unique among active publishers. It is independent of the physical device name. `DeviceName` is the selected device name, empty before selection.
- A creates a fresh `Guid.NewGuid()` for each start attempt, including failed attempts. All events for that attempt share the ID; `Guid.Empty` is invalid. GUIDs identify sessions, not their chronological order.
- Both times use `Time.realtimeSinceStartupAsDouble * 1000` in milliseconds. `ReceivedAtMs` is when A observes a new frame; `ChangedAtMs` is when the input state changes. Neither is sensor capture time or UTC.
- Rotation is clockwise, restricted to 0/90/180/270 degrees. Apply vertical correction in texture space before rotation. B's user mirror mode is separate and does not change tracking coordinates.
- The texture is a borrowed, mutable live reference, not a copied frame image. A owns creation, capture, stopping and destruction. B retains it only for display.

| State | Meaning | B adapter behavior |
| --- | --- | --- |
| `Waiting` | Starting; awaiting first usable frame | `ShowWaiting()` |
| `Streaming` | A usable frame has actually arrived | `PresentFrame()` on the next frame event |
| `Error` | Device missing, start failure or frame timeout | `ShowError()` |
| `Stopped` | Explicit stop or disable | `ShowWaiting()` |

`CameraInputError` values are `None`, `DeviceNotFound`, `StartFailed`, and `FrameTimeout`. Non-error states use `None`. `Description` is optional diagnostic text; consumers branch on state/error codes, not message strings.

A publishes `Waiting` for each new session. On its first usable frame, publish `Streaming` before publishing that frame. Publish frames for every newly received image, independently of mallet detection, calibration or GPU analysis success. Publish state on transitions rather than on every frame. Publish error/stop synchronously before stopping or destroying the texture so B can clear its image and overlays. Do not publish frames for a terminated session. Restart begins with a new session's `Waiting`.

```csharp
public static event Action<CameraFrameData> OnCameraFrameReceived;
public static event Action<CameraInputStateData> OnCameraInputStateChanged;
public static void PublishCameraFrame(CameraFrameData frame);
public static void PublishCameraInputState(CameraInputStateData state);
public static bool TryGetCameraInputState(string cameraId, out CameraInputStateData state);
```

All calls and subscriptions run on Unity's main thread. New events use `event Action<T>` so publishers must use the methods; legacy public Actions remain unchanged. State is cached by logical camera ID before synchronous notification. Query returns a copy without broadcasting a replay. Frames/textures are never cached or replayed. The bus does not validate payloads, arbitrate session ordering, detect timeouts, or retry. A must discard obsolete-session callbacks before publishing; B must match incoming frames to its camera and current session.

B subscribes in `OnEnable`, then queries its camera state. If absent, wait; even cached `Streaming` requires a new frame before showing video. B unsubscribes and clears its displayed reference in `OnDisable`. Error/stopped states remain available to late subscribers until replaced. `SubsystemRegistration` clears the camera state cache and camera event subscriptions on Play startup. Timeout thresholds and restart execution belong to subsequent steps. This contract does not establish spatial alignment, depth estimation or hit correctness.

## 4. 开发边界与职责 (Module Responsibilities)

Failure publishers call `PublishTrackingLost(id, observationTimestampMs)`. The timestamp uses the same `Time.realtimeSinceStartupAsDouble * 1000` clock as the observation's `MalletState.TimestampMs`; do not substitute callback arrival time. The existing `OnTrackingLost(int)` signature remains available. B's cursors order `OnTrackingLostAt` against tracked samples and do not let an unversioned legacy notification override ordered state.

*   **Module A (CV):** Publishes frames and input state under the contract above, and sends tracking analysis through `GameEventBus.OnMalletsTracked?.Invoke(leftMallet, rightMallet);`. Do not implement collision detection in CV code.
*   **Module B (AR/UI):** 只负责监听总线。监听 `OnMalletsTracked` 更新画面上的光标位置；监听 `OnValidHit` 播放视觉特效。
*   **Module C (Logic/Audio):** 监听 `OnMalletsTracked`。维护一个虚拟乐器的 Collider 列表。计算上一帧和当前帧的线段是否穿过 Collider（射线检测）。如果穿过，计算力度，并调用 `GameEventBus.OnValidHit?.Invoke(hitData);`。
