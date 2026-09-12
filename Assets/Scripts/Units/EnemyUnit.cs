using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 敌人单位。数值/行为/动画全部来自 EnemyDef（EnemyCatalog 注册），
    /// AI 移动决策见 EnemySteer，刷怪调度见 EnemySpawner，击杀结算见 KillKit。
    /// </summary>
    public class EnemyUnit : MonoBehaviour
    {
        public EnemyDef Def;
        public Transform BarAnchor; // 模型自带 HPBar_Bar 锚点（血条贴这里显示，没有则按固定头顶高度）
        public float Hp;
        public float MaxHp;
        public bool Dead;
        public bool IsBoss;

        // ---- AI 计时器 ----
        public float HitCd;        // 近身攻击冷却
        public float ShootCd;      // 远程射击冷却
        public float SlamCd;       // Boss 砸地冷却
        public int FlankSide = 1;  // 骑兵绕后方向
        public float FlankT;
        public float WanderT;      // 游荡计时
        public float WanderA;      // 游荡朝向
        public float DevourTick;   // 强化吞噬外圈持续伤害结算计时

        // ---- 受击击退（水平速度，米/秒；由 EnemySpawner 每帧积分并衰减，AI 移动之外叠加） ----
        public float KnockX;
        public float KnockZ;

        /// <summary>施加一次击退：dir 为归一化水平方向，speed 为初速度</summary>
        public void ApplyKnockback(float dirX, float dirZ, float speed)
        {
            KnockX = dirX * speed;
            KnockZ = dirZ * speed;
        }

        /// <summary>敌人在世界 XZ 平面上的体长（包围盒 X/Z 取大值，用于按体型算击退距离等）</summary>
        public float WorldLength()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            float len = 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                var s = renderers[i].bounds.size;
                len = Mathf.Max(len, s.x, s.z);
            }
            return len > 0.0001f ? len : 1f;
        }

        // ---- Boss 骨骼动画状态 ----
        public Animator Animator;
        public string AnimClip = "";
        public bool Dying;
        public float DieT;
        public float AttackT;
        public float HitT;
        public float LastHp;

        /// <summary>体积档位。Boss 固定按最高档 3 参与体积压制计算</summary>
        public int Size()
        {
            return IsBoss ? 3 : (Def != null ? Def.Size : 0);
        }

        /// <summary>初始化一个敌人实例（从对象池取出后调用）。Boss 的基础数值同样来自 EnemyDef</summary>
        public void Init(EnemyDef def, bool isBoss, float hpMul)
        {
            Def = def;
            IsBoss = isBoss;
            Dead = false;

            MaxHp = (def != null ? def.Hp : 1f) * hpMul;
            Hp = MaxHp;
            LastHp = MaxHp;

            HitCd = 0f;
            ShootCd = 1f + Random.value;
            SlamCd = (def != null && def.Combat != null) ? def.Combat.Cd : 0f;
            FlankSide = Random.value < 0.5f ? 1 : -1;
            FlankT = 0f;
            WanderT = 0f;
            WanderA = Random.value * Mathf.PI * 2f;
            DevourTick = 0f;
            KnockX = 0f;
            KnockZ = 0f;

            Dying = false;
            DieT = 0f;
            AttackT = 0f;
            HitT = 0f;
            AnimClip = "";
        }

        /// <summary>本单位的伤害值（统一从定义取）</summary>
        public float Damage()
        {
            return Def != null ? Def.Dmg : 0f;
        }

        /// <summary>本单位的移动速度（统一从定义取，spdMul 为时间难度倍率）</summary>
        public float MoveSpeed(float spdMul)
        {
            return (Def != null ? Def.Speed : 2f) * spdMul;
        }
    }
}
