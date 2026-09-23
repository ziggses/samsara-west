using System;
using System.IO;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SamsaraWest.Editor
{
    /// <summary>
    /// 工程一键初始化：导入数据 → 建配置资产 → 建引导场景 → 挂进构建设置 → 定渲染与画面基线。
    /// 新机器 clone 下来跑一次这个（或跑 Tools/setup-project.ps1），工程立刻到「可编译、可出包、可跑测试」的状态。
    /// </summary>
    public static class ProjectSetup
    {
        private const string SettingsFolder = SamsaraWestPaths.ProjectRoot + "/Settings";
        private const string UrpAssetPath = SettingsFolder + "/SamsaraWest_URP_2D.asset";
        private const string Renderer2DAssetPath = SettingsFolder + "/SamsaraWest_Renderer2D.asset";

        /// <summary>
        /// PixelPerfectCamera 的宿主类型在 URP 各版本间搬过家（14 用 Experimental 命名空间，16+ 归到 Rendering.Universal），
        /// 因此按顺序探测，找到哪个用哪个。
        /// </summary>
        private static readonly string[] PixelPerfectCameraTypeNames =
        {
            "UnityEngine.Experimental.Rendering.Universal.PixelPerfectCamera, Unity.RenderPipelines.Universal.Runtime",
            "UnityEngine.Rendering.Universal.PixelPerfectCamera, Unity.RenderPipelines.Universal.Runtime",
            "UnityEngine.U2D.PixelPerfectCamera, Unity.2D.PixelPerfect",
        };

        [MenuItem("SamsaraWest/工程/一键初始化骨架", priority = 1)]
        public static void RunAll()
        {
            ConfigureProjectSettings();
            ConfigureRenderPipeline();

            // 数据与本地化先落地，场景才引用得到。
            var import = CsvImporter.ImportAll(false);
            var localization = LocalizationImporter.ImportAll();

            CreateBattleConfigAsset();
            CreateBootstrapScene();
            ConfigureBuildSettings();
            CheckExternalAssetsJunction();

            Debug.Log(
                $"[SamsaraWest] 工程初始化完成：数据问题 {import.Report.Issues.Count} 条，本地化键 {localization.KeyCount} 个。");
            Debug.Log($"[SamsaraWest] {import.Report.Summary()}");
            Debug.Log($"[SamsaraWest] {localization.Report.Summary()}");

            ValidationLog.Write("数据导入", import.Report);
            ValidationLog.Write("本地化导入", localization.Report);

            if (import.Report.HasErrors || localization.Report.HasErrors)
            {
                Debug.LogWarning("[SamsaraWest] 初始化完成，但存在校验错误。打开 SamsaraWest/数据/数据工具窗口 查看。");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(2);
                }

                return;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        public static void ConfigureProjectSettings()
        {
            PlayerSettings.companyName = "SamsaraWest";
            PlayerSettings.productName = "西游：第八十二难";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                // 像素美术走 Linear 才不会在叠加混合时发灰。这一步会触发一次资源重导入。
                PlayerSettings.colorSpace = ColorSpace.Linear;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[SamsaraWest] 播放器设置已就绪（1920×1080、Linear）。");
        }

        /// <summary>
        /// 建立 URP 2D 管线资产并挂到 GraphicsSettings。
        /// 这里刻意走类型名字符串而不是编译期引用：URP 的资产创建 API 在不同小版本间会变，
        /// 用反射可以让骨架在包升级时仍然编得过，最坏情况只是提示「未能自动创建，请手动创建」。
        /// </summary>
        public static void ConfigureRenderPipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(UrpAssetPath);
            if (existing != null)
            {
                GraphicsSettings.defaultRenderPipeline = existing;
                QualitySettings.renderPipeline = null;
                Debug.Log("[SamsaraWest] 已存在 URP 管线资产，直接挂载。");
                return;
            }

            var rendererDataType = Type.GetType("UnityEngine.Rendering.Universal.Renderer2DData, Unity.RenderPipelines.Universal.Runtime");
            var pipelineType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
            if (rendererDataType == null || pipelineType == null)
            {
                Debug.LogWarning("[SamsaraWest] 未找到 URP 运行时类型，跳过渲染管线自动创建。请在 Project Settings > Graphics 手动指定 URP 资产。");
                return;
            }

            try
            {
                CsvImporter.EnsureFolder(SettingsFolder);

                var rendererData = ScriptableObject.CreateInstance(rendererDataType);
                AssetDatabase.CreateAsset(rendererData, Renderer2DAssetPath);

                var pipeline = ScriptableObject.CreateInstance(pipelineType);
                var serialized = new SerializedObject(pipeline);
                var list = serialized.FindProperty("m_RendererDataList");
                if (list != null)
                {
                    list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
                }

                var defaultIndex = serialized.FindProperty("m_DefaultRendererIndex");
                if (defaultIndex != null)
                {
                    defaultIndex.intValue = 0;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(pipeline, UrpAssetPath);
                AssetDatabase.SaveAssets();

                GraphicsSettings.defaultRenderPipeline = (RenderPipelineAsset)pipeline;
                QualitySettings.renderPipeline = null;
                Debug.Log($"[SamsaraWest] 已创建 URP 2D 管线资产：{UrpAssetPath}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SamsaraWest] 自动创建 URP 管线失败：{exception.Message}。请手动创建后在 Graphics 设置里指定。");
            }
        }

        public static BattleConfig CreateBattleConfigAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<BattleConfig>(SamsaraWestPaths.BattleConfigAsset);
            if (config != null)
            {
                return config;
            }

            CsvImporter.EnsureFolder(SamsaraWestPaths.ProjectRoot + "/Battle/Config");
            config = BattleConfig.CreateDefault();
            AssetDatabase.CreateAsset(config, SamsaraWestPaths.BattleConfigAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SamsaraWest] 已创建默认战斗数值配置：{SamsaraWestPaths.BattleConfigAsset}");
            return config;
        }

        [MenuItem("SamsaraWest/工程/重建引导场景", priority = 2)]
        public static void RebuildBootstrapScene()
        {
            CreateBootstrapScene(true);
        }

        /// <summary>
        /// 建引导场景。场景已存在且三个资产引用都还在时直接跳过：重建会重新分配其中的 fileID，
        /// 于是「每次跑初始化都把工作区弄脏」——而初始化本身是幂等操作，不该有这种副作用。
        /// 引用失效（资产被删掉重建、GUID 变了）时才重建，否则场景里的引用会静默变成 null。
        /// </summary>
        public static void CreateBootstrapScene(bool force = false)
        {
            if (!force)
            {
                if (SceneIsUpToDate(out var reason))
                {
                    Debug.Log(
                        $"[SamsaraWest] 引导场景已是既有状态，跳过重建：{SamsaraWestPaths.BootstrapScene}（强制重建用菜单 SamsaraWest/工程/重建引导场景）。");
                    return;
                }

                Debug.Log($"[SamsaraWest] 重建引导场景：{reason}");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<DefinitionCatalog>(SamsaraWestPaths.DefinitionCatalogAsset);
            var localizationTable = AssetDatabase.LoadAssetAtPath<LocalizationTable>(SamsaraWestPaths.LocalizationTableAsset);
            var battleConfig = AssetDatabase.LoadAssetAtPath<BattleConfig>(SamsaraWestPaths.BattleConfigAsset);

            var absoluteScenePath = CsvImporter.ToAbsolutePath(SamsaraWestPaths.BootstrapScene);
            var sceneFolder = Path.GetDirectoryName(absoluteScenePath);
            if (!string.IsNullOrEmpty(sceneFolder))
            {
                Directory.CreateDirectory(sceneFolder);
            }

            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();
            AddPixelPerfectCamera(cameraObject);

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
            var serialized = new SerializedObject(bootstrap);
            Assign(serialized, "_definitionCatalog", catalog);
            Assign(serialized, "_localizationTable", localizationTable);
            Assign(serialized, "_battleConfig", battleConfig);

            var seed = serialized.FindProperty("_masterSeed");
            if (seed != null)
            {
                // 0 表示「启动时取时钟种子」，便于每次游玩都不一样；回归测试会显式注入固定种子。
                seed.longValue = 0L;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SamsaraWestPaths.BootstrapScene))
            {
                Debug.LogError($"[SamsaraWest] 引导场景保存失败：{SamsaraWestPaths.BootstrapScene}");
                return;
            }

            Debug.Log($"[SamsaraWest] 已生成引导场景：{SamsaraWestPaths.BootstrapScene}（相机已设 480×270 参考分辨率、32 PPU）。");
        }

        /// <summary>
        /// 判断已有引导场景是否还引用得到当前的三个数据资产。只看「文件在不在」不够：
        /// 资产被删掉重建后 GUID 会变，场景里的引用会静默变成 null，运行期才发现就晚了。
        /// GetDependencies 不打开场景，因此不会打断编辑器里正在编辑的其它场景。
        /// </summary>
        private static bool SceneIsUpToDate(out string reason)
        {
            var absoluteScenePath = CsvImporter.ToAbsolutePath(SamsaraWestPaths.BootstrapScene);
            if (!File.Exists(absoluteScenePath))
            {
                reason = "场景文件不存在";
                return false;
            }

            var dependencies = AssetDatabase.GetDependencies(SamsaraWestPaths.BootstrapScene, true);
            var required = new[]
            {
                SamsaraWestPaths.DefinitionCatalogAsset,
                SamsaraWestPaths.LocalizationTableAsset,
                SamsaraWestPaths.BattleConfigAsset,
            };

            for (var i = 0; i < required.Length; i++)
            {
                if (Array.IndexOf(dependencies, required[i]) < 0)
                {
                    reason = $"场景未引用 {required[i]}（该资产可能被删掉重建过）";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(SamsaraWestPaths.BootstrapScene, true),
            };

            Debug.Log("[SamsaraWest] 构建设置已更新：仅包含引导场景（后续章节场景由 Flow 动态加载）。");
        }

        [MenuItem("SamsaraWest/工程/检查外部素材 junction", priority = 3)]
        public static void CheckExternalAssetsJunction()
        {
            var absolute = CsvImporter.ToAbsolutePath(SamsaraWestPaths.ExternalAssets);

            if (!Directory.Exists(absolute))
            {
                Debug.LogWarning(
                    "[SamsaraWest] 未找到外部素材目录 Assets/_External。请在仓库根目录执行 Tools/setup-external-assets.ps1 建立 junction（换机器后必须重建，junction 不随 git 分发）。");
                return;
            }

            var info = new DirectoryInfo(absolute);
            var isJunction = info.Attributes.HasFlag(FileAttributes.ReparsePoint);
            Debug.Log(isJunction
                ? $"[SamsaraWest] 外部素材 junction 正常：{absolute}"
                : $"[SamsaraWest] {absolute} 是普通目录而不是 junction。若你正在做美术接入，请改用 junction 以免素材混进仓库。");
        }

        private static void AddPixelPerfectCamera(GameObject cameraObject)
        {
            Type type = null;
            for (var i = 0; i < PixelPerfectCameraTypeNames.Length && type == null; i++)
            {
                type = Type.GetType(PixelPerfectCameraTypeNames[i]);
            }

            if (type == null)
            {
                Debug.LogWarning("[SamsaraWest] 未找到 URP 的 PixelPerfectCamera，跳过像素完美配置。");
                return;
            }

            var component = cameraObject.AddComponent(type);
            var serialized = new SerializedObject(component);
            SetInt(serialized, "m_AssetsPPU", 32);
            SetInt(serialized, "m_RefResolutionX", 480);
            SetInt(serialized, "m_RefResolutionY", 270);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(SerializedObject serialized, string propertyName, int value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void Assign(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[SamsaraWest] 未找到序列化字段 {propertyName}，场景里需要手动指定。");
                return;
            }

            property.objectReferenceValue = value;
        }
    }
}
