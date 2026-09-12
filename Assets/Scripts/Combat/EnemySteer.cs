using UnityEngine;

namespace BattleFortress
{
    /// <summary>普通敌人一次移动决策的结果（Boss 有独立砸地逻辑，不走这里）</summary>
    public struct SteerResult
    {
        public float Tx;          // 期望移动方向 X（世界平面，已归一化）
        public float Tz;          // 期望移动方向 Z
        public float SpeedMul;    // 基础移速倍率（羊逃跑/游荡会变速）
        // 攻击请求统一由 EnemyCombat 处理，这里只输出走位
    }

    /// <summary>
    /// 普通敌兵 AI 转向决策：只做「往哪走、是否要攻击」的纯计算，
    /// 不直接改位置、不生成实体。行为模式由 EnemyDef.Ai 决定，
    /// 新增 AI 模式时扩展 EnemyAi 枚举并在这里加一个 SteerXxx 分支即可。数值全部来自 GameConfig.Ai。
    /// 注意：SteerResult 是值类型 struct，各 SteerXxx 必须用 ref 接收结果，否则写入不会带回调用方。
    /// </summary>
    public static class EnemySteer
    {
        /// <summary>
        /// 计算一只普通敌人的移动方向。
        /// dx/dz = 玩家相对敌人的水平方向（指向玩家），dl = 两者距离。
        /// </summary>
        public static SteerResult Steer(EnemyUnit e, float dx, float dz, float dl, float dt)
        {
            var r = new SteerResult { SpeedMul = 1f };
            if (e.Def == null) { SteerChase(ref r, dx, dz, dl); return r; }

            switch (e.Def.Ai)
            {
                case EnemyAi.RangedKite: SteerRangedKite(ref r, dx, dz, dl); break;
                case EnemyAi.Flank: SteerFlank(e, ref r, dx, dz, dl, dt); break;
                case EnemyAi.FleeWander: SteerFleeWander(e, ref r, dx, dz, dl, dt); break;
                case EnemyAi.SideCombat: SteerSideCombat(e, ref r, dx, dz, dl, dt); break;
                default: SteerChase(ref r, dx, dz, dl); break; // Chase：直线追击
            }
            return r;
        }

        /// <summary>远程风筝：太近后退、太远逼近、射程带内站住（开火由 EnemyCombat 解耦处理）</summary>
        private static void SteerRangedKite(ref SteerResult r, float dx, float dz, float dl)
        {
            float sign = dl < GameConfig.Ai.ArcherKeepMin ? -1f
                : dl > GameConfig.Ai.ArcherKeepMax ? 1f : 0f;
            r.Tx = (dx / dl) * sign;
            r.Tz = (dz / dl) * sign;
        }

        /// <summary>绕侧切入：远距离朝玩家方向叠加侧向绕行，贴近后直冲</summary>
        private static void SteerFlank(EnemyUnit e, ref SteerResult r, float dx, float dz, float dl, float dt)
        {
            e.FlankT += dt;
            if (dl <= GameConfig.Ai.FlankBreakDist)
            {
                r.Tx = dx / dl;
                r.Tz = dz / dl;
                return;
            }
            float px = -dz / dl;
            float pz = dx / dl;
            float w = Mathf.Min(1f, dl / GameConfig.Ai.FlankBlendDist) * e.FlankSide;
            r.Tx = (dx / dl) * GameConfig.Ai.FlankForward + px * w;
            r.Tz = (dz / dl) * GameConfig.Ai.FlankForward + pz * w;
            float tl = Mathf.Sqrt(r.Tx * r.Tx + r.Tz * r.Tz);
            if (tl > 0.0001f) { r.Tx /= tl; r.Tz /= tl; }
        }

        /// <summary>近逃远追、中间按随机方向游荡（各自带移速倍率）</summary>
        private static void SteerFleeWander(EnemyUnit e, ref SteerResult r, float dx, float dz, float dl, float dt)
        {
            if (dl < GameConfig.Ai.WanderFleeDist)
            {
                r.Tx = -(dx / dl);
                r.Tz = -(dz / dl);
                r.SpeedMul = GameConfig.Ai.FleeSpeedMul;
            }
            else if (dl > GameConfig.Ai.WanderGatherDist)
            {
                r.Tx = dx / dl;
                r.Tz = dz / dl;
                r.SpeedMul = GameConfig.Ai.GatherSpeedMul;
            }
            else
            {
                e.WanderT -= dt;
                if (e.WanderT <= 0f)
                {
                    e.WanderT = GameConfig.Ai.WanderIntervalMin + Random.value * GameConfig.Ai.WanderIntervalRand;
                    e.WanderA = Random.value * Mathf.PI * 2f;
                }
                r.Tx = Mathf.Cos(e.WanderA);
                r.Tz = Mathf.Sin(e.WanderA);
                r.SpeedMul = GameConfig.Ai.WanderSpeedMul;
            }
        }

        /// <summary>侧炮车：绕到玩家左/右侧并排位置，到位后几乎停住把侧面让给侧炮；周期性换边</summary>
        private static void SteerSideCombat(EnemyUnit e, ref SteerResult r, float dx, float dz, float dl, float dt)
        {
            var player = PlayerTransform();
            float fx = 0f, fz = 1f;
            if (player != null)
            {
                var fwd = player.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f) { fx = fwd.x; fz = fwd.z; }
            }
            // 玩家左侧向量 = cross(up, forward) = (fz, -fx)
            float lx = fz, lz = -fx;
            e.FlankT += dt;
            float flip = GameConfig.Ai.SideShooterFlipEvery;
            if (flip > 0f && e.FlankT > flip) { e.FlankT = 0f; e.FlankSide = -e.FlankSide; }

            float rx = dx + e.FlankSide * lx * GameConfig.Ai.SideShooterKeepDist;
            float rz = dz + e.FlankSide * lz * GameConfig.Ai.SideShooterKeepDist;
            float rl = Mathf.Sqrt(rx * rx + rz * rz);
            if (rl < GameConfig.Ai.SideShooterArriveDist)
            {
                r.Tx = 0f; r.Tz = 0f;
                r.SpeedMul = GameConfig.Ai.SideShooterHoldSpeedMul; // 到位横住，等侧炮开火
            }
            else
            {
                r.Tx = rx / rl;
                r.Tz = rz / rl;
            }
        }

        /// <summary>直线追击（Chase 默认行为）</summary>
        private static void SteerChase(ref SteerResult r, float dx, float dz, float dl)
        {
            r.Tx = dx / dl;
            r.Tz = dz / dl;
        }

        private static Transform _player;
        private static Transform PlayerTransform()
        {
            if (_player == null)
            {
                var pf = UnityEngine.Object.FindObjectOfType<PlayerFortress>();
                if (pf != null) _player = pf.transform;
            }
            return _player;
        }
    }
}
