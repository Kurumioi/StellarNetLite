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
5. **回放沙盒隔离**
6. **断线重连可恢复**
7. **编辑器工具辅助治理**
8. **配置化而不是满地硬编码**

它不是“全自动状态同步黑盒”，而是一套**可讲清楚、可追踪、可排障、可横向扩展**的工程化底座。

---

# 3. 当前框架已经解决了什么问题
当前版本已经系统性解决了这些核心问题：

---

## 3.1 解决了协议流转不透明的问题
通过：
- `[NetMsg]`
- `NetMessageMapper`
- `AutoRegistry` (0反射装配)
- 强类型发送器 (`NetClient.Send<T>`)
  框架把协议的：
- ID
- Scope
- Dir
- 发送入口
- 接收入口
- Handler 绑定
  统一收口，避免多人协作时协议流转变得不可追踪。

---

## 3.2 解决了房间功能膨胀成巨石类的问题
通过：
- `Room`
- `RoomComponent`
- `ServerRoomFactory`
- `ClientRoomFactory`
- `ComponentIds` 动态装配
  框架把房间逻辑拆成多个横向扩展组件，避免所有玩法都堆到一个房间主类中。

---

## 3.3 解决了客户端 View 和网络层互相污染的问题
通过：
- `ClientApp`
- `ClientModule`
- `ClientRoomComponent`
- `GlobalTypeNetEvent`
- `RoomNetEventSystem`
  框架建立了：
- 协议接入层
- 轻状态层
- 表现层
  三段式解耦，避免 UI 直接解析网络包，同时通过**无侵入式协议直抛**省去了冗余的中间 DTO 转换。

---

## 3.4 解决了回放与在线房间串线的问题
通过：
- `ClientAppState.Idle / OnlineRoom / ReplayRoom`
- `ClientReplayPlayer`
- 回放模式禁止真实发包
- 回放模式拦截真实房间包
- 本地回放房间独立装配
  框架把回放定义为独立沙盒，不再把回放当成在线房间的附属标记位。

---

## 3.5 解决了房间进入时序错乱的问题
通过：
- `S2C_CreateRoomResult / S2C_JoinRoomResult`
- 客户端先本地装配
- `C2S_RoomSetupReady`
- 服务端收到 Ready 后才正式加房
  框架采用二段式握手，避免组件未装好时快照先到。

---

## 3.6 解决了重复请求和旧包重入的问题
通过：
- 客户端自动递增 `Seq`
- 服务端 Session 记录 `LastReceivedSeq`
- 旧包 / 重复包直接拦截
  框架内置了会话级防重放能力。

---

## 3.7 解决了协议与组件 ID 管理混乱的问题
通过：
- `LiteProtocolScanner`
- 自动扫描 `[NetMsg]` 与 `[RoomComponent]`
- 双端冲突检测与阻断
- 自动生成 `MsgIdConst.cs` 与 `ComponentIdConst.cs`
  框架把网络标识的管理从“人工维护 Excel 表与死记硬背魔数”推进成了编译期自动生成与辅助治理的闭环流程。

---

## 3.8 解决了房间和录像文件长期堆积的问题
通过：
- 空房间超时清理
- 离线 Session 超时清理
- Replay 文件滚动清理
- 最大房间寿命控制
  框架已具备基础治理能力，而不是只管联机不管运行期收尾。

---

# 4. 总体分层说明
StellarNet Lite 当前按以下维度分层：

---

## 4.1 Shared 层
Shared 负责双端共享的基础契约和底座能力，包括：
- 协议定义 (纯净的 `class`)
- 网络封套
- Replay 结构
- 元数据特性
- 消息映射器
- 基础配置结构
- 序列化器接口与默认实现
- 自动绑定器所依赖的公共定义

一句话：
> **Shared 共享的是“契约”和“最基础能力”，绝不包含任何客户端表现层的事件定义。**

---

## 4.2 Server 层
Server 负责所有权威逻辑和运行时治理，包括：
- Session 管理
- Room 管理
- 权威状态修改
- 房间组件装配
- 协议路由
- Replay 记录与存储
- GC 与超时销毁
- 重连恢复
- 对客户端请求做合法性校验

一句话：
> **Server 是真相来源，也是风险控制中心。**

---

## 4.3 Client 层
Client 负责客户端网络接入、轻状态镜像和表现桥接，包括：
- 客户端状态机
- 在线房间 / 回放房间切换
- 全局消息接入
- 房间消息接入
- 轻状态缓存
- **事件系统 (GlobalTypeNetEvent / RoomNetEventSystem)**
- Replay 播放器
- View 输入桥接

一句话：
> **Client 负责“接权威结果”和“组织表现”，不负责定义真相。**

---

## 4.4 Editor 层
Editor 层负责开发效率和治理工具，包括：
- 网络配置编辑窗口
- 协议扫描器
- 预制体 Hash 扫描器
- `MsgIdConst.cs` 生成
- 脚手架模板生成
- 开发期工具辅助

一句话：
> **Editor 层不是运行时逻辑，而是帮助团队降低接入成本和犯错概率。**

---

# 5. Shared 层功能说明
Shared 层是整个框架的公共基础。  
它的内容会同时被 Server 和 Client 依赖。

---

## 5.1 Shared/Core 的功能
### 5.1.1 协议元数据系统
包括：
- `NetScope`
- `NetDir`
- `NetMsgAttribute`
- `NetHandlerAttribute`
  这套系统负责定义：
- 一个协议属于 Global 还是 Room
- 一个协议是 C2S 还是 S2C
- 一个方法是否是可自动绑定的消息处理函数
  这相当于协议系统的“语法层”。

---

### 5.1.2 统一传输封套
包括：
- `Packet`
  `Packet` 负责承载运行时公共上下文：
- `Seq`
- `MsgId`
- `Scope`
- `RoomId`
- `Payload`
  这样协议类本身只需要专注于业务字段，不需要承担传输上下文。

---

### 5.1.3 Replay 基础结构
包括：
- `ReplayFrame`
- `ReplayFile`
  它们负责表达：
- 一帧回放消息
- 一份完整回放文件
  这为服务端录制和客户端本地重演提供共同的数据结构基础。

---

### 5.1.4 协议映射中心
包括：
- `NetMessageMapper`
  它在启动时扫描所有 `[NetMsg]` 类型，建立：
- 协议类型 -> 元数据
- 元数据 -> `Id / Scope / Dir`
  这一步是：
- 强类型发送器
- AutoRegistry
- 协议扫描工具
- 调试日志
- 收发路由
  共同依赖的基础。

---

## 5.2 Shared/Infrastructure 的功能
### 5.2.1 网络配置结构
包括：
- `NetConfig`
- `NetConfigLoader`
  它们负责定义和加载统一的网络配置，例如：
- IP
- Port
- MaxConnections
- TickRate
- MaxRoomLifetimeHours
- MaxReplayFiles
- OfflineTimeoutLobbyMinutes
- OfflineTimeoutRoomMinutes
- EmptyRoomTimeoutMinutes
- MinClientVersion

---

### 5.2.2 默认序列化器
包括：
- `INetSerializer`
- `JsonNetSerializer`
  它们提供了统一序列化接口和基于 `Newtonsoft.Json` 的默认实现。
  这意味着你可以：
- 直接用默认 JSON 序列化
- 也可以后续替换成更高性能的自定义序列化器 (如 MessagePack)

---

### 5.2.3 Mirror 封装结构
包括：
- `MirrorPacketMsg`
  它负责把框架的 `Packet` 结构包装进 Mirror 的网络消息传输链。

---

### 5.2.4 统一日志入口
包括：
- `NetLogger`
  它统一了日志输出格式与上下文附带能力，包括：
- 模块名
- RoomId
- SessionId
- 补充上下文
  这样在多人联机排障时，日志不再是随意拼出来的无上下文字符串。

---

## 5.3 Shared/Binders 的功能
### 5.3.1 AutoRegistry
`AutoRegistry` 负责：
- 注册服务端与客户端的 Component 工厂
- 绑定 `[NetHandler]` 到对应的 Dispatcher
  它解决的问题是：
- 彻底消灭运行时的反射开销 (0反射)
- 不必手写一大堆 `switch(msgId)`
- 模块接线统一

---

# 6. Server 层功能说明
Server 层承载所有权威逻辑与治理能力，是框架中最核心的运行时层。

---

## 6.1 Server/Core 的功能
### 6.1.1 ServerApp
`ServerApp` 是服务端总入口，负责：
- 初始化服务端环境
- 管理 Session
- 管理房间集合
- 接收客户端 `Packet`
- 执行 Seq 防重放
- 按 `Scope` 路由到全局或房间处理链
- 对客户端发消息
- 驱动房间更新与超时治理
  可以把它理解成：
> **服务端网络运行时总门面。**

---

### 6.1.2 Session
`Session` 表示一个服务端权威会话对象，负责记录：
- `SessionId`
- 用户身份
- 当前连接映射
- 当前是否在线
- 当前房间归属
- 上次接收的 Seq
- 重连授权状态
- 离线时间
  它是：
- 防重放
- 房间归属校验
- 断线重连
- 单播消息目标
  这些逻辑的关键上下文。

---

### 6.1.3 Room
`Room` 是房间运行时容器，负责：
- `RoomId`
- 成员列表
- 房间状态
- 组件集合
- 房间事件分发
- 房间当前 Tick
- Replay 录制
- 房间广播 / 单播
- 成员加入 / 离开 / 离线 / 上线通知
- 开始游戏 / 结束游戏
- 房间销毁
  它本身不是玩法巨石类，而是：
> **房间作用域容器 + 生命周期主控 + 广播入口。**

---

### 6.1.4 GlobalDispatcher / RoomDispatcher
服务端分发器负责把 `Packet` 转到正确的处理节点。
#### GlobalDispatcher
负责路由：
- `NetScope.Global`
- `NetDir.C2S`
  例如：登录、建房、加房、离房、录像列表请求等。

#### RoomDispatcher
负责路由：
- `NetScope.Room`
- `NetDir.C2S`
  例如：Ready、房间聊天、表情请求、移动指令等。

---

### 6.1.5 ServerRoomFactory
它负责：
- 注册 `ComponentId -> 组件构造器`
- 根据 `ComponentIds` 装配房间组件
- 绑定消息处理函数
- 执行初始化
- 失败时回滚已装配内容

---

### 6.1.6 RoomComponent
`RoomComponent` 是所有服务端房间组件的基类。  
它提供统一生命周期回调，例如：
- `OnInit`
- `OnMemberJoined`
- `OnMemberLeft`
- `OnMemberOffline`
- `OnMemberOnline`
- `OnSendSnapshot`
- `OnGameStart`
- `OnGameEnd`
- `OnDestroy`

---

## 6.2 Server/Modules 的功能
这一层承载全局域业务逻辑。
### 当前已覆盖的典型模块包括：
#### 6.2.1 ServerUserModule
负责：登录、会话建立、版本校验、重连协商。
#### 6.2.2 ServerRoomModule
负责：建房、加房、离房、房间列表、进房握手、房间装配授权。
#### 6.2.3 ServerLobbyModule
负责：大厅类全局逻辑、房间列表同步。
#### 6.2.4 ServerReplayModule
负责：录像列表、录像下载、重命名、Replay 文件读取。

---

## 6.3 Server/Components 的功能
这一层承载房间域业务逻辑。
### 当前已覆盖的典型组件包括：
#### 6.3.1 ServerRoomSettingsComponent
负责房间基础设置与成员快照，例如：
- 成员列表与 Ready 状态
- 游戏开始 / 结束
- 快照同步

#### 6.3.2 ServerObjectSyncComponent
负责权威状态同步，例如：
- 维护 Transform 与 Animator 的权威状态
- 按 EntitySyncMask 掩码打包同步帧

#### 6.3.3 ServerSocialRoomComponent
负责交友房间业务逻辑，例如：
- 玩家生成与销毁
- 移动验证与动作播放
- 聊天气泡广播

---

## 6.4 Server/Infrastructure 的功能
### 6.4.1 Replay 文件存储
负责：
- 保存房间录像 (采用 GZip 压缩)
- 采用 UrlSafe Base64 编码文件名，实现 0 I/O 元数据读取
### 6.4.2 录像滚动清理
负责：每次保存后清理超量旧录像，保留最新 `MaxReplayFiles`。
### 6.4.3 运行时超时治理
负责：空房间回收、超时房间销毁、离线 Session 过期治理。

---

# 7. Client 层功能说明
Client 层不是权威逻辑层，而是客户端网络运行时与表现桥接层。

---

## 7.1 Client/Core 的功能
### 7.1.1 ClientApp
`ClientApp` 是客户端运行时总门面，负责：
- 当前状态机
- 全局发包入口
- 收包入口
- 全局 / 房间消息路由
- 在线房间与回放房间切换
- 房间进入 / 离开
- 回放模式阻断真实房间包
- 回放模式阻断真实房间发包

---

### 7.1.2 ClientSession
负责保存客户端本地会话信息，例如：
- 当前 `SessionId`
- 当前用户基础信息
- 当前房间绑定信息
- 是否存在可恢复房间

---

### 7.1.3 ClientRoom
表示当前客户端持有的房间实例，负责：
- `RoomId`
- 组件集合
- `RoomNetEventSystem` (沙盒化事件总线)
- 房间消息分发
- 轻状态桥接
- 房间作用域生命周期

---

### 7.1.4 ClientRoomFactory
负责：注册客户端房间组件构造器、按 `ComponentIds` 装配客户端组件、自动绑定 `[NetHandler]`。

---

### 7.1.5 ClientReplayPlayer
负责：
- 加载 Replay 文件
- 创建本地回放房间
- 按 Tick 播放历史帧
- 暂停 / 恢复 / 倍速 / Seek

---

### 7.1.6 ClientGlobalDispatcher / ClientRoomDispatcher
负责：接收 `S2C` 消息，根据 `MsgId` 找到绑定的处理函数，调用对应 ClientModule / ClientRoomComponent。

---

## 7.2 Client/Modules 的功能
这一层处理全局 `S2C` 协议。
### 当前已覆盖的典型模块包括：
#### 7.2.1 ClientUserModule
负责：登录结果处理、用户信息同步、重连提示与恢复链入口。
#### 7.2.2 ClientRoomModule
负责：建房结果、加房结果、离房结果、进房与本地装配握手。
#### 7.2.3 ClientLobbyModule
负责大厅级全局结果接入。
#### 7.2.4 ClientReplayModule
负责：Replay 列表结果、Replay 分块下载、内存 GZip 解压。

---

## 7.3 Client/Components 的功能
这一层处理房间域 `S2C` 协议。
### 当前已覆盖的典型组件包括：
#### 7.3.1 ClientRoomSettingsComponent
负责：房间成员列表镜像、Ready 状态镜像、游戏开始 / 结束结果、房间快照事件转发。
#### 7.3.2 ClientObjectSyncComponent
负责：接收服务端 ObjectSync 帧，缓存并预测 Transform 与 Animator 数据。
#### 7.3.3 ClientSocialRoomComponent
负责：交友房间业务同步接入，如聊天气泡直抛。

---

# 8. Editor 工具层功能说明
Editor 工具层是当前框架非常重要的一部分。  
它不参与运行时联机，但直接影响团队的接入效率和犯错概率。

---

## 8.1 LiteProtocolScanner
功能包括：
1. 扫描所有 `[NetMsg]` 与 `[RoomComponent]` 特性
2. 检查 `MsgId` 与 `ComponentId` 是否存在双端冲突或命名错位
3. 辅助生成 `MsgIdConst.cs` 与 `ComponentIdConst.cs`
4. 生成 0 反射装配代码 `AutoRegistry.cs`

---

## 8.2 NetPrefabScanner
功能包括：
1. 扫描 Resources 下的网络预制体
2. 计算稳定的路径 Hash
3. 生成 `NetPrefabConsts.cs`，避免硬编码路径

---

## 8.3 NetConfigEditorWindow
功能包括：图形化编辑 `NetConfig`，保存到 `StreamingAssets` 或 `PersistentDataPath`。

---

## 8.4 StellarNetScaffoldWindow
功能包括：一键生成 Shared 协议模板、Server 组件/模块模板、Client 组件/模块模板，统一架构口径。

---

# 9. Config 配置系统说明
Config 系统当前主要由 `NetConfig` 与 `NetConfigLoader` 构成。

---

## 9.1 配置项覆盖范围
当前配置已经覆盖的典型内容包括：
- 服务 IP / 端口 / 最大连接数 / Tick 频率
- 最大房间寿命 / 最大录像文件数量
- 大厅离线超时 / 房间离线超时 / 空房间超时
- 最低客户端版本

---

## 9.2 配置读取路径
支持两类位置：`StreamingAssets` 和 `PersistentDataPath`。

---

# 10. 核心网络通信链路说明
当前网络链路可以概括为：

---

## 10.1 客户端发包链
1. View 采集输入
2. 调用 `NetClient.Send<T>()`
3. 自动查 `NetMessageMapper`
4. 自动校验方向必须是 `C2S`
5. 自动判断是 Global 还是 Room
6. 自动递增 `Seq`
7. 自动绑定 `RoomId`
8. 包装成 `Packet`
9. 通过 Mirror 发给服务端

---

## 10.2 服务端收包链
1. Mirror 收到 `MirrorPacketMsg`
2. 转成 `Packet`
3. `ServerApp.OnReceivePacket()`
4. 先执行 Seq 防重放
5. 根据 `Scope` 分流：Global -> `GlobalDispatcher` / Room -> `RoomDispatcher`
6. 找到对应 Handler
7. 业务模块校验并处理
8. 修改权威状态
9. 给客户端返回 `S2C`

---

## 10.3 服务端发包链
1. Global 业务结果走 `ServerApp.SendMessageToSession<T>()`
2. 房间广播走 `Room.BroadcastMessage<T>()`
3. 房间单播走 `Room.SendMessageTo<T>()`
4. 自动查 `NetMessageMapper`
5. 自动绑定 `MsgId / Scope / RoomId`
6. 通过 Mirror 发给客户端
7. 如果是房间广播，同时进入 Replay 记录链

---

## 10.4 客户端收包链
1. Mirror 收到服务端消息
2. 转成 `Packet`
3. `ClientApp.OnReceivePacket()`
4. 根据当前状态做合法性过滤
5. 按 `Scope` 分流
6. 模块或组件接收消息，更新轻状态
7. **直接抛出协议对象 (无侵入直抛)**
8. View 监听协议事件刷新表现

---

# 11. 房间系统功能说明
房间系统是当前框架最核心的业务宿主层。

---

## 11.1 房间系统当前具备的能力
包括：房间创建 / 加入 / 离开 / 成员管理 / 状态管理 / 组件化装配 / 生命周期推进 / 广播与单播 / 快照 / 回放录制 / GC / 重连恢复。

---

## 11.2 房间进入采用二段握手
当前框架不是“建房成功就直接算进房”，而是：
1. 服务端授权进入
2. 客户端本地装房间
3. 客户端发送 `C2S_RoomSetupReady`
4. 服务端正式加入成员

---

# 12. 回放系统功能说明
回放系统当前不是附属能力，而是框架正式能力的一部分。

---

## 12.1 当前回放系统已具备的能力
包括：服务端按房间广播录制 ReplayFrame / 录像文件 GZip 保存 / 录像列表读取 / 客户端断点下载录像 / 客户端本地装回放房间 / 按 Tick 重演历史房间消息 / 暂停 / 恢复 / 倍速 / Seek / 退出回放。

---

## 12.2 录制内容是什么
录制的不是 Transform 轨迹，也不是整个场景快照。  
录制的是：> **房间广播出去的协议帧。**

---

# 13. 断线重连系统功能说明
当前框架已经支持基于 Session 的断线重连恢复链。

---

## 13.1 当前已覆盖的重连能力
包括：弱网底层 RTT 监控主动熔断 / 服务端保留 Session / 登录后识别是否存在可恢复房间 / 客户端确认是否接受重连 / 服务端返回 `RoomId + ComponentIds` / 客户端先本地装房间 / 客户端发 `C2S_ReconnectReady` / 服务端触发房间组件 `OnSendSnapshot()` / 客户端恢复房间上下文。

---

# 14. 协议系统功能说明
协议系统是当前框架最重要的“契约层”。

---

## 14.1 当前协议系统已经具备的能力
包括：协议特性化定义 / 全局与房间作用域区分 / C2S 与 S2C 方向区分 / 启动期扫描 / 元数据映射 / 自动生成消息常量 / 自动绑定处理函数 / 强类型发送器自动查元数据。

---

# 15. 事件系统功能说明
当前客户端事件系统已经按作用域拆分为两层，并实现了**无侵入式协议直抛**。

---

## 15.1 GlobalTypeNetEvent
适合：大厅、登录、房间列表、Replay 列表、下载完成通知。
它负责全局层面的内部解耦，基于泛型静态类，实现 0GC 极速派发。

---

## 15.2 RoomNetEventSystem
适合：房间快照、局内同步、成员变化、HP 变化、表情、结算。
它挂载在 `ClientRoom` 实例上，负责某个房间内部的事件传播，确保回放沙盒与在线房间绝对隔离。

---

# 16. 日志与治理能力说明
当前框架不只关心“能跑”，也已经具备了基础治理能力。

---

## 16.1 NetLogger
统一日志输出上下文，便于查房间问题、查 Session 问题、查装配失败、查回放和重连流程。

---

## 16.2 GC 与清理
负责处理：长时间离线 Session 清理、空房间回收、超时房间销毁、结束房间清理、Replay 文件滚动清理。

---

# 17. GameDemo 与示例覆盖范围说明
`GameDemo` 不是框架核心层，而是示例业务层。

---

## 17.1 当前 Demo 已覆盖的能力
包括：登录、建房、加房、离房、房间成员快照、Ready 切换、房主开始/结束游戏、交友房间移动与动作、聊天气泡、游戏结束、Replay 下载与播放、Replay 倍速与拖动、断线重连、服务端状态面板观察。

---

# 18. 当前框架边界与未覆盖项
这份文档也必须把边界说清楚，避免团队误以为框架已经“无所不能”。

---

## 18.1 当前框架不负责的内容
包括：高竞技 PVP 公平性、回滚预测、强物理权威同步、命中判定反作弊、分布式服务治理、大型 MMO 级别后台能力、自动玩法恢复策略、复杂表现层框架。

---

## 18.2 当前框架的实现边界 (承载预估)
基于 Unity 主线程 Tick 与 JSON 序列化的当前架构，单服理论承载约为 **100~300 CCU**。单房间支持 **4~16 人**（高频空间/动画同步，如交友房间）或 **50+ 人**（低频状态同步，如棋牌/回合制）。

当前已经足够支撑：
- 中小型房间制联机项目
- Demo 到商业化早中期迭代
- 多人协作开发
- Replay / Reconnect / RoomGC 等生产级基础链路

---

# 19. 推荐阅读顺序
如果你是第一次接项目，建议按下面顺序读：
1. `README.md`
2. `Docs/StellarNet Literal 功能说明文档.md`
3. `Docs/StellarNet Lite 开发者使用手册 v1.0.md`
4. `Docs/StellarNet Lite 快速接入指南.md`

---

# 20. 总结
StellarNet Lite 当前已经形成了一套相对完整的中小型房间制联机底座，能力覆盖包括：
- Shared / Server / Client 三层分离
- 协议系统 / 强类型发送器 / 0反射装配
- 房间组件化 / 二段进房握手
- 回放录制与播放 / 断线重连恢复
- Session / Room / Replay GC
- 弱网主动熔断
- 编辑器协议扫描器 / 预制体扫描器 / 脚手架生成器

它真正的价值不是“功能列表很多”，而是：
> **这些能力已经能形成一条清晰、可维护、可横向扩展、可持续迭代的工程链。**