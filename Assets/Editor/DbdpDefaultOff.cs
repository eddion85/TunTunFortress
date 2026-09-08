using UnityEditor;
using UnityEngine;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 让 Player 预制体（及当前打开场景的实例）里模型自带的 dbdp 炮节点默认处于关闭状态。
    /// 模型重新导入后 dbdp 会恢复为激活，执行一次本工具即可重新写回“默认关闭”覆盖；
    /// 运行时仍由 PlayerFortress 按 GS.HasFrontCannon 控制显隐。
    /// </summary>
    [InitializeOnLoad]
    public static class DbdpDefaultOff
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string NodeName = "dbdp";

        static DbdpDefaultOff()
        {
            // 编译/加载后延迟执行一次（幂等：已经是关闭状态则不写盘）
            EditorApplication.delayCall += AutoOnce;
        }

        private static void AutoOnce()
        {
            EditorApplication.delayCall -= AutoOnce;
            Apply();
        }

        [MenuItem("吞吞堡垒/dbdp 节点恢复默认关闭")]
        public static void Apply()
        {
            // 1) 预制体资产：在嵌套模型实例上写入 m_IsActive=0 覆盖
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                int changed = 0;
                var all = root.GetComponentsInChildren<Transform>(true);
                foreach (var t in all)
                {
                    if (t.name == NodeName && t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        changed++;
                    }
                }
                if (changed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    Debug.Log("[dbdp] Player 预制体已写入默认关闭，节点数=" + changed);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // 2) 当前打开场景里的 Player 实例同步关闭（不保存场景，由用户决定）
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t.name == NodeName && t.gameObject.activeSelf)
                    t.gameObject.SetActive(false);
            }
        }
    }
}
