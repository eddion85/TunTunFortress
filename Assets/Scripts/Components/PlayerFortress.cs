using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 玩家堡垒：摇杆移动、冲撞头锤、进化换外观。
    /// 对应 LayaAir 版 components/PlayerFortress.ts。
    /// 注意：这里不做 Reset，局内状态统一由 GameDirector 初始化，
    /// 否则组件启动顺序不定会覆盖掉跨关继承的养成数据。
    /// </summary>
    public class PlayerFortress : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera cam;

        [Header("移动")]
        [SerializeField] private float speed = 6.2f;

        /// <summary>模型正前方若与 +Z 不一致，用这个角度修正</summary>
        [Header("朝向")]
        [SerializeField] private float yawOffset = 0f;

        [Header("四阶外观（同时只显示一个）")]
        [SerializeField] private GameObject tier0;
        [SerializeField] private GameObject tier1;
        [SerializeField] private GameObject tier2;
        [SerializeField] private GameObject tier3;

        private Vector3 _dir = new Vector3(0f, 0f, 1f);
        private float _dash;
        private float _cd;
        private float _evolveT;
        private float _evolveTick;
        private float _dashTick;
        private GameObject[] _tiers;
        private int _skinStage = -1;

        // ---------------- 生命周期 ----------------
        private void OnEnable()
        {
            _tiers = new[] { tier0, tier1, tier2, tier3 };
            ApplyTier(GS.Stage);
            GameBus.On(GameEvents.Evolve, OnEvolve);
            GameBus.On(GameEvents.Skill, OnSkill);
            GameBus.On(GameEvents.Restart, OnRestart);
            GameBus.On(GameEvents.Revive, OnRevive);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Evolve, OnEvolve);
            GameBus.Off(GameEvents.Skill, OnSkill);
            GameBus.Off(GameEvents.Restart, OnRestart);
            GameBus.Off(GameEvents.Revive, OnRevive);
        }

        private void Start()
        {
            if (cam == null) cam = Camera.main;
        }

        /// <summary>按进化阶段切换堡垒外观：四个形态都在场景里，只做显隐</summary>
        private void ApplyTier(int stage)
        {
            if (stage == _skinStage) return;
            _skinStage = stage;
            if (_tiers == null) return;
            for (int i = 0; i < _tiers.Length; i++)
                if (_tiers[i] != null) _tiers[i].SetActive(i == stage);
        }

        // ---------------- 冲撞技能 ----------------
        public bool SkillReady() { return _cd <= 0f && !GS.Over && !GS.Paused; }
        public float SkillRatio() { return Mathf.Max(0f, _cd) / GameConfig.DashCd; }
        public bool Dashing() { return _dash > 0f; }
        /// <summary>进化金光期间无敌 [策划书 4.1]</summary>
        public bool Invincible() { return _evolveT > 0f; }

        private void OnSkill(object payload)
        {
            if (!SkillReady()) return;
            _cd = GameConfig.DashCd;
            _dash = 0.34f;
            AudioKit.PlaySfx(SfxKeys.Dash);
            var p = transform.position;
            Fx.PlayVfx(VfxKeys.RingWave, p.x, p.y + 0.3f, p.z, 2.6f);
            Fx.Shake(0.7f);
            GameBus.Emit(GameEvents.Float, "冲撞头锤!");
        }

        private void OnEvolve(object payload)
        {
            int stage = payload is int ? (int)payload : 0;
            float s = GameConfig.StageScale[Mathf.Clamp(stage, 0, 3)];
            transform.localScale = new Vector3(s, s, s);
            ApplyTier(stage);

            var pp = transform.position;
            Fx.PlayVfx(VfxKeys.EvolveSheet, pp.x, pp.y + 1.4f, pp.z, 5.5f);
            Fx.PlayVfx(VfxKeys.RingWave, pp.x, pp.y + 0.3f, pp.z, 5.0f);
            Fx.PlayVfx(VfxKeys.LightBeam, pp.x, pp.y + 2f, pp.z, 4.0f);
            Fx.Shake(2.0f);
            GameBus.Emit(GameEvents.Float, "进化 " + stage + " 阶!");

            // 金光变身 1.5s：期间无敌并持续放光效 [策划书 4.1]
            _evolveT = GameConfig.EvolveShowTime;
        }

        private void OnRestart(object payload)
        {
            // 按 GS.Stage 还原外观：重新开局时 stage 已清零自然回到 Tier0
            int st = Mathf.Clamp(GS.Stage, 0, 3);
            float s = GameConfig.StageScale[st];
            transform.localScale = new Vector3(s, s, s);
            transform.position = Vector3.zero;
            _dash = 0f;
            _cd = 0f;
            _evolveT = 0f;
            ApplyTier(st);
        }

        /// <summary>复活：保留进化形态与等级，回到场地中心、清空冲撞冷却并播一次光复演出</summary>
        private void OnRevive(object payload)
        {
            transform.position = Vector3.zero;
            _dash = 0f;
            _cd = 0f;
            var pp = transform.position;
            Fx.PlayVfx(VfxKeys.EvolveSheet, pp.x, pp.y + 1.4f, pp.z, 5.5f);
            Fx.PlayVfx(VfxKeys.RingWave, pp.x, pp.y + 0.3f, pp.z, 5.0f);
            Fx.PlayVfx(VfxKeys.LightBeam, pp.x, pp.y + 2f, pp.z, 4.0f);
            Fx.Shake(1.4f);
            GameBus.Emit(GameEvents.Float, "复活!");
        }

        // ---------------- 每帧 ----------------
        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            if (_cd > 0f) _cd -= dt;
            if (GS.Over || GS.Paused) return;

            // 无敌 = 进化金光演出 或 复活保护（复活时长由配置驱动）
            GS.Invincible = _evolveT > 0f || GS.ReviveInvincibleT > 0f;
            if (_evolveT > 0f)
            {
                _evolveT -= dt;
                _evolveTick -= dt;
                if (_evolveTick <= 0f)
                {
                    _evolveTick = 0.18f;
                    var ep = transform.position;
                    Fx.PlayVfx(VfxKeys.StarSpark,
                        ep.x + (Random.value * 2f - 1f), ep.y + 1f + Random.value, ep.z + (Random.value * 2f - 1f), 1.2f);
                    Fx.PlayVfx(VfxKeys.LightBeam, ep.x, ep.y + 1.5f, ep.z, 2.2f);
                }
            }

            // 摇杆 → 世界方向（相对相机）
            float ax = Joy.x;
            float ay = Joy.y;
            bool moving = false;
            if (Mathf.Abs(ax) > 0.08f || Mathf.Abs(ay) > 0.08f)
            {
                Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
                Vector3 r = cam != null ? cam.transform.right : Vector3.right;
                f.y = 0f; r.y = 0f;
                if (f.sqrMagnitude > 0.0001f) f.Normalize(); else f = Vector3.forward;
                if (r.sqrMagnitude > 0.0001f) r.Normalize(); else r = Vector3.right;

                // Laya 版 ay 向下为正，这里沿用：-ay 表示向上推摇杆时沿相机前方前进
                Vector3 d = r * ax + f * -ay;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f)
                {
                    _dir = d.normalized;
                    moving = true;
                }
            }

            if (_dash > 0f)
            {
                _dash -= dt;
                _dashTick -= dt;
                if (_dashTick <= 0f)
                {
                    _dashTick = 0.13f;
                    var cp = transform.position;
                    Fx.PlayVfx(VfxKeys.SpeedLine, cp.x, cp.y + 1f, cp.z, 2.2f);
                }
            }

            if (!moving && _dash <= 0f) return;

            float boost = _dash > 0f ? 3.1f : 1f;
            float sp = speed * GS.SpeedMul * boost;
            Vector3 pos = transform.position;
            float half = GameConfig.ArenaHalf;

            // 边界钳制：贴边时保留切向移动，避免斜向顶墙时完全卡死
            pos.x = Mathf.Clamp(pos.x + _dir.x * sp * dt, -half, half);
            pos.z = Mathf.Clamp(pos.z + _dir.z * sp * dt, -half, half);
            pos.y = 0f;
            transform.position = pos;

            if (_dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(_dir, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);

            // 冲撞头锤：碾碎路径上的敌人 [策划书 4.1]
            if (_dash > 0f)
            {
                for (int i = 0; i < Registry.Enemies.Count; i++)
                {
                    var e = Registry.Enemies[i];
                    if (e == null || e.Dead || e.IsBoss) continue;
                    var ep = e.transform.position;
                    float dx = ep.x - pos.x;
                    float dz = ep.z - pos.z;
                    if (dx * dx + dz * dz < 2.4f * 2.4f)
                    {
                        float cd = GameConfig.SideDmg * 1.5f * GS.DmgMul * GS.SizeFactor(e.Size());
                        e.Hp -= cd;
                        Fx.PopDmg(ep, cd, true);
                        Fx.PlayVfx(VfxKeys.HitSheet, ep.x, 1f, ep.z, 1.9f);
                        Fx.Shake(0.5f);
                    }
                }
            }
        }
    }
}
