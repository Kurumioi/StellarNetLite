using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.GameDemo.Shared;

namespace StellarNet.Lite.GameDemo.Server
{
    /// <summary>
    /// 服务端胶囊对战业务组件。
    /// 职责：维护房间内所有玩家的坐标与血量，校验移动与攻击请求，处理结算与房间销毁。
    /// </summary>
    public sealed class ServerDemoGameComponent : RoomComponent
    {
        private readonly Dictionary<string, DemoPlayerInfo> _players = new Dictionary<string, DemoPlayerInfo>();
        private readonly Func<object, byte[]> _serializeFunc;
        private bool _isGameOver = false;

        public ServerDemoGameComponent(Func<object, byte[]> serializeFunc)
        {
            _serializeFunc = serializeFunc;
        }

        public override void OnInit()
        {
            _players.Clear();
            _isGameOver = false;
        }

        public override void OnMemberJoined(Session session)
        {
            if (session == null) return;

            // 初始状态：随机出生点，满血10点
            var newPlayer = new DemoPlayerInfo
            {
                SessionId = session.SessionId,
                PosX = UnityEngine.Random.Range(-5f, 5f),
                PosY = 1f,
                PosZ = UnityEngine.Random.Range(-5f, 5f),
                Hp = 10
            };

            _players[session.SessionId] = newPlayer;

            // 广播新玩家加入
            var msg = new S2C_DemoPlayerJoined { Player = newPlayer };
            Broadcast(1004, msg);

            // 给新玩家下发全量快照
            OnSendSnapshot(session);
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null) return;

            if (_players.Remove(session.SessionId))
            {
                var msg = new S2C_DemoPlayerLeft { SessionId = session.SessionId };
                Broadcast(1005, msg);

                CheckWinCondition();
            }
        }

        public override void OnSendSnapshot(Session session)
        {
            if (session == null) return;

            var snapshot = new List<DemoPlayerInfo>();
            foreach (var kvp in _players)
            {
                snapshot.Add(kvp.Value);
            }

            var msg = new S2C_DemoSnapshot { Players = snapshot.ToArray() };
            SendTo(session, 1003, msg);
        }

        [NetHandler]
        public void OnC2S_DemoMoveReq(Session session, C2S_DemoMoveReq msg)
        {
            if (session == null || msg == null)
            {
                Debug.LogError("[ServerDemoGame] 移动请求非法：参数为空");
                return;
            }

            if (_isGameOver) return;

            if (!_players.TryGetValue(session.SessionId, out var player))
            {
                Debug.LogError($"[ServerDemoGame] 移动请求失败：未找到玩家数据, SessionId: {session.SessionId}");
                return;
            }

            if (player.Hp <= 0) return; // 死亡玩家禁止移动

            // 更新服务端权威坐标
            player.PosX = msg.TargetX;
            player.PosY = msg.TargetY;
            player.PosZ = msg.TargetZ;

            // 广播移动同步
            var syncMsg = new S2C_DemoMoveSync
            {
                SessionId = session.SessionId,
                TargetX = msg.TargetX,
                TargetY = msg.TargetY,
                TargetZ = msg.TargetZ
            };
            Broadcast(1006, syncMsg);
        }

        [NetHandler]
        public void OnC2S_DemoAttackReq(Session session, C2S_DemoAttackReq msg)
        {
            if (session == null || msg == null)
            {
                Debug.LogError("[ServerDemoGame] 攻击请求非法：参数为空");
                return;
            }

            if (_isGameOver) return;

            if (string.IsNullOrEmpty(msg.TargetSessionId))
            {
                Debug.LogError($"[ServerDemoGame] 攻击请求失败：目标 SessionId 为空, 发起者: {session.SessionId}");
                return;
            }

            if (!_players.TryGetValue(session.SessionId, out var attacker)) return;
            if (attacker.Hp <= 0) return; // 死亡玩家禁止攻击

            if (!_players.TryGetValue(msg.TargetSessionId, out var target)) return;
            if (target.Hp <= 0) return; // 目标已死亡，鞭尸无效

            // 扣除血量
            target.Hp -= 1;

            // 广播血量变更
            var hpMsg = new S2C_DemoHpSync
            {
                SessionId = target.SessionId,
                Hp = target.Hp
            };
            Broadcast(1007, hpMsg);

            // 触发结算判定
            if (target.Hp <= 0)
            {
                CheckWinCondition();
            }
        }

        private void CheckWinCondition()
        {
            if (_isGameOver) return;

            int aliveCount = 0;
            string lastAliveSessionId = string.Empty;

            foreach (var kvp in _players)
            {
                if (kvp.Value.Hp > 0)
                {
                    aliveCount++;
                    lastAliveSessionId = kvp.Key;
                }
            }

            // 结算条件：房间内曾有多人，且当前只剩1人存活
            if (aliveCount <= 1 && _players.Count > 1)
            {
                _isGameOver = true;
                
                var overMsg = new S2C_DemoGameOver { WinnerSessionId = lastAliveSessionId };
                Broadcast(1008, overMsg);

                Debug.Log($"[ServerDemoGame] 游戏结束，胜利者: {lastAliveSessionId}。即将强制销毁房间。");
                
                // 游戏结束，强制解散房间清理资源
                Room.Destroy();
            }
        }

        private void Broadcast(int msgId, object msgObj)
        {
            byte[] payload = _serializeFunc(msgObj);
            var packet = new Packet(msgId, NetScope.Room, Room.RoomId, payload);
            Room.Broadcast(packet);
        }

        private void SendTo(Session session, int msgId, object msgObj)
        {
            byte[] payload = _serializeFunc(msgObj);
            var packet = new Packet(msgId, NetScope.Room, Room.RoomId, payload);
            Room.SendTo(session, packet);
        }
    }
}
