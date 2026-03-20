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
- [11. 房间系统与装配机制说明](#11-房间系统与装配机制说明)
- [12. 对象同步系统说明](#12-对象同步系统说明)
- [13. 回放系统功能说明](#13-回放系统功能说明)
- [14. 断线重连系统功能说明](#14-断线重连系统功能说明)
- [15. 协议与事件系统功能说明](#15-协议与事件系统功能说明)
- [16. UI 路由与表现层解耦说明](#16-ui-路由与表现层解耦说明)
- [17. 日志与治理能力说明](#17-日志与治理能力说明)
- [18. 当前框架边界与未覆盖项](#18-当前框架边界与未覆盖项)
- [19. 总结](#19-总结)

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

如果你是主程或框架维护者，这份文档更像一份**当前版本能力清单**。

---

# 2. 框架整体定位

StellarNet Lite 是一个面向 Unity 中小型联机项目的轻量级房间网络框架。

它主要服务于以下类型项目：

- 房间制 / 对局制
- 中小型商业项目
- 长期维护型多人联机玩法
- 社交元宇宙 / 棋牌 / 休闲竞技 / 小型副本

核心定位非常明确：

1. **服务端绝对权威**
2. **协议事件驱动**
3. **房间组件化装配**
4. **客户端表现解耦**
5. **流式回放沙盒隔离与录像管理**
6. **断线重连可恢复**
7. **编辑器工具辅助治理**
8. **配置化而不是满地硬编码**
9. **底层网络库防腐隔离（`INetworkTransport`）**

它不是“全自动状态同步黑盒”，而是一套：

> **可讲清楚、可追踪、可排障、可横向扩展的工程化联机底座。**

---

# 3. 当前框架已经解决了什么问题

当前版本已经系统性解决了这些核心问题。

---

## 3.1 协议流转不透明的问题

通过：

- `[NetMsg]`
- `NetMessageMapper`
- `AutoMessageMetaRegistry`
- `AutoRegistry`
- 强类型发送器 `NetClient.Send<T>()`

框架把协议的：

- ID
- Scope
- Dir
- 发送入口
- 接收入口

全部收口，避免多人协作时协议流转变得不可追踪。

---

## 3.2 房间功能膨胀成巨石类的问题

通过：

- `Room`
- `RoomComponent`
- `ComponentIds`
- 房间模板 `RoomTypeTemplateRegistry`

框架把房间逻辑拆成多个横向扩展组件，避免所有玩法都堆到一个房间主类中。

---

## 3.3 客户端 View 和网络层互相污染的问题

通过：

- `ClientModule`
- `ClientRoomComponent`
- `GlobalTypeNetEvent`
- `RoomNetEventSystem`
- `RoomUIRouterBase<T>`

框架建立了：

- 协议接入层
- 轻状态层
- 表现层

三段式解耦。

同时引入：

- `UGUIFollowTarget`

把 UI 跟随逻辑从业务脚本里剥离出来。

---

## 3.4 回放与在线房间串线的问题

通过：

- `ClientApp.EnterReplayRoom`
- `ClientReplayPlayer`
- `RoomNetEventSystem`
- 回放房间独立组件装配

框架把回放定义为**独立沙盒房间**，不再把回放当成在线房间附属状态。

---

## 3.5 GC 与内存抖动问题

通过：

- `ArrayPool<byte>` 发包
- `Packet.PayloadOffset` 切片
- `ArraySegment<byte>` 底层传输承接
- `ILiteNetSerializable`
- `FileStream` 分块下载
- `GZipStream` 边录边压

框架显著降低了：

- 高频发包 GC
- 大文件回放内存峰值
- 中间对象创建量

---

## 3.6 底层网络库强绑定问题

通过：

- `INetworkTransport`

框架核心逻辑与 Mirror 解耦。  
Mirror 当前只是默认传输实现，而不是整个框架不可替换的一部分。

---

## 3.7 回放 Seek 时对象世界难以恢复的问题

通过：

- `ObjectSpawnState`
- `ReplayObjectSnapshotFrame`
- `ServerObjectSyncComponent.ExportSpawnStates()`
- `ClientObjectSyncComponent.ApplyReplaySnapshot()`

框架已经支持：

- 普通消息帧重演
- 对象关键帧恢复
- 中段 Seek 先恢复对象世界，再补后续消息

这使回放系统从“能播”升级为“可稳定跳转与恢复”。

---

# 4. 总体分层说明

StellarNet Lite 当前按以下维度分层。

---

## 4.1 Shared 层

Shared 负责双端共享的基础契约和底座能力，包括：

- 协议定义
- 网络封套
- 元数据特性
- 序列化抽象
- 自动绑定器公共定义
- 回放共享结构
- 预制体常量
- 组件常量

> **Shared 共享的是契约和底层结构，不包含业务 UI 表现逻辑。**

---

## 4.2 Server 层

Server 负责所有权威逻辑和运行时治理，包括：

- Session 管理
- Room 管理
- 权威状态修改
- 房间组件装配
- Replay 录制与重命名
- GC
- 重连恢复

> **Server 是真相来源，也是风险控制中心。**

---

## 4.3 Client 层

Client 负责客户端网络接入、轻状态镜像和表现桥接，包括：

- 状态机
- 事件系统
- 回放播放器
- 房间实例
- 表现桥接
- UI 路由

> **Client 负责接权威结果和组织表现，不负责定义真相。**

---

## 4.4 Editor 层

Editor 层负责开发效率和治理工具，包括：

- 协议扫描器
- 组件扫描器
- 预制体扫描器
- 脚手架生成器
- 配置窗口

> **Editor 层不是运行时逻辑，而是团队开发流程的一部分。**

---

# 5. Shared 层功能说明

---

## 5.1 Shared/Core 的功能

- **协议元数据系统**
    - `NetScope`
    - `NetDir`
    - `NetMsgAttribute`

- **统一传输封套**
    - `Packet`
    - 包含 `Seq`、`MsgId`、`Scope`、`RoomId`、`PayloadOffset`、`PayloadLength`

- **协议映射中心**
    - `NetMessageMapper`
    - Runtime 读取静态注册表

- **传输层防腐接口**
    - `INetworkTransport`

- **组件与模块元数据**
    - `RoomComponentMeta`
    - `GlobalModuleMeta`

---

## 5.2 Shared/Infrastructure 的功能

- **配置结构**
    - `NetConfig`
    - `NetConfigLoader`

- **混合序列化器**
    - `INetSerializer`
    - `LiteNetSerializer`

- **统一日志入口**
    - `NetLogger`

- **Mirror 封套桥接**
    - `MirrorPacketMsg`

---

## 5.3 Shared/Protocol 的功能

- 全局协议
- 房间协议
- 对象同步协议
- 常量表
- 预制体 Hash 表

当前对象同步协议已经明确分成：

- `ObjectSpawnState`
    - 完整生成态
- `ObjectSyncState`
    - 增量同步态

---

## 5.4 Shared/Replay 的功能

- `ReplayFrameKind`
- `ReplayFormatDefines`
- `ReplayObjectSnapshotFrame`

这部分是当前回放关键帧能力的核心共享结构。

---

## 5.5 Shared/Binders 的功能

- `AutoRegistry`
- `RoomTypeTemplateRegistry`

其中：

- `AutoRegistry` 负责静态装配聚合
- `RoomTypeTemplateRegistry` 负责房间模板语义注册

---

# 6. Server 层功能说明

---

## 6.1 Server/Core 的功能

- **ServerApp**
    - 服务端总门面
    - Session / Room 管理
    - Seq 防重放

- **Session**
    - 权威会话对象
    - 记录连接映射、在线状态、房间授权、Seq

- **Room**
    - 房间作用域容器
    - 生命周期主控
    - 广播入口
    - Replay 录制驱动

- **Dispatcher**
    - `GlobalDispatcher`
    - `RoomDispatcher`

- **ServerRoomFactory**
    - 房间组件工厂
    - 两阶段装配与失败回滚

- **ITickableComponent**
    - 可 Tick 组件接口

---

## 6.2 Server/Modules 的功能（全局域）

当前全局模块包括：

- `ServerUserModule`
    - 登录
    - 版本校验
    - 顶号与重连恢复

- `ServerRoomModule`
    - 建房
    - 加房
    - 离房
    - 房间装配就绪握手

- `ServerLobbyModule`
    - 大厅房间列表

- `ServerReplayModule`
    - 录像列表
    - 分块下载
    - 断点续传
    - 录像重命名

---

## 6.3 Server/Components 的功能（房间域）

当前内置 / 示例组件包括：

- `ServerRoomSettingsComponent`
    - 成员信息
    - 准备状态
    - 房主迁移
    - 开始 / 结束对局

- `ServerObjectSyncComponent`
    - 对象生成 / 销毁
    - 空间与动画同步
    - 对象完整生成态导出
    - 回放关键帧录制支撑

- `ServerSocialRoomComponent`
    - 社交房移动验证
    - 动作播放
    - 聊天气泡

---

## 6.4 Server/Infrastructure 的功能

- `ServerReplayStorage`
    - GZip 流式录制
    - 回放文件保存
    - 录像滚动清理

---

# 7. Client 层功能说明

---

## 7.1 Client/Core 的功能

- **ClientApp**
    - 客户端总门面
    - 管理状态机：
        - `InLobby`
        - `OnlineRoom`
        - `ReplayRoom`
        - `ConnectionSuspended`

- **ClientSession**
    - 保存本地会话信息
    - 保存断线恢复上下文

- **ClientRoom**
    - 客户端房间实例
    - 持有 `RoomNetEventSystem`

- **ClientRoomFactory**
    - 本地房间组件装配

- **ClientReplayPlayer**
    - 回放控制器
    - 支持播放 / 暂停 / 倍速 / Seek

---

## 7.2 Client/Modules 的功能

- `ClientUserModule`
    - 登录结果
    - 重连结果
    - 被踢下线

- `ClientRoomModule`
    - 建房 / 加房 / 离房结果
    - 本地房间装配
    - 首次装配握手

- `ClientLobbyModule`
    - 大厅房间列表

- `ClientReplayModule`
    - 录像列表
    - 下载开始
    - 分块接收
    - 下载完成

---

## 7.3 Client/Components 的功能

- `ClientRoomSettingsComponent`
    - 房间快照
    - 成员进出
    - 准备状态
    - 开始 / 结束

- `ClientObjectSyncComponent`
    - 对象缓存
    - 预测数据查询
    - 应用对象关键帧恢复

- `ClientSocialRoomComponent`
    - 社交房间业务事件接入
    - 绑定不同 Router
    - 驱动对象生成视图

---

## 7.4 Client/Views 的功能

- `ObjectSpawnerView`
    - 根据 `PrefabHash` 实例化对象
- `NetTransformView`
    - 位置平滑与追赶
- `NetAnimatorView`
    - 动画状态同步
- `NetIdentity`
    - 挂接 `NetId` 与 `SyncService`

---

## 7.5 Client/Infrastructure 的功能

- `ClientNetworkMonitor`
    - RTT 监控
    - 弱网提示
    - 主动熔断

- `GlobalUIRouter`
    - 全局 UI 导航

- `RoomUIRouterBase<T>`
    - 房间组件局部 UI 生命周期控制

---

# 8. Editor 工具层功能说明

当前 Editor 工具已经覆盖以下能力。

---

## 8.1 LiteProtocolScanner

负责：

- 扫描 `[NetMsg]`
- 扫描 `[RoomComponent]`
- 扫描 `[ServerModule]` / `[ClientModule]`
- 检查冲突
- 生成分片与聚合表

生成内容包括：

- `MsgIdConst`
- `ComponentIdConst`
- `AutoMessageMetaRegistry`
- `AutoRegistry`
- 各类 Generated Binder 分片

---

## 8.2 NetPrefabScanner

负责：

- 扫描 `Resources/NetPrefabs`
- 生成路径 Hash
- 生成 `NetPrefabConsts`
- 生成 `HashToPathMap`

---

## 8.3 NetConfigEditorWindow

负责：

- 图形化编辑 `NetConfig`
- 选择 `StreamingAssets / PersistentDataPath`
- 保存配置文件
- 提供脏状态可见性

---

## 8.4 StellarNetScaffoldWindow

负责：

- 生成房间组件脚手架
- 生成全局模块脚手架
- 生成 manifest
- 统一打上 `ScaffoldFeature` 标记
- 支持删除与取消托管

---

## 8.5 当前实际菜单入口

当前工程真实菜单路径为：

- `StellarNetLite/强制重新生成协议与组件常量表`
- `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`
- `StellarNetLite/网络配置 (NetConfig)`
- `StellarNetLite/业务脚手架生成器`

---

# 9. Config 配置系统说明

当前配置系统支持两类根路径：

- `StreamingAssets`
- `PersistentDataPath`

配置内容包括：

- IP
- Port
- MaxConnections
- TickRate
- 房间最大寿命
- 录像保留数
- 大厅离线 GC 时间
- 房间离线 GC 时间
- 空房间熔断时间
- 最低客户端版本

当前设计强调：

- 配置可以图形化编辑
- 运行时有默认值兜底
- Android 平台对 StreamingAssets 异步读取做了兼容处理

---

# 10. 核心网络通信链路说明

---

## 10.1 客户端发包链

View 采集输入  
-> `NetClient.Send<T>()`  
-> `NetMessageMapper.TryGetMeta()`  
-> 递增 `Seq`  
-> `ArrayPool<byte>` 序列化  
-> 组装 `Packet`  
-> `INetworkTransport.SendToServer()`

---

## 10.2 服务端收发链

`INetworkTransport` 抛出数据  
-> `ServerApp.OnReceivePacket()`  
-> 绑定 / 创建 `Session`  
-> Seq 防重放  
-> Global / Room Dispatcher  
-> 业务模块处理  
-> 服务端发 `S2C`  
-> 按需同步写入 Replay

---

## 10.3 客户端收包链

`INetworkTransport` 抛出数据  
-> `ClientApp.OnReceivePacket()`  
-> 按 Scope 分流  
-> 进入 `ClientModule / ClientRoomComponent`  
-> 事件直抛  
-> View 层监听刷新

---

# 11. 房间系统与装配机制说明

当前房间系统不是简单的“进房就立即发快照”，而是采用显式装配握手。

---

## 11.1 在线进房流程

1. 客户端请求建房 / 加房
2. 服务端校验成功
3. 服务端返回 `RoomId + ComponentIds`
4. 客户端本地创建 `ClientRoom`
5. `ClientRoomFactory.BuildComponents()`
6. 客户端发送 `C2S_RoomSetupReady`
7. 服务端正式把成员加入房间
8. 服务端触发组件快照下发

---

## 11.2 房间模板机制

当前工程中，建房 UI 已经不再鼓励直接面向裸组件清单，而是建议通过：

- `RoomTypeTemplateRegistry`

把：

- 交友房间
- 副本房间
- 棋牌房间

这类业务概念映射成组件组合。

这样可以降低 UI 层与底层组件装配细节的耦合。

---

# 12. 对象同步系统说明

这是当前框架最关键的基础能力之一。

---

## 12.1 服务端对象同步

`ServerObjectSyncComponent` 负责：

- `SpawnObject`
- `DestroyObject`
- 维护 `ServerSyncEntity`
- 打包 `S2C_ObjectSync`
- 导出 `ObjectSpawnState[]`

---

## 12.2 客户端对象同步

`ClientObjectSyncComponent` 负责：

- 接收对象生成 / 销毁 / 增量同步
- 缓存对象状态
- 对外提供预测查询接口
- 应用回放关键帧

---

## 12.3 完整生成态与增量态的区分

当前对象同步明确拆成两类结构：

- `ObjectSpawnState`
    - 完整生成态
    - 用于在线生成 / 重连恢复 / 回放关键帧

- `ObjectSyncState`
    - 增量态
    - 用于运行时高频刷新

这是当前版本非常重要的架构升级点。

---

## 12.4 客户端对象表现落地

当前对象实例化由：

- `ObjectSpawnerView`

负责，而不是由 `ClientObjectSyncComponent` 直接创建。

表现层组件：

- `NetTransformView`
- `NetAnimatorView`

通过查询同步缓存拉取数据，而不是自己维护权威状态。

---

# 13. 回放系统功能说明

当前回放系统已经是生产可用形态，而不是简单 Demo。

---

## 13.1 录制

服务端：

- 在房间开始录制时创建回放上下文
- 普通消息帧写入 Replay
- 对象关键帧按策略插入 Replay
- 通过 `GZipStream` 压缩落盘

---

## 13.2 下载

客户端：

- 发起 `C2S_DownloadReplay`
- 服务端回复 `S2C_DownloadReplayStart`
- 服务端分块下发 `S2C_DownloadReplayChunk`
- 客户端写入 `.tmp`
- 完成后转存为 `.replay`

支持：

- 分块下载
- 断点续传
- 下载缓存命中

---

## 13.3 播放

`ClientReplayPlayer` 负责：

- 启动回放
- 解压 `.raw`
- 构建稀疏索引
- 构建关键帧索引
- 顺序播放
- 倍速 / 暂停
- `Seek`

---

## 13.4 关键帧恢复

当前回放系统不再只依赖消息重演。

当执行 `Seek` 时：

1. 找最近对象关键帧
2. 重建回放沙盒房间
3. 应用关键帧恢复对象世界
4. 再补后续消息帧

这让中段跳转更稳定，也让对象世界恢复不再依赖从头补播。

---

## 13.5 录像重命名

房主可通过：

- `C2S_RenameReplay`

对最近一段时间内生成的录像做显示名重命名。

---

# 14. 断线重连系统功能说明

当前重连系统覆盖了从物理断开到状态恢复的完整链路。

---

## 14.1 客户端侧

- 在线房间断开后进入 `ConnectionSuspended`
- 自动尝试重连
- 重连超时抛出 `Local_ReconnectTimeout`
- 可选择继续等待或放弃

---

## 14.2 服务端侧

- 旧账号映射旧 `Session`
- 顶号时踢掉旧连接
- 恢复 `Session` 与连接绑定
- 判断是否存在可恢复房间

---

## 14.3 恢复链路

1. 客户端重连成功后重新发送 `C2S_Login`
2. 服务端返回 `HasReconnectRoom`
3. 客户端发送 `C2S_ConfirmReconnect`
4. 服务端返回 `S2C_ReconnectResult`
5. 客户端本地重建房间组件
6. 客户端发送 `C2S_ReconnectReady`
7. 服务端调用各组件 `OnSendSnapshot()`

---

# 15. 协议与事件系统功能说明

---

## 15.1 协议系统

当前协议系统特点：

- 基于 Attribute 标注
- Editor 扫描
- 生成静态元数据
- Runtime 直接读取

支持：

- JSON 常规协议
- `ILiteNetSerializable` 二进制协议

---

## 15.2 事件系统

当前事件系统分为两类：

- `GlobalTypeNetEvent`
    - 全局域事件
- `RoomNetEventSystem`
    - 房间实例级事件

并提供：

- `.UnRegisterWhenGameObjectDestroyed`
- `.UnRegisterWhenMonoDisable`

用于生命周期绑定。

---

# 16. UI 路由与表现层解耦说明

当前工程已经形成两层路由。

---

## 16.1 全局 UI 路由

- `GlobalUIRouter`

负责：

- 登录
- 大厅
- 回放面板
- 状态跌落处理
- 断线回退

---

## 16.2 房间组件 UI 路由

- `RoomUIRouterBase<T>`
- `XXXOnlineUIRouter`
- `XXXReplayUIRouter`

负责：

- 局部 UI 生命周期
- 在线与回放分离
- 组件自己的 UI 自己开、自己关

---

## 16.3 表现层坐标解耦

通过：

- `UGUIFollowTarget`

框架把 UI 跟随从业务脚本中剥离出来，避免业务脚本直接承担坐标换算职责。

---

# 17. 日志与治理能力说明

---

## 17.1 NetLogger

提供统一日志输出格式，包含：

- Level
- Module
- RoomId
- SessionId
- ExtraContext

当前规则里最重要的一点是：

> **Error 日志不允许被宏裁剪。**

---

## 17.2 运行时治理

当前已覆盖：

- 空房间回收
- 离线 Session GC
- 结束房间残留清理
- 录像文件滚动清理
- 弱网 RTT 检测
- 主动熔断

---

## 17.3 编辑器治理

当前已覆盖：

- 协议冲突扫描
- 组件冲突扫描
- 预制体 Hash 冲突扫描
- 脚手架托管清单
- 配置编辑

---

# 18. 当前框架边界与未覆盖项

当前框架**不负责**：

- 回滚同步
- 预测纠正型高竞技战斗底座
- 强反作弊体系
- 分布式服务治理
- MMO 级大世界同步
- 大规模跨服路由

当前框架更适合作为：

- 中小型房间制项目底座
- 商业化早中期联机项目底座
- 需要长期维护的多人玩法架构基础

---

# 19. 总结

StellarNet Lite 当前已经形成了一条比较完整的工程链：

- **服务端权威**
- **协议事件驱动**
- **房间组件化**
- **Editor 扫描生成**
- **Runtime 静态装配**
- **客户端事件直抛**
- **全局 / 房间 UI 路由分层**
- **对象同步与完整生成态**
- **流式回放与对象关键帧恢复**
- **断线重连**
- **日志与治理工具**

它真正的价值不是“某一个功能很炫”，而是：

> **这些能力已经能形成一条清晰、可维护、可横向扩展、可排障的生产级工程链。**

如果你需要的是：

- 中小型联机项目
- 房间制玩法
- 强调工程可控
- 强调长期维护

那么这套框架已经足够作为正式项目底座继续扩展。