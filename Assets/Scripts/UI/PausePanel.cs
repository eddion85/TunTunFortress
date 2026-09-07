using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 暂停面板 + 局外养成三线强化 [策划书 7 保留接口]
    /// 攻击 / 生命 / 吞噬各 5 级，消耗累计金币，存 PlayerPrefs。
    /// 对应 LayaAir 版 ui/PausePanel.ts。
    /// </summary>
    public class PausePanel : MonoBehaviour
    {
        [Header("面板")]
        [SerializeField] private Button pauseBtn;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button resumeBtn;

        [Header("三线强化")]
        [SerializeField] private Button atkBtn;
        [SerializeField] private Button hpBtn;
        [SerializeField] private Button devourBtn;
        [SerializeField] private Text atkLabel;
        [SerializeField] private Text hpLabel;
        [SerializeField] private Text devourLabel;
        [SerializeField] private Text goldLabel;

        private bool _open;

        private void Start()
        {
            if (panel != null) panel.SetActive(false);
            if (pauseBtn != null) pauseBtn.onClick.AddListener(Toggle);
            if (resumeBtn != null) resumeBtn.onClick.AddListener(Toggle);
            if (atkBtn != null) atkBtn.onClick.AddListener(() => Buy("atkLv"));
            if (hpBtn != null) hpBtn.onClick.AddListener(() => Buy("hpLv"));
            if (devourBtn != null) devourBtn.onClick.AddListener(() => Buy("devourLv"));
            GameBus.On(GameEvents.Restart, CloseOnModal);
            GameBus.On(GameEvents.Result, CloseOnModal);
            Refresh();
        }

        private void OnDestroy()
        {
            GameBus.Off(GameEvents.Restart, CloseOnModal);
            GameBus.Off(GameEvents.Result, CloseOnModal);
        }

        // 重开 / 结算时强制收起暂停面板，避免多层叠加
        private void CloseOnModal(object payload)
        {
            _open = false;
            if (panel != null) panel.SetActive(false);
        }

        private void Toggle()
        {
            if (GS.Over) return;
            AudioKit.PlaySfx(SfxKeys.Click);
            _open = !_open;
            if (panel != null) panel.SetActive(_open);
            GS.Paused = _open;
            if (_open) Refresh();
        }

        private void Buy(string line)
        {
            if (Meta.Upgrade(line))
            {
                AudioKit.PlaySfx(SfxKeys.CardPick);
                GameBus.Emit(GameEvents.Float, "强化成功!");
            }
            else
            {
                AudioKit.PlaySfx(SfxKeys.Click);
                GameBus.Emit(GameEvents.Float, "金币不足");
            }
            Refresh();
        }

        private static string LineText(string name, int lv)
        {
            if (lv >= 5) return name + " Lv.5 MAX";
            return name + " Lv." + lv + "\n" + Meta.Cost(lv) + " 金币";
        }

        private void Refresh()
        {
            var d = Meta.Data;
            if (atkLabel != null) atkLabel.text = LineText("攻击", d.atkLv);
            if (hpLabel != null) hpLabel.text = LineText("生命", d.hpLv);
            if (devourLabel != null) devourLabel.text = LineText("吞噬", d.devourLv);
            if (goldLabel != null) goldLabel.text = "累计金币 " + d.gold;
        }
    }
}
