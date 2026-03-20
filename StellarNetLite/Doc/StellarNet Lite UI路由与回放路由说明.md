# StellarNet Lite UI路由与回放路由说明

> 面向客户端主程、玩法开发者、UI 开发者  
> 核心目标：**讲清楚客户端全局 UI 路由、房间组件运行时 UI 路由、回放时 UI 路由三者的职责边界与协作方式，以及基于 UGUIFollow 的表现层彻底解耦。**

---

## 目录

- [1. 文档定位](#1-文档定位)
- [2. 为什么必须单独设计 UI 路由层](#2-为什么必须单独设计-ui-路由层)
- [3. 当前工程中的 UI 路由总体结构](#3-当前工程中的-ui-路由总体结构)
- [4. 客户端全局 UI 路由：GlobalUIRouter](#4-客户端全局-ui-路由globaluirouter)
- [5. 房间组件运行时 UI 路由：RoomUIRouterBaseT](#5-房间组件运行时-ui-路由roomuirouterbaset)
- [6. 在线态 UI 路由与回放态 UI 路由的差异](#6-在线态-ui-路由与回放态-ui-路由的差异)
- [7. 当前工程中的实际 Router 列表](#7-当前工程中的实际-router-列表)
- [8. 全局事件与房间事件在 UI 路由中的分工](#8-全局事件与房间事件在-ui-路由中的分工)
- [9. 回放时 UI 路由的真实工作方式](#9-回放时-ui-路由的真实工作方式)
- [10. Component、Router、UIPanel 的职责边界](#10-componentrouteruipanel-的职责边界)
- [11. 表现层坐标解耦：UGUIFollow 的引入](#11-表现层坐标解耦uguifollow-的引入)
- [12. 当前工程中的关键实现口径](#12-当前工程中的关键实现口径)
- [13. 常见错误写法](#13-常见错误写法)
- [14. 排障建议](#14-排障建议)
- [15. 总结](#15-总结)

---

## 1. 文档定位

这份文档不是介绍协议，也不是介绍房间组件实现细节。  
它只解决一件事：

> **客户端 UI 到底该由谁控制，什么时候控制，在线房间和回放房间的 UI 为什么不能混在一起。**

当前工程已经形成了比较清晰的路由分层，但如果没有文档约束，后续多人协作时很容易出现：

- 把全局 UI 跳转写进 `ClientRoomComponent`
- 把房间 UI 打开关闭写进全局 Router
- 把回放 UI 复用到在线态面板
- 把业务事件直接在 Panel 里硬接到底层网络
- 房间销毁或 UI 隐藏后不清理，残留旧事件监听
- 把 3D 坐标跟随逻辑硬写在 UI 业务脚本里
- 把房间模板、房间状态、UI 打开关闭混成一个巨石面板控制器

所以这份文档的核心价值是：**统一团队写 UI 的责任边界与生命周期规范。**

---

## 2. 为什么必须单独设计 UI 路由层

在小 Demo 阶段，很多项目会这样写：

- 登录成功后，直接在 `Panel_Login` 里打开大厅
- 进房成功后，直接在 `ClientRoomComponent` 里打开房间面板
- 游戏结束后，直接在 `Panel_SocialRoomView` 里关闭自己再打开结算
- 回放时复用在线房间面板，加一堆 `if (isReplay)` 分支

这种写法短期看很快，长期一定变成灾难，原因有三个。

### 2.1 UI 跳转会和网络状态强耦合

一旦网络状态发生变化，例如：

- 物理断连
- 进入软挂起 `ConnectionSuspended`
- 重连成功
- 被踢下线
- 房间销毁
- 回放开始
- 回放退出

你会发现 UI 逻辑散落在 Panel、Module、Component 各处，最后没人知道某个面板到底该由谁负责打开和关闭。

### 2.2 在线房间与回放房间的 UI 责任完全不同

在线房间里，UI 常常要支持：

- 输入
- 操作按钮
- 网络状态反馈
- 房主权限
- 离房交互

回放房间里，UI 常常只需要：

- 播放控制
- 时间轴
- 倍速
- 只读表现
- 禁止业务请求

如果不拆路由层，最后就会变成一个 Panel 里到处写 `if (isReplay)` 分支，快速失控。

### 2.3 房间组件横向扩展时，UI 也必须横向扩展

StellarNet Lite 的核心思想之一是：

> **新增业务优先横向扩展一个房间组件，而不是修改核心类。**

既然房间逻辑是按组件拆的，那么 UI 路由也应该跟着组件拆，而不是重新搞一个巨石 `UIManager` 接管所有业务。

---

## 3. 当前工程中的 UI 路由总体结构

当前客户端 UI 路由分为两层：

### 第一层：全局 UI 路由

负责跨房间、跨状态的大导航，例如：

- 登录
- 大厅
- 回放面板
- 全局网络监控面板
- 状态跌落回退

当前实现类：`GlobalUIRouter`

### 第二层：房间组件 UI 路由

负责单个房间组件在不同状态下的局部 UI 生命周期，例如：

- 在线要开什么
- 回放要开什么
- 离房要关什么
- 游戏开始 / 结束时切什么局部 UI

当前实现基类：`RoomUIRouterBase<T>`

当前子类示例：

- `RoomSettingsOnlineUIRouter`
- `RoomSettingsReplayUIRouter`
- `SocialOnlineUIRouter`
- `SocialReplayUIRouter`

---

## 4. 客户端全局 UI 路由：GlobalUIRouter

### 4.1 定位

`GlobalUIRouter` 是客户端全局导航总线。它只关心大状态的跃迁：

- 我现在是不是在大厅
- 我是不是掉出了房间
- 我是不是被踢下线
- 我是不是下载完录像
- 我是不是因为物理断连需要回退登录态

### 4.2 当前工程中的核心职责

- **登录成功后切到大厅**：监听 `S2C_LoginResult`
- **离开房间后回退大厅**：监听 `Local_RoomLeft`
- **被踢下线时回退登录**：监听 `S2C_KickOut`
- **下载完录像后切回放面板**：监听 `S2C_DownloadReplayResult`
- **物理断连后的 UI 处理**：`HandlePhysicalDisconnect()`
- **在线房间态异常跌落回大厅时的兜底回退**

### 4.3 它不应该做什么

它**不应该**直接处理房间内某个组件的局部 UI 切换，例如：

- 社交房开始游戏时打开 `Panel_SocialRoomView`
- 房间设置组件结算时打开 `Panel_StellarNetGameOver`
- 某个业务组件进入房间就打开自己的面板

这些都属于**房间组件 Router 的责任**。

---

## 5. 房间组件运行时 UI 路由：RoomUIRouterBaseT

### 5.1 定位

让每个 `ClientRoomComponent` 都可以有自己的 UI 生命周期控制器，并且在线态和回放态可以分别实现。

### 5.2 当前基类职责

`RoomUIRouterBase<T>` 的职责很单纯：

- **绑定组件**：`Bind(T component)`
- **解绑组件**：`Unbind()`
- **生命周期收尾**：`OnDestroy()` 自动调用 `Unbind()`
- **记录当前绑定组件和房间上下文**

Router **只负责**把“当前房间组件已进入 / 离开 / 处于在线 / 处于回放”的事实，映射成 UI 打开关闭动作。

它**不负责**：

- 收网络包
- 修改房间模型
- 计算业务状态
- 直接发网络请求

### 5.3 当前基类的设计意义

这个基类的价值不在抽象层次本身，而在于统一了三件事：

1. **Bind / Unbind 成对**
2. **自己的 UI 自己收尾**
3. **组件和 UI 生命周期显式关联**

这样做可以明显降低以下问题：

- 面板残留
- 回放和在线串 UI
- 旧房间的事件继续响应
- 房间销毁后 Router 还持有旧组件引用

---

## 6. 在线态 UI 路由与回放态 UI 路由的差异

当前工程明确把房间组件路由拆成两类：

- `XXXOnlineUIRouter`
- `XXXReplayUIRouter`

### 6.1 在线态 Router

关心：

- 交互
- 输入
- 房主权限
- 按钮响应
- 结束对局

例如：

- `SocialOnlineUIRouter`
- `RoomSettingsOnlineUIRouter`

### 6.2 回放态 Router

关心：

- 只读展示
- 与时间轴同步
- 禁止在线业务请求
- 不自动拉起在线交互面板

例如：

- `SocialReplayUIRouter`
- `RoomSettingsReplayUIRouter`

### 6.3 当前工程中的一个重要事实

`SocialReplayUIRouter` 当前基本是空实现，这不是缺失，而是一个**非常明确的架构表达**：

> **社交房在线 UI 不应默认复用到回放态。**

这比在同一个 Panel 里堆 `if (isReplay)` 更健康。

---

## 7. 当前工程中的实际 Router 列表

当前可见的 Router 列表如下：

- **全局 Router**
    - `GlobalUIRouter`

- **房间设置组件 Router**
    - `RoomSettingsOnlineUIRouter`
    - `RoomSettingsReplayUIRouter`

- **社交房组件 Router**
    - `SocialOnlineUIRouter`
    - `SocialReplayUIRouter`

各自职责：

- `RoomSettingsOnlineUIRouter`
    - 进入在线房间时打开 `Panel_StellarNetRoom`
    - 游戏结束时打开 `Panel_StellarNetGameOver`
    - 解绑时关闭相关通用房间 UI

- `SocialOnlineUIRouter`
    - 监听 `S2C_GameStarted` 打开 `Panel_SocialRoomView`
    - 监听 `S2C_GameEnded` 关闭 `Panel_SocialRoomView`

- `Replay` 路由
    - 不接管在线交互 UI
    - 保持回放态只读原则

---

## 8. 全局事件与房间事件在 UI 路由中的分工

### 8.1 `GlobalTypeNetEvent`

处理“跨房间、跨状态”的导航和全局交互，例如：

- 登录
- 大厅
- 录像列表
- 弱网
- 断线挂起
- 系统提示

适合：

- `GlobalUIRouter`
- 大厅 Panel
- 登录 Panel
- 全局网络监控面板

### 8.2 `Room.NetEventSystem`

处理“当前房间实例内”的业务表现与局部 UI，例如：

- 房间快照
- 成员进出
- 游戏开始
- 气泡
- 对象生成 / 销毁本地事件

适合：

- 组件级 Router
- 房间内 Panel
- ObjectSpawner 相关表现脚本

### 8.3 为什么必须分开

因为回放房间和在线房间都可能在客户端存在生命周期切换。  
`Room.NetEventSystem` 绑定到 `ClientRoom` 实例本身，可以天然做到：

- 当前房间沙盒化
- 旧房间事件不串到新房间
- 回放房间和在线房间不会共用一个业务事件池

---

## 9. 回放时 UI 路由的真实工作方式

回放不是给在线房间加一个标记位。回放是：

1. `ClientReplayPlayer` 创建本地沙盒房间
2. `ClientApp.EnterReplayRoom(roomId)` 进入 `ReplayRoom`
3. `ClientRoomFactory.BuildComponents()` 再装一套客户端房间组件
4. 组件根据 `ClientAppState.ReplayRoom` 决定挂哪套 Router
5. 回放播放器顺序重演消息帧，必要时用对象关键帧恢复对象世界
6. UI 只作为回放房间的表现层，不参与在线交互

所以：

> **回放的 UI 路由本质上是“ReplayRoom 的 UI 路由”，不是“在线房间 UI 上加回放分支”。**

---

## 10. Component、Router、UIPanel 的职责边界

### 10.1 `ClientRoomComponent` 负责什么

- 接网络
- 持轻状态
- 抛事件
- 决定当前模式下挂哪个 Router

**不负责**：

- 全局导航
- 直接管理所有 Panel 生命周期
- 把复杂 UI 打开关闭逻辑写死在组件内部

### 10.2 Router 负责什么

- 接状态
- 管开关
- 收生命周期
- 负责“自己开的 UI 自己关”

**不负责**：

- 网络底层处理
- 修改业务状态
- 直接当成表现脚本使用

一个重要规范：

> **Panel 绝不能自己决定“我什么时候该关闭”。**

比如：

- `Panel_SocialRoomView` 不能监听 `S2C_GameEnded` 来关闭自己
- 必须由 `SocialOnlineUIRouter` 来关闭它

### 10.3 UIPanel 负责什么

- 做显示
- 做交互
- 做输入
- 监听表现事件刷新界面

**核心规范**：

UIPanel 监听事件时，必须使用：

- `.UnRegisterWhenMonoDisable(this)`  
  或
- `.UnRegisterWhenGameObjectDestroyed(gameObject)`

特别是对于会被对象池回收或频繁隐藏的 UI，**优先使用 `UnRegisterWhenMonoDisable`**，防止 UI 隐藏后仍在后台响应网络事件。

---

## 11. 表现层坐标解耦：UGUIFollow 的引入

在早期 Demo 中，UI 气泡往往会把 3D 坐标跟随逻辑写在业务脚本里。当前架构已经明确不推荐这种做法。

### 当前工程推荐做法（以 `Panel_SocialRoomView` 为例）

1. 实例化 UI 预制体
2. 挂 `SocialRoomBubbleItem`
    - 只负责文本更新和生存时间
3. 挂 `UGUIFollowTarget`
    - 只负责 3D 到 2D 的坐标映射与平滑跟随
4. 业务脚本只负责决定“什么时候生成气泡、内容是什么”

这样做的收益：

- 业务脚本不再直接持有摄像机换算逻辑
- 跟随系统可以统一调平滑策略
- 面板层和底层跟随能力彻底解耦

---

## 12. 当前工程中的关键实现口径

这一节专门对齐当前仓库代码口径，避免文档和代码脱节。

### 12.1 房间进入后的 UI 打开顺序

当前在线房间的大致顺序是：

1. 全局模块收到 `S2C_CreateRoomResult` / `S2C_JoinRoomResult`
2. `ClientApp.EnterOnlineRoom`
3. `ClientRoomFactory.BuildComponents`
4. 各个 `ClientRoomComponent.OnInit()`
5. 组件内部根据状态挂接对应 Router
6. `GlobalUIRouter` 通过 `Local_RoomEntered` 关闭大厅 / 登录 UI
7. 局部 Router 决定房间面板何时打开

### 12.2 当前回放房间的 UI 入口

回放面板 `Panel_StellarNetReplay` 是一个**全局面板**。  
它不属于某个房间组件 Router。

原因很简单：

- 它控制的是“回放播放器”
- 它不是某个房间业务组件的局部 UI
- 它承担的是全局播放控制职责

所以它应继续由全局导航体系托管，而不是挂到某个房间组件路由里。

### 12.3 房间设置 UI 与社交房 UI 的分工

当前工程里：

- `Panel_StellarNetRoom`
    - 偏房间准备区 / 成员信息 / 开始游戏 / 离房
- `Panel_SocialRoomView`
    - 偏对局内交互 / 聊天气泡 / 社交房内操作

它们由不同 Router 管理，这是健康的，因为这两个面板本来就是两个不同的业务阶段。

### 12.4 回放态为什么通常不打开 `Panel_SocialRoomView`

因为它包含：

- 聊天输入
- 发送按钮
- 结束游戏按钮
- 在线房间上下文依赖

这些在回放态都不是合理职责。  
回放态应该优先考虑只读展示，而不是复用在线交互面板。

---

## 13. 常见错误写法

- **错误 1**：在 `ClientRoomComponent` 里直接管理所有 Panel
- **错误 2**：在 `Panel` 里决定进房 / 离房的大导航
- **错误 3**：在线态和回放态共用同一个交互面板，靠 `if (isReplay)` 到处分支
- **错误 4**：房间销毁后不解绑 Router，导致面板不关、串 UI
- **错误 5**：UI 监听事件忘记调用 `.UnRegisterWhenMonoDisable(this)`，导致隐藏状态下疯狂报错
- **错误 6**：在 UI 业务脚本里手写 `Camera.main.WorldToScreenPoint`
- **错误 7**：让回放 UI 直接发送在线业务请求
- **错误 8**：让房间业务面板自己监听全局断线逻辑并做大厅跳转

---

## 14. 排障建议

- **UI 没打开**
    - 检查 `ClientRoomComponent.OnInit()` 是否正确挂了对应 Router
    - 检查 `RoomUIRouterBase.Bind()` 是否执行
    - 检查 `GlobalUIRouter.Init()` 是否已完成

- **旧 UI 残留**
    - 检查 `Router.Unbind()` 是否执行
    - 检查 `OnUnbind()` 里是否关闭了自己打开的 Panel

- **回放打开了在线交互 UI**
    - 检查组件 `OnInit()` 是否误复用了 `OnlineUIRouter`
    - 检查当前 `ClientAppState` 是否正确为 `ReplayRoom`

- **UI 隐藏后后台疯狂报错**
    - 检查事件注册是否漏写 `.UnRegisterWhenMonoDisable(this)`

- **UI 气泡不跟随或坐标错乱**
    - 检查气泡父节点是否误挂了 `LayoutGroup`
    - 检查是否正确挂载 `UGUIFollowTarget`
    - 检查目标对象是否已由 `ObjectSpawnerView` 生成

- **游戏结束后社交房面板没关**
    - 检查 `SocialOnlineUIRouter` 是否成功监听到 `S2C_GameEnded`
    - 检查房间事件是不是发到了错误作用域

- **大厅和房间 UI 同时残留**
    - 检查 `GlobalUIRouter.OnRoomEntered()` 是否成功执行
    - 检查是否有面板直接绕过 Router 自己打开自己

---

## 15. 总结

当前工程的 UI 路由已经形成了非常健康的结构：

- **`GlobalUIRouter` 管大导航**
- **`RoomUIRouterBase<T>` 管局部开关**
- **在线 / 回放 Router 明确分离**
- **Panel 只做展示与交互，不做状态机**
- **`UnRegisterWhenMonoDisable` 防事件泄漏**
- **`UGUIFollowTarget` 剥离坐标映射**

如果后续继续扩展新的房间业务，请始终保持：

> **一个房间组件，如果需要 UI，就优先同时补一对 OnlineUIRouter / ReplayUIRouter。**

这不是形式主义，而是在多人协作和长期维护里，避免 UI 体系重新退化成巨石脚本的关键约束。