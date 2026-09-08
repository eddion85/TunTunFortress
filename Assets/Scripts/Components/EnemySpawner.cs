using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 敌人生成与 AI：羊/奶牛逃跑、农夫追击、弓箭手远程、骑兵绕后、Boss 砸地。
    /// 对应 LayaAir 版 components/EnemySpawner.ts。
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Transform player;

        [Header("敌人预制体")]
        [SerializeField] private GameObject tplSheep;
        [SerializeField] private GameObject tplCow;
        [SerializeField] private GameObject tplFarmer;
        [SerializeField] private GameObject tplArcher;
        [SerializeField] private GameObject tplRider;

        [Header("投射物 / Boss")]
        [SerializeField] private GameObject tplArrow;
        [SerializeField] private GameObject tplBoss;   // 战争坦克

        [SerializeField] private float arrowHeight = 1f;

        private readonly Dictionary<string, GameObject> _tpl = new Dictionary<string, GameObject>();
        private float _timer = GameConfig.Survival.SpawnFirstDelay;
        private int _devourSeq;
        private readonly List<float> _spawnStamps = new List<float>(); // 普通兵出生时间戳（滚动窗口限流）

        // ---------------- 生命周期 ----------------
        private void Awake()
        {
            _tpl["sheep"] = tplSheep;
            _tpl["cow"] = tplCow;
            _tpl["farmer"] = tplFarmer;
            _tpl["archer"] = tplArcher;
            _tpl["rider"] = tplRider;
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
        private static int MobCount()
        {
            int n = 0;
            for (int i = 0; i < Registry.Enemies.Count; i++)
            {
                var e = Registry.Enemies[i];
                if (e != null && !e.IsBoss && !e.Dead) n++;
            }
            return n;
        }

        /// <summary>场上存活的 Boss 数量</summary>
        private static int AliveBossCount()
        {
            int n = 0;
            for (int i = 0; i < Registry.Enemies.Count; i++)
            {
                var e = Registry.Enemies[i];
                if (e != null && e.IsBoss && !e.Dead) n++;
            }
            return n;
        }

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
        /// 按配置权重表选怪：权重来自 GameConfig.Survival.MobTable（时间波段），
        /// 超过当前解锁档位 maxTier 的敌种权重清零，全部为 0 时退回羊。
        /// </summary>
        private EnemyKind PickKind()
        {
            var profile = GS.Profile();
            var band = GameConfig.MobBandAt(GS.Elapsed);
            int total = 0;
            for (int i = 0; i < EnemyDefs.KINDS.Length; i++)
            {
                int w = (i < band.w.Length && i <= profile.maxTier) ? band.w[i] : 0;
                total += w;
            }
            if (total <= 0) return EnemyDefs.KINDS[0];

            int roll = Random.Range(0, total);
            for (int i = 0; i < EnemyDefs.KINDS.Length; i++)
            {
                int w = (i < band.w.Length && i <= profile.maxTier) ? band.w[i] : 0;
                roll -= w;
                if (roll < 0) return EnemyDefs.KINDS[i];
            }
            return EnemyDefs.KINDS[0];
        }

        private EnemyUnit SpawnAt(EnemyKind kind, float x, float z)
        {
            GameObject prefab;
            if (!_tpl.TryGetValue(kind.key, out prefab) || prefab == null) return null;

            var go = ObjectPool.Spawn(prefab, new Vector3(x, 0f, z), Quaternion.identity, transform);
            float sc = kind.scale;
            go.transform.localScale = new Vector3(sc, sc, sc);

            // 血量倍率完全由时间难度配置推导
            float hpMul = GS.Profile().hpMul;

            var unit = go.GetComponent<EnemyUnit>();
            if (unit == null) unit = go.AddComponent<EnemyUnit>();
            unit.Init(kind, false, hpMul);
            Registry.Enemies.Add(unit);
            return unit;
        }

        private void Spawn()
        {
            // 普通兵数量受配置上限约束（Boss 不占此名额），总单位数另有 MaxUnits 兜底
            if (MobCount() >= GameConfig.Survival.MobCap) return;
            if (WindowSpawnBlocked()) return; // 30s 滚动窗口出生数量上限
            if (Registry.Enemies.Count >= GameConfig.MaxUnits || player == null) return;
            var kind = PickKind();
            Vector3 pp = player.position;
            float ang = Random.value * Mathf.PI * 2f;
            float dist = 15f + Random.value * 8f;
            float half = GameConfig.ArenaHalf;
            float x = Mathf.Clamp(pp.x + Mathf.Cos(ang) * dist, -half, half);
            float z = Mathf.Clamp(pp.z + Mathf.Sin(ang) * dist, -half, half);
            if (SpawnAt(kind, x, z) != null) _spawnStamps.Add(GS.Elapsed);
        }

        /// <summary>Boss 出场：使用专属战争坦克模型；强度随存活时间成长（配置驱动）。slot=同屏序号，多只时环形落位</summary>
        private void SpawnBoss(int slot)
        {
            if (player == null) return;

            Vector3 pp = player.position;
            // Boss 复用骑兵的战斗数值/行为，但用专属坦克模型
            var kind = EnemyDefs.KindByKey("rider");
            GameObject prefab = tplBoss != null ? tplBoss : tplRider;
            if (prefab == null) return;

            GS.BossAlive = true;

            // 多只 Boss 沿玩家外圈不同方向落位，避免叠在一起
            float ang = -Mathf.PI / 2f + slot * (Mathf.PI * 2f / Mathf.Max(2, GameConfig.Survival.BossCountMax));
            float bx = Mathf.Clamp(pp.x + Mathf.Cos(ang) * 18f, -GameConfig.ArenaHalf, GameConfig.ArenaHalf);
            float bz = Mathf.Clamp(pp.z + Mathf.Sin(ang) * 18f, -GameConfig.ArenaHalf, GameConfig.ArenaHalf);
            var go = ObjectPool.Spawn(prefab, new Vector3(bx, 0f, bz), Quaternion.identity, transform);
            go.transform.localScale = new Vector3(EnemyDefs.Boss.Scale, EnemyDefs.Boss.Scale, EnemyDefs.Boss.Scale);

            // Boss 血量 = 时间难度倍率 × Boss 档位成长
            float hpMul = GS.Profile().hpMul * GameConfig.BossHpMulAt(GS.Elapsed);

            var unit = go.GetComponent<EnemyUnit>();
            if (unit == null) unit = go.AddComponent<EnemyUnit>();
            unit.Init(kind, true, hpMul);
            unit.Animator = BossMotion.Setup(go);
            unit.AnimClip = "idle";
            Registry.Enemies.Add(unit);

            AudioKit.PlaySfx(SfxKeys.BossAppear);
            AudioKit.PlayMusic(BgmKeys.Boss);
            var bp = unit.transform.position;
            Fx.PlayVfx(VfxKeys.RingWave, bp.x, 0.3f, bp.z, 7f);
            Fx.PlayVfx(VfxKeys.LightBeam, bp.x, 2f, bp.z, 5.5f);
            Fx.Shake(2.4f);
            GameBus.Emit(GameEvents.Boss, true);
            GameBus.Emit(GameEvents.Float, "农场守卫来袭!");
        }

        // ---------------- 投射物 ----------------
        /// <summary>弓箭手射箭 [策划书 4.4]</summary>
        private void ShootArrow(EnemyUnit e, Vector3 pp)
        {
            if (tplArrow == null) return;
            Vector3 p = e.transform.position;
            var go = ObjectPool.Spawn(tplArrow, new Vector3(p.x, arrowHeight, p.z), Quaternion.identity, transform);

            float dx = pp.x - p.x;
            float dz = pp.z - p.z;
            float dl = Mathf.Sqrt(dx * dx + dz * dz);
            if (dl < 0.0001f) dl = 1f;

            var proj = go.GetComponent<Projectile>();
            if (proj == null) proj = go.AddComponent<Projectile>();
            proj.Init(dx / dl, dz / dl, e.Damage() * GS.Profile().dmgMul);
            proj.transform.rotation = Quaternion.LookRotation(new Vector3(dx / dl, 0f, dz / dl), Vector3.up);
            Registry.Projectiles.Add(proj);

            AudioKit.PlaySfx(SfxKeys.Arrow);
        }

        private void UpdateProjectiles(float dt, Vector3 pp)
        {
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
                if (dx * dx + dz * dz < 1.2f * 1.2f)
                {
                    GS.Damage(a.Dmg);
                    Fx.PlayVfx(VfxKeys.HitSheet, p.x, 1f, p.z, 1.6f);
                    Fx.Shake(0.4f);
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

        // ---------------- 吞噬结算 ----------------
        private void Devour(EnemyUnit e)
        {
            e.Dead = true;
            GS.AddCombo();
            _devourSeq = (_devourSeq + 1) % 3;
            AudioKit.PlaySfx(_devourSeq == 0 ? SfxKeys.Devour1 : _devourSeq == 1 ? SfxKeys.Devour2 : SfxKeys.Devour3);

            // Boss 死亡：安排下一只 Boss 的出场时间（配置间隔）；是否真的补由主循环按数量目标判断
            if (e.IsBoss)
            {
                GS.BossWindowStart = GS.Elapsed;
                GS.NextBossTime = GS.Elapsed + GameConfig.Survival.BossInterval;
                AudioKit.PlaySfx(SfxKeys.ExplosionBig);
                GameBus.Emit(GameEvents.Boss, false);
            }

            float exp;
            if (e.Kind != null && e.Kind.expMax > e.Kind.expMin)
                exp = e.Kind.expMin + Random.value * (e.Kind.expMax - e.Kind.expMin);
            else
                exp = e.Kind != null ? e.Kind.exp : 0f;

            GS.AddExp(e.IsBoss ? EnemyDefs.Boss.Exp : exp);
            GS.AddKill(e.Kind != null ? e.Kind.prog : 0, e.IsBoss);

            var ep = e.transform.position;
            // 小怪死亡只播一层爆炸，Boss 才叠满层次
            Fx.PlayVfx(VfxKeys.ExplosionSheet, ep.x, ep.y + 0.9f, ep.z, e.IsBoss ? 5.0f : 1.8f);
            if (e.IsBoss)
            {
                Fx.PlayVfx(VfxKeys.DevourRing, ep.x, ep.y + 0.6f, ep.z, 5.0f);
                Fx.PlayVfx(VfxKeys.StarSpark, ep.x, ep.y + 1.2f, ep.z, 3.0f);
            }
            Fx.Shake(e.IsBoss ? 1.8f : 0.22f);
            DropSystem.SpawnDrop(ep.x, ep.z, e.Kind, e.IsBoss);

            // Boss 有骨骼死亡动画：节点延迟到 die 播放完再销毁（在主循环里处理）
            if (!e.IsBoss || e.Animator == null) ObjectPool.Despawn(e.gameObject);
        }

        private static void FaceDir(EnemyUnit e, float tx, float tz)
        {
            if (tx * tx + tz * tz < 0.0001f) return;
            e.transform.rotation = Quaternion.LookRotation(new Vector3(tx, 0f, tz), Vector3.up);
        }

        /// <summary>Boss 骨骼动画状态机：优先级 attack &gt; hit &gt; run/idle</summary>
        private static void UpdateBossAnim(EnemyUnit e, float dt, float tx, float tz, float sp)
        {
            if (e.Animator == null) return;
            if (e.AttackT > 0f) { e.AttackT -= dt; BossMotion.Play(e, "attack"); return; }
            if (e.AnimClip == "hit")
            {
                e.HitT -= dt;
                if (e.HitT <= 0f) BossMotion.Play(e, "run");
                return;
            }
            float moving = sp * Mathf.Sqrt(tx * tx + tz * tz);
            BossMotion.Play(e, moving > 0.4f ? "run" : "idle");
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
                        if (!e.Dying) { Devour(e); e.Dying = true; e.DieT = 1.6f; BossMotion.Play(e, "die"); }
                        e.DieT -= dt;
                        if (e.DieT <= 0f)
                        {
                            ObjectPool.Despawn(e.gameObject);
                            Registry.Enemies.RemoveAt(i);
                        }
                    }
                    else
                    {
                        Devour(e);
                        Registry.Enemies.RemoveAt(i);
                    }
                    continue;
                }

                if (e.IsBoss && e.Dying) continue;

                // Boss 受击瞬间播一次 hit（不覆盖 attack）
                if (e.IsBoss && e.Animator != null && e.Hp < e.LastHp - 0.01f)
                {
                    e.LastHp = e.Hp;
                    if (!(e.AttackT > 0f)) { BossMotion.Play(e, "hit"); e.HitT = 0.4f; }
                }

                // 吞噬：核心圈内秒杀；强化外圈只磨血，越近越痛
                bool boosted = GS.DevourBoosted();
                float coreR = boosted ? radius * GameConfig.DevourCoreRatio : radius;
                if (!e.IsBoss && dl < coreR && e.Size() <= GS.DevourSizeCap())
                {
                    Devour(e);
                    Registry.Enemies.RemoveAt(i);
                    continue;
                }

                if (boosted && !e.IsBoss && dl < radius)
                {
                    e.DevourTick -= dt;
                    if (e.DevourTick <= 0f)
                    {
                        e.DevourTick = GameConfig.DevourDotTick;
                        float band = Mathf.Max(0.001f, radius - coreR);   // 核心圈边缘满额、外圈边缘 15%
                        float t = Mathf.Clamp01(1f - (dl - coreR) / band);
                        float factor = 0.15f + 0.85f * Mathf.Pow(t, 1.3f);
                        float dmg = GameConfig.DevourDot * factor * GS.DmgMul * GameConfig.DevourDotTick;
                        e.Hp -= dmg;
                        Fx.PopDmg(p, dmg, false);
                    }
                }

                e.HitCd -= dt;
                float sp = e.MoveSpeed(profile.spdMul);
                float tx = 0f, tz = 0f;

                // 强化吞噬 = 真空吸附，敌人被拖向玩家（与冲撞的玩家突进分工）
                if (GS.DevourBoosted() && !e.IsBoss && dl < radius * 2.4f)
                {
                    float pull = 9f * (1f - dl / (radius * 2.4f)) + 3f;
                    p.x += (dx / dl) * pull * dt;
                    p.z += (dz / dl) * pull * dt;
                    e.transform.position = p;
                }

                if (e.IsBoss)
                {
                    // Boss：贴近砸地，血量过半后提速
                    sp = EnemyDefs.Boss.Speed * (e.Hp < e.MaxHp * EnemyDefs.Boss.Phase2At ? 1.4f : 1f);
                    e.SlamCd -= dt;
                    if (dl < EnemyDefs.Boss.SlamRadius && e.SlamCd <= 0f)
                    {
                        e.SlamCd = EnemyDefs.Boss.SlamCd;
                        GS.Damage(EnemyDefs.Boss.Dmg * profile.dmgMul);
                        AudioKit.PlaySfx(SfxKeys.ExplosionBig);
                        Fx.PlayVfx(VfxKeys.RingWave, p.x, 0.3f, p.z, 4.5f);
                        Fx.PlayVfx(VfxKeys.ExplosionSheet, p.x, 1f, p.z, 4.0f);
                        Fx.Shake(1.4f);
                        if (e.Animator != null)
                        {
                            BossMotion.Play(e, "attack");
                            e.AttackT = 0.7f;
                        }
                    }
                    tx = dx / dl;
                    tz = dz / dl;
                }
                else if (e.Kind != null && e.Kind.ranged)
                {
                    // 弓箭手：维持 8~11m 射程带 [策划书 4.4]
                    e.ShootCd -= dt;
                    float sign = dl < 8f ? -1f : dl > 11f ? 1f : 0f;
                    if (dl < 14f && e.ShootCd <= 0f)
                    {
                        e.ShootCd = 2.2f;
                        ShootArrow(e, pp);
                    }
                    tx = (dx / dl) * sign;
                    tz = (dz / dl) * sign;
                }
                else if (e.Kind != null && e.Kind.flank)
                {
                    // 骑兵：远距离绕侧切入 [策划书 4.4]
                    e.FlankT += dt;
                    if (dl > 6f)
                    {
                        float px = -dz / dl;
                        float pz = dx / dl;
                        float w = Mathf.Min(1f, dl / 14f) * e.FlankSide;
                        tx = (dx / dl) * 0.75f + px * w;
                        tz = (dz / dl) * 0.75f + pz * w;
                        float tl = Mathf.Sqrt(tx * tx + tz * tz);
                        if (tl > 0.0001f) { tx /= tl; tz /= tl; }
                    }
                    else
                    {
                        tx = dx / dl;
                        tz = dz / dl;
                    }
                }
                else if (e.Kind != null && e.Kind.wander)
                {
                    // 羊：近逃远追、中间游荡
                    if (dl < 4f)
                    {
                        tx = -(dx / dl); tz = -(dz / dl); sp *= 1.1f;
                    }
                    else if (dl > 9f)
                    {
                        tx = dx / dl; tz = dz / dl; sp *= 0.85f;
                    }
                    else
                    {
                        e.WanderT -= dt;
                        if (e.WanderT <= 0f)
                        {
                            e.WanderT = 1.2f + Random.value * 1.5f;
                            e.WanderA = Random.value * Mathf.PI * 2f;
                        }
                        tx = Mathf.Cos(e.WanderA);
                        tz = Mathf.Sin(e.WanderA);
                        sp *= 0.6f;
                    }
                }
                else
                {
                    // 农夫/奶牛：直线追击
                    tx = dx / dl;
                    tz = dz / dl;
                }

                // 近身伤害（弓箭手靠射箭，不做贴身）
                // 只有即将被核心圈吞下的小体积敌人不造成贴身伤害；
                // 强化外圈的敌人只是被磨血/吸附，仍活着，贴身照常造成伤害
                bool willBeEaten = !e.IsBoss && dl < coreR && e.Size() <= GS.DevourSizeCap();
                float hitR = e.IsBoss ? 3.2f : 1.9f;
                bool isRanged = e.Kind != null && e.Kind.ranged;
                if (dl < hitR && !willBeEaten && !isRanged && e.HitCd <= 0f && e.Damage() > 0f)
                {
                    e.HitCd = 1f;
                    GS.Damage(e.Damage() * profile.dmgMul);
                    Fx.PlayVfx(VfxKeys.HitSheet, pp.x, 1.2f, pp.z, 1.8f);
                    Fx.Shake(0.45f);
                }

                float half = GameConfig.ArenaHalf + 4f;
                p.x = Mathf.Clamp(p.x + tx * sp * dt, -half, half);
                p.z = Mathf.Clamp(p.z + tz * sp * dt, -half, half);
                p.y = 0f;
                e.transform.position = p;
                FaceDir(e, tx, tz);

                if (e.IsBoss) UpdateBossAnim(e, dt, tx, tz, sp);
            }
        }
    }
}
