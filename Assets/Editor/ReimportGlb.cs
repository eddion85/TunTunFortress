using System.IO;
using UnityEditor;
using UnityEngine;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 安装 glTFast 后，旧的 .glb.meta 仍是 DefaultImporter（Unity 不会自动切换导入器）。
    /// 这里删除所有 glb/gltf 的旧 meta 并强制重新导入，让 glTFast 的 ScriptedImporter 接管，
    /// 使 .glb 变成可实例化的模型 Prefab。命令行入口 ReimportGlb.Run。
    /// </summary>
    public static class ReimportGlb
    {
        [MenuItem("吞吞堡垒/0-重装 glTFast 导入 glb")]
        public static void Run()
        {
            string root = FortressImport.ModelRoot;
            if (!Directory.Exists(root))
            {
                Debug.LogError("[REIMPORT] 模型目录不存在: " + root);
                EditorApplication.Exit(2);
                return;
            }

            var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            int deleted = 0;
            foreach (var f in files)
            {
                string p = f.Replace("\\", "/");
                string ext = Path.GetExtension(p).ToLower();
                if (ext != ".glb" && ext != ".gltf") continue;
                string meta = p + ".meta";
                if (File.Exists(meta))
                {
                    File.Delete(meta);
                    deleted++;
                }
            }
            Debug.Log("[REIMPORT] 删除旧 meta 数量: " + deleted);

            // 先登记一次（不强制同步），让 Unity 识别扩展名为 glTFast 的 ScriptedImporter
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            // 逐个串行导入：一次性 ForceSynchronous 批量导入多个带蒙皮 glb 时，
            // glTFast 的 SortAndNormalizeBoneWeightsJob 会发生并发竞争（JobHandle 未 Complete）。
            // 串行单文件导入可让每个模型的骨骼 Job 在返回前正确结束。
            var models = System.Array.FindAll(
                Directory.GetFiles(root, "*.*", SearchOption.AllDirectories), f =>
                {
                    string e = Path.GetExtension(f).ToLower();
                    return e == ".glb" || e == ".gltf";
                });
            foreach (var f in models)
            {
                string p = f.Replace("\\", "/");
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }

            // 校验：逐个 glb 现在应能加载为 GameObject
            int ok = 0, bad = 0;
            foreach (var f in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
            {
                string p = f.Replace("\\", "/");
                string ext = Path.GetExtension(p).ToLower();
                if (ext != ".glb" && ext != ".gltf") continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                var imp = AssetImporter.GetAtPath(p);
                if (go != null) { ok++; }
                else
                {
                    bad++;
                    Debug.LogError("[REIMPORT] 仍无法加载模型: " + p + " importer=" + (imp == null ? "null" : imp.GetType().Name));
                }
            }
            Debug.Log($"[REIMPORT] glb 模型可加载 {ok} / 失败 {bad}");
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(bad == 0 ? 0 : 3);
        }
    }
}
