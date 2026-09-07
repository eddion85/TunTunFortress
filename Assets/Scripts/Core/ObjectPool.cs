using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>挂在池化实例上，记录它来自哪个预制体</summary>
    public class Pooled : MonoBehaviour
    {
        public GameObject prefab;
    }

    /// <summary>
    /// 简易对象池。移动端小游戏要避免频繁 Instantiate/Destroy 造成 GC 卡顿，
    /// 敌人、炮弹、箭矢、掉落物全部走这里复用。
    /// 对应 LayaAir 版里 clone() / destroy() 的成对调用。
    /// </summary>
    public static class ObjectPool
    {
        private static readonly Dictionary<GameObject, List<GameObject>> _free =
            new Dictionary<GameObject, List<GameObject>>();
        private static readonly Dictionary<GameObject, GameObject> _prefabOf =
            new Dictionary<GameObject, GameObject>();
        private static Transform _root;

        private static Transform Root()
        {
            if (_root == null)
            {
                var go = new GameObject("[ObjectPool]");
                go.hideFlags = HideFlags.HideAndDontSave;
                _root = go.transform;
            }
            return _root;
        }

        public static GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent = null)
        {
            if (prefab == null) return null;

            List<GameObject> list;
            if (!_free.TryGetValue(prefab, out list))
            {
                list = new List<GameObject>();
                _free[prefab] = list;
            }

            GameObject go;
            if (list.Count > 0)
            {
                go = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(pos, rot);
                go.SetActive(true);
            }
            else
            {
                go = Object.Instantiate(prefab, pos, rot);
                var p = go.GetComponent<Pooled>();
                if (p == null) p = go.AddComponent<Pooled>();
                p.prefab = prefab;
                if (parent != null) go.transform.SetParent(parent, false);
            }

            _prefabOf[go] = prefab;
            return go;
        }

        public static void Despawn(GameObject go)
        {
            if (go == null) return;

            GameObject prefab;
            if (!_prefabOf.TryGetValue(go, out prefab))
            {
                // 不是池里创建的，直接销毁
                Object.Destroy(go);
                return;
            }

            go.SetActive(false);
            go.transform.SetParent(Root(), false);

            List<GameObject> list;
            if (!_free.TryGetValue(prefab, out list))
            {
                list = new List<GameObject>();
                _free[prefab] = list;
            }
            if (!list.Contains(go)) list.Add(go);
        }

        /// <summary>彻底清空（切场景时用）</summary>
        public static void Clear()
        {
            _free.Clear();
            _prefabOf.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }
    }
}
