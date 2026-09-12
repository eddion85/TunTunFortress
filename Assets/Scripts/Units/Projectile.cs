using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 敌方投射物（弓箭手的箭、侧炮兵的炮弹等任意远程攻击）。直线飞行，命中玩家造成伤害。
    /// 弹体外观由 prefab 决定，飞行参数由 EnemyAttackDef 在发射时传入，本类不关心是谁发射的。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public float Dx;
        public float Dz;
        public float Speed = GameConfig.Ai.ArrowSpeed;
        public float Life = GameConfig.Ai.ArrowLife;
        public float Dmg;

        /// <summary>弓箭手默认参数（箭速/寿命走配置）</summary>
        public void Init(float dx, float dz, float dmg)
        {
            Dx = dx;
            Dz = dz;
            Dmg = dmg;
            Speed = GameConfig.Ai.ArrowSpeed;
            Life = GameConfig.Ai.ArrowLife;
        }

        /// <summary>通用发射：自定义弹速/寿命（不同敌人攻击方式用不同弹道）</summary>
        public void Init(float dx, float dz, float dmg, float speed, float life)
        {
            Dx = dx;
            Dz = dz;
            Dmg = dmg;
            Speed = speed > 0.0001f ? speed : GameConfig.Ai.ArrowSpeed;
            Life = life > 0.0001f ? life : GameConfig.Ai.ArrowLife;
        }
    }
}
