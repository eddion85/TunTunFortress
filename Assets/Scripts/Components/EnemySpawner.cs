using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 敌人生成调度 + 主循环驱动：刷怪/Boss 时间表、投射物模拟、逐帧驱动 AI 与攻击。
    /// 敌人「是什么」（数值/模型/动画/AI/攻击）全部由 EnemyCatalog 里的 EnemyDef 决定：
    /// 走位看 EnemySteer，攻击看 EnemyCombat，本类只做「什么时候刷、每帧按什么顺序驱动」。
    /// 新增敌种/Boss/攻击效果只改 EnemyCatalog / EnemyCombat + GameConfig，不需要改本类。
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Transform player;

        [Header("敌人预制体（可选兜底：正常由 EnemyDef.PrefabPath 从 Resources 加载）")]
        [SerializeField] private GameObject tplSheep;
        [SerializeField] private GameObject tplCow;
        [SerializeField] private GameObject tplFarmer;
        [SerializeField] private GameObject tplArcher;
        [SerializeField] private GameObject tplRider;
        [SerializeField] private GameObject tplBoss;

        private readonly Dictionary<string, GameObject> _tplCache = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _legacy = new Dictionary<string, GameObject>();
        private float _timer = GameConfig.Survival.SpawnFirstDelay;
        private readonly List<float> _spawnStamps = new List<float>(); // 普通兵出生时间戳（滚动窗口限流）

        // ---------------- 生命周期 ----------------
        private void Awake()
        {
            // 旧场景槽位作为 Resources 缺失时的兜底，不再是主路径
            _legacy["sheep"] = tplSheep;
            _legacy["cow"] = tplCow;
            _legacy["farmer"] = tplFarmer;
            _legacy["archer"] = tplArcher;
            _legacy["rider"] = tplRider;
            _legacy["boss_tank"] = tplBoss;
        }

        private void OnEnable()
        {
            GameBus.On(GameEvents.Restart, ClearAll);
            GameBus.On(GameEvents.Revive, OnRevive);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Restart, ClearAll);
            GameBus.Off(GameEvents.Revive, OnRevive);
        }

        private void Start()
        {
            if (player == null)
            {
                var pf = FindObjectOfType<PlayerFortress>();
                if (pf != null) player = pf.transform;
            }
        }

        /// <summary>按定义取模板：优先 Resources 路径，缺失时回退旧 Inspector 槽位（带缓存）</summary>
        private GameObject GetTemplate(EnemyDef def)
        {
            if (def == null) return null;
            if (_tplCache.TryGetValue(def.Key, out var cached)) return cached;

            GameObject tpl = null;
            if (!string.IsNullOrEmpty(def.PrefabPath))
                tpl = Resources.Load<GameObject>(def.PrefabPath);
            if (tpl == null) _legacy.TryGetValue(def.Key, out tpl);

            _tplCache[def.Key] = tpl;
            return tpl;
        }

        private void ClearAll(object payload)
        {
            DespawnAllEnemies();
            DespawnAllArrows();
            _timer = GameConfig.Survival.SpawnFirstDelay;
            _spawnStamps.Clear();
        }

        /// <summary>复活：按配置清空普通兵与弓箭，Boss 保留（继续战斗）</summary>
        private void OnRevive(object payload)
        {
            bool clearMobs = payload is bool && (bool)payload;
            DespawnAllArrows();
            if (clearMobs)
            {
                for (int i = Registry.Enemies.Count - 1; i >= 0; i--)
                {
                    var e = Registry.Enemies[i];
                    if (e == null) { Registry.Enemies.RemoveAt(i); continue; }
                    if (e.IsBoss) continue;
                    ObjectPool.Despawn(e.gameObject);
                    Registry.Enemies.RemoveAt(i);
                }
            }
            _timer = GameConfig.Survival.SpawnFirstDelay;
            _spawnStamps.Clear(); // 复活清场后刷怪窗口重新计数
            if (player != null) player.position = Vector3.zero;
        }

        private static void DespawnAllEnemies()
        {
            for (int i = 0; i < Registry.Enemies.Count; i++)
                if (Registry.Enemies[i] != null) ObjectPool.Despawn(Registry.Enemies[i].gameObject);
            Registry.Enemies.Clear();
        }

        private static void DespawnAllArrows()
        {
            for (int i = 0; i < Registry.Projectiles.Count; i++)
                if (Registry.Projectiles[i] != null) ObjectPool.Despawn(Registry.Projectiles[i].gameObject);
            Registry.Projectiles.Clear();
        }

        /// <summary>场上存活的普通兵数量（Boss 不占普通兵上限）</summary>
        private static int MobCount() => Registry.CountAlive(e => !e.IsBoss);

        /// <summary>场上存活的 Boss 数量</summary>
        private static int AliveBossCount() => Registry.CountAlive(e => e.IsBoss);

        /// <summary>滚动窗口限流：最近 SpawnWindowSeconds 秒内出生的普通兵是否已达上限</summary>
        private bool WindowSpawnBlocked()
        {
            float now = GS.Elapsed;
            float win = GameConfig.Survival.SpawnWindowSeconds;
            _spawnStamps.RemoveAll(t => now - t > win);
            return _spawnStamps.Count >= GameConfig.Survival.SpawnPerWindowCap;
        }

        // ---------------- 刷怪 ----------------
        /// <summary>
        /// 按配置权重表选怪：权重来自 GameConfig.Survival.MobTable（时间波段，下标对应 EnemyCatalog.Mobs），
        /// 超过当前解锁档位 maxTier 的敌种权重清零，全部为 0 时退回第一个。
        /// </summary>
        private EnemyDef PickKind()
        {
            var profile = GS.Profile();
            var band = GameConfig.MobBandAt(GS.Elapsed);
            var mobs = EnemyCatalog.Mobs;
            int total = 0;
            for (int i = 0; i < mobs.Length; i++)
            {
                int w = (i < band.w.Length && i <= profile.maxTier) ? band.w[i] : 0;
                total += w;
            }
            if (total <= 0) return mobs[0];

            int roll = Random.Range(0, total);
            for (int i = 0; i < mobs.Length; i++)
            {
                int w = (i < band.w.Length && i <= profile.maxTier) ? band.w[i] : 0;
                roll -= w;
                if (roll < 0) return mobs[i];
            }
            return mobs[0];
        }

        private EnemyUnit SpawnAt(EnemyDef def, float x, float z)
        {
            GameObject prefab = GetTemplate(def);
            if (prefab == null) return null;

            var go = ObjectPool.Spawn(prefab, new Vector3(x, 0f, z), Quaternion.identity, transform);
            float sc = def.Scale;
            go.transform.localScale = new Vector3(sc, sc, sc);

            // 血量倍率完全由时间难度配置推导
            float hpMul = GS.Profile().hpMul;

            var unit = go.GetComponent<EnemyUnit>();
            if (unit == null) unit = go.AddComponent<EnemyUnit>();
            unit.Init(def, def.IsBoss, hpMul);

            // 攻击挂点表现（炮塔转向/炮管后坐/隐藏自带 UI），没有对应节点时自动空转
            var rig = go.GetComponent<EnemyAttackRig>();
            if (rig == null) rig = go.AddComponent<EnemyAttackRig>();
            rig.Setup(def.Combat);

            // 车轮滚动（模型有 Wheels 节点且定义开启时）
            if (def.HasWheels)
            {
                var wheels = go.GetComponent<EnemyWheelRig>();
                if (wheels == null) wheels = go.AddComponent<EnemyWheelRig>();
                wheels.Setup(GameConfig.Ai.EnemyWheelsNode, GameConfig.Ai.EnemyWheelSpinSign, def.Scale);
            }

            // 血条锚点：优先贴模型自带 HPBar_Bar，找不到时血条系统按固定头顶高度兜底
            unit.BarAnchor = RigNodes.FindFirst(go.transform, GameConfig.Ai.EnemyBarNode);

            Registry.Enemies.Add(unit);
            return unit;
        }

        private void Spawn()
        {
            // 普通兵数量受配置上限约束（Boss 不占此名额），总单位数另有 MaxUnits 兜底
            if (MobCount() >= GameConfig.Survival.MobCap) return;
            if (WindowSpawnBlocked()) return; // 30s 滚动窗口出生数量上限
            if (Registry.Enemies.Count >= GameConfig.MaxUnits || player == null) return;
            var def = PickKind();
            Vector3 pp = player.position;
            float ang = Random.value * Mathf.PI * 2f;
            float dist = GameConfig.Ai.SpawnRingMin + Random.value * GameConfig.Ai.SpawnRingJitter;
            float x = GameConfig.ClampArena(pp.x + Mathf.Cos(ang) * dist);
            float z = GameConfig.ClampArena(pp.z + Mathf.Sin(ang) * dist);
            if (SpawnAt(def, x, z) != null) _spawnStamps.Add(GS.Elapsed);
        }

        /// <summary>Boss 出场：种类/模型/数值/动画全部来自 EnemyCatalog.BossAt；强度随存活时间成长（配置驱动）。slot=同屏序号，多只时环形落位</summary>
        private void SpawnBoss(int slot)
        {
            if (player == null) return;

            EnemyDef def = EnemyCatalog.BossAt(GS.Elapsed);
            GameObject prefab = GetTemplate(def);
            if (prefab == null) return;

            GS.BossAlive = true;

            // 多只 Boss 沿玩家外圈不同方向落位，避免叠在一起
            Vector3 pp = player.position;
            float ang = -Mathf.PI / 2f + slot * (Mathf.PI * 2f / Mathf.Max(2, GameConfig.Survival.BossCountMax));
            float ring = GameConfig.Ai.BossSpawnRing;
            float bx = GameConfig.ClampArena(pp.x + Mathf.Cos(ang) * ring);
            float bz = GameConfig.ClampArena(pp.z + Mathf.Sin(ang) * ring);
            var go = ObjectPool.Spawn(prefab, new Vector3(bx, 0f, bz), Quaternion.identity, transform);
            go.transform.localScale = new Vector3(def.Scale, def.Scale, def.Scale);

            // Boss 血量 = 时间难度倍率 × Boss 档位成长
            float hpMul = GS.Profile().hpMul * GameConfig.BossHpMulAt(GS.Elapsed);

            var unit = go.GetComponent<EnemyUnit>();
            if (unit == null) unit = go.AddComponent<EnemyUnit>();
            unit.Init(def, true, hpMul);
            if (def.HasAnimator)
            {
                unit.Animator = BossMotion.Setup(go, def.AnimIdle);
                unit.AnimClip = def.AnimIdle;
            }
            Registry.Enemies.Add(unit);

            AudioKit.PlaySfx(SfxKeys.BossAppear);
            AudioKit.PlayMusic(BgmKeys.Boss);
            var bp = unit.transform.position;
            Fx.PlayVfx(VfxKeys.RingWave, bp.x, 0.3f, bp.z, GameConfig.Ai.BossAppearRingFx);
            Fx.PlayVfx(VfxKeys.LightBeam, bp.x, 2f, bp.z, GameConfig.Ai.BossAppearBeamFx);
            Fx.Shake(GameConfig.Ai.BossAppearShake);
            GameBus.Emit(GameEvents.Boss, true);
            GameBus.Emit(GameEvents.Float, "农场守卫来袭!");
        }

        // ---------------- 投射物（所有敌人远程攻击共用飞行/命中模拟） ----------------
        private void UpdateProjectiles(float dt, Vector3 pp)
        {
            float hitR = GameConfig.Ai.ArrowHitRadius;
            for (int i = Registry.Projectiles.Count - 1; i >= 0; i--)
            {
                var a = Registry.Projectiles[i];
                if (a == null) { Registry.Projectiles.RemoveAt(i); continue; }

                a.Life -= dt;
                Vector3 p = a.transform.position;
                p.x += a.Dx * a.Speed * dt;
                p.z += a.Dz * a.Speed * dt;
                a.transform.position = p;

                float dx = pp.x - p.x;
                float dz = pp.z - p.z;
                if (dx * dx + dz * dz < hitR * hitR)
                {
                    GS.Damage(a.Dmg);
                    Fx.PlayVfx(VfxKeys.HitSheet, p.x, 1f, p.z, GameConfig.Ai.ArrowHitFx);
                    Fx.Shake(GameConfig.Ai.ArrowHitShake);
                    ObjectPool.Despawn(a.gameObject);
                    Registry.Projectiles.RemoveAt(i);
                    continue;
                }

                if (a.Life <= 0f)
                {
                    ObjectPool.Despawn(a.gameObject);
                    Registry.Projectiles.RemoveAt(i);
                }
            }
        }

        private static void FaceDir(EnemyUnit e, float tx, float tz)
        {
            if (tx * tx + tz * tz < 0.0001f) return;
            e.transform.rotation = Quaternion.LookRotation(new Vector3(tx, 0f, tz), Vector3.up);
        }

        /// <summary>Boss 骨骼动画状态机：优先级 attack &gt; hit &gt; run/idle（片段名来自 EnemyDef）</summary>
        private static void UpdateBossAnim(EnemyUnit e, float dt, float tx, float tz, float sp)
        {
            var d = e.Def;
            if (e.Animator == null || d == null) return;
            if (e.AttackT > 0f) { e.AttackT -= dt; BossMotion.Play(e, d.AnimAttack); return; }
            if (e.AnimClip == d.AnimHit)
            {
                e.HitT -= dt;
                if (e.HitT <= 0f) BossMotion.Play(e, d.AnimRun);
                return;
            }
            float moving = sp * Mathf.Sqrt(tx * tx + tz * tz);
            BossMotion.Play(e, moving > 0.4f ? d.AnimRun : d.AnimIdle);
        }

        // ---------------- 主循环 ----------------
        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            if (player == null) return;
            Vector3 pp = player.position;
            if (GS.Over || GS.Paused) return;

            var profile = GS.Profile();

            // 时间驱动刷怪：间隔与每波份数都来自配置，普通兵达上限时 Spawn 内部自行跳过
            _timer -= dt;
            if (_timer <= 0f)
            {
                _timer = Mathf.Max(GameConfig.Survival.SpawnIntervalMin, profile.spawnInterval);
                for (int b = 0; b < profile.burst; b++) Spawn();
            }

            // Boss 按时间表出场：数量目标随存活时间递增（后期难度来源），不足且到点就补
            int aliveBoss = AliveBossCount();
            GS.BossAlive = aliveBoss > 0;
            int bossTarget = GameConfig.BossCountAt(GS.Elapsed);
            if (aliveBoss < bossTarget && GS.Elapsed >= GS.NextBossTime)
            {
                SpawnBoss(aliveBoss);
                // 同批次还缺 Boss 时按较短间隔继续补齐，否则走正常 Boss 间隔
                GS.NextBossTime = GS.Elapsed + (aliveBoss + 1 < bossTarget
                    ? GameConfig.Survival.BossBatchGap
                    : GameConfig.Survival.BossInterval);
            }

            UpdateProjectiles(dt, pp);

            float radius = GS.DevourRadius();

            for (int i = Registry.Enemies.Count - 1; i >= 0; i--)
            {
                var e = Registry.Enemies[i];
                if (e == null) { Registry.Enemies.RemoveAt(i); continue; }

                Vector3 p = e.transform.position;
                float dx = pp.x - p.x;
                float dz = pp.z - p.z;
                float dl = Mathf.Sqrt(dx * dx + dz * dz);
                if (dl < 0.0001f) dl = 1f;

                // 血量归零立即死亡；Boss 有骨骼 die 动画，先播完再销毁
                if (e.Hp <= 0f)
                {
                    if (e.IsBoss && e.Animator != null)
                    {
                        if (!e.Dying)
                        {
                            KillKit.Reward(e);
                            e.Dying = true;
                            e.DieT = GameConfig.Ai.BossDieAnimTime;
                            BossMotion.Play(e, e.Def != null ? e.Def.AnimDie : "die");
                        }
                        e.DieT -= dt;
                        if (e.DieT <= 0f)
                        {
                            ObjectPool.Despawn(e.gameObject);
                            Registry.Enemies.RemoveAt(i);
                        }
                    }
                    else
                    {
                        KillKit.Reward(e);
                        Registry.Enemies.RemoveAt(i);
                    }
                    continue;
                }

                if (e.IsBoss && e.Dying) continue;

                // Boss 受击瞬间播一次 hit（不覆盖 attack）
                if (e.IsBoss && e.Animator != null && e.Hp < e.LastHp - 0.01f)
                {
                    e.LastHp = e.Hp;
                    if (!(e.AttackT > 0f)) { BossMotion.Play(e, e.Def.AnimHit); e.HitT = GameConfig.Ai.BossHitAnimTime; }
                }

                // 吞噬：核心圈内秒杀；强化外圈只磨血，越近越痛
                bool boosted = GS.DevourBoosted();
                float coreR = boosted ? radius * GameConfig.DevourCoreRatio : radius;
                if (!e.IsBoss && dl < coreR && e.Size() <= GS.DevourSizeCap())
                {
                    KillKit.Reward(e);
                    Registry.Enemies.RemoveAt(i);
                    continue;
                }

                if (boosted && !e.IsBoss && dl < radius)
                {
                    e.DevourTick -= dt;
                    if (e.DevourTick <= 0f)
                    {
                        e.DevourTick = GameConfig.DevourDotTick;
                        float band = Mathf.Max(0.001f, radius - coreR);   // 核心圈边缘满额、外圈边缘 DevourDotEdge
                        float t = Mathf.Clamp01(1f - (dl - coreR) / band);
                        float factor = GameConfig.Ai.DevourDotEdge + (1f - GameConfig.Ai.DevourDotEdge) * Mathf.Pow(t, GameConfig.Ai.DevourDotPow);
                        float dmg = GameConfig.DevourDot * factor * GS.DmgMul * GameConfig.DevourDotTick;
                        e.Hp -= dmg;
                        Fx.PopDmg(p, dmg, false);
                    }
                }

                e.HitCd -= dt;
                float sp = e.MoveSpeed(profile.spdMul);
                float tx = 0f, tz = 0f;

                // 强化吞噬 = 真空吸附，敌人被拖向玩家（与冲撞的玩家突进分工）
                if (boosted && !e.IsBoss && dl < radius * GameConfig.Ai.DevourPullRadiusMul)
                {
                    float pullRange = radius * GameConfig.Ai.DevourPullRadiusMul;
                    float pull = GameConfig.Ai.DevourPullFar * (1f - dl / pullRange) + GameConfig.Ai.DevourPullNear;
                    p.x += (dx / dl) * pull * dt;
                    p.z += (dz / dl) * pull * dt;
                    e.transform.position = p;
                }

                if (e.IsBoss)
                {
                    // Boss 走位：直线逼近 + 二阶段提速（砸地攻击由 EnemyCombat 统一结算）
                    StepBossMove(e, dx, dz, dl, ref sp, out tx, out tz);
                }
                else if (e.Def != null)
                {
                    // 普通敌兵：走位决策在 EnemySteer
                    var steer = EnemySteer.Steer(e, dx, dz, dl, dt);
                    tx = steer.Tx;
                    tz = steer.Tz;
                    sp *= steer.SpeedMul;
                }
                else
                {
                    // 兜底：没有定义的单位直线追击
                    tx = dx / dl;
                    tz = dz / dl;
                }

                // 攻击统一由 EnemyCombat 按 EnemyAttackDef 结算（远程投射/Boss 砸地；Contact 在下面贴身处理）
                EnemyCombat.Tick(e, dx, dz, dl, dt, transform);

                // 贴身伤害：只有 Contact 攻击方式的敌人贴身造成伤害；Projectile 靠弹体，None 不攻击
                // 即将被核心圈吞下的小体积敌人不造成贴身伤害
                bool willBeEaten = !e.IsBoss && dl < coreR && e.Size() <= GS.DevourSizeCap();
                float hitR = e.IsBoss ? GameConfig.Ai.MeleeHitRadiusBoss : GameConfig.Ai.MeleeHitRadius;
                bool isContact = e.Def != null && e.Def.Combat != null && e.Def.Combat.Kind == EnemyAttackKind.Contact;
                if (dl < hitR && !willBeEaten && isContact && e.HitCd <= 0f && e.Damage() > 0f)
                {
                    e.HitCd = GameConfig.Ai.MeleeHitCd;
                    GS.Damage(e.Damage() * profile.dmgMul);
                    Fx.PlayVfx(VfxKeys.HitSheet, pp.x, 1.2f, pp.z, GameConfig.Ai.MeleeHitFx);
                    Fx.Shake(GameConfig.Ai.MeleeHitShake);
                }

                // 受击击退：在 AI 移动之外叠加一段水平速度并线性衰减（攻城锤等近战武器用）
                if (e.KnockX != 0f || e.KnockZ != 0f)
                {
                    p.x += e.KnockX * dt;
                    p.z += e.KnockZ * dt;
                    float decay = GameConfig.RamKnockDecel * dt;
                    e.KnockX = Mathf.MoveTowards(e.KnockX, 0f, decay);
                    e.KnockZ = Mathf.MoveTowards(e.KnockZ, 0f, decay);
                }

                // 被击退的短暂窗口内大幅压制自身追击（否则 Boss 边退边追，净位移几乎为 0）
                float moveSuppress = (e.KnockX != 0f || e.KnockZ != 0f)
                    ? GameConfig.RamKnockMoveSuppress : 1f;

                p.x = GameConfig.ClampArena(p.x + tx * sp * dt * moveSuppress, 4f);
                p.z = GameConfig.ClampArena(p.z + tz * sp * dt * moveSuppress, 4f);
                p.y = 0f;
                e.transform.position = p;
                FaceDir(e, tx, tz);

                if (e.IsBoss) UpdateBossAnim(e, dt, tx, tz, sp);
            }
        }

        /// <summary>Boss 走位：直线逼近，血量低于 Phase2At 提速（攻击结算已移到 EnemyCombat）</summary>
        private static void StepBossMove(EnemyUnit e, float dx, float dz, float dl,
            ref float sp, out float tx, out float tz)
        {
            bool phase2 = e.Hp < e.MaxHp * e.Def.Phase2At;
            sp = e.Def.Speed * (phase2 ? GameConfig.Ai.BossPhase2Mul : 1f);
            tx = dx / dl;
            tz = dz / dl;
        }
    }
}
