# StellarNet Lite 框架功能说明文档
> 面向项目负责人、主程、框架维护者、正式接入开发者  
> 用于完整说明当前框架已经具备的功能边界、分层职责、运行链路、编辑器工具、配置系统与示例覆盖范围

---

# 目录
- [1. 文档定位](#1-文档定位)
- [2. 框架整体定位](#2-框架整体定位)
- [3. 当前框架已经解决了什么问题](#3-当前框架已经解决了什么问题)
- [4. 总体分层说明](#4-总体分层说明)
- [5. Shared 层功能说明](#5-shared-层功能说明)
- [6. Server 层功能说明](#6-server-层功能说明)
- [7. Client 层功能说明](#7-client-层功能说明)
- [8. Editor 工具层功能说明](#8-editor-工具层功能说明)
- [9. Config 配置系统说明](#9-config-配置系统说明)
- [10. 核心网络通信链路说明](#10-核心网络通信链路说明)
- [11. 房间系统功能说明](#11-房间系统功能说明)
- [12. 回放系统功能说明](#12-回放系统功能说明)
- [13. 断线重连系统功能说明](#13-断线重连系统功能说明)
- [14. 协议系统功能说明](#14-协议系统功能说明)
- [15. 事件系统功能说明](#15-事件系统功能说明)
- [16. 日志与治理能力说明](#16-日志与治理能力说明)
- [17. GameDemo 与示例覆盖范围说明](#17-gamedemo-与示例覆盖范围说明)
- [18. 当前框架边界与未覆盖项](#18-当前框架边界与未覆盖项)
- [19. 推荐阅读顺序](#19-推荐阅读顺序)
- [20. 总结](#20-总结)

---

# 1. 文档定位
这份文档不是 API 手册，也不是接入教程。  
它的目标是帮助你完整回答下面这些问题：
- 这个框架到底做了哪些事
- 哪些能力已经具备
- 哪些能力属于 Shared
- 哪些能力属于 Server
- 哪些能力属于 Client
- Editor 工具已经覆盖到哪里
- Config 系统是怎么工作的
- 回放、重连、房间、协议、事件、日志分别落在哪一层
- 当前哪些能力是“框架负责”，哪些能力仍然要业务自己实现

如果你是第一次接触 StellarNet Lite，这份文档建议放在 README 和开发者手册之间阅读。  
如果你是主程或框架维护者，这份文档更像一份 **当前版本能力清单**。

---

# 2. 框架整体定位
StellarNet Lite 是一个面向 Unity 中小型联机项目的轻量级房间网络框架。  
它主要服务于以下类型项目：
- 房间制 / 对局制
- 中小型商业项目
- 长期维护型多人联机玩法 (如社交元宇宙、棋牌、休闲竞技)

核心定位非常明确：
1. **服务端绝对权威**
2. **协议事件驱动**
3. **房间组件化装配**
4. **客户端表现解耦 (0GC协议直抛)**
5. **流式回放沙盒隔离**
6. **断线重连可恢复**
7. **编辑器工具辅助治理**
8. **配置化而不是满地硬编码**
9. **底层网络库防腐隔离 (INetworkTransport)**

它不是“全自动状态同步黑盒”，而是一套**可讲清楚、可追踪、可排障、可横向扩展**的工程化底座。

---

# 3. 当前框架已经解决了什么问题
当前版本已经系统性解决了这些核心问题：

## 3.1 解决了协议流转不透明的问题
通过 `[NetMsg]`、`NetMessageMapper`、`AutoRegistry` (0反射装配) 与强类型发送器 (`NetClient.Send<T>`)，框架把协议的 ID、Scope、Dir、发送接收入口统一收口，避免多人协作时协议流转变得不可追踪。

## 3.2 解决了房间功能膨胀成巨石类的问题
通过 `Room`、`RoomComponent` 与 `ComponentIds` 动态装配，框架把房间逻辑拆成多个横向扩展组件，避免所有玩法都堆到一个房间主类中。

## 3.3 解决了客户端 View 和网络层互相污染的问题
通过 `ClientModule`、`ClientRoomComponent` 与无侵入式事件总线 (`GlobalTypeNetEvent` / `RoomNetEventSystem`)，框架建立了协议接入层、轻状态层、表现层三段式解耦，省去了冗余的中间 DTO 转换。

## 3.4 解决了回放与在线房间串线的问题
通过 `ClientAppState.ReplayRoom` 与 `ClientReplayPlayer`，框架把回放定义为独立沙盒，阻断真实房间包与发包请求，不再把回放当成在线房间的附属标记位。

## 3.5 解决了 GC 与内存爆炸问题 (核心性能)
通过引入 `System.Buffers.ArrayPool` 实现 0GC 发包，通过 `ArraySegment` 与 `PayloadOffset` 实现 0GC 底层切片解析，通过 `FileStream` 实现录像流式落盘与断点续传，彻底解决了高频同步与大文件带来的内存抖动。

## 3.6 解决了底层网络库强绑定的问题
通过引入 `INetworkTransport` 接口，框架核心逻辑完全不依赖 Mirror。Mirror 仅作为默认插件存在，业务可随时无缝切换至 KCP、LiteNetLib 等其他底层库。

---

# 4. 总体分层说明
StellarNet Lite 当前按以下维度分层：

## 4.1 Shared 层
Shared 负责双端共享的基础契约和底座能力，包括协议定义、网络封套、元数据特性、序列化抽象、自动绑定器公共定义。
> **Shared 共享的是“契约”和“最基础能力”，绝不包含任何客户端表现层的事件定义。**

## 4.2 Server 层
Server 负责所有权威逻辑和运行时治理，包括 Session 管理、Room 管理、权威状态修改、房间组件装配、Replay 录制、GC 与重连。
> **Server 是真相来源，也是风险控制中心。**

## 4.3 Client 层
Client 负责客户端网络接入、轻状态镜像和表现桥接，包括状态机、事件系统、回放播放器、View 输入桥接。
> **Client 负责“接权威结果”和“组织表现”，不负责定义真相。**

## 4.4 Editor 层
Editor 层负责开发效率和治理工具，包括协议/预制体扫描器、常量表生成、脚手架模板生成。
> **Editor 层不是运行时逻辑，而是帮助团队降低接入成本和犯错概率。**

---

# 5. Shared 层功能说明

## 5.1 Shared/Core 的功能
- **协议元数据系统**：`NetScope`、`NetDir`、`NetMsgAttribute`。
- **统一传输封套**：`Packet` (包含 `Seq`, `MsgId`, `Scope`, `RoomId`, `PayloadOffset`, `PayloadLength`，支持 0GC 切片)。
- **协议映射中心**：`NetMessageMapper`，启动时扫描建立元数据映射。
- **传输层防腐接口**：`INetworkTransport`，隔离底层网络库。

## 5.2 Shared/Infrastructure 的功能
- **网络配置结构**：`NetConfig` 与 `NetConfigLoader`（支持异步加载与 Android 平台适配）。
- **混合序列化器**：`INetSerializer` 接口。默认提供 `LiteNetSerializer`，支持常规协议的 JSON 序列化，以及高频协议（如 `ObjectSync`）基于 `ILiteNetSerializable` 的二进制 0GC 序列化。
- **统一日志入口**：`NetLogger`，统一输出模块名、RoomId、SessionId 等上下文。

## 5.3 Shared/Binders 的功能
- **AutoRegistry**：0反射静态装配器，彻底消灭运行时的反射开销与手动 `switch(msgId)`。

---

# 6. Server 层功能说明

## 6.1 Server/Core 的功能
- **ServerApp**：服务端网络运行时总门面，负责 Session/Room 管理与 Seq 防重放。
- **Session**：服务端权威会话对象，记录连接映射、在线状态与重连授权。
- **Room**：房间作用域容器 + 生命周期主控 + 广播入口。
- **Dispatcher**：`GlobalDispatcher` 与 `RoomDispatcher`，负责路由分发。
- **ServerRoomFactory**：负责组件的两阶段装配与失败回滚。
- **ITickableComponent**：可 Tick 组件接口，消除无效虚函数调用。

## 6.2 Server/Modules 的功能 (全局域)
- **ServerUserModule**：登录、版本校验、重连协商。
- **ServerRoomModule**：建房、加房、离房、进房握手。
- **ServerLobbyModule**：大厅房间列表同步。
- **ServerReplayModule**：录像列表、基于 `FileStream` 的分块下载与断点续传。

## 6.3 Server/Components 的功能 (房间域)
- **ServerRoomSettingsComponent**：房间基础设置与成员快照。
- **ServerObjectSyncComponent**：权威物理与动画同步底座，按 `EntitySyncMask` 掩码打包同步帧。
- **ServerSocialRoomComponent**：交友房间业务逻辑（移动验证、动作播放、聊天气泡）。

## 6.4 Server/Infrastructure 的功能
- **流式录像存储**：`ServerReplayStorage`，采用 `GZipStream` 边打边压直接落盘，0 I/O 元数据读取。
- **运行时超时治理**：空房间回收、离线 Session 过期治理、录像滚动清理。

---

# 7. Client 层功能说明

## 7.1 Client/Core 的功能
- **ClientApp**：客户端运行时总门面，管理 `ClientAppState` (Idle/Online/Replay/Suspended)。
- **ClientSession**：保存本地会话信息与断线恢复上下文。
- **ClientRoom**：客户端持有的房间实例，挂载 `RoomNetEventSystem` 沙盒化事件总线。
- **ClientReplayPlayer**：基于 `FileStream` 与 `GZipStream` 的本地回放播放器。

## 7.2 Client/Modules 与 Components
- **ClientModules**：处理全局 `S2C` 协议（登录结果、大厅列表、录像分块下载写入）。
- **ClientComponents**：处理房间 `S2C` 协议（成员镜像、`ObjectSync` 缓存与航位推测预测）。

---

# 8. Editor 工具层功能说明
- **LiteProtocolScanner**：扫描冲突，生成 `MsgIdConst`、`ComponentIdConst` 与 `AutoRegistry`。
- **NetPrefabScanner**：扫描预制体，生成稳定的路径 Hash 常量表。
- **StellarNetScaffoldWindow**：一键生成符合现行架构口径的业务骨架。
- **NetConfigEditorWindow**：图形化编辑网络配置。

---

# 9. Config 配置系统说明
支持两类位置：`StreamingAssets` 和 `PersistentDataPath`。
覆盖参数包括：IP、端口、TickRate、最大房间寿命、录像保留数、各类离线超时时间、最低客户端版本拦截等。

---

# 10. 核心网络通信链路说明

## 10.1 客户端发包链
View 采集输入 -> `NetClient.Send<T>()` -> 查元数据校验方向 -> 递增 Seq -> 申请 `ArrayPool` 0GC 序列化 -> 组装 `Packet` -> 交由 `INetworkTransport` 发送。

## 10.2 服务端收发链
`INetworkTransport` 抛出数据 -> `ServerApp` 切片解析 -> Seq 防重放 -> `Dispatcher` 路由 -> 业务模块处理 -> `ArrayPool` 0GC 序列化 S2C 协议 -> `INetworkTransport` 广播/单播 -> 同步进入 `GZipStream` 录像流。

## 10.3 客户端收包链
`INetworkTransport` 抛出数据 -> `ClientApp` 切片解析 -> 状态机合法性过滤 -> 分发至 Component -> **无侵入直抛协议对象** -> View 监听刷新。

---

# 11. 房间系统与二段握手
建房/加房成功后，服务端不会立刻下发快照，而是：
1. 服务端授权进入。
2. 客户端本地装配房间组件。
3. 客户端发送 `C2S_RoomSetupReady`。
4. 服务端正式加入成员并下发快照。

---

# 12. 回放系统功能说明
回放系统已实现生产级重构：
- **录制**：服务端按房间广播录制协议帧，直接写入 `GZipStream` 落盘，不占用大块内存。
- **下载**：客户端发起分块下载请求，服务端按 Chunk 读取 `FileStream` 下发，客户端追加写入临时文件（支持断点续传）。
- **播放**：客户端创建本地回放沙盒，通过 `BinaryReader` 直接读取解压流，按 Tick 重演历史消息。

---

# 13. 断线重连系统功能说明
包含：弱网底层 RTT 监控主动熔断 -> 服务端保留 Session -> 登录后识别可恢复房间 -> 客户端确认重连 -> 客户端本地装配沙盒 -> 发送 `C2S_ReconnectReady` -> 服务端触发 `OnSendSnapshot()` 状态对齐。

---

# 14. 协议与事件系统功能说明
- **协议系统**：基于特性与元数据映射，支持二进制与 JSON 混合序列化。
- **事件系统**：`GlobalTypeNetEvent` 与 `RoomNetEventSystem`，支持 0GC 协议直抛，并提供 `.UnRegisterWhenGameObjectDestroyed` 链式生命周期绑定。

---

# 15. 日志与治理能力说明
- **NetLogger**：统一输出模块名、RoomId、SessionId 上下文。
- **GC 治理**：长时间离线 Session 清理、空房间回收、超时房间销毁、结束房间清理、Replay 文件滚动清理。

---

# 16. 当前框架边界与未覆盖项
**不负责的内容**：高竞技 PVP 公平性、回滚预测、强物理权威同步、命中判定反作弊、分布式服务治理、大型 MMO 级别后台能力。
**承载边界**：单服 100~300 CCU，适合中小型房间制联机项目、Demo 到商业化早中期迭代。

---

# 17. 总结
StellarNet Lite 当前已经形成了一套相对完整的中小型房间制联机底座，其真正的价值在于：**这些能力已经能形成一条清晰、可维护、可横向扩展、高性能（0GC/流式处理）的工程链。**