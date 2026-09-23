namespace SamsaraWest.Editor
{
    /// <summary>
    /// 骨架期所有「约定路径」的唯一出处。工具、测试、脚本都从这里取路径，
    /// 避免字符串散落导致某天改了目录名却只改了一半。
    /// </summary>
    public static class SamsaraWestPaths
    {
        public const string ProjectRoot = "Assets/_Project";

        /// <summary>外部素材挂载点（junction，不入 git）。</summary>
        public const string ExternalAssets = "Assets/_External";

        // 策划输入面（入 git）
        public const string DataTables = ProjectRoot + "/Data/Tables";
        public const string LocalizationTables = ProjectRoot + "/Localization/Tables";

        // 程序产物（入 git，便于 diff 与 Code Review）
        public const string DefinitionsRoot = ProjectRoot + "/Data/Definitions";
        public const string GeneratedRoot = ProjectRoot + "/Data/Generated";

        public const string ImportManifestAsset = GeneratedRoot + "/ImportManifest.asset";
        public const string DefinitionCatalogAsset = GeneratedRoot + "/DefinitionCatalog.asset";

        public const string LocalizationGeneratedRoot = ProjectRoot + "/Localization/Generated";
        public const string LocalizationTableAsset = LocalizationGeneratedRoot + "/LocalizationTable_zh-Hans.asset";
        public const string LocalizationKeysSource = LocalizationGeneratedRoot + "/LocalizationKeys.g.cs";

        public const string LocalizationScanIgnoreFile = ProjectRoot + "/Localization/scan-ignore.txt";

        // 可运行入口
        public const string BootstrapScene = ProjectRoot + "/Flow/Scenes/Bootstrap.unity";
        public const string BattleConfigAsset = ProjectRoot + "/Battle/Config/BattleConfig_Default.asset";

        // 出包产物统一落在 E 盘工程内，避免撑爆 C 盘
        public const string BuildsRoot = "Builds";
        public const string WindowsBuildFolder = BuildsRoot + "/Windows";
        public const string WebGlBuildFolder = BuildsRoot + "/WebGL";
    }
}
