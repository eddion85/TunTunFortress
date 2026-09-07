using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 掉落物系统：负责金币 / 血包 / 磁铁的生成、浮动自转、磁吸与拾取。
    /// 对应 LayaAir 版 components/DropSystem.ts（从 EnemySpawner 拆出，单一职责）。
    /// </summary>
    public class DropSystem : MonoBehaviour
    {
        [Header("掉落物预制体")]
        [SerializeField] private GameObject tplCoin;
        [SerializeField] private GameObject tplHealth;
        [SerializeField] private GameObject tplMagnet;

        [Header("引用")]
        [SerializeField] private Transform player;

        private static DropSystem _inst;

        private void OnEnable()
        {
            _inst = this;
            GameBus.On(GameEvents.Restart, ClearAll);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Restart, ClearAll);
            if (_inst == this) _inst = null;
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
            for (int i = 0; i < Registry.Drops.Count; i++)
                if (Registry.Drops[i] != null) ObjectPool.Despawn(Registry.Drops[i].gameObject);
            Registry.Drops.Clear();
        }

        /// <summary>供 EnemySpawner 在敌人死亡时调用</summary>
        public static void SpawnDrop(float x, float z, EnemyKind kind, bool isBoss)
        {
            if (_inst != null) _inst.Drop(x, z, kind, isBoss);
        }

        private void Drop(float x, float z, EnemyKind kind, bool isBoss)
        {
            GameObject tpl = null;
            string type = "";
            float roll = Random.value;

            if (isBoss)
            {
                tpl = tplHealth; type = DropItem.TypeHealthBig;
            }
            else if (roll < 0.08f)
            {
                tpl = tplHealth; type = DropItem.TypeHealthSmall;
            }
            else if (roll < 0.12f)
            {
                tpl = tplMagnet; type = DropItem.TypeMagnet;
            }
            else if (roll < 0.55f)
            {
                tpl = tplCoin; type = DropItem.TypeCoin;
            }
            if (tpl == null) return;

            var go = ObjectPool.Spawn(tpl, new Vector3(x, 0.7f, z), Quaternion.identity, transform);

            // 轻微放大让俯视机位下一眼能看见（Prefab 已按真实尺寸归一化，这里只做小幅强调）
            float dsc = type == DropItem.TypeHealthBig ? 1.4f : 1.25f;
            go.transform.localScale = new Vector3(dsc, dsc, dsc);

            var item = go.GetComponent<DropItem>();
            if (item == null) item = go.AddComponent<DropItem>();
            item.Init(type, (kind != null ? kind.prog : 0) + 3);
            Registry.Drops.Add(item);
        }

        private void Update()
        {
            if (GS.Over || GS.Paused) return;
            if (player == null) return;

            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            Vector3 pp = player.position;
            bool magnet = GS.MagnetTimer > 0f;
            float pickR = GS.DevourRadius() + 1.6f;

            for (int i = Registry.Drops.Count - 1; i >= 0; i--)
            {
                var d = Registry.Drops[i];
                if (d == null) { Registry.Drops.RemoveAt(i); continue; }

                d.Life -= dt;
                Vector3 p = d.transform.position;
                p.y = 0.85f + Mathf.Sin(GS.Elapsed * 3f + i) * 0.25f;

                d.Spin += dt * 2.4f;
                d.transform.localRotation = Quaternion.Euler(0f, d.Spin * Mathf.Rad2Deg, 0f);

                float dx = pp.x - p.x;
                float dz = pp.z - p.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < 0.0001f) dist = 1f;

                // 磁铁生效时吸附 [策划书 8 道具]
                if (magnet && dist < EnemyDefs.Pickups.MagnetRadius)
                {
                    p.x += (dx / dist) * 12f * dt;
                    p.z += (dz / dist) * 12f * dt;
                }
                d.transform.position = p;

                if (dist < pickR)
                {
                    if (d.Type == DropItem.TypeCoin)
                    {
                        GS.Coins += d.Value;
                        GS.AddExp(4f);
                        AudioKit.PlaySfx(SfxKeys.Coin);
                        GameBus.Emit(GameEvents.Float, "+" + d.Value + " 金币");
                        Fx.PlayVfx(VfxKeys.StarSpark, p.x, p.y + 0.6f, p.z, 1.3f);
                    }
                    else if (d.Type == DropItem.TypeHealthSmall || d.Type == DropItem.TypeHealthBig)
                    {
                        float v = d.Type == DropItem.TypeHealthBig ? EnemyDefs.Pickups.HealthBig : EnemyDefs.Pickups.HealthSmall;
                        GS.Heal(v);
                        AudioKit.PlaySfx(SfxKeys.Health);
                        GameBus.Emit(GameEvents.Float, "+" + v + " HP");
                        Fx.PlayVfx(VfxKeys.SoftCircle, p.x, p.y + 0.6f, p.z, 1.8f);
                    }
                    else if (d.Type == DropItem.TypeMagnet)
                    {
                        GS.MagnetTimer = EnemyDefs.Pickups.MagnetTime;
                        AudioKit.PlaySfx(SfxKeys.Magnet);
                        GameBus.Emit(GameEvents.Float, "磁力吸附!");
                        Fx.PlayVfx(VfxKeys.RingWave, p.x, 0.3f, p.z, 2.2f);
                    }

                    ObjectPool.Despawn(d.gameObject);
                    Registry.Drops.RemoveAt(i);
                    continue;
                }

                if (d.Life <= 0f)
                {
                    ObjectPool.Despawn(d.gameObject);
                    Registry.Drops.RemoveAt(i);
                }
            }
        }
    }
}
