using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 自动武器 [策划书 4.1]：
    /// - 侧方排炮：左右各一门，各自独立选敌、独立冷却，从自身炮口射出
    /// - 正面直射炮：一阶进化后解锁，从车头炮口沿朝向直射，伤害更高
    /// 伤害计算含体积压制系数 [策划书 4.1]
    /// 对应 LayaAir 版 components/AutoWeapon.ts。
    /// </summary>
    public class AutoWeapon : MonoBehaviour
    {
        [Header("炮弹")]
        [SerializeField] private GameObject shellTpl;

        [Header("炮口")]
        [SerializeField] private Transform muzzleL;
        [SerializeField] private Transform muzzleR;
        [SerializeField] private Transform muzzleF;

        [Header("主相机：只对屏幕内可见敌人开火")]
        [SerializeField] private Camera cam;

        private float _cdL = 0.6f;
        private float _cdR = 1.2f;
        private float _cdF = 1.6f;
        private int _sideSeq;   // 侧炮发炮序号（轮流点名 Boss）
        private int _frontSeq;  // 正面炮发炮序号
        private readonly List<Shell> _shots = new List<Shell>();

        private void OnEnable()
        {
            GameBus.On(GameEvents.Restart, ClearShots);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Restart, ClearShots);
        }

        private void Start()
        {
            if (cam == null) cam = Camera.main;
        }

        private void ClearShots(object payload)
        {
            for (int i = 0; i < _shots.Count; i++)
                if (_shots[i] != null) ObjectPool.Despawn(_shots[i].gameObject);
            _shots.Clear();
            _cdL = 0.6f;
            _cdR = 1.2f;
            _cdF = 1.6f;
            _sideSeq = 0;
            _frontSeq = 0;
        }

        /// <summary>取炮口世界坐标，未接线时回退到堡垒中心上方</summary>
        private Vector3 Muzzle(Transform node, Vector3 pos)
        {
            if (node != null) return node.position;
            return new Vector3(pos.x, pos.y + 1.1f, pos.z);
        }

        /// <summary>
        /// 选敌。
        /// </summary>
        /// <param name="pos">堡垒位置</param>
        /// <param name="side">-1 = 只挑左半边、1 = 只挑右半边、0 = 不限</param>
        /// <param name="cone">&gt;0 时只挑正前方锥形内的目标（正面炮用）</param>
        /// <param name="bossOnly">只挑 Boss（Boss 体积大，不受左右半场限制，避免被近身小兵永久「抢火」）</param>
        private EnemyUnit Nearest(Vector3 pos, int side, float cone, bool bossOnly = false)
        {
            float fx = 0f, fz = 1f, rx = 1f, rz = 0f;
            if (side != 0 || cone > 0f)
            {
                Vector3 f = transform.forward; f.y = 0f; f.Normalize();
                Vector3 r = transform.right; r.y = 0f; r.Normalize();
                fx = f.x; fz = f.z;
                rx = r.x; rz = r.z;
            }

            EnemyUnit best = null;
            float bd = GameConfig.WeaponRange * GameConfig.WeaponRange;

            for (int i = 0; i < Registry.Enemies.Count; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;
                if (bossOnly && !e.IsBoss) continue;

                Vector3 p = e.transform.position;
                float dx = p.x - pos.x;
                float dz = p.z - pos.z;
                float d = dx * dx + dz * dz;
                if (d >= bd) continue;

                // 只对「看得到」的敌人开火：必须在屏幕范围内，
                // 避免玩家明明没看到敌人、炮却自动把屏幕外目标打死的违和感
                if (!IsOnScreen(p)) continue;

                // 点名 Boss 时不看左右半场（Boss 是大型目标，两侧炮都该能打）
                if (!bossOnly && side != 0)
                {
                    // 投影到右向量，判断敌人在左半边还是右半边
                    if ((dx * rx + dz * rz) * side < 0f) continue;
                }
                if (cone > 0f)
                {
                    float dl = Mathf.Sqrt(d);
                    if (dl < 0.0001f) dl = 1f;
                    if ((dx * fx + dz * fz) / dl < cone) continue;
                }

                bd = d;
                best = e;
            }
            return best;
        }

        /// <summary>
        /// 侧炮选敌：正常选最近敌人；但每 BossTargetEvery 发强制点名射程内最近的 Boss，
        /// 避免 Boss 被一圈近身小兵挡住火力、血条完全不掉。
        /// </summary>
        private EnemyUnit PickSideTarget(Vector3 pos, int side)
        {
            var normal = Nearest(pos, side, 0f);
            int every = Mathf.Max(1, GameConfig.Survival.BossTargetEvery);
            _sideSeq++;
            if (_sideSeq % every != 0) return normal;
            var boss = Nearest(pos, side, 0f, true);
            return boss != null ? boss : normal;
        }

        /// <summary>
        /// 敌人是否落在屏幕可见范围内。
        /// 留 15% 边距，边缘敌人不会被生硬地选不中。
        /// </summary>
        private bool IsOnScreen(Vector3 p)
        {
            if (cam == null) return true;   // 未接线时退回原逻辑，不锁死武器
            Vector3 vp = cam.WorldToScreenPoint(new Vector3(p.x, 1.2f, p.z));
            if (vp.z <= 0f) return false;   // 相机背后
            float mx = Screen.width * 0.15f;
            float my = Screen.height * 0.15f;
            return vp.x > -mx && vp.x < Screen.width + mx && vp.y > -my && vp.y < Screen.height + my;
        }

        /// <summary>开火。kind: 0=左侧炮 1=右侧炮 2=正面炮</summary>
        private void Fire(Vector3 from, EnemyUnit target, float dmg, int kind)
        {
            bool front = kind == 2;
            AudioKit.PlaySfx(SfxKeys.Cannon);
            Fx.PlayVfx(VfxKeys.FireBall, from.x, from.y, from.z, front ? 1.6f : 1.15f);
            Fx.Shake(front ? 0.3f : 0.14f);

            if (shellTpl == null) return;
            var shell = ObjectPool.Spawn(shellTpl, from, Quaternion.identity, transform.parent);
            float sc = front ? 0.95f : 0.7f;
            shell.transform.localScale = new Vector3(sc, sc, sc);

            var sh = shell.GetComponent<Shell>();
            if (sh == null) sh = shell.AddComponent<Shell>();
            sh.Init(target, dmg, front);
            _shots.Add(sh);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);

            if (!GS.Over && !GS.Paused)
            {
                Vector3 pos = transform.position;
                float sideCd = GameConfig.SideCd * GS.CdMul;

                // 左侧炮：只打左半边目标
                _cdL -= dt;
                if (_cdL <= 0f)
                {
                    var t = PickSideTarget(pos, -1);
                    if (t != null)
                    {
                        _cdL = sideCd;
                        Fire(Muzzle(muzzleL, pos), t, GameConfig.SideDmg * GS.DmgMul, 0);
                    }
                    else _cdL = 0.25f;
                }

                // 右侧炮：只打右半边目标，与左炮交替开火（初始 CD 错开）
                _cdR -= dt;
                if (_cdR <= 0f)
                {
                    var t = PickSideTarget(pos, 1);
                    if (t != null)
                    {
                        _cdR = sideCd;
                        Fire(Muzzle(muzzleR, pos), t, GameConfig.SideDmg * GS.DmgMul, 1);
                    }
                    else _cdR = 0.25f;
                }

                // 正面直射炮：商店主动加装后解锁（GS.HasFrontCannon），只打正前方锥形内目标
                if (GS.HasFrontCannon)
                {
                    _cdF -= dt;
                    if (_cdF <= 0f)
                    {
                        // 正面炮同样隔发点名锥形内 Boss，避免被小兵挡火
                        _frontSeq++;
                        var t = (_frontSeq % Mathf.Max(1, GameConfig.Survival.BossTargetEvery) == 0)
                            ? (Nearest(pos, 0, 0.35f, true) ?? Nearest(pos, 0, 0.35f))
                            : Nearest(pos, 0, 0.35f);
                        if (t != null)
                        {
                            _cdF = GameConfig.FrontCd * GS.CdMul;
                            Fire(Muzzle(muzzleF, pos), t, GameConfig.FrontDmg * GS.DmgMul, 2);
                        }
                        else _cdF = 0.25f;
                    }
                }
            }

            // 炮弹飞行
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                if (s == null) { _shots.RemoveAt(i); continue; }

                s.Life -= dt;
                bool hasTarget = s.Target != null && !s.Target.Dead && s.Target.gameObject.activeInHierarchy;
                Vector3 sp = s.transform.position;

                if (!hasTarget || s.Life <= 0f)
                {
                    ObjectPool.Despawn(s.gameObject);
                    _shots.RemoveAt(i);
                    continue;
                }

                Vector3 tp = s.Target.transform.position;
                float dx = tp.x - sp.x;
                float dz = tp.z - sp.z;
                float dl = Mathf.Sqrt(dx * dx + dz * dz);

                if (dl < 0.9f)
                {
                    if (!s.Target.Dead)
                    {
                        // 体积压制系数 [策划书 4.1]
                        float dmg = s.Dmg * GS.SizeFactor(s.Target.Size());
                        s.Target.Hp -= dmg;
                        Fx.PopDmg(tp, dmg, s.Front);
                        AudioKit.PlaySfx(SfxKeys.HitEnemy);
                        Fx.PlayVfx(VfxKeys.HitSheet, tp.x, tp.y + 1f, tp.z, s.Front ? 2.0f : 1.5f);
                        // 只有正面重炮补一层冲击波，普通命中保持干净
                        if (s.Front) Fx.PlayVfx(VfxKeys.RingWave, tp.x, tp.y + 0.6f, tp.z, 1.5f);
                        Fx.Shake(s.Front ? 0.45f : 0.2f);
                    }
                    ObjectPool.Despawn(s.gameObject);
                    _shots.RemoveAt(i);
                    continue;
                }

                float step = (s.Front ? 26f : 20f) * dt;
                sp.x += (dx / dl) * step;
                sp.z += (dz / dl) * step;
                s.transform.position = sp;
            }
        }
    }
}
