using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 冲撞技能按钮：显示 CD 与遮罩，点击触发 EV.SKILL。
    /// 对应 LayaAir 版 ui/SkillButton.ts。
    /// </summary>
    public class SkillButton : MonoBehaviour
    {
        [SerializeField] private Text cdLabel;
        [SerializeField] private Transform playerNode;
        [SerializeField] private Image cdMask;
        [SerializeField] private float maskHeight = 140f;

        private PlayerFortress _ctrl;
        private Button _btn;

        private void Start()
        {
            if (playerNode != null) _ctrl = playerNode.GetComponent<PlayerFortress>();
            if (_ctrl == null && playerNode != null) _ctrl = playerNode.GetComponentInChildren<PlayerFortress>();

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
            GameBus.Emit(GameEvents.Skill, null);
        }

        private void Update()
        {
            if (cdLabel == null || _ctrl == null) return;
            var img = _btn != null ? _btn.image : GetComponent<Image>();

            if (_ctrl.SkillReady())
            {
                cdLabel.text = "冲撞";
                if (img != null) { var c = img.color; c.a = 1f; img.color = c; }
                if (cdMask != null) cdMask.gameObject.SetActive(false);
            }
            else
            {
                float r = _ctrl.SkillRatio();
                cdLabel.text = (r * GameConfig.DashCd).ToString("0.0");
                if (img != null) { var c = img.color; c.a = 0.55f; img.color = c; }
                // CD 遮罩自上而下退去
                if (cdMask != null)
                {
                    cdMask.gameObject.SetActive(true);
                    cdMask.rectTransform.sizeDelta = new Vector2(cdMask.rectTransform.sizeDelta.x, Mathf.Max(1f, maskHeight * r));
                }
            }
        }
    }
}
