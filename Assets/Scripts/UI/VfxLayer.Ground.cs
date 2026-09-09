using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>VfxLayer partial：常驻吞噬圈（呼吸/强化高亮）与玩家、敌人的地面阴影。</summary>
    public partial class VfxLayer
    {
        // ---------------- 吞噬圈 ----------------
        /// <summary>
        /// 常驻吞噬圈：把 3D 世界半径投影成 2D 圆环贴在玩家脚下，
        /// 让玩家随时看清能吞掉多大范围，并随进化/词条实时变化。
        /// </summary>
        private void UpdateDevourRing(float dt)
        {
            if (playerNode == null || container == null) return;

            if (_ring == null)
            {
                var go = new GameObject("DevourRing", typeof(RectTransform));
                go.transform.SetParent(container, false);
                go.transform.SetAsFirstSibling();
                _ring = go.AddComponent<Image>();
                _ring.raycastTarget = false;
                var sp = Resources.Load<Sprite>(VfxKeys.DevourRing);
                if (sp != null) _ring.sprite = sp;
            }

            Vector3 pp = playerNode.position;
            float r = GS.DevourRadius();

            Vector2 cLocal;
            if (!Project(new Vector3(pp.x, 0.05f, pp.z), out cLocal))
            {
                _ring.gameObject.SetActive(false);
                return;
            }
            Vector2 eLocal;
            if (!Project(new Vector3(pp.x + r, 0.05f, pp.z), out eLocal))
            {
                _ring.gameObject.SetActive(false);
                return;
            }

            float px = Mathf.Max(24f, Mathf.Abs(eLocal.x - cLocal.x));

            // 呼吸脉冲，提示这是活跃的吞噬范围
            _ringT += dt;
            float pulse = 1f + Mathf.Sin(_ringT * 3.2f) * 0.05f;

            _ring.gameObject.SetActive(true);
            _ring.rectTransform.sizeDelta = new Vector2(px * 2f * pulse, px * 2f * pulse * 0.55f);
            _ring.rectTransform.anchoredPosition = cLocal;

            // 基础状态柔和显示；强化期间更亮，末段快闪预警
            if (GS.DevourBoosted())
            {
                float left = GS.DevourTimer;
                float blink = left < 1.5f ? 0.45f + Mathf.Abs(Mathf.Sin(_ringT * 14f)) * 0.5f : 0.85f;
                _ring.color = new Color(1f, 0.824f, 0.290f, blink);
            }
            else
            {
                _ring.color = new Color(1f, 1f, 1f, 0.4f + Mathf.Sin(_ringT * 3.2f) * 0.08f);
            }
        }

        // ---------------- 地面阴影 ----------------
        private void UpdateShadows()
        {
            if (container == null) return;
            int n = 0;

            if (playerNode != null)
            {
                UpdateShadow(n++, playerNode.position, 1.5f);
            }
            for (int i = 0; i < Registry.Enemies.Count && n < maxShadow; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;
                float s = e.IsBoss ? 2.2f : 0.55f + (e.Kind != null ? e.Kind.size : 1) * 0.25f;
                UpdateShadow(n++, e.transform.position, s);
            }

            for (int i = n; i < _shadows.Count; i++)
                if (_shadows[i] != null) _shadows[i].gameObject.SetActive(false);
        }

        private void UpdateShadow(int index, Vector3 world, float sizeScale)
        {
            Image sp;
            if (index < _shadows.Count && _shadows[index] != null)
            {
                sp = _shadows[index];
            }
            else
            {
                var go = new GameObject("Shadow", typeof(RectTransform));
                go.transform.SetParent(container, false);
                go.transform.SetAsFirstSibling();
                sp = go.AddComponent<Image>();
                sp.raycastTarget = false;
                var spr = Resources.Load<Sprite>(VfxKeys.ShadowBlob);
                if (spr != null) sp.sprite = spr;
                sp.color = new Color(1f, 1f, 1f, 0.42f);
                if (index < _shadows.Count) _shadows[index] = sp;
                else _shadows.Add(sp);
            }

            Vector2 local;
            if (!Project(new Vector3(world.x, 0.02f, world.z), out local))
            {
                sp.gameObject.SetActive(false);
                return;
            }

            // 距离越远投影越小
            Vector3 sp3 = cam.WorldToScreenPoint(new Vector3(world.x, 0.02f, world.z));
            float scale = Mathf.Max(0.35f, Mathf.Min(2.4f, 26f / Mathf.Max(4f, sp3.z)));
            float w = 86f * sizeScale * scale;
            sp.gameObject.SetActive(true);
            sp.rectTransform.sizeDelta = new Vector2(w, w * 0.5f);
            sp.rectTransform.anchoredPosition = local;
        }
    }
}
