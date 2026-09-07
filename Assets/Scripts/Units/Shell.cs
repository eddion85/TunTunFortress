using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 玩家炮弹出膛体（追踪目标）。
    /// 对应 LayaAir 版 AutoWeapon 里的 _shots 元素。
    /// </summary>
    public class Shell : MonoBehaviour
    {
        public EnemyUnit Target;
        public float Life = 1.6f;
        public float Dmg;
        public bool Front;   // 是否正面重炮（伤害高、飞行快、命中特效更重）

        public void Init(EnemyUnit target, float dmg, bool front)
        {
            Target = target;
            Dmg = dmg;
            Front = front;
            Life = 1.6f;
        }
    }
}
