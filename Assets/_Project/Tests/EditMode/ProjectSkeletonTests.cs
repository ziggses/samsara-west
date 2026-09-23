using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using SamsaraWest.Editor;
using UnityEditor;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 架构约束靠 asmdef 落地，而不是靠文档里的君子协定。这里把「谁可以依赖谁」
    /// 写成断言：一旦有人图省事加了反向引用，测试会先于 Code Review 拦下来。
    /// </summary>
    public sealed class ProjectSkeletonTests
    {
        private static readonly string[] ModuleFolders =
        {
            "Core", "Data", "Flow", "Exploration", "Battle", "Narrative",
            "Progression", "Economy", "UI", "Save", "Localization", "Audio",
        };

        /// <summary>只允许依赖 Core 的叶子模块。</summary>
        private static readonly string[] CoreOnlyModules = { "Data", "Save", "Localization", "Audio" };

        /// <summary>允许依赖 Core + Data 的玩法模块。</summary>
        private static readonly string[] CoreAndDataModules =
        {
            "Battle", "Exploration", "Narrative", "Progression", "Economy",
        };

        [Serializable]
        private sealed class AssemblyDefinitionJson
        {
            public string name;
            public string[] references;
            public string[] includePlatforms;
        }

        [Test]
        public void ProjectPaths_AllResolveToExistingAssetsOrFolders()
        {
            var assets = new[]
            {
                SamsaraWestPaths.BootstrapScene,
                SamsaraWestPaths.BattleConfigAsset,
                SamsaraWestPaths.DefinitionCatalogAsset,
                SamsaraWestPaths.ImportManifestAsset,
                SamsaraWestPaths.LocalizationTableAsset,
                SamsaraWestPaths.LocalizationKeysSource,
                SamsaraWestPaths.LocalizationScanIgnoreFile,
            };

            for (var i = 0; i < assets.Length; i++)
            {
                Assert.IsNotNull(
                    AssetDatabase.LoadMainAssetAtPath(assets[i]),
                    $"约定路径指向的资产不存在：{assets[i]}");
            }

            var folders = new[]
            {
                SamsaraWestPaths.DataTables,
                SamsaraWestPaths.LocalizationTables,
                SamsaraWestPaths.DefinitionsRoot,
                SamsaraWestPaths.GeneratedRoot,
                SamsaraWestPaths.LocalizationGeneratedRoot,
            };

            for (var i = 0; i < folders.Length; i++)
            {
                Assert.IsTrue(AssetDatabase.IsValidFolder(folders[i]), $"约定目录不存在：{folders[i]}");
            }
        }

        [Test]
        public void EveryModule_HasAssemblyDefinitionNamedAfterItsFolder()
        {
            for (var i = 0; i < ModuleFolders.Length; i++)
            {
                var module = ModuleFolders[i];
                var definition = ReadAssemblyDefinition(module);

                Assert.AreEqual(
                    $"SamsaraWest.{module}",
                    definition.name,
                    $"{module} 的程序集名必须与目录名一致，否则开发者找不到对应关系。");
            }
        }

        [Test]
        public void BootstrapScene_IsEnabledInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            for (var i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled && scenes[i].path == SamsaraWestPaths.BootstrapScene)
                {
                    return;
                }
            }

            Assert.Fail($"{SamsaraWestPaths.BootstrapScene} 必须作为启用场景出现在 Build Settings 里，否则出包后起不来。");
        }

        [Test]
        public void Core_HasNoProjectModuleDependencies()
        {
            var references = ProjectReferences(ReadAssemblyDefinition("Core"));

            Assert.AreEqual(0, references.Count, $"Core 必须自洽，不允许依赖任何模块：{Join(references)}");
        }

        [Test]
        public void LeafModules_DependOnlyOnCore()
        {
            for (var i = 0; i < CoreOnlyModules.Length; i++)
            {
                var module = CoreOnlyModules[i];
                var references = ProjectReferences(ReadAssemblyDefinition(module));

                Assert.IsTrue(
                    references.IsSubsetOf(new[] { "SamsaraWest.Core" }),
                    $"{module} 只允许依赖 Core，实际：{Join(references)}");
            }
        }

        [Test]
        public void GameplayModules_DependOnlyOnCoreAndData()
        {
            var allowed = new HashSet<string> { "SamsaraWest.Core", "SamsaraWest.Data" };

            for (var i = 0; i < CoreAndDataModules.Length; i++)
            {
                var module = CoreAndDataModules[i];
                var references = ProjectReferences(ReadAssemblyDefinition(module));

                Assert.IsTrue(references.IsSubsetOf(allowed), $"{module} 只允许依赖 Core + Data，实际：{Join(references)}");
            }
        }

        [Test]
        public void Flow_IsTheOnlyPlaceThatConnectsDataLocalizationSaveAndBattle()
        {
            var references = ProjectReferences(ReadAssemblyDefinition("Flow"));

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "SamsaraWest.Core",
                    "SamsaraWest.Data",
                    "SamsaraWest.Localization",
                    "SamsaraWest.Save",
                    "SamsaraWest.Battle",
                },
                references);

            Assert.IsFalse(references.Contains("SamsaraWest.UI"), "Flow 不应直接依赖 UI：表现层只能被上层驱动。");
        }

        [Test]
        public void RuntimeModules_NeverReferenceEditorOrUi()
        {
            for (var i = 0; i < ModuleFolders.Length; i++)
            {
                var module = ModuleFolders[i];
                var references = ProjectReferences(ReadAssemblyDefinition(module));

                Assert.IsFalse(references.Contains("SamsaraWest.Editor"), $"{module} 不得依赖编辑器程序集。");
                Assert.IsFalse(
                    references.Contains("SamsaraWest.UI"),
                    $"{module} 不得依赖 UI：界面是消费方，反向依赖会把表现层焊进逻辑层。");
            }
        }

        [Test]
        public void EditorAssembly_IsEditorOnlyAndSeesEveryModule()
        {
            var definition = ReadAssemblyDefinition("Editor");

            CollectionAssert.AreEqual(new[] { "Editor" }, definition.includePlatforms, "编辑器工具必须只在 Editor 平台编译。");

            var references = ProjectReferences(definition);
            for (var i = 0; i < ModuleFolders.Length; i++)
            {
                CollectionAssert.Contains(
                    references,
                    $"SamsaraWest.{ModuleFolders[i]}",
                    $"编辑器工具需要能访问 {ModuleFolders[i]}，否则校验与导入工具做不了。");
            }
        }

        [Test]
        public void DataTables_FolderContainsExactlyTheRegisteredTables()
        {
            var folder = CsvImporter.ToAbsolutePath(SamsaraWestPaths.DataTables);
            var files = Directory.GetFiles(folder, "*.csv");

            Assert.AreEqual(
                DefinitionImportMap.Bindings.Count,
                files.Length,
                $"策划表数量与登记的定义类型数量不一致。多出的表会被导入管线报为「未登记」，少一个说明漏了表。");
        }

        private static AssemblyDefinitionJson ReadAssemblyDefinition(string module)
        {
            var assetPath = $"{SamsaraWestPaths.ProjectRoot}/{module}/SamsaraWest.{module}.asmdef";
            var absolute = CsvImporter.ToAbsolutePath(assetPath);
            Assert.IsTrue(File.Exists(absolute), $"缺少程序集定义：{assetPath}");

            var json = File.ReadAllText(absolute, Encoding.UTF8);
            var definition = JsonUtility.FromJson<AssemblyDefinitionJson>(json);
            Assert.IsNotNull(definition, $"无法解析程序集定义：{assetPath}");
            return definition;
        }

        private static HashSet<string> ProjectReferences(AssemblyDefinitionJson definition)
        {
            var result = new HashSet<string>();
            var references = definition.references;
            if (references == null)
            {
                return result;
            }

            for (var i = 0; i < references.Length; i++)
            {
                if (references[i].StartsWith("SamsaraWest.", StringComparison.Ordinal))
                {
                    result.Add(references[i]);
                }
            }

            return result;
        }

        private static string Join(IEnumerable<string> values) => string.Join(", ", values);
    }
}
