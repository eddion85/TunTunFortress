using System;
using UnityEditor;
using UnityEngine;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 命令行入口：当用 -executeMethod RunPipeline 调用时，自动按顺序跑完
    /// ① 资源导入配置 ② Prefab 生成 ③ GameScene 搭建，然后退出 Unity。
    /// </summary>
    public static class PipelineRunner
    {
        public static void RunPipeline()
        {
            try
            {
                Debug.Log("[PIPELINE] ① 配置资源导入...");
                FortressImport.ConfigureAll();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[PIPELINE] ② 生成 Prefab...");
                FortressPrefabs.BuildAll();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[PIPELINE] ③ 搭建 GameScene...");
                SceneBuilder.BuildScene();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[PIPELINE] 全部完成 ✓");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[PIPELINE] 失败: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
