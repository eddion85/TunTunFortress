using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 顶部 HUD：血条 / 经验条 / 吞噬进度条 + 各类数值文字 + 浮动提示。
    /// 对应 LayaAir 版 ui/HUDView.ts。
    /// </summary>
    public class HUDView : MonoBehaviour
    {
        [Header("进度条（Image.type = Filled / Horizontal）")]
        [SerializeField] private Image hpFill;
        [SerializeField] private Image expFill;
        [SerializeField] private Image progFill;

        [Header("文字")]
        [SerializeField] private Text hpLabel;
        [SerializeField] private Text expLabel;
        [SerializeField] private Text progLabel;
        [SerializeField] private Text levelLabel;
        [SerializeField] private Text killLabel;
        [SerializeField] private Text coinLabel;
        [SerializeField] private Text comboLabel;
        [SerializeField] private Text levelNameLabel;

        [Header("浮动提示")]
        [SerializeField] private RectTransform floatRoot;   // 浮动文字挂载点
        [SerializeField] private float floatRise = 120f;
        [SerializeField] private float floatLife = 0.7f;

        private class FloatTip
        {
            public Text lb;
            public float life;
        }

        private readonly List<FloatTip> _tips = new List<FloatTip>();
        private static Font _font;

        private void OnEnable()
        {
            GameBus.On(GameEvents.Float, OnFloat);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Float, OnFloat);
        }

        private static Font DefaultFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        private void OnFloat(object payload)
        {
            string text = payload as string;
            if (string.IsNullOrEmpty(text)) return;

            RectTransform root = floatRoot != null ? floatRoot : (transform as RectTransform);
            if (root == null) return;

            var go = new GameObject("Tip", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var lb = go.AddComponent<Text>();
            lb.raycastTarget = false;
            lb.font = DefaultFont();
            lb.text = text;
            lb.fontSize = 34;
            lb.fontStyle = FontStyle.Bold;
            lb.color = new Color(1f, 0.953f, 0.769f);   // #fff3c4
            lb.alignment = TextAnchor.MiddleCenter;
            lb.horizontalOverflow = HorizontalWrapMode.Overflow;

            var rt = go.transform as RectTransform;
            rt.sizeDelta = new Vector2(320f, 50f);
            rt.anchoredPosition = new Vector2(Random.value * 60f - 30f, 0f);

            _tips.Add(new FloatTip { lb = lb, life = floatLife });
        }

        private void Update()
        {
            if (hpFill != null)
            {
                float r = Mathf.Clamp01(GS.Hp / Mathf.Max(1f, GS.MaxHp));
                hpFill.fillAmount = r;
                // 低血预警：血条低于 30% 开始闪烁，避免玩家没注意到血量
                var c = hpFill.color;
                c.a = r < 0.3f ? 0.55f + Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 8f)) * 0.45f : 1f;
                hpFill.color = c;
            }

            if (expFill != null) expFill.fillAmount = Mathf.Clamp01(GS.Exp / Mathf.Max(1f, GS.ExpNeed));

            // 第三条：Boss 来袭倒计时（Boss 存活时常亮满格）
            if (progFill != null)
            {
                if (GS.BossAlive) progFill.fillAmount = 1f;
                else
                {
                    float win = Mathf.Max(0.001f, GS.NextBossTime - GS.BossWindowStart);
                    progFill.fillAmount = Mathf.Clamp01((GS.Elapsed - GS.BossWindowStart) / win);
                }
            }

            if (hpLabel != null) hpLabel.text = Mathf.CeilToInt(GS.Hp) + " / " + GS.MaxHp;
            if (expLabel != null) expLabel.text = "EXP " + Mathf.FloorToInt(GS.Exp) + " / " + GS.ExpNeed;

            if (progLabel != null)
            {
                // 进度条常驻：局内已进行时间（mm:ss），不显示关卡/击败目标类文案
                progLabel.text = "存活 " + GS.TimeText();
            }

            if (levelLabel != null) levelLabel.text = "Lv." + GS.Level + " 阶" + GS.Stage;
            if (killLabel != null) killLabel.text = "吞噬 " + GS.Kills;
            if (coinLabel != null) coinLabel.text = GS.Coins.ToString();

            if (levelNameLabel != null)
            {
                // 仅保留下一只 Boss 的纯时间倒计时（时间驱动），不再出现阶段/守卫叙事文案
                levelNameLabel.text = GS.BossAlive
                    ? "守卫交战中"
                    : "下一只 " + Mathf.CeilToInt(Mathf.Max(0f, GS.NextBossTime - GS.Elapsed)) + "s";
            }

            if (comboLabel != null)
            {
                bool show = GS.Combo >= 3;
                comboLabel.gameObject.SetActive(show);
                if (show) comboLabel.text = "连击 x" + GS.Combo;
            }

            // 浮动提示上升淡出
            float dt = Time.deltaTime;
            for (int i = _tips.Count - 1; i >= 0; i--)
            {
                var t = _tips[i];
                t.life -= dt;
                if (t.life <= 0f || t.lb == null)
                {
                    if (t.lb != null) Destroy(t.lb.gameObject);
                    _tips.RemoveAt(i);
                    continue;
                }
                float k = 1f - t.life / floatLife;
                var rt = t.lb.rectTransform;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, k * floatRise);
                var c = t.lb.color;
                c.a = 1f - k * k;
                t.lb.color = c;
            }
        }
    }
}
