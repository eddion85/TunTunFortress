using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 玩家堡垒专用车轮滚动表现（程序化，无需动画片段）：
    /// 挂在某个形态(tier)根物体上，收集其 Drive 节点下所有带网格的轮子，
    /// 每帧按该形态的水平位移 / 轮半径绕本地车轴自转。只负责轮子转动，
    /// 地面车辙由 PlayerFortress 自己的逻辑负责，这里不重复落印。
    /// 未激活形态上挂载也安全：轮半径延迟到该形态首次激活（bounds 有效）时再测量。
    /// </summary>
    public class PlayerWheelRig : MonoBehaviour
    {
        private Transform[] _wheels;
        private float[] _radius;
        private Vector3 _lastPos;
        private bool _hasLast;
        private int _spinSign = 1;
        private Vector3 _spinAxis = Vector3.right; // 本地滚动轴，方向不对时由 Setup 指定

        /// <param name="driveNode">形态内轮子父节点名（玩家为 Drive）</param>
        /// <param name="spinAxis">本地滚动轴，默认绕本地 X</param>
        /// <param name="spinSign">旋转方向 +1/-1</param>
        public void Setup(string driveNode, Vector3 spinAxis, int spinSign = 1)
        {
            _spinAxis = spinAxis.sqrMagnitude > 0.0001f ? spinAxis.normalized : Vector3.right;
            _spinSign = spinSign;
            _wheels = RigNodes.CollectRendered(transform, driveNode);
            _radius = null; // 延迟到首次激活 Update 再测，避免在未激活形态上取到空 bounds
            _hasLast = false;
        }

        private void Update()
        {
            if (_wheels == null || GS.Paused || GS.Over) return;
            EnsureMeasured();

            Vector3 p = transform.position;
            if (!_hasLast) { _lastPos = p; _hasLast = true; return; }

            Vector3 d = p - _lastPos;
            d.y = 0f;
            float dist = d.magnitude;
            _lastPos = p;
            if (dist < 0.00001f) return;

            for (int i = 0; i < _wheels.Length; i++)
            {
                if (_wheels[i] == null) continue;
                float r = _radius != null ? _radius[i] : 1f;
                float angle = dist / r * Mathf.Rad2Deg * _spinSign;
                _wheels[i].Rotate(_spinAxis, angle, Space.Self);
            }
        }

        /// <summary>轮半径依赖 Renderer.bounds，必须在形态激活时测量才有效</summary>
        private void EnsureMeasured()
        {
            if (_radius != null || _wheels == null) return;
            _radius = new float[_wheels.Length];
            for (int i = 0; i < _wheels.Length; i++)
            {
                float r = 1f;
                if (_wheels[i] != null)
                {
                    var mr = _wheels[i].GetComponent<MeshRenderer>();
                    if (mr != null) r = mr.bounds.size.y * 0.5f;
                }
                _radius[i] = Mathf.Max(0.05f, r);
            }
        }
    }
}
