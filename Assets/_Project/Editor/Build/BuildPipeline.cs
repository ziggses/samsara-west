using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SamsaraWest.Editor.BuildPipeline
{
    /// <summary>
    /// 一键出包（FND-09）。注意本类型的全名与 UnityEditor.BuildPipeline 同名，
    /// 因此文件内一律用 <c>UnityEditor.BuildPipeline</c> 全名调用，避免解析到自身。
    /// 命令行入口：
    /// Unity.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod SamsaraWest.Editor.BuildPipeline.BuildPipeline.BuildWindows -logFile &lt;日志&gt;
    /// </summary>
    public static class BuildPipeline
    {
        [MenuItem("SamsaraWest/出包/Windows x64", priority = 60)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, SamsaraWestPaths.WindowsBuildFolder, "SamsaraWest.exe");
        }

        [MenuItem("SamsaraWest/出包/WebGL（试玩版）", priority = 61)]
        public static void BuildWebGl()
        {
            Build(BuildTarget.WebGL, SamsaraWestPaths.WebGlBuildFolder, "index.html");
        }

        private static void Build(BuildTarget target, string folder, string fileName)
        {
            var scenes = CollectScenes();
            if (scenes.Count == 0)
            {
                Fail("构建场景列表为空。先执行 SamsaraWest/工程/一键初始化骨架。");
                return;
            }

            var outputFolder = CsvImporter.ToAbsolutePath(folder);
            System.IO.Directory.CreateDirectory(outputFolder);
            var location = System.IO.Path.Combine(outputFolder, fileName);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = location,
                target = target,
                options = BuildOptions.None,
            };

            Debug.Log($"[SamsaraWest] 开始出包：{target} → {location}（{scenes.Count} 个场景）");
            var report = UnityEditor.BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[SamsaraWest] 出包成功：{location}，体积 {summary.totalSize / (1024 * 1024)} MB，耗时 {summary.totalTime.TotalSeconds:0.0} 秒。");
                ExitIfBatchMode(0);
                return;
            }

            Fail($"出包失败：{summary.result}，错误 {summary.totalErrors} 条。");
        }

        private static List<string> CollectScenes()
        {
            var result = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene != null && scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    result.Add(scene.path);
                }
            }

            return result;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[SamsaraWest] {message}");
            ExitIfBatchMode(1);
        }

        private static void ExitIfBatchMode(int exitCode)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }
}
