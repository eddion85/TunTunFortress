using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 强化吞噬按钮。
    /// 基础吞噬始终自动生效；此按钮花金币开启限时强化：吞噬圈放大、可吞高一档体积，
    /// 并附带真空吸附。对应 LayaAir 版 ui/DevourButton.ts。
    /// </summary>
    public class DevourButton : MonoBehaviour
    {
        [SerializeField] private Text cdLabel;
        [SerializeField] private Transform playerNode;

        private Button _btn;

        private void Start()
        {
            _btn = GetComponent<Button>();
            if (_btn != null) _btn.onClick.AddListener(OnTap);
        }

        private void OnDestroy()
        {
            if (_btn != null) _btn.onClick.RemoveListener(OnTap);
        }

        private void OnTap()
        {
            if (GS.Paused || GS.Over) return;
            if (!GS.ActivateDevour())
            {
                if (GS.DevourBoosted())
                    GameBus.Emit(GameEvents.Float, "强化吞噬进行中");
                else if (GS.Coins < GameConfig.DevourCoinCost)
                    GameBus.Emit(GameEvents.Float, "金币不足（需 " + GameConfig.DevourCoinCost + " 金币）");
                else
                    GameBus.Emit(GameEvents.Float, "稍等一下");
                return;
            }

            AudioKit.PlaySfx(SfxKeys.Devour1);
            if (playerNode != null)
            {
                var p = playerNode.position;
                Fx.PlayVfx(VfxKeys.DevourRing, p.x, p.y + 0.4f, p.z, 3.0f);
            }
            GameBus.Emit(GameEvents.Float, "强化吞噬! -" + GameConfig.DevourCoinCost + " 金币");
        }

        private void Update()
        {
            var img = _btn != null ? _btn.image : GetComponent<Image>();
            if (GS.DevourBoosted())
            {
                if (img != null)
                {
                    var c = img.color; c.a = 1f; img.color = c;
                }
                if (cdLabel != null) cdLabel.text = GS.DevourTimer.ToString("0.0") + "s";
            }
            else if (GS.DevourReady())
            {
                if (img != null)
                {
                    var c = img.color;
                    c.a = 0.9f + Mathf.Sin(Time.realtimeSinceStartup * 6f) * 0.1f;
                    img.color = c;
                }
                if (cdLabel != null) cdLabel.text = "强吞\n" + GameConfig.DevourCoinCost + "金";
            }
            else
            {
                if (img != null)
                {
                    var c = img.color; c.a = 0.42f; img.color = c;
                }
                if (cdLabel != null)
                    cdLabel.text = "强吞\n" + Mathf.FloorToInt(GS.Coins) + "/" + GameConfig.DevourCoinCost;
            }
        }
    }
}
