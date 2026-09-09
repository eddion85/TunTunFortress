using UnityEngine;

namespace BattleFortress
{
    /// <summary>普通敌人一次移动决策的结果（Boss 有独立砸地逻辑，不走这里）</summary>
    public struct SteerResult
    {
        public float Tx;          // 期望移动方向 X（世界平面，已归一化）
        public float Tz;          // 期望移动方向 Z
        public float SpeedMul;    // 基础移速倍率（羊逃跑/游荡会变速）
        public bool WantShoot;    // 弓箭手本帧是否请求射箭（由生成器负责真正生成箭矢）
    }

    /// <summary>
    /// 普通敌兵 AI 转向决策：只做「往哪走、是否要射箭」的纯计算，
    /// 不直接改位置、不生成实体，方便以后新增行为模式或敌种。
    /// 数值全部来自 GameConfig.Ai。
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
            var k = e.Kind;

            if (k.ranged)
            {
                // 弓箭手：维持 8~11m 射程带，进入射程且冷却好就请求射箭
                e.ShootCd -= dt;
                float sign = dl < GameConfig.Ai.ArcherKeepMin ? -1f
                    : dl > GameConfig.Ai.ArcherKeepMax ? 1f : 0f;
                if (dl < GameConfig.Ai.ArcherShootRange && e.ShootCd <= 0f) r.WantShoot = true;
                r.Tx = (dx / dl) * sign;
                r.Tz = (dz / dl) * sign;
            }
            else if (k.flank)
            {
                // 骑兵：远距离绕侧切入，贴近后直冲
                e.FlankT += dt;
                if (dl > GameConfig.Ai.FlankBreakDist)
                {
                    float px = -dz / dl;
                    float pz = dx / dl;
                    float w = Mathf.Min(1f, dl / GameConfig.Ai.FlankBlendDist) * e.FlankSide;
                    r.Tx = (dx / dl) * GameConfig.Ai.FlankForward + px * w;
                    r.Tz = (dz / dl) * GameConfig.Ai.FlankForward + pz * w;
                    float tl = Mathf.Sqrt(r.Tx * r.Tx + r.Tz * r.Tz);
                    if (tl > 0.0001f) { r.Tx /= tl; r.Tz /= tl; }
                }
                else
                {
                    r.Tx = dx / dl;
                    r.Tz = dz / dl;
                }
            }
            else if (k.wander)
            {
                // 羊：近逃远追、中间游荡
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
                        e.WanderT = GameConfig.Ai.WanderIntervalMin +
                                    Random.value * GameConfig.Ai.WanderIntervalRand;
                        e.WanderA = Random.value * Mathf.PI * 2f;
                    }
                    r.Tx = Mathf.Cos(e.WanderA);
                    r.Tz = Mathf.Sin(e.WanderA);
                    r.SpeedMul = GameConfig.Ai.WanderSpeedMul;
                }
            }
            else
            {
                // 农夫/奶牛：直线追击
                r.Tx = dx / dl;
                r.Tz = dz / dl;
            }

            return r;
        }
    }
}
