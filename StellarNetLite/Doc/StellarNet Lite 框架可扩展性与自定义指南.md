# StellarNet Lite 框架可扩展性与自定义指南
> 面向资深开发与架构师：明确框架的“变”与“不变”，指导如何进行底层替换与业务定制。

---

## 1. 传输层自定义 (The Transport Layer)
**支持修改：极高 (完全解耦)**
框架通过 `INetworkTransport` 接口彻底隔离了底层网络库。
- **可替换内容**：你可以将默认的 Mirror 替换为 **手写 KCP、ENet、Websocket 或 SteamSocket**。
- **修改位置**：实现 `INetworkTransport` 接口，并在启动入口（如 `GameLauncher`）替换管理器实例。
- **约束**：必须保留 `Packet` 封套结构，确保 `MsgId` 和 `Payload` 能够正确透传。

## 2. 序列化协议 (Serialization)
**支持修改：高 (混合模式)**
框架默认支持 **JSON**（用于低频/复杂结构）和 **ILiteNetSerializable**（用于高频/二进制）。
- **可替换内容**：你可以接入 **Protobuf、MemoryPack 或 FlatBuffers**。
- **修改位置**：修改 `LiteNetSerializer.cs` 中的 `Serialize/Deserialize` 实现。
- **约束**：建议保留 `ILiteNetSerializable` 接口语义，以维持现有的 0GC 内存池分配逻辑。

## 3. 房间业务逻辑 (Room Business)
**支持修改：极高 (横向扩展)**
框架的核心哲学是“组件化装配”。
- **可替换内容**：所有的玩法逻辑（如战斗、交易、社交、副本进度）。
- **修改位置**：通过新增 `ServerRoomComponent` 和 `ClientRoomComponent` 实现。
- **约束**：严禁修改 `Room.cs` 核心生命周期驱动，所有业务必须在组件回调（`OnTick`, `OnGameStart` 等）中完成。

## 4. UI 表现与路由 (UI & Routing)
**支持修改：高 (表现层自由)**
`RoomUIRouterBase` 提供了 UI 与网络状态的桥接。
- **可替换内容**：你可以将 UGUI 替换为 **UI Toolkit 或 FairyGUI**。
- **修改位置**：在 `XXXOnlineUIRouter` 中修改 `OnBind/OnUnbind` 的打开关闭逻辑。
- **约束**：Panel 绝不能直接持有网络状态，必须通过 Router 驱动。

## 5. 核心内核 (The Core Kernel)
**支持修改：低 (地基部分)**
涉及 `ServerApp`、`ClientApp`、`Session` 管理和 `Packet` 分发。
- **不可动内容**：
    - `Seq` 防重放机制。
    - `Scope` (Global/Room) 分发路由。
    - 房间的两阶段装配握手流程。
- **理由**：这些是框架运行的底层契约，修改可能导致回放系统、断线重连逻辑全面崩溃。

## 6. 回放与录像系统 (Replay System)
**支持修改：中 (格式可扩展)**
- **可替换内容**：录像文件的存储介质（如从本地 File 改为云端 Stream）。
- **修改位置**：`ServerReplayStorage.cs` 和 `ClientReplayPlayer.cs`。
- **约束**：必须维持 `ReplayFrameKind` 的基本分类，否则 `Seek` 时的对象快照恢复将失效。

---

## 总结：修改建议表

| 模块 | 开放程度 | 建议方式 |
| :--- | :--- | :--- |
| **底层网络库** | 100% | 实现 `INetworkTransport` |
| **玩法功能** | 100% | 新增 `RoomComponent` |
| **UI 框架** | 100% | 替换 `UIRouter` 内部实现 |
| **协议格式** | 80% | 扩展 `ILiteNetSerializable` |
| **分发路由** | 10% | 仅限修改 `Dispatcher` 优先级 |
| **会话管理** | 0% | 保持 `Session` 权威性 |

**架构师寄语：**
> “优秀的框架像乐高，底板是稳固的，但积木是可以任意拼装和替换的。请尽情在组件层挥洒创意，但请尊重内核的生命周期契约。”