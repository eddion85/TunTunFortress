using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>VfxLayer partial：敌人头顶血条（含满血小兵与 Boss 加宽血条，每帧实时刷新）。</summary>
    public partial class VfxLayer
    {
        // ---------------- 血条 ----------------
        private Bar ObtainBar()
        {
            for (int i = 0; i < _bars.Count; i++)
                if (_bars[i] != null && _bars[i].root != null && !_bars[i].root.activeSelf) return _bars[i];

            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(container, false);
            var bg = go.AddComponent<Image>();
            bg.color = barBgColor;
            bg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            var fill = fillGo.AddComponent<Image>();
            fill.color = barFillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.raycastTarget = false;

            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(1.5f, 1.5f);
            frt.offsetMax = new Vector2(-1.5f, -1.5f);

            var bar = new Bar { root = go, fill = fill };
            _bars.Add(bar);
            return bar;
        }

        private void UpdateBars()
        {
            int used = 0;
            for (int i = 0; i < Registry.Enemies.Count && used < maxBar; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;

                // 所有敌人都显示血条（含满血小兵），Boss 更宽；fillAmount 每帧实时刷新
                float ratio = Mathf.Clamp01(e.Hp / Mathf.Max(1f, e.MaxHp));

                var bar = ObtainBar();
                Vector3 p = e.transform.position;
                float h = e.IsBoss ? 3.4f : 1.8f;
                Vector2 local;
                Vector3 barWorld = e.BarAnchor != null ? e.BarAnchor.position : new Vector3(p.x, p.y + h, p.z);
                if (!Project(barWorld, out local))
                {
                    bar.root.SetActive(false);
                    continue;
                }

                bar.root.SetActive(true);
                float w = e.IsBoss ? 220f : 60f;
                float bh = e.IsBoss ? 20f : 8f;

                var rt = bar.root.transform as RectTransform;
                rt.sizeDelta = new Vector2(w, bh);
                rt.anchoredPosition = new Vector2(local.x, local.y);
                bar.fill.fillAmount = ratio;
                used++;
            }

            // 回收未用到的血条
            int active = 0;
            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i] == null || _bars[i].root == null) continue;
                if (_bars[i].root.activeSelf)
                {
                    active++;
                    if (active > used) _bars[i].root.SetActive(false);
                }
            }
        }
    }
}
