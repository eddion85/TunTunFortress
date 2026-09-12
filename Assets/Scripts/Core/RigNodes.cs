using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// glb 实例化后层级很深，攻击挂点/轮子/血条锚点都靠名字递归查找。
    /// 统一放这里，EnemyAttackRig / EnemyWheelRig / 刷怪器共用，新增表现组件时直接复用。
    /// </summary>
    public static class RigNodes
    {
        /// <summary>找第一个同名节点（找不到返回 null；节点未激活也能找到，Transform 层级仍在）</summary>
        public static Transform FindFirst(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindFirst(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>收集全部同名节点（如 4/8 个炮口）</summary>
        public static Transform[] FindAll(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            var list = new List<Transform>();
            FindAll(root, name, list);
            return list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>收集某节点下所有带渲染器的叶子节点（轮子：Wheels 下的 Rad.* 网格）</summary>
        public static Transform[] CollectRendered(Transform root, string subtreeName)
        {
            var subtree = FindFirst(root, subtreeName);
            if (subtree == null) return null;
            var list = new List<Transform>();
            Walk(subtree, list);
            return list.Count > 0 ? list.ToArray() : null;
        }

        private static void FindAll(Transform root, string name, List<Transform> result)
        {
            if (root.name == name) result.Add(root);
            for (int i = 0; i < root.childCount; i++)
                FindAll(root.GetChild(i), name, result);
        }

        private static void Walk(Transform t, List<Transform> result)
        {
            bool hasMesh = t.GetComponent<MeshRenderer>() != null || t.GetComponent<SkinnedMeshRenderer>() != null;
            if (hasMesh) result.Add(t);
            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), result);
        }
    }
}
