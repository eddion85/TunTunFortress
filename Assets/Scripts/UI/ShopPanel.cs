using System;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 局内金币装备面板 [策划书 4 局内成长]
    /// 用局内实时金币购买武器与强化，与暂停面板的局外永久养成区分开。
    /// 对应 LayaAir 版 ui/ShopPanel.ts。
    /// </summary>
    public class ShopPanel : MonoBehaviour
    {
        private class ShopEntry
        {
            public string key;
            public string title;
            public string icon;
            public string desc;
            public int basePrice;
            public int max;
            public Action apply;
        }

        [Header("面板")]
        [SerializeField] private Button shopBtn;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button closeBtn;
        [SerializeField] private Text coinLabel;

        [Header("槽位")]
        [SerializeField] private Button[] slots = new Button[4];
        [SerializeField] private Image[] icons = new Image[4];
        [SerializeField] private Text[] texts = new Text[4];

        private static readonly ShopEntry[] Entries =
        {
            new ShopEntry { key = "front",  title = "正面直射炮", icon = "art/ui/img_icon_weapon_front",
                            desc = "解锁正面高伤主炮", basePrice = 60, max = 1,
                            apply = () => { GS.HasFrontCannon = true; } },
            new ShopEntry { key = "side",   title = "侧炮升星",   icon = "art/ui/img_icon_weapon_side",
                            desc = "伤害 +25%", basePrice = 45, max = 5,
                            apply = () => { GS.DmgMul *= 1.25f; } },
            new ShopEntry { key = "magnet", title = "磁力线圈",   icon = "art/ui/img_icon_weapon_magnet",
                            desc = "吞噬圈 +12%", basePrice = 50, max = 3,
                            apply = () => { GS.DevourMul *= 1.12f; GS.HasMagnet = true; } },
            new ShopEntry { key = "repair", title = "紧急修复",   icon = "art/ui/img_icon_repair",
                            desc = "回复 35 生命", basePrice = 40, max = 9,
                            apply = () => { GS.Heal(35f); } }
        };

        private bool _open;
        private readonly int[] _lv = new int[4];

        private void Start()
        {
            if (panel != null) panel.SetActive(false);
            // 装备改为击杀里程碑系统自动发放，局内不再允许手动购买：隐藏商店入口（脚本保留）
            if (shopBtn != null) shopBtn.gameObject.SetActive(false);
            if (shopBtn != null) shopBtn.onClick.AddListener(Toggle);
            if (closeBtn != null) closeBtn.onClick.AddListener(Toggle);

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    int idx = i;
                    slots[i].onClick.AddListener(() => Buy(idx));
                }
                if (i < icons.Length && icons[i] != null && i < Entries.Length)
                {
                    var sp = Resources.Load<Sprite>(Entries[i].icon);
                    if (sp != null) icons[i].sprite = sp;
                }
            }

            GameBus.On(GameEvents.Restart, OnRestart);
            GameBus.On(GameEvents.Result, CloseOnModal);
            Refresh();
        }

        private void OnDestroy()
        {
            GameBus.Off(GameEvents.Restart, OnRestart);
            GameBus.Off(GameEvents.Result, CloseOnModal);
        }

        // 结算面板弹出时强制收起商店，避免多层叠加
        private void CloseOnModal(object payload)
        {
            _open = false;
            if (panel != null) panel.SetActive(false);
        }

        private void OnRestart(object payload)
        {
            // 无尽生存没有下一关，重新开局一律清零已购装备等级（复活不发 Restart，故不受影响）
            for (int i = 0; i < _lv.Length; i++) _lv[i] = 0;

            _open = false;
            if (panel != null) panel.SetActive(false);
            Refresh();
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

        private int LevelOf(string key)
        {
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i].key == key) return _lv[i];
            return 0;
        }

        private void SetLevelOf(string key, int v)
        {
            for (int i = 0; i < Entries.Length; i++)
                if (Entries[i].key == key) { _lv[i] = v; return; }
        }

        private int PriceOf(ShopEntry e)
        {
            int lv = LevelOf(e.key);
            return Mathf.RoundToInt(e.basePrice * (1f + lv * 0.6f));
        }

        private void Buy(int index)
        {
            if (index < 0 || index >= Entries.Length) return;
            var e = Entries[index];
            int lv = LevelOf(e.key);
            if (lv >= e.max)
            {
                AudioKit.PlaySfx(SfxKeys.Click);
                GameBus.Emit(GameEvents.Float, "已达上限");
                return;
            }
            int price = PriceOf(e);
            if (GS.Coins < price)
            {
                AudioKit.PlaySfx(SfxKeys.Click);
                GameBus.Emit(GameEvents.Float, "金币不足");
                return;
            }

            GS.Coins -= price;
            SetLevelOf(e.key, lv + 1);
            e.apply();
            AudioKit.PlaySfx(SfxKeys.CardPick);
            GameBus.Emit(GameEvents.Float, e.title + " 已装备!");
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < texts.Length; i++)
            {
                if (i >= Entries.Length || texts[i] == null) continue;
                var e = Entries[i];
                int lv = LevelOf(e.key);
                texts[i].text = lv >= e.max
                    ? e.title + "\nMAX"
                    : e.title + "\n" + e.desc + "\n" + PriceOf(e) + " 金币";
            }
            if (coinLabel != null) coinLabel.text = "局内金币 " + GS.Coins;
        }

        private void Update()
        {
            // 面板打开时实时刷新金币显示
            if (_open && coinLabel != null) coinLabel.text = "局内金币 " + GS.Coins;
        }
    }
}
