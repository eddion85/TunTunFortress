using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 敌方投射物（弓箭手的箭）。直线飞行，命中玩家造成伤害。
    /// 对应 LayaAir 版 PROJECTILES 数组里的元素。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public float Dx;
        public float Dz;
        public float Speed = GameConfig.Ai.ArrowSpeed;
        public float Life = GameConfig.Ai.ArrowLife;
        public float Dmg;

        public void Init(float dx, float dz, float dmg)
        {
            Dx = dx;
            Dz = dz;
            Dmg = dmg;
            Speed = GameConfig.Ai.ArrowSpeed;
            Life = GameConfig.Ai.ArrowLife;
        }
    }
}
