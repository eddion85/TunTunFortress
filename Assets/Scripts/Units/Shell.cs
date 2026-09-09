using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 玩家炮弹出膛体：携带发射它的武器定义（WeaponDef），由 AutoWeapon 统一推进与结算。
    /// Direct 弹追踪目标本体；Area 弹追踪目标的「地面落点」，目标中途死亡仍会飞到最后落点爆炸。
    /// </summary>
    public class Shell : MonoBehaviour
    {
        public WeaponDef Def;
        public EnemyUnit Target;
        public float Dmg;
        public Vector3 Aim;      // 地面落点（y=0），每帧随目标刷新
        public float Life;
        public float StartY;     // 出膛高度，飞行中逐步压到 0，形成「落到地面」的弧线
        public float StartDist;  // 出膛时距落点的水平距离，用于算高度插值
        public float TrailT;     // 飞行拖尾留火计时

        /// <summary>目标是否仍可追踪（存活、激活）</summary>
        public bool TargetAlive => Target != null && !Target.Dead && Target.gameObject.activeInHierarchy;

        public void Init(WeaponDef def, EnemyUnit target, float dmg)
        {
            Def = def;
            Target = target;
            Dmg = dmg;
            Life = def != null ? def.ShellLife : GameConfig.ShellLife;
            Aim = GroundPoint(target != null ? target.transform.position : transform.position);
            StartY = transform.position.y;
            Vector3 p = transform.position;
            StartDist = Mathf.Max(0.001f, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Aim.x, Aim.z)));
        }

        /// <summary>把任意世界坐标压到地面落点</summary>
        public static Vector3 GroundPoint(Vector3 p)
        {
            return new Vector3(p.x, 0f, p.z);
        }
    }
}
