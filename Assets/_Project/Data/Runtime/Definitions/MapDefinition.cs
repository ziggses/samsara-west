using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 地图。ID 沿用剧本既有写法 <c>CH01_MAP01</c>。
    /// 瓦片像素尺寸固定 32（与已交付素材一致），世界层 PPU 留待切片阶段再定。
    /// </summary>
    [CreateAssetMenu(fileName = "Map", menuName = "SamsaraWest/定义/地图 Map")]
    public sealed class MapDefinition : DefinitionBase
    {
        [CsvColumn("chapterIndex", required: true)] [SerializeField] private int _chapterIndex = 1;
        [CsvColumn("sceneName", required: true)] [SerializeField] private string _sceneName;

        [CsvColumn("gridWidth", required: true)] [SerializeField] private int _gridWidth = 20;
        [CsvColumn("gridHeight", required: true)] [SerializeField] private int _gridHeight = 12;

        [Tooltip("瓦片像素尺寸。已交付素材为 32×32，除非美术变更否则不要改。")]
        [CsvColumn("tilePixelSize")] [SerializeField] private int _tilePixelSize = 32;

        [Tooltip("本地坐标下瓦片原点，用于对齐网格与背景图。")]
        [CsvColumn("originOffsetX")] [SerializeField] private float _originOffsetX;
        [CsvColumn("originOffsetY")] [SerializeField] private float _originOffsetY;

        [CsvColumn("encounterTableId")] [SerializeField] private string _encounterTableId;
        [CsvColumn("encounterRate")] [SerializeField] private float _encounterRate;

        [CsvColumn("isTown")] [SerializeField] private bool _isTown;
        [CsvColumn("isSafeZone")] [SerializeField] private bool _isSafeZone;
        [CsvColumn("allowsSaving")] [SerializeField] private bool _allowsSaving = true;

        [CsvColumn("bgmKey")] [SerializeField] private string _bgmKey;
        [CsvColumn("ambientKey")] [SerializeField] private string _ambientKey;
        [CsvColumn("backgroundSpriteKey")] [SerializeField] private string _backgroundSpriteKey;

        [Tooltip("相邻地图 ID，用于地图切换与快速移动。")]
        [CsvColumn("connections")] [SerializeField] private string[] _connections = System.Array.Empty<string>();

        public override DefinitionKind Kind => DefinitionKind.Map;

        public int ChapterIndex => _chapterIndex;

        public string SceneName => _sceneName;

        public int GridWidth => _gridWidth;

        public int GridHeight => _gridHeight;

        public int TilePixelSize => _tilePixelSize;

        public float OriginOffsetX => _originOffsetX;

        public float OriginOffsetY => _originOffsetY;

        public string EncounterTableId => _encounterTableId;

        public float EncounterRate => _encounterRate;

        public bool IsTown => _isTown;

        public bool IsSafeZone => _isSafeZone;

        public bool AllowsSaving => _allowsSaving;

        public string BgmKey => _bgmKey;

        public string AmbientKey => _ambientKey;

        public string BackgroundSpriteKey => _backgroundSpriteKey;

        public string[] Connections => _connections ?? System.Array.Empty<string>();

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_chapterIndex < 1 || _chapterIndex > 8)
            {
                report.Error("MAP_CHAPTER_INVALID", $"章节序号必须在 1–8，当前 {_chapterIndex}。", Id, fieldName: "chapterIndex");
            }

            if (string.IsNullOrWhiteSpace(_sceneName))
            {
                report.Error("MAP_SCENE_EMPTY", "场景名不能为空，否则无法加载。", Id, fieldName: "sceneName");
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(_sceneName ?? string.Empty, @"^[A-Za-z0-9_]+$"))
            {
                report.Error(
                    "MAP_SCENE_FORMAT",
                    $"场景名 '{_sceneName}' 只能包含字母、数字与下划线（Unity 场景名限制）。",
                    Id,
                    fieldName: "sceneName");
            }

            if (_gridWidth < 1 || _gridHeight < 1)
            {
                report.Error("MAP_GRID_INVALID", $"网格尺寸必须为正，当前 {_gridWidth}×{_gridHeight}。", Id, fieldName: "gridWidth");
            }

            if (_tilePixelSize != 32)
            {
                report.Warn(
                    "MAP_TILE_SIZE_OFF_BASELINE",
                    $"瓦片尺寸为 {_tilePixelSize}，与已交付素材的 32×32 基线不一致，确认是美术规格变更。",
                    Id,
                    fieldName: "tilePixelSize");
            }

            if (_encounterRate < 0f || _encounterRate > 1f)
            {
                report.Error("MAP_RATE_INVALID", $"遭遇率必须落在 [0,1]，当前 {_encounterRate}。", Id, fieldName: "encounterRate");
            }

            if (_encounterRate > 0f && string.IsNullOrWhiteSpace(_encounterTableId))
            {
                report.Error("MAP_RATE_NO_TABLE", "设置了遭遇率但没有遭遇表 ID。", Id, fieldName: "encounterTableId");
            }

            if (!string.IsNullOrWhiteSpace(_encounterTableId) && _encounterRate <= 0f)
            {
                report.Warn("MAP_TABLE_NO_RATE", "配置了遭遇表但遭遇率为 0，野外不会触发战斗。", Id, fieldName: "encounterRate");
            }

            for (var i = 0; i < Connections.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Map, Connections[i]))
                {
                    report.Error("MAP_CONNECTION_PATTERN", $"相邻地图 ID '{Connections[i]}' 不符合地图命名规则。", Id, fieldName: "connections");
                }

                if (Connections[i] == Id)
                {
                    report.Error("MAP_SELF_CONNECTION", "地图不能连接到自己。", Id, fieldName: "connections");
                }
            }

            if (_isTown && _encounterRate > 0f)
            {
                report.Warn("MAP_TOWN_ENCOUNTERS", "城镇地图设置了野外遭遇率，通常城镇不应有随机战斗。", Id, fieldName: "encounterRate");
            }
        }
    }
}
