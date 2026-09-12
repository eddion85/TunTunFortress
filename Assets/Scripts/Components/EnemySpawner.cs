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

        // ---------------- 重开/复活清场 ----------------
        private void ClearAll(object payload)
        {
            DespawnAllEnemies();
            DespawnAllProjectiles();
            _timer = GameConfig.Survival.SpawnFirstDelay;
            _spawnStamps.Clear();
        }

        /// <summary>复活：按配置清空普通兵与投射物，Boss 保留（继续战斗）</summary>
        private void OnRevive(object payload)
        {
            bool clearMobs = payload is bool && (bool)payload;
            DespawnAllProjectiles();
            if (clearMobs) DespawnNonBossEnemies();
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

        private static void DespawnNonBossEnemies()
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

        private static void DespawnAllProjectiles()
        {
            for (int i = 0; i < Registry.Projectiles.Count; i++)
                if (Registry.Projectiles[i] != null) ObjectPool.Despawn(Registry.Projectiles[i].gameObject);
            Registry.Projectiles.Clear();
        }

        /// <summary>场上存活的普通兵数量（Boss 不占普通兵上限）</summary>
        private static int MobCount() => Registry.CountAlive(e => !e.IsBoss);

        /// <summary>场上存活的 Boss 数量</summary>
        private static int AliveBossCount() => Registry.CountAlive(e => e.IsBoss);

        // ---------------- 模板加载 ----------------
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

        // ---------------- 刷怪调度 ----------------
        /// <summary>滚动窗口限流：最近 SpawnWindowSeconds 秒内出生的普通兵是否已达上限</summary>
        private bool WindowSpawnBlocked()
        {
            float now = GS.Elapsed;
            float win = GameConfig.Survival.SpawnWindowSeconds;
            _spawnStamps.RemoveAll(t => now - t > win);
            return _spawnStamps.Count >= GameConfig.Survival.SpawnPerWindowCap;
        }

        /// <summary>时间驱动的普通兵刷怪：间隔与每波份数都来自配置，达上限时 SpawnOne 内部自行跳过</summary>
        private void TickSpawnSchedule(float dt, DiffProfile profile)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = Mathf.Max(GameConfig.Survival.SpawnIntervalMin, profile.spawnInterval);
            for (int b = 0; b < profile.burst; b++) SpawnOne();
        }

        /// <summary>Boss 按时间表补员：数量目标随存活时间递增（后期难度来源），不足且到点就补</summary>
        private void TickBossSchedule()
        {
            int aliveBoss = AliveBossCount();
            GS.BossAlive = aliveBoss > 0;
            int bossTarget = GameConfig.BossCountAt(GS.Elapsed);
            if (aliveBoss >= bossTarget || GS.Elapsed < GS.NextBossTime) return;

            SpawnBoss(aliveBoss);
            // 同批次还缺 Boss 时按较短间隔继续补齐，否则走正常 Boss 间隔
            GS.NextBossTime = GS.Elapsed + (aliveBoss + 1 < bossTarget
                ? GameConfig.Survival.BossBatchGap
                : GameConfig.Survival.BossInterval);
        }

        /// <summary>
        /// 按配置权重表选怪：权重来自 GameConfig.Survival.MobTable（时间波段，下标对应 EnemyCatalog.Mobs），
        /// 超过当前解锁档位 maxTier 的敌种权重清零，全部为 0 时退回第一个。
        /// </summary>
        private EnemyDef PickKind()
        {
            var profile = GS.Profile();
            var band = GameConfig.MobBandAt(GS.Elapsed);
            var mobs = EnemyCatalog.Mobs;
            int WeightAt(int i) => (i < band.w.Length && i <= profile.maxTier) ? band.w[i] : 0;

            int total = 0;
            for (int i = 0; i < mobs.Length; i++) total += WeightAt(i);
            if (total <= 0) return mobs[0];

            int roll = Random.Range(0, total);
            for (int i = 0; i < mobs.Length; i++)
            {
                roll -= WeightAt(i);
                if (roll < 0) return mobs[i];
            }
            return mobs[0];
        }

        /// <summary>实例化一只敌人并挂齐组件（攻击表现/车轮/血条锚点），数值全部来自 EnemyDef</summary>
        private EnemyUnit SpawnAt(EnemyDef def, float x, float z)
        {
            GameObject prefab = GetTemplate(def);
            if (prefab == null) return null;

            var go = ObjectPool.Spawn(prefab, new Vector3(x, 0f, z), Quaternion.identity, transform);
            float sc = def.Scale;
            go.transform.localScale = new Vector3(sc, sc, sc);

            var unit = go.GetComponent<EnemyUnit>();
            if (unit == null) unit = go.AddComponent<EnemyUnit>();
            unit.Init(def, def.IsBoss, GS.Profile().hpMul);

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

        private void SpawnOne()
        {
            // 普通兵数量受配置上限约束（Boss 不占此名额），总单位数另有 MaxUnits 兜底
            if (MobCount() >= GameConfig.Survival.MobCap) return;
            if (WindowSpawnBlocked()) return; // 滚动窗口出生数量上限
            if (Registry.Enemies.Count >= GameConfig.MaxUnits || player == null) return;

            var def = PickKind();
            Vector3 ringPos = PickRingPosition(GameConfig.Ai.SpawnRingMin, GameConfig.Ai.SpawnRingJitter);
            if (SpawnAt(def, ringPos.x, ringPos.z) != null) _spawnStamps.Add(GS.Elapsed);
        }

        /// <summary>在玩家外圈随机环上取一个出生点（无边界地图时坐标不再钳制）</summary>
        private Vector3 PickRingPosition(float ringMin, float ringJitter)
        {
            Vector3 pp = player.position;
            float ang = Random.value * Mathf.PI * 2f;
            float dist = ringMin + Random.value * ringJitter;
            return new Vector3(
                GameConfig.ClampArena(pp.x + Mathf.Cos(ang) * dist),
                0f,
                GameConfig.ClampArena(pp.z + Mathf.Sin(ang) * dist));
        }

        /// <summary>Boss 出场：种类/模型/数值/动画全部来自 EnemyCatalog.BossAt；slot=同屏序号，多只时环形落位</summary>
        private void SpawnBoss(int slot)
        {
            if (player == null) return;

            EnemyDef def = EnemyCatalog.BossAt(GS.Elapsed);
            GameObject prefab = GetTemplate(def);
            if (prefab == null) return;

            GS.BossAlive = true;

            Vector3 pos = PickBossPosition(slot);
            var go = ObjectPool.Spawn(prefab, pos, Quaternion.identity, transform);
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
            Fx.PlayVfx(VfxKeys.RingWave, pos.x, 0.3f, pos.z, GameConfig.Ai.BossAppearRingFx);
            Fx.PlayVfx(VfxKeys.LightBeam, pos.x, 2f, pos.z, GameConfig.Ai.BossAppearBeamFx);
            Fx.Shake(GameConfig.Ai.BossAppearShake);
            GameBus.Emit(GameEvents.Boss, true);
            GameBus.Emit(GameEvents.Float, "农场守卫来袭!");
        }

        /// <summary>多只 Boss 沿玩家外圈不同方向落位，避免叠在一起</summary>
        private Vector3 PickBossPosition(int slot)
        {
            Vector3 pp = player.position;
            float ang = -Mathf.PI / 2f + slot * (Mathf.PI * 2f / Mathf.Max(2, GameConfig.Survival.BossCountMax));
            float ring = GameConfig.Ai.BossSpawnRing;
            return new Vector3(
                GameConfig.ClampArena(pp.x + Mathf.Cos(ang) * ring),
                0f,
                GameConfig.ClampArena(pp.z + Mathf.Sin(ang) * ring));
        }

        // ---------------- 敌人投射物（所有敌人远程攻击共用飞行/命中模拟） ----------------
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

                if (TryProjectileHitPlayer(a, p, pp, hitR)) { Registry.Projectiles.RemoveAt(i); continue; }
                if (a.Life <= 0f)
                {
                    ObjectPool.Despawn(a.gameObject);
                    Registry.Projectiles.RemoveAt(i);
                }
            }
        }

        /// <summary>投射物命中玩家：扣血+命中反馈并回收，返回是否命中</summary>
        private static bool TryProjectileHitPlayer(Projectile a, Vector3 p, Vector3 pp, float hitR)
        {
            float dx = pp.x - p.x;
            float dz = pp.z - p.z;
            if (dx * dx + dz * dz >= hitR * hitR) return false;

            GS.Damage(a.Dmg);
            Fx.PlayVfx(VfxKeys.HitSheet, p.x, 1f, p.z, GameConfig.Ai.ArrowHitFx);
            Fx.Shake(GameConfig.Ai.ArrowHitShake);
            ObjectPool.Despawn(a.gameObject);
            return true;
        }

        // ---------------- 单个敌人逐帧驱动 ----------------
        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            if (player == null || GS.Over || GS.Paused) return;
            Vector3 pp = player.position;

            var profile = GS.Profile();
            TickSpawnSchedule(dt, profile);
            TickBossSchedule();
            UpdateProjectiles(dt, pp);
            TickAllEnemies(dt, pp, profile);
        }

        private void TickAllEnemies(float dt, Vector3 pp, DiffProfile profile)
        {
            float devourRadius = GS.DevourRadius();
            bool boosted = GS.DevourBoosted();
            float coreR = boosted ? devourRadius * GameConfig.DevourCoreRatio : devourRadius;

            for (int i = Registry.Enemies.Count - 1; i >= 0; i--)
            {
                var e = Registry.Enemies[i];
                if (e == null) { Registry.Enemies.RemoveAt(i); continue; }

                Vector3 p = e.transform.position;
                float dx = pp.x - p.x;
                float dz = pp.z - p.z;
                float dl = Mathf.Sqrt(dx * dx + dz * dz);
                if (dl < 0.0001f) dl = 1f;

                if (HandleDeath(e, dt, i)) continue;
                if (e.IsBoss && e.Dying) continue;
                TickBossHitAnim(e, dt);
                if (TickDevour(e, ref p, dx, dz, dt, dl, devourRadius, coreR, boosted, i)) continue;

                e.HitCd -= dt;
                Vector3 move = ResolveMoveDir(e, dx, dz, dl, dt, profile, out float speed);

                // 攻击统一由 EnemyCombat 按 EnemyAttackDef 结算（远程投射/Boss 砸地；Contact 在下面贴身处理）
                EnemyCombat.Tick(e, dx, dz, dl, dt, transform);
                TickContactDamage(e, dl, coreR, pp, profile.dmgMul);
                IntegrateMovement(e, p, move.x, move.z, speed, dt);

                if (e.IsBoss) UpdateBossAnim(e, dt, move.x, move.z, speed);
            }
        }

        /// <summary>血量归零处理：Boss 播完 die 动画再销毁，普通兵立即结算。返回 true 表示已从列表移除</summary>
        private bool HandleDeath(EnemyUnit e, float dt, int index)
        {
            if (e.Hp > 0f) return false;

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
                    Registry.Enemies.RemoveAt(index);
                }
                return true;
            }

            KillKit.Reward(e);
            Registry.Enemies.RemoveAt(index);
            return true;
        }

        /// <summary>Boss 受击瞬间播一次 hit（不覆盖 attack）</summary>
        private static void TickBossHitAnim(EnemyUnit e, float dt)
        {
            if (!e.IsBoss || e.Animator == null || !(e.Hp < e.LastHp - 0.01f)) return;
            e.LastHp = e.Hp;
            if (!(e.AttackT > 0f))
            {
                BossMotion.Play(e, e.Def.AnimHit);
                e.HitT = GameConfig.Ai.BossHitAnimTime;
            }
        }

        /// <summary>吞噬：核心圈内秒杀；强化外圈只磨血，越近越痛；强化时真空吸附。返回 true 表示敌人已被吞下并移除</summary>
        private bool TickDevour(EnemyUnit e, ref Vector3 p, float dx, float dz, float dt, float dl,
            float radius, float coreR, bool boosted, int index)
        {
            // 核心圈：可吞下体积的非 Boss 直接结算
            if (!e.IsBoss && dl < coreR && e.Size() <= GS.DevourSizeCap())
            {
                KillKit.Reward(e);
                Registry.Enemies.RemoveAt(index);
                return true;
            }

            // 强化外圈：按离核心圈的距离做持续掉血
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

            // 强化吞噬 = 真空吸附，敌人被拖向玩家（与冲撞的玩家突进分工）
            if (boosted && !e.IsBoss && dl < radius * GameConfig.Ai.DevourPullRadiusMul)
            {
                float pullRange = radius * GameConfig.Ai.DevourPullRadiusMul;
                float pull = GameConfig.Ai.DevourPullFar * (1f - dl / pullRange) + GameConfig.Ai.DevourPullNear;
                p.x += (dx / dl) * pull * dt;
                p.z += (dz / dl) * pull * dt;
                e.transform.position = p;
            }
            return false;
        }

        /// <summary>决策本帧移动方向与速度：Boss 直线逼近+二阶段提速，普通兵走 EnemySteer，无定义时兜底直冲</summary>
        private static Vector3 ResolveMoveDir(EnemyUnit e, float dx, float dz, float dl, float dt,
            DiffProfile profile, out float speed)
        {
            speed = e.MoveSpeed(profile.spdMul);
            float tx, tz;
            if (e.IsBoss)
            {
                bool phase2 = e.Hp < e.MaxHp * e.Def.Phase2At;
                speed = e.Def.Speed * (phase2 ? GameConfig.Ai.BossPhase2Mul : 1f);
                tx = dx / dl;
                tz = dz / dl;
            }
            else if (e.Def != null)
            {
                var steer = EnemySteer.Steer(e, dx, dz, dl, dt);
                tx = steer.Tx;
                tz = steer.Tz;
                speed *= steer.SpeedMul;
            }
            else
            {
                tx = dx / dl;
                tz = dz / dl;
            }
            return new Vector3(tx, 0f, tz);
        }

        /// <summary>贴身接触伤害：只有 Contact 攻击方式的敌人贴身造成伤害；即将被吞下的敌人不造成伤害</summary>
        private static void TickContactDamage(EnemyUnit e, float dl, float coreR, Vector3 pp, float dmgMul)
        {
            bool willBeEaten = !e.IsBoss && dl < coreR && e.Size() <= GS.DevourSizeCap();
            float hitR = e.IsBoss ? GameConfig.Ai.MeleeHitRadiusBoss : GameConfig.Ai.MeleeHitRadius;
            bool isContact = e.Def != null && e.Def.Combat != null && e.Def.Combat.Kind == EnemyAttackKind.Contact;
            if (!(dl < hitR && !willBeEaten && isContact && e.HitCd <= 0f && e.Damage() > 0f)) return;

            e.HitCd = GameConfig.Ai.MeleeHitCd;
            GS.Damage(e.Damage() * dmgMul);
            Fx.PlayVfx(VfxKeys.HitSheet, pp.x, 1.2f, pp.z, GameConfig.Ai.MeleeHitFx);
            Fx.Shake(GameConfig.Ai.MeleeHitShake);
        }

        /// <summary>位移积分：先叠加击退速度并衰减，再在被击退窗口压制追击，最后钳制在场地内并朝向移动方向</summary>
        private static void IntegrateMovement(EnemyUnit e, Vector3 p, float tx, float tz, float speed, float dt)
        {
            if (e.KnockX != 0f || e.KnockZ != 0f)
            {
                p.x += e.KnockX * dt;
                p.z += e.KnockZ * dt;
                float decay = GameConfig.RamKnockDecel * dt;
                e.KnockX = Mathf.MoveTowards(e.KnockX, 0f, decay);
                e.KnockZ = Mathf.MoveTowards(e.KnockZ, 0f, decay);
            }

            // 被击退的短暂窗口内大幅压制自身追击（否则 Boss 边退边追，净位移几乎为 0）
            bool knocked = e.KnockX != 0f || e.KnockZ != 0f;
            float moveSuppress = knocked ? GameConfig.RamKnockMoveSuppress : 1f;

            p.x = GameConfig.ClampArena(p.x + tx * speed * dt * moveSuppress, 4f);
            p.z = GameConfig.ClampArena(p.z + tz * speed * dt * moveSuppress, 4f);
            p.y = 0f;
            e.transform.position = p;
            FaceMovementDir(e, tx, tz);
        }

        private static void FaceMovementDir(EnemyUnit e, float tx, float tz)
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
    }
}
