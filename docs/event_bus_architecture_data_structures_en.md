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

## 3. 开发边界与职责 (Module Responsibilities)

*   **Module A (CV - 用户当前负责):** 只负责消费摄像头画面，计算 `MalletState`，并在每帧调用 `GameEventBus.OnMalletsTracked?.Invoke(leftMallet, rightMallet);`。**不要**在 CV 代码里写任何碰撞检测代码。
*   **Module B (AR/UI):** 只负责监听总线。监听 `OnMalletsTracked` 更新画面上的光标位置；监听 `OnValidHit` 播放视觉特效。
*   **Module C (Logic/Audio):** 监听 `OnMalletsTracked`。维护一个虚拟乐器的 Collider 列表。计算上一帧和当前帧的线段是否穿过 Collider（射线检测）。如果穿过，计算力度，并调用 `GameEventBus.OnValidHit?.Invoke(hitData);`。