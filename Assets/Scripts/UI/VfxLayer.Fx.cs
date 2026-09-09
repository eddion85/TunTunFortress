using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>VfxLayer partial：单图/序列帧特效播放、加色材质、特效对象池与逐帧推进。</summary>
    public partial class VfxLayer
    {
        // ---------------- 特效 ----------------
        private Sprite[] LoadFrames(string url)
        {
            Sprite[] arr;
            if (_frameCache.TryGetValue(url, out arr)) return arr;
            arr = Resources.LoadAll<Sprite>(url);
            if (arr == null || arr.Length == 0)
            {
                // 没切成子 sprite 时，退回整图
                var single = Resources.Load<Sprite>(url);
                arr = single != null ? new[] { single } : new Sprite[0];
            }
            _frameCache[url] = arr;
            return arr;
        }

        private Image ObtainFx(List<Image> pool)
        {
            Image img;
            if (pool.Count > 0)
            {
                img = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            }
            else
            {
                var go = new GameObject("Fx", typeof(RectTransform));
                go.transform.SetParent(container, false);
                img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
            }
            img.gameObject.SetActive(true);
            img.color = Color.white;
            img.transform.localRotation = Quaternion.identity;
            img.rectTransform.anchoredPosition = Vector2.zero;
            img.rectTransform.localScale = Vector3.one;
            return img;
        }

        private void RecycleFx(Image img, List<Image> pool)
        {
            if (img == null) return;
            img.gameObject.SetActive(false);
            if (pool.Count < 24) pool.Add(img);
            else Destroy(img.gameObject);
        }

        /// <summary>加色材质（懒加载，Resources/shaders/UIAdditive）</summary>
        private static Material AdditiveMaterial()
        {
            if (_additiveMat == null)
            {
                var sh = Resources.Load<Shader>("shaders/UIAdditive");
                if (sh != null) _additiveMat = new Material(sh) { name = "Runtime_UIAdditive", hideFlags = HideFlags.HideAndDontSave };
            }
            return _additiveMat;
        }

        /// <summary>按特效资源选择材质：黑底发光类用加色，其余用 uGUI 默认透明混合</summary>
        private void ApplyFxMaterial(Image img, string url)
        {
            var mat = AdditiveUrls.Contains(url) ? AdditiveMaterial() : null;
            if (mat != null) mat.SetFloat("_AddBoost", additiveBoost);
            img.material = mat;   // null = 回到 UI/Default，保证对象池复用时材质正确切换
        }

        private void OnVfx(object payload)
        {
            var p = payload as VfxPayload;
            if (p == null || container == null) return;

            // 拖尾走独立列表与对象池，保证烟团再多也挤不掉受击/爆炸等战斗特效
            var live = p.trail ? _trailItems : _items;
            var pool = p.trail ? _trailPool : _fxPool;
            int cap = p.trail ? Mathf.Max(1, maxTrail) : maxFx;
            if (live.Count >= cap)
            {
                // 池满时淘汰最老的一个，而不是直接丢弃新特效
                var oldest = live[0];
                live.RemoveAt(0);
                if (oldest != null && oldest.img != null) RecycleFx(oldest.img, pool);
            }

            var frames = LoadFrames(p.url);
            if (frames.Length == 0) return;

            var img = ObtainFx(pool);
            ApplyFxMaterial(img, p.url);
            if (p.trail) img.transform.SetAsFirstSibling(); // 扬尘压在战斗特效/玩家下层
            img.sprite = frames[0];

            bool sheet = frames.Length > 1;
            float life = p.life > 0f ? p.life : (sheet ? 0.28f : 0.34f);
            live.Add(new FxItem
            {
                img = img,
                world = new Vector3(p.x, p.y, p.z),
                life = life,
                maxLife = life,
                frames = frames,
                scale = p.scale,
                spin = sheet ? 0f : Random.value * 40f - 20f,
                rise = p.rise >= 0f ? p.rise : (sheet ? 10f : 26f),
                growEnd = p.grow >= 0f ? p.grow : (sheet ? 0.35f : 0.8f),
                startAlpha = p.alpha >= 0f ? p.alpha : 1f,
                frame = 0
            });
        }

        /// <summary>推进一批特效：序列帧、贴地投影、扩散、上飘、淡出；寿命结束回收到对应池</summary>
        private void StepFxList(List<FxItem> list, List<Image> pool, float dt)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var it = list[i];
                var img = it.img;
                if (img == null) { list.RemoveAt(i); continue; }

                it.life -= dt;
                if (it.life <= 0f)
                {
                    RecycleFx(img, pool);
                    list.RemoveAt(i);
                    continue;
                }

                float t = 1f - it.life / it.maxLife;

                // 序列帧推进
                if (it.frames.Length > 1)
                {
                    int fi = Mathf.Min(it.frames.Length - 1, Mathf.FloorToInt(t * it.frames.Length));
                    if (fi != it.frame)
                    {
                        it.frame = fi;
                        img.sprite = it.frames[fi];
                    }
                }

                Vector2 local;
                if (!Project(it.world, out local))
                {
                    img.gameObject.SetActive(false);
                    continue;
                }
                img.gameObject.SetActive(true);
                img.rectTransform.anchoredPosition = new Vector2(local.x, local.y - t * it.rise);

                float grow = 1f + t * it.growEnd;
                float s = it.scale * grow * 0.75f;
                img.rectTransform.localScale = new Vector3(s, s, 1f);

                var col = img.color;
                // 序列帧只轻微淡出；单帧特效（含拖尾烟团）从 startAlpha 线性淡出
                col.a = it.frames.Length > 1 ? 1f - t * 0.25f : it.startAlpha * (1f - t);
                img.color = col;

                if (it.spin != 0f) img.rectTransform.Rotate(0f, 0f, it.spin * dt);
            }
        }
    }
}
