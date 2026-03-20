# StellarNet Lite 实体同步与业务开发指南

> 面向玩法逻辑开发者的深度实战教程  
> 核心目标：**掌握 ObjectSyncComponent 的底层原理，学会将“空间动画同步”与“纯业务逻辑”正确结合，并理解对象完整生成态、回放关键帧与高频序列化在当前框架中的位置。**

---

## 目录

- [1. 核心原理：ObjectSync 到底在干什么](#1-核心原理objectsync-到底在干什么)
- [2. 架构心智：为什么物理同步与业务逻辑要分离](#2-架构心智为什么物理同步与业务逻辑要分离)
- [3. 当前版本新增心智：完整生成态与回放关键帧](#3-当前版本新增心智完整生成态与回放关键帧)
- [4. 基础基建：预制体与表现层挂载](#4-基础基建预制体与表现层挂载)
- [5. 实战演练：从 0 到 1 开发打怪闯关玩法](#5-实战演练从-0-到-1-开发打怪闯关玩法)
- [6. 进阶品类扩展思路（生存建造 / 赛车竞速）](#6-进阶品类扩展思路生存建造--赛车竞速)
- [7. 当前工程里的表现层落地口径](#7-当前工程里的表现层落地口径)
- [8. 避坑与终极排障指南](#8-避坑与终极排障指南)

---

## 1. 核心原理：ObjectSync 到底在干什么

在 StellarNet Lite 中，我们明确弃用了 Mirror 原生的：

- `NetworkTransform`
- `NetworkAnimator`

取而代之的是框架自带的对象同步底座。

相关核心组件是：

- 服务端：`ServerObjectSyncComponent`
- 客户端：`ClientObjectSyncComponent`
- 表现层：`NetTransformView`、`NetAnimatorView`、`ObjectSpawnerView`

---

### 1.1 服务端权威（ServerObjectSyncComponent）

它是整个房间的“空间与动画真理中心”。

负责：

- 分配全局唯一 `NetId`
- 维护所有实体的权威状态
- 记录位置、旋转、缩放、速度
- 记录动画状态哈希、归一化时间、参数
- 生成对象
- 销毁对象
- 按 Tick 输出增量同步包
- 在录制时导出对象关键帧

当前它维护的核心对象是：

- `ServerSyncEntity`

---

### 1.2 客户端缓存与预测（ClientObjectSyncComponent）

它不是直接操作 GameObject 的脚本，它更像一个**客户端对象状态缓存池**。

它负责：

- 接收 `S2C_ObjectSpawn`
- 接收 `S2C_ObjectDestroy`
- 接收 `S2C_ObjectSync`
- 将同步结果写入本地缓存字典
- 对 Transform 数据做简单本地预测
- 对回放关键帧做对象世界重建

它对外暴露的不是“直接操作场景对象”，而是查询接口：

- `TryGetTransformData`
- `TryGetAnimatorData`

表现层组件再通过这些接口去拉数据。

---

### 1.3 掩码驱动（EntitySyncMask）

框架不会无脑同步全部数据。  
生成实体时，必须指定 `EntitySyncMask`：

- `None`
    - 仅生成
    - 不做 Transform / Animator 增量同步

- `Transform`
    - 只同步空间信息

- `Animator`
    - 只同步动画

- `All`
    - 全量同步

这意味着你可以按实体类型精细控制带宽成本。

---

## 2. 架构心智：为什么物理同步与业务逻辑要分离

新手最容易犯的错误是：

- 试图把怪物 HP
- 玩家背包
- 得分
- 归属权
- Buff

全都塞进 `ObjectSyncComponent` 里同步。

这是错的。

### 正确原则

> **`ObjectSyncComponent` 只管“看得见的空间和动画”。**  
> **业务数据必须由你自己的 RoomComponent 通过自定义协议同步。**

它们之间唯一共享的纽带是：

- `NetId`

例如：

- 服务端 `DungeonComponent` 记录：
    - `Dictionary<int, int> MonsterHpDict`
- 客户端 UI 收到 `S2C_EntityHpChanged`
- 用 `NetId` 找到对应怪物
- 再把血条挂到这个怪物头顶

---

### 为什么必须这样拆

因为这两类数据的节奏完全不同：

- **空间同步**
    - 高频
    - 连续
    - 可降频
    - 可插值
    - 可预测

- **业务事件**
    - 通常离散
    - 通常必须精准
    - 不适合走统一高频增量流

如果把它们强行混在一起，会带来：

- 包体膨胀
- GC 压力增大
- 回放职责变乱
- 重连恢复难以分层
- 排障困难

---

## 3. 当前版本新增心智：完整生成态与回放关键帧

这是当前版本和很多早期文档最大的区别。

---

### 3.1 `ObjectSpawnState` 是对象“完整生成态”的共享事实源

当前对象生成协议已经收敛成：

- `S2C_ObjectSpawn`
    - 内部持有 `ObjectSpawnState`

这意味着：

- 在线对象生成
- 重连快照恢复
- 回放关键帧恢复
- 本地对象生成事件

都尽量依赖同一份共享结构，而不是各自维护一套扁平字段副本。

`ObjectSpawnState` 里包含：

- `NetId`
- `PrefabHash`
- `Mask`
- Transform 完整字段
- Velocity
- Scale
- Animator 完整字段
- `OwnerSessionId`

这份结构的意义非常大：

> **对象完整状态现在只有一份事实源。**

---

### 3.2 增量同步和完整生成态不是一回事

当前框架里：

- `ObjectSpawnState`
    - 用于完整恢复
    - 用于生成
    - 用于关键帧

- `ObjectSyncState`
    - 用于增量同步
    - 用于高频运行时刷新

不要混淆这两者。

**一句话理解：**

- `ObjectSpawnState` 解决“这个对象完整长什么样”
- `ObjectSyncState` 解决“这个对象这一帧变了什么”

---

### 3.3 回放为什么要对象关键帧

如果回放只有普通消息帧，那么 Seek 到中段时，你只能：

- 从头把所有对象消息重放到目标时间

这会带来：

- 慢
- 状态恢复不稳定
- 对象世界容易出现中间缺失

所以当前版本加入了：

- `ReplayObjectSnapshotFrame`

它保存的是：

- 某个 Tick 下，当前对象世界的完整生成态数组

客户端 Seek 时可以：

1. 找到最近关键帧
2. 先恢复对象世界
3. 再补后续消息帧

这样回放的中段跳转才能稳定。

---

## 4. 基础基建：预制体与表现层挂载

在写玩法代码前，必须先准备好客户端表现层预制体。

---

### 步骤 1：制作预制体

在：

- `Assets/Resources/NetPrefabs/`

目录下创建网络预制体，例如：

- `Monster_Slime.prefab`

根节点建议具备：

- `NetIdentity`

如果需要同步空间，挂：

- `NetTransformView`

如果需要同步动画，挂：

- `NetAnimatorView`

---

### 步骤 2：生成预制体常量表

点击菜单：

- `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`

框架会生成：

- `NetPrefabConsts`

例如：

```csharp
public const int NetPrefabs_Monster_Slime = 123456789;
```

同时还会生成：

- `HashToPathMap`

用于运行时 `ObjectSpawnerView` 通过 `PrefabHash` 反查资源路径。

---

### 步骤 3：确认对象生成链路

当前对象生成落地链路是：

1. 服务端 `ServerObjectSyncComponent.SpawnObject(...)`
2. 广播 `S2C_ObjectSpawn`
3. 客户端 `ClientObjectSyncComponent` 写入缓存
4. 广播本地事件 `Local_ObjectSpawned`
5. `ObjectSpawnerView` 响应事件并实例化预制体
6. `NetTransformView` / `NetAnimatorView` 开始拉取同步数据

所以：

> **ClientObjectSyncComponent 不直接实例化 GameObject。**  
> **真正做实例化的是 ObjectSpawnerView。**

---

## 5. 实战演练：从 0 到 1 开发打怪闯关玩法

我们开发一个 `DungeonComponent`（闯关组件）。

目标流程：

- 房主点击开始
- 服务端生成怪物
- 玩家发送攻击请求
- 服务端扣血并同步
- 血量归零后服务端销毁怪物

---

### 5.1 定义业务协议（Shared）

这里只定义业务数据，不把 HP 硬塞进 ObjectSync。

```csharp
using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Game.Shared.Protocol
{
    [NetMsg(2001, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_AttackEntityReq : ILiteNetSerializable
    {
        public int TargetNetId;
        public int Damage;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(TargetNetId);
            writer.Write(Damage);
        }

        public void Deserialize(BinaryReader reader)
        {
            TargetNetId = reader.ReadInt32();
            Damage = reader.ReadInt32();
        }
    }

    [NetMsg(2002, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_EntityHpChanged : ILiteNetSerializable
    {
        public int NetId;
        public int CurrentHp;
        public int MaxHp;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(CurrentHp);
            writer.Write(MaxHp);
        }

        public void Deserialize(BinaryReader reader)
        {
            NetId = reader.ReadInt32();
            CurrentHp = reader.ReadInt32();
            MaxHp = reader.ReadInt32();
        }
    }
}
```

---

### 5.2 编写服务端业务组件（Server）

这里通过 `ServerObjectSyncComponent` 生成实体，但 HP 自己维护。

```csharp
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Game.Shared.Protocol;

namespace StellarNet.Lite.Game.Server.Components
{
    [RoomComponent(105, "Dungeon", "闯关打怪玩法")]
    public sealed class ServerDungeonComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private ServerObjectSyncComponent _syncService;
        private readonly Dictionary<int, int> _monsterHpDict = new Dictionary<int, int>();

        private const int MaxMonsterHp = 100;

        public ServerDungeonComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _monsterHpDict.Clear();

            if (Room == null)
            {
                NetLogger.LogError("ServerDungeonComponent", "初始化失败: Room 为空");
                return;
            }

            _syncService = Room.GetComponent<ServerObjectSyncComponent>();
            if (_syncService == null)
            {
                NetLogger.LogError("ServerDungeonComponent", $"初始化失败: 缺失 ServerObjectSyncComponent, RoomId:{Room.RoomId}");
            }
        }

        public override void OnGameStart()
        {
            if (Room == null || _syncService == null)
            {
                return;
            }

            Vector3 spawnPos = new Vector3(0f, 0f, 5f);

            ServerSyncEntity monsterEntity = _syncService.SpawnObject(
                NetPrefabConsts.NetPrefabs_Monster_Slime,
                EntitySyncMask.All,
                spawnPos,
                Vector3.zero,
                Vector3.zero
            );

            if (monsterEntity == null)
            {
                NetLogger.LogError("ServerDungeonComponent", $"生成怪物失败: SpawnObject 返回 null, RoomId:{Room.RoomId}");
                return;
            }

            _monsterHpDict[monsterEntity.NetId] = MaxMonsterHp;
        }

        [NetHandler]
        public void OnC2S_AttackEntityReq(Session session, C2S_AttackEntityReq msg)
        {
            if (session == null || msg == null)
            {
                return;
            }

            if (Room == null || Room.State != RoomState.Playing || _syncService == null)
            {
                return;
            }

            if (!_monsterHpDict.TryGetValue(msg.TargetNetId, out int currentHp))
            {
                return;
            }

            currentHp -= msg.Damage;
            if (currentHp < 0)
            {
                currentHp = 0;
            }

            _monsterHpDict[msg.TargetNetId] = currentHp;

            Room.BroadcastMessage(new S2C_EntityHpChanged
            {
                NetId = msg.TargetNetId,
                CurrentHp = currentHp,
                MaxHp = MaxMonsterHp
            });

            if (currentHp <= 0)
            {
                _monsterHpDict.Remove(msg.TargetNetId);
                _syncService.DestroyObject(msg.TargetNetId);
                return;
            }

            ServerSyncEntity entity = _syncService.GetEntity(msg.TargetNetId);
            if (entity != null)
            {
                entity.AnimStateHash = Animator.StringToHash("Hit");
                entity.AnimNormalizedTime = 0f;
            }
        }
    }
}
```

---

### 5.3 编写客户端业务组件（Client）

客户端只收业务同步，不直接操纵对象生成。

```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;

namespace StellarNet.Lite.Game.Client.Components
{
    [RoomComponent(105, "Dungeon", "闯关打怪玩法")]
    public sealed class ClientDungeonComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;

        public ClientDungeonComponent(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_EntityHpChanged(S2C_EntityHpChanged msg)
        {
            if (msg == null || Room == null)
            {
                return;
            }

            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
```

---

### 5.4 表现层交互（View）

这里的 UI 只负责显示，不负责权威状态。

```csharp
using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;

public class DungeonUIView : MonoBehaviour
{
    private void OnEnable()
    {
        if (NetClient.CurrentRoom == null)
        {
            return;
        }

        NetClient.CurrentRoom.NetEventSystem.Register<S2C_EntityHpChanged>(OnHpChanged)
            .UnRegisterWhenMonoDisable(this);
    }

    private void OnHpChanged(S2C_EntityHpChanged msg)
    {
        Debug.Log($"怪物 {msg.NetId} 血量变化: {msg.CurrentHp}/{msg.MaxHp}");
    }
}
```

---

## 6. 进阶品类扩展思路（生存建造 / 赛车竞速）

---

### 6.1 生存建造类

特点：

- 同屏实体多
- 大部分静态
- 生成和销毁多
- 高频增量同步少

推荐做法：

- 树、石头、建筑
    - `EntitySyncMask.None`
- 玩家
    - `EntitySyncMask.All`
- 掉落物
    - 一般也可以 `None`

为什么静态物体适合 `None`：

- 只需要生成一次
- 不需要每 Tick 打包 Transform
- 节省带宽

---

### 6.2 赛车竞速类

特点：

- 位置精度要求高
- 插值手感重要
- 可能需要更高频率同步

推荐做法：

- 输入走独立协议，例如 `C2S_DriveInput`
- 服务端负责物理
- 客户端 `NetTransformView` 做平滑
- 调整：
    - `PosSmoothTime`
    - `SnapThreshold`
    - `CatchUpThreshold`

不建议直接把赛车物理“真相”放在客户端。

---

## 7. 当前工程里的表现层落地口径

这一节专门对齐当前代码实现。

---

### 7.1 对象生成不是 ClientObjectSyncComponent 干的

很多人第一次看会误以为：

- `ClientObjectSyncComponent` 收到 `S2C_ObjectSpawn`
- 然后它自己直接 `Instantiate`

当前工程不是这样。

正确流程是：

- `ClientObjectSyncComponent`
    - 只写缓存
    - 广播 `Local_ObjectSpawned`
- `ObjectSpawnerView`
    - 监听本地事件
    - 按 `PrefabHash` 加载预制体
    - 实例化场景对象
    - 挂接 `NetIdentity`

这是非常重要的职责分离。

---

### 7.2 回放关键帧恢复和在线生成共用完整结构

当前统一依赖：

- `ObjectSpawnState`

它被复用于：

- 在线 `S2C_ObjectSpawn`
- 本地 `Local_ObjectSpawned`
- 回放 `ReplayObjectSnapshotFrame`
- 服务端 `ExportSpawnStates()`

这意味着你后续如果要扩展对象完整字段，不应该到处加字段，而应该优先维护：

- `ObjectSpawnState`

---

### 7.3 表现层不要直接篡改网络对象 Transform

当前 `NetTransformView` 会持续从同步缓存拉取权威数据。  
所以如果你在客户端手改：

- `transform.position`

很快就会被同步结果覆盖回来。

这不是 bug，而是框架刻意保持的：

- **服务端权威原则**

---

## 8. 避坑与终极排障指南

### 坑 1：服务端调用了 `SpawnObject`，客户端没生成模型

检查：

1. 预制体是否放在 `Resources/NetPrefabs/`
2. 是否点击了  
   `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`
3. `NetPrefabConsts` 是否生成了对应常量
4. 预制体是否能被 `Resources.Load` 找到
5. 场景中是否存在 `ObjectSpawnerView`

---

### 坑 2：模型生成了，但在原地滑步，动画不播

原因通常是：

- 服务端 `SpawnObject` 时 `Mask` 不对
- 漏掉了 `Animator`
- 预制体没挂 `NetAnimatorView`

解决：

- 玩家 / 怪物一般用 `EntitySyncMask.All`

---

### 坑 3：怪物死亡后客户端模型还在

原因：

- 你只删了业务字典
- 忘了调用 `_syncService.DestroyObject(netId)`

规范：

> **业务状态销毁与对象世界销毁必须成对出现。**

---

### 坑 4：客户端改了位置，一秒后弹回去

原因：

- 客户端不是权威

正确做法：

- 发请求给服务端
- 服务端改权威状态
- 等服务端同步回来
- 客户端表现层自动平滑移动

---

### 坑 5：回放 Seek 后对象世界不完整

检查：

1. 房间是否接入 `ServerObjectSyncComponent`
2. 是否真的有对象关键帧写入
3. 客户端是否有 `ClientObjectSyncComponent`
4. `ObjectSpawnerView` 是否还在监听本地生成事件

---

### 坑 6：为什么有的对象适合 `None`

因为“生成一次后长期静止”的对象，不需要高频增量同步。  
如果你还给它们走 Transform 增量流，只是在白白浪费带宽。

---

### 坑 7：为什么不把所有东西都塞进一个大同步包

因为那样会导致：

- 同步频率被最慢的一类数据绑死
- 包体越来越大
- 回放和重连恢复边界模糊
- 业务与表现耦合

当前架构刻意把：

- 对象空间 / 动画同步
- 业务事件同步

分成两套职责，这是为了长期维护而不是为了短期省事。

---

## 总结

使用 StellarNet Lite 做实体同步时，请牢牢记住下面四句话：

1. **ObjectSync 只负责看得见的空间和动画**
2. **业务状态一律走你自己的协议和 RoomComponent**
3. **完整生成态和增量同步不是同一个概念**
4. **回放关键帧的意义，是让对象世界能在中段被稳定恢复**

只要你守住这四条，后续不管是社交房、打怪房、赛车房还是建造房，都会越做越顺，而不是越做越乱。