# StellarNet Lite

[![Unity Version](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity.com/)
[![Version](https://img.shields.io/badge/Version-1.0.0-green.svg)](https://github.com/)
[![License](https://img.shields.io/badge/License-MIT-orange.svg)](https://opensource.org/licenses/MIT)
> 面向 Unity 的轻量级、服务端权威、房间制联机框架。

当前仓库中真正需要阅读和维护的手写源码位于 `StellarNetLite/`；`StellarNetLiteGenerated/` 是编辑器扫描器生成的输出目录，不应手改。

## 项目定位

StellarNet Lite 不是自动同步变量式的黑盒方案，而是一套强调可控、可追踪、可扩展的工程化联机底座。当前代码已经落地的核心设计是：

- 服务端权威：客户端只发请求，服务端维护会话、房间、对象和状态真相。
- 显式协议驱动：所有网络消息通过 `[NetMsg]`、`[NetHandler]` 和 `NetClient.Send<T>` 明确声明。
- 房间组件化：玩法逻辑拆成 `ServerRoomComponent` / `ClientRoomComponent`，由房间按组件清单动态装配。
- 全局模块化：登录、大厅、回放、Ping 等公共能力通过 Server/Client Module 装配。
- 传输层解耦：核心运行时只依赖 `INetworkTransport`，当前仓库内置 `KCP`、`TCP`、`UDP` 三套实现。
- 无运行时反射：协议表、组件表、模块绑定器、网络预制体常量都由 Editor 工具生成，Runtime 直接读取静态表。
- 混合序列化：低频消息走 JSON，高频消息可实现 `ILiteNetSerializable` 走手写二进制。

## 当前代码结构

```text
.
├── README.md
├── StellarNetLite/
│   ├── Runtime/
│   │   ├── Shared/         # Packet/协议元数据/配置/序列化/日志
│   │   ├── Client/         # ClientApp/ClientRoom/事件系统/回放沙盒接入
│   │   ├── Server/         # ServerApp/Room/Session/GC/Headless 运行时
│   │   ├── Transports/     # KCP / TCP / UDP 传输实现
│   │   └── StellarNetAppManager.cs
│   ├── Extensions/
│   │   ├── DefaultGameFlow/    # 登录、大厅、建房/进房/离房、重连流程
│   │   ├── RoomFlow/           # 房间成员快照、准备态、房主迁移、开局/结算
│   │   ├── ObjectSync/         # 对象生成/销毁/批量同步/客户端预测/动画同步
│   │   ├── Replay/             # 录像录制、分片下载、Seek、快照恢复、本地播放
│   │   └── NetworkMonitoring/  # Ping、弱网告警、弱网阻断、主动熔断
│   ├── Editor/
│   │   ├── LiteProtocolScanner.cs
│   │   ├── NetPrefabScanner.cs
│   │   ├── NetConfigEditorWindow.cs
│   │   ├── ServerMonitorWindow.cs
│   │   └── StellarNetScaffoldWindow.cs
│   ├── Samples/SocialDemo/
│   │   ├── Shared/         # Demo 协议、房间模板
│   │   ├── Server/         # 社交房间示例服务端逻辑
│   │   ├── Client/         # UI 路由、房间表现层、回放面板、输入控制
│   │   └── GameLauncher.cs
│   └── Doc/                # 编号化框架文档
└── StellarNetLiteGenerated/ # 自动生成输出，占位目录，不手改
```

如果你是把这套代码导入 Unity 工程使用，通常对应的工程内路径会是：

- `Assets/StellarNetLite/`
- `Assets/StellarNetLiteGenerated/`

## 当前实现了什么

### 运行时主链

- `StellarNetAppManager` 统一装配传输层、序列化器、`ServerApp`、`ClientApp` 和自动生成注册表。
- `ServerApp` 负责未鉴权白名单、会话索引、账号顶号、房间 GC、离线保留和路由分发。
- `ClientApp` 维护 `InLobby / OnlineRoom / ReplayRoom / ConnectionSuspended` 四态，并对弱网、回放、重连做发送门控。
- `Room` 负责成员管理、组件生命周期、广播、录像录制、重连快照和房间 Tick。
- `NetMessageMapper` 只读取生成表，不在 Runtime 做反射扫描。

### 默认扩展

- `DefaultGameFlow`
  - 登录、版本校验、顶号、重连确认、重连完成握手
  - 房间创建、加入、指定 RoomId 加入或创建、离开、房间两阶段确认
  - 大厅房间列表、在线玩家列表、全局聊天、服务器公告推送
- `RoomFlow`
  - 房间成员全量快照
  - 成员加入/离开、准备状态同步
  - 房主迁移
  - 开始游戏 / 结束游戏
- `ObjectSync`
  - 服务端对象生成、销毁、批量同步
  - Transform + Animator 双掩码同步
  - 客户端远端插值、本地软和解、回放时序补偿、防滑步
  - `NetIdentity` + `ObjectSpawnerView` 表现层桥接
- `Replay`
  - 房间消息录制
  - 组件快照关键帧录制
  - GZip 录像文件
  - 断点续传式录像下载
  - 本地 `.raw` 解压缓存
  - 稀疏索引 + 快照索引 Seek
  - 本地回放沙盒、倍速、暂停、重播
- `NetworkMonitoring`
  - Ping/Pong RTT 监控
  - 弱网告警、弱网阻断
  - 长时间阻断后主动断开，接入统一重连链

### 编辑器工具链

当前仓库内置的菜单工具包括：

- `StellarNetLite/重新生成协议与组件常量表`
- `StellarNetLite/强制重新生成协议与组件常量表`
- `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`
- `StellarNetLite/网络配置 (NetConfig)`
- `StellarNetLite/业务脚手架生成器`
- `StellarNetLite/服务端运行时监控 (Server Monitor)`
- `StellarNetLite/Folder Content Copy Tool`

这些工具会生成或维护：

- `MsgIdConst`
- `ComponentIdConst`
- `AutoMessageMetaRegistry`
- `AutoRegistry`
- `NetPrefabConsts`

## 传输层现状

当前仓库的运行时代码不再以 Mirror 为默认传输层。

实际已经落地并可直接挂载到 `StellarNetAppManager` 同物体上的传输实现是：

- `KcpTransportProvider`
- `TcpTransportProvider`
- `UdpTransportProvider`

其中：

- `KCP` 是当前仓库里最完整、最适合示例运行的方案。
- `TCP` 是纯 .NET Socket 实现，便于调试和理解。
- `UDP` 代码里已明确标注为教学/实验用途，不适合直接承载生产链路。

`README` 旧版本里提到的 `Mirror`、`StellarNetMirrorManager` 已经不符合当前 Runtime 实现。

## SocialDemo 示例

`Samples/SocialDemo` 不是玩具空壳，它实际串起了当前框架的大部分能力：

- `GameLauncher` 负责根据 `Client / Server / Host` 模式启动 `StellarNetAppManager`
- 登录页支持登录、断线后的手动重连、重连确认
- 大厅页支持房间列表、录像列表、在线玩家列表、全局聊天
- 建房页支持房间模板选择、指定 RoomId 创建/加入
- 房间页支持成员列表、准备、房主开始游戏
- 社交房间支持角色移动、动作、头顶聊天气泡
- 结算页支持离房与录像重命名
- 回放页支持下载、播放、暂停、Seek、预设倍速和自定义倍速

当前示例房间模板注册在 `RoomTypeTemplateRegistry`，默认模板是：

- `SocialRoom`
- `RoomSettings`
- `ObjectSync`

## 快速开始

### 1. 导入到 Unity 工程

- 将 `StellarNetLite/` 放入 `Assets/StellarNetLite/`
- 保留 `StellarNetLiteGenerated/` 作为生成输出目录
- 工程需要可用的 `Newtonsoft.Json`
- KCP 传输所需的 `kcp2k.Runtime.dll` 已随仓库放在 `Runtime/Transports/KCP/`

### 2. 准备运行时入口

在同一个 GameObject 上挂载：

- `StellarNetAppManager`
- 一种传输实现：`KcpTransportProvider` / `TcpTransportProvider` / `UdpTransportProvider`

如果你要跑无头服务端，还可以再挂：

- `HeadlessServerLogger`

### 3. 先生成静态表

首次导入、协议改动、组件改动、模块改动或网络预制体改动之后，都要重新生成：

- `StellarNetLite/重新生成协议与组件常量表`
- `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`

如果怀疑生成表脏了，用：

- `StellarNetLite/强制重新生成协议与组件常量表`

### 4. 配置网络参数

通过菜单打开：

- `StellarNetLite/网络配置 (NetConfig)`

默认读取路径是：

- `StreamingAssets/NetConfig/netconfig.json`

编辑器工具也支持保存到：

- `PersistentDataPath/NetConfig/netconfig.json`

### 5. 跑示例

优先使用 `Samples/SocialDemo/Client/Scenes/KCP/` 下的场景。

当前代码实现层面，推荐理解为：

- `KCPClientScene`：客户端
- `KCPHostScene`：本地 Host
- `KCPServerScene`：独立服务端 / 无头服务端入口


## 开发模式

一个新功能通常按下面的约定扩展：

```csharp
[NetMsg(1300, NetScope.Room, NetDir.C2S)]
public sealed class C2S_MyFeatureReq
{
    public int Value;
}

[RoomComponent(300, "MyFeature", "我的功能")]
public sealed class ServerMyFeatureComponent : ServerRoomComponent
{
    [NetHandler]
    public void OnC2S_MyFeatureReq(Session session, C2S_MyFeatureReq msg)
    {
        // 服务端权威处理
    }
}
```

对应客户端再实现：

- `ClientRoomComponent` 或 `ClientModule`
- 处理 `S2C_*` 消息
- 通过 `Room.NetEventSystem` 或 `GlobalTypeNetEvent` 抛给表现层

写完之后不要忘记重新生成静态表，否则 Runtime 不会识别新协议和新组件。

## 无头服务器与诊断

`HeadlessServerLogger` 已经实现了完整的无头命令台和错误日志保留，支持：

- `help`
- `status`
- `rooms`
- `room <roomId>`
- `sessions`
- `session <sessionId|accountId>`
- `kick <sessionId|accountId> [reason]`
- `logs [count]`
- `findlog <keyword> [limit]`
- `logfiles`
- `persist`
- `gc`
- `exit`

相关输出位置：

- 服务端日志：`Application.dataPath/../ServerLogs`
- 服务端录像：`Application.persistentDataPath/Replays`
- 客户端录像缓存：`Application.persistentDataPath/ClientReplays`

## 使用注意

- `StellarNetLiteGenerated/` 是生成目录，不要手工维护。
- 新网络预制体必须放在 `Resources` 下，并在根节点挂 `NetIdentity`，否则 `NetPrefabScanner` 不会收录。
- 低频消息默认走 JSON；高频消息建议实现 `ILiteNetSerializable`。
- `UDP` 仅适合教学和实验。
- 当前样例 UI 和面板系统是 `Samples/SocialDemo` 的表现层实现，不是 Runtime 核心的一部分。

## 文档

仓库内的 `StellarNetLite/Doc/` 已经包含编号化学习文档，覆盖的主题与当前代码结构一致，包括：

- 核心概念与完整流程
- 通讯与传输层
- 全局模块与房间组件开发
- 实体同步与动画同步
- UI 路由与表现层解耦
- 回放与重连
- 编辑器工具链
- 无头服务端与持久化工具

如果你要继续扩展这套框架，建议先读 Runtime，再对照 `Doc/` 和 `Samples/SocialDemo/` 一起看。

## 👤 作者

*   **作者**: 小梦
*   **QQ**: 2649933509