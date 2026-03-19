# StellarNet Lite 实体同步与业务开发指南
> 面向玩法逻辑开发者的深度实战教程
> 核心目标：**掌握 ObjectSyncComponent 的底层原理，学会将“空间动画同步”与“纯业务逻辑”完美结合，并熟练使用 `ILiteNetSerializable` 实现 0GC 极速同步。**

---

## 目录
- [1. 核心原理：ObjectSync 到底在干什么？](#1-核心原理objectsync-到底在干什么)
- [2. 架构心智：为什么物理同步与业务逻辑要分离？](#2-架构心智为什么物理同步与业务逻辑要分离)
- [3. 基础基建：预制体与表现层挂载](#3-基础基建预制体与表现层挂载)
- [4. 实战演练：从 0 到 1 开发“打怪闯关”玩法](#4-实战演练从-0-到-1-开发打怪闯关玩法)
- [5. 进阶品类扩展思路 (生存建造 / 赛车竞速)](#5-进阶品类扩展思路-生存建造--赛车竞速)
- [6. 避坑与终极排障指南](#6-避坑与终极排障指南)

---

## 1. 核心原理：ObjectSync 到底在干什么？
在 StellarNet Lite 中，我们坚决弃用了 Mirror 原生的 `NetworkTransform` 和 `NetworkAnimator`。取而代之的是框架自带的 `ObjectSyncComponent` (双端组件)。

### 1.1 服务端权威 (ServerObjectSyncComponent)
它是整个房间的“物理与动画真理中心”。
- 负责分配全局唯一的 `NetId`。
- 负责在内存中维护所有实体的 `Transform` (位置/旋转/缩放/速度) 和 `Animator` (状态哈希/归一化时间/BlendTree参数)。
- 按照设定的 Tick 频率，将发生变化的实体状态打包成 `S2C_ObjectSync` 数组。
- **性能核心**：该协议实现了 `ILiteNetSerializable` 接口，底层采用纯二进制流写入，彻底消除了 JSON 序列化带来的 GC 开销与带宽浪费。

### 1.2 客户端预测与缓存 (ClientObjectSyncComponent)
它是客户端的“数据缓存池”。
- 接收到同步包后，底层通过 `ArraySegment` 的 Offset 切片进行 0GC 解析，**不直接操作 GameObject**，而是将数据写入内存字典。
- 提供 `TryGetTransformData` 和 `TryGetAnimatorData` 接口，供表现层按需拉取。
- 在两次同步包的间隔期间，利用服务端的下发的 `Velocity` (速度) 进行本地航位推测 (Dead Reckoning)，保证移动平滑。

### 1.3 掩码驱动 (EntitySyncMask)
框架极其抠门地压榨带宽。生成实体时，必须指定 `EntitySyncMask`：
- `None`: 仅生成，不同步任何物理和动画（适合静态宝箱、掉落物）。
- `Transform`: 仅同步空间信息（适合子弹、无动作的载具）。
- `Animator`: 仅同步动画（适合固定位置的炮台、NPC）。
- `All`: 全量同步（适合玩家、复杂怪物）。

---

## 2. 架构心智：为什么物理同步与业务逻辑要分离？
**新手最容易犯的错误**：试图把怪物的血量 (HP)、玩家的背包数据，强行塞进 `ObjectSyncComponent` 里同步。

**StellarNet Lite 的核心架构约束**：
> **`ObjectSyncComponent` 纯粹只管“看得见的空间和动画”。**
> **你的业务数据（如 HP、MP、得分、归属权），必须由你自己写的 `RoomComponent`（如 `DungeonComponent`）通过自定义协议来同步！**

它们之间的唯一纽带是：**`NetId`**。
- 服务端：`DungeonComponent` 记录 `Dictionary<int, int> MonsterHpDict` (Key 为 NetId，Value 为 HP)。
- 客户端：UI 监听到 HP 变化协议，通过 `NetId` 找到对应的怪物，利用 `UGUIFollowTarget` 将血条挂载到怪物头顶进行刷新。

这种解耦带来的巨大收益是：物理同步可以降频（如 10Hz），但业务事件（如受到致命一击）可以做到 0 延迟即时下发，且互不干扰。

---

## 3. 基础基建：预制体与表现层挂载
在写代码前，必须先准备好客户端的表现层预制体。

### 步骤 1：制作预制体
1. 在 `Assets/Resources/NetPrefabs/` 目录下创建一个怪物预制体 `Monster_Slime.prefab`。
2. 根节点必须挂载 `NetIdentity` 组件（框架自带，用于标识 NetId）。
3. 如果需要同步空间，挂载 `NetTransformView`。
4. 如果需要同步动画，挂载 `NetAnimatorView`，并拖入对应的 Animator 引用。

### 步骤 2：生成常量表
点击编辑器顶部菜单：`StellarNet/Lite 生成网络预制体常量表`。
框架会自动计算路径 Hash，并在 `NetPrefabConsts.cs` 中生成：
`public const int NetPrefabs_Monster_Slime = 123456789;`

---

## 4. 实战演练：从 0 到 1 开发“打怪闯关”玩法
我们将开发一个 `DungeonComponent`（闯关组件）。
**流程**：房主点击开始 -> 服务端生成怪物 -> 玩家发送攻击请求 -> 服务端扣血并同步 -> 血量归零服务端销毁怪物。

### 4.1 定义业务协议 (Shared 层)
新建 `DungeonProtocols.cs`。注意，这里只定义业务数据。**高频业务协议强烈建议实现 `ILiteNetSerializable`。**

```csharp
using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Game.Shared.Protocol
{
    // 玩家请求攻击某个实体 (高频，使用二进制序列化)
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

    // 服务端广播实体血量变化
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

### 4.2 编写服务端业务组件 (Server 层)
新建 `ServerDungeonComponent.cs`。它将调用 `ServerObjectSyncComponent` 的 API 来生成物理实体，同时自己维护 HP 数据。

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
        
        // 纯业务数据：记录 NetId 对应的 HP
        private readonly Dictionary<int, int> _monsterHpDict = new Dictionary<int, int>();
        private const int MaxMonsterHp = 100;

        public ServerDungeonComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _monsterHpDict.Clear();
            // 获取同房间内的物理同步底座
            _syncService = Room.GetComponent<ServerObjectSyncComponent>();
        }

        public override void OnGameStart()
        {
            if (_syncService == null) return;

            // 游戏开始，生成一只怪物
            Vector3 spawnPos = new Vector3(0, 0, 5);
            // 调用底座 API 生成实体，指定需要同步 Transform 和 Animator
            var monsterEntity = _syncService.SpawnObject(
                NetPrefabConsts.NetPrefabs_Monster_Slime, 
                EntitySyncMask.All, 
                spawnPos, 
                Vector3.zero, 
                Vector3.zero
            );

            // 记录业务数据
            _monsterHpDict[monsterEntity.NetId] = MaxMonsterHp;
            NetLogger.LogInfo("ServerDungeon", $"生成怪物成功，NetId: {monsterEntity.NetId}");
        }

        [NetHandler]
        public void OnC2S_AttackEntityReq(Session session, C2S_AttackEntityReq msg)
        {
            if (session == null || msg == null) return;
            if (Room.State != RoomState.Playing || _syncService == null) return;

            int targetId = msg.TargetNetId;

            // 1. 校验目标是否存在且存活
            if (!_monsterHpDict.TryGetValue(targetId, out int currentHp)) return;

            // 2. 扣除血量 (权威逻辑)
            currentHp -= msg.Damage;
            if (currentHp < 0) currentHp = 0;
            _monsterHpDict[targetId] = currentHp;

            // 3. 广播血量变化业务事件
            var hpMsg = new S2C_EntityHpChanged 
            { 
                NetId = targetId, 
                CurrentHp = currentHp, 
                MaxHp = MaxMonsterHp 
            };
            Room.BroadcastMessage(hpMsg);

            // 4. 如果死亡，调用底座 API 销毁物理实体
            if (currentHp <= 0)
            {
                _monsterHpDict.Remove(targetId);
                _syncService.DestroyObject(targetId);
                NetLogger.LogInfo("ServerDungeon", $"怪物 {targetId} 死亡，已销毁");
            }
            else
            {
                // 播放受击动画 (直接修改底座的权威动画状态)
                var entity = _syncService.GetEntity(targetId);
                if (entity != null)
                {
                    entity.AnimStateHash = Animator.StringToHash("Hit");
                    entity.AnimNormalizedTime = 0f;
                }
            }
        }
    }
}
```

### 4.3 编写客户端业务组件 (Client 层)
新建 `ClientDungeonComponent.cs`。它只负责接收 HP 变化，并抛给 UI 层。怪物的生成和移动已经由底层的 `ObjectSpawnerView` 和 `NetTransformView` 自动处理了！

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
            if (msg == null) return;
            // 0GC 直抛给表现层。View 层（如怪物头顶的血条 UI）监听此事件进行刷新
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
```

### 4.4 表现层交互 (View 层)
在客户端的 UI 脚本中监听血量变化。注意使用 `UnRegisterWhenMonoDisable` 防止幽灵响应。

```csharp
using UnityEngine;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Game.Shared.Protocol;

public class DungeonUIView : MonoBehaviour
{
    private void OnEnable()
    {
        if (NetClient.CurrentRoom != null)
        {
            NetClient.CurrentRoom.NetEventSystem.Register<S2C_EntityHpChanged>(OnHpChanged)
                .UnRegisterWhenMonoDisable(this); // 核心规范：UI 隐藏时自动注销
        }
    }

    private void OnHpChanged(S2C_EntityHpChanged msg)
    {
        // 利用 UGUIFollowTarget 找到对应的怪物头顶血条并刷新
        Debug.Log($"怪物 {msg.NetId} 血量变化: {msg.CurrentHp}/{msg.MaxHp}");
    }
}
```

---

## 5. 进阶品类扩展思路 (生存建造 / 赛车竞速)
掌握了上述 MSV 解耦心智后，任何品类都可以轻松扩展。

### 5.1 生存建造类 (如饥荒、Minecraft)
- **特点**：同屏实体极多（树木、石头、建筑），但大部分是静态的。
- **做法**：
    - 玩家砍树：发 `C2S_Interact`。
    - 服务端生成静态资源：调用 `SpawnObject`，但 **Mask 设为 EntitySyncMask.None**。
    - 为什么设为 None？因为树木不会动，不需要每帧打包它的 Transform 进 `S2C_ObjectSync` 浪费带宽。客户端收到 `S2C_ObjectSpawn` 后，会在原地生成树木预制体，随后它就静静地待在那里。
    - 树木被砍倒：服务端调用 `DestroyObject`，并广播掉落物（同样 Mask=None）。

### 5.2 赛车竞速类 (如马里奥赛车)
- **特点**：对位置精度要求极高，需要客户端表现层平滑。
- **做法**：
    - 玩家输入：发 `C2S_DriveInput` (油门、转向)。
    - 服务端物理：服务端用简单的射线或物理引擎计算赛车位置，每秒 15 次广播 `S2C_ObjectSync`。
    - 客户端表现：`NetTransformView` 会自动利用服务端下发的 `Velocity` 进行航位推测。如果发现赛车漂移，你可以调整 `NetTransformView` 面板上的 `PosSmoothTime` 和 `SnapThreshold`，让插值更符合赛车的手感。

---

## 6. 避坑与终极排障指南
在使用 `ObjectSyncComponent` 时，如果遇到表现异常，请按以下顺序排查：

### 坑 1：服务端调用了 SpawnObject，但客户端没生成模型？
- **排查 1**：检查预制体是否放在了 `Resources/NetPrefabs` 目录下。
- **排查 2**：检查是否点击了菜单 `StellarNet/Lite 生成网络预制体常量表`。如果没点，服务端传过去的 Hash 客户端根本不认识，控制台会报 `未知的 PrefabHash` 错误。
- **排查 3**：检查预制体根节点是否挂载了 `NetIdentity` 组件。

### 坑 2：模型生成了，但在原地滑步，动画不播？
- **原因**：服务端调用 `SpawnObject` 时，传入的 Mask 可能是 `EntitySyncMask.Transform`，**漏掉了 Animator**。
- **解决**：必须传入 `EntitySyncMask.All` 或 `EntitySyncMask.Animator`。同时检查预制体上是否挂载了 `NetAnimatorView` 组件。

### 坑 3：怪物死亡后，客户端模型还在？
- **原因**：服务端业务逻辑中，只清除了自己的 `_monsterHpDict`，**忘记调用** `_syncService.DestroyObject(targetId)`。
- **规范**：业务数据的销毁与物理底座的销毁必须成对出现。

### 坑 4：为什么我在客户端直接修改了 transform.position，一秒后又弹回去了？
- **原因**：**严重违反了服务端权威原则！**
- **解析**：客户端的 `NetTransformView` 在 `Update` 中会死死咬住底层 `ClientObjectSyncComponent` 缓存的权威坐标。你在表现层强行修改坐标，下一帧就会被同步数据覆盖。
- **正确做法**：客户端只能发请求给服务端，服务端修改 `ServerSyncEntity.Position`，然后等服务端下发同步包，客户端表现层自动平滑移动过去。**永远不要在客户端直接修改受网络管制的 Transform！**