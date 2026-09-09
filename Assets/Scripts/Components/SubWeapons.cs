using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 副武器系统：连枷（环绕近身碾压）+ 地雷舱（行进布雷）。
    /// 两者均由升级卡解锁，与主炮（AutoWeapon）并行运作。
    /// 对应 LayaAir 版 components/SubWeapons.ts。
    /// </summary>
    public class SubWeapons : MonoBehaviour
    {
        [Header("预制体")]
        [SerializeField] private GameObject flailTpl;
        [SerializeField] private GameObject mineTpl;

        [Header("解锁后显示的堡垒挂载外观")]
        [SerializeField] private GameObject mineBayVis;
        [SerializeField] private GameObject magnetVis;

        private readonly List<GameObject> _flails = new List<GameObject>();
        private readonly List<float> _hitCd = new List<float>();
        private readonly List<Mine> _mines = new List<Mine>();
        private float _ang;
        private float _mineCd;
        private Vector3 _lastPos;

        private const float MaxMines = 10;

        private class Mine
        {
            public GameObject node;
            public float life = 14f;
            public float arm = 0.6f;
        }

        private void OnEnable()
        {
            GameBus.On(GameEvents.Restart, OnRestart);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Restart, OnRestart);
        }

        private void Start()
        {
            if (flailTpl != null) flailTpl.SetActive(false);
            if (mineTpl != null) mineTpl.SetActive(false);
            if (mineBayVis != null) mineBayVis.SetActive(false);
            if (magnetVis != null) magnetVis.SetActive(false);
            _lastPos = transform.position;
        }

        /// <summary>清理场上残留副武器实体；连枷是否重建由 GS.HasFlail 决定</summary>
        private void OnRestart(object payload)
        {
            for (int i = 0; i < _mines.Count; i++)
                if (_mines[i] != null && _mines[i].node != null) ObjectPool.Despawn(_mines[i].node);
            _mines.Clear();
            _mineCd = 0f;

            if (!GS.HasFlail)
            {
                for (int i = 0; i < _flails.Count; i++)
                    if (_flails[i] != null) ObjectPool.Despawn(_flails[i]);
                _flails.Clear();
                _hitCd.Clear();
            }
        }

        private void SpawnFlails()
        {
            if (flailTpl == null) return;
            for (int i = 0; i < 2; i++)
            {
                var f = ObjectPool.Spawn(flailTpl, transform.position, Quaternion.identity, transform.parent);
                f.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                _flails.Add(f);
                _hitCd.Add(0f);
            }
        }

        private void Update()
        {
            if (GS.Over || GS.Paused) return;
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            Vector3 pos = transform.position;

            // 解锁后显示对应堡垒挂载件
            if (mineBayVis != null && mineBayVis.activeSelf != GS.HasMine) mineBayVis.SetActive(GS.HasMine);
            if (magnetVis != null && magnetVis.activeSelf != GS.HasMagnet) magnetVis.SetActive(GS.HasMagnet);

            // ---- 连枷：绕堡垒旋转，碰到敌人造成伤害 ----
            if (GS.HasFlail)
            {
                if (_flails.Count == 0) SpawnFlails();
                _ang += dt * 3.4f;
                const float R = 2.6f;

                for (int i = 0; i < _flails.Count; i++)
                {
                    var f = _flails[i];
                    if (f == null) continue;

                    float a = _ang + (i * Mathf.PI * 2f) / _flails.Count;
                    float fx = pos.x + Mathf.Cos(a) * R;
                    float fz = pos.z + Mathf.Sin(a) * R;
                    f.transform.position = new Vector3(fx, pos.y + 0.8f, fz);
                    f.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);

                    _hitCd[i] -= dt;
                    if (_hitCd[i] > 0f) continue;

                    for (int k = 0; k < Registry.Enemies.Count; k++)
                    {
                        var e = Registry.Enemies[k];
                        if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;
                        var ep = e.transform.position;
                        float dx = ep.x - fx;
                        float dz = ep.z - fz;
                        if (dx * dx + dz * dz < 1.1f * 1.1f)
                        {
                            float dmg = GameConfig.SideDmg * 0.85f * GS.DmgMul * GS.SizeFactor(e.Size());
                            e.Hp -= dmg;
                            Fx.PopDmg(ep, dmg, false);
                            AudioKit.PlaySfx(SfxKeys.HitEnemy);
                            Fx.PlayVfx(VfxKeys.HitSheet, ep.x, ep.y + 0.9f, ep.z, 1.5f);
                            Fx.Shake(0.18f);
                            _hitCd[i] = 0.35f;
                            break;
                        }
                    }
                }
            }

            // ---- 地雷舱：移动一段距离后布雷，敌人靠近引爆 ----
            if (GS.HasMine)
            {
                _mineCd -= dt;
                float dx = pos.x - _lastPos.x;
                float dz = pos.z - _lastPos.z;
                float moved = Mathf.Sqrt(dx * dx + dz * dz);

                if (_mineCd <= 0f && moved > 2.2f && mineTpl != null && _mines.Count < MaxMines)
                {
                    var m = ObjectPool.Spawn(mineTpl, new Vector3(pos.x, 0.15f, pos.z), Quaternion.identity, transform.parent);
                    m.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);
                    _mines.Add(new Mine { node = m });
                    _mineCd = 1.6f;
                    _lastPos = pos;
                }
            }

            // 地雷引爆检测
            for (int i = _mines.Count - 1; i >= 0; i--)
            {
                var m = _mines[i];
                if (m == null || m.node == null) { _mines.RemoveAt(i); continue; }

                m.life -= dt;
                m.arm -= dt;
                if (m.life <= 0f)
                {
                    ObjectPool.Despawn(m.node);
                    _mines.RemoveAt(i);
                    continue;
                }
                if (m.arm > 0f) continue;

                Vector3 mp = m.node.transform.position;
                bool boom = false;
                for (int k = 0; k < Registry.Enemies.Count; k++)
                {
                    var e = Registry.Enemies[k];
                    if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;
                    var ep = e.transform.position;
                    float dx = ep.x - mp.x;
                    float dz = ep.z - mp.z;
                    if (dx * dx + dz * dz < 1.4f * 1.4f) { boom = true; break; }
                }
                if (!boom) continue;

                // 范围爆炸（统一走 DamageKit：半径 3m、伤害 = 正面炮 ×1.2；特效尺寸按伤害基数换算）
                float mineFxBase = GameConfig.FrontDmg * 1.2f;
                DamageKit.Explode(new Vector3(mp.x, 0f, mp.z), 3.0f,
                    mineFxBase * GS.DmgMul, true,
                    mineFxBase * GameConfig.ExplosionFxPerDmg,
                    mineFxBase * GameConfig.RingFxPerDmg,
                    mineFxBase * GameConfig.ImpactShakePerDmg,
                    SfxKeys.ExplosionSmall);

                ObjectPool.Despawn(m.node);
                _mines.RemoveAt(i);
            }
        }
    }
}
