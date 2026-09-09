using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>VfxLayer partial：世界空间伤害飘字（弹出、上飘、淡出、对象池复用）。</summary>
    public partial class VfxLayer
    {
        // ---------------- 伤害数字 ----------------
        private void OnDmg(object payload)
        {
            var p = payload as DmgPayload;
            if (p == null || container == null || _dmgs.Count >= maxDmg) return;

            Text lb;
            if (_dmgPool.Count > 0)
            {
                lb = _dmgPool[_dmgPool.Count - 1];
                _dmgPool.RemoveAt(_dmgPool.Count - 1);
            }
            else
            {
                var go = new GameObject("Dmg", typeof(RectTransform));
                go.transform.SetParent(container, false);
                lb = go.AddComponent<Text>();
                lb.raycastTarget = false;
                lb.font = UiKit.RuntimeFont();
            }

            lb.gameObject.SetActive(true);
            lb.text = p.text;
            lb.fontSize = p.big ? 46 : 34;
            lb.fontStyle = FontStyle.Bold;
            lb.color = new Color(1f, 0.824f, 0.290f);      // #ffd24a
            lb.alignment = TextAnchor.MiddleCenter;
            lb.horizontalOverflow = HorizontalWrapMode.Overflow;
            lb.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = lb.rectTransform;
            rt.sizeDelta = new Vector2(160f, 60f);
            rt.anchoredPosition = Vector2.zero;
            lb.color = p.big ? new Color(1f, 0.824f, 0.290f) : new Color(1f, 0.953f, 0.769f);

            _dmgs.Add(new DmgText
            {
                lb = lb,
                world = new Vector3(p.x, p.y, p.z),
                life = 0.6f,
                max = 0.6f,
                dx = Random.value * 46f - 23f,
                big = p.big
            });
        }

        private void UpdateDmgs(float dt)
        {
            for (int i = _dmgs.Count - 1; i >= 0; i--)
            {
                var d = _dmgs[i];
                d.life -= dt;
                if (d.life <= 0f || d.lb == null)
                {
                    if (d.lb != null)
                    {
                        d.lb.gameObject.SetActive(false);
                        if (_dmgPool.Count < 24) _dmgPool.Add(d.lb);
                        else Destroy(d.lb.gameObject);
                    }
                    _dmgs.RemoveAt(i);
                    continue;
                }

                float t = 1f - d.life / d.max;
                Vector2 local;
                if (!Project(d.world, out local))
                {
                    d.lb.gameObject.SetActive(false);
                    continue;
                }
                d.lb.gameObject.SetActive(true);
                d.lb.rectTransform.anchoredPosition = new Vector2(local.x + d.dx * t, local.y - 90f * t);
                d.lb.color = new Color(d.lb.color.r, d.lb.color.g, d.lb.color.b, t < 0.65f ? 1f : (1f - t) / 0.35f);

                // 弹出缩放：先放大再收回
                float baseSize = d.big ? 1.4f : 1.15f;
                float k = t < 0.18f ? baseSize * (1f + (0.18f - t) * 2.2f) : baseSize;
                d.lb.rectTransform.localScale = new Vector3(k, k, 1f);
            }
        }
    }
}
