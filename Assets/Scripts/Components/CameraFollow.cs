using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 相机跟随 + 震屏。对应 LayaAir 版 components/CameraFollow.ts。
    /// 用 LateUpdate 保证在玩家移动之后再跟，避免画面抖动。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float height = 17f;
        [SerializeField] private float back = 14f;

        private float _shake;

        private void OnEnable()
        {
            GameBus.On(GameEvents.Shake, OnShake);
            GameBus.On(GameEvents.Restart, OnRestart);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Shake, OnShake);
            GameBus.Off(GameEvents.Restart, OnRestart);
        }

        private void Start()
        {
            if (target == null)
            {
                var pf = FindObjectOfType<PlayerFortress>();
                if (pf != null) target = pf.transform;
            }
        }

        private void OnShake(object payload)
        {
            float p = payload is float ? (float)payload : 0.4f;
            // 取较大值，避免密集小震盖掉大震
            if (p > _shake) _shake = Mathf.Min(2.6f, p);
        }

        private void OnRestart(object payload)
        {
            _shake = 0f;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 tp = target.position;
            float zoom = 1f + GS.Stage * 0.16f;

            float wantX = tp.x;
            float wantY = height * zoom;
            float wantZ = tp.z - back * zoom;

            Vector3 cur = transform.position;
            cur.x += (wantX - cur.x) * 0.14f;
            cur.y += (wantY - cur.y) * 0.08f;
            cur.z += (wantZ - cur.z) * 0.14f;

            // 震屏偏移
            if (_shake > 0.001f)
            {
                float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
                float a = _shake * 0.42f;
                cur.x += (Random.value * 2f - 1f) * a;
                cur.y += (Random.value * 2f - 1f) * a * 0.6f;
                cur.z += (Random.value * 2f - 1f) * a;
                _shake -= dt * 6.5f;
                if (_shake < 0f) _shake = 0f;
            }

            transform.position = cur;
            transform.LookAt(new Vector3(tp.x, tp.y + 1.2f, tp.z), Vector3.up);
        }
    }
}
