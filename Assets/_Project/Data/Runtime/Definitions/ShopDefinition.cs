using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>商店。按章节出现，价格由基础价乘倍率得出，不重复写数值。</summary>
    [CreateAssetMenu(fileName = "Shop", menuName = "SamsaraWest/定义/商店 Shop")]
    public sealed class ShopDefinition : DefinitionBase
    {
        [CsvColumn("chapterIndex", required: true)] [SerializeField] private int _chapterIndex = 1;
        [CsvColumn("mapId")] [SerializeField] private string _mapId;

        [CsvColumn("itemIds")] [SerializeField] private string[] _itemIds = System.Array.Empty<string>();
        [CsvColumn("recipeIds")] [SerializeField] private string[] _recipeIds = System.Array.Empty<string>();

        [Tooltip("售价倍率。1 为标准价，低于 1 相当于打折。")]
        [CsvColumn("priceMultiplier")] [SerializeField] private float _priceMultiplier = 1f;

        [Tooltip("回收价倍率，相对于道具基础价。")]
        [CsvColumn("buybackMultiplier")] [SerializeField] private float _buybackMultiplier = 0.4f;

        [CsvColumn("restocksOnChapterChange")] [SerializeField] private bool _restocksOnChapterChange = true;
        [CsvColumn("merchantNameKey")] [SerializeField] private string _merchantNameKey;

        public override DefinitionKind Kind => DefinitionKind.Shop;

        public int ChapterIndex => _chapterIndex;

        public string MapId => _mapId;

        public string[] ItemIds => _itemIds ?? System.Array.Empty<string>();

        public string[] RecipeIds => _recipeIds ?? System.Array.Empty<string>();

        public float PriceMultiplier => _priceMultiplier;

        public float BuybackMultiplier => _buybackMultiplier;

        public bool RestocksOnChapterChange => _restocksOnChapterChange;

        public string MerchantNameKey => _merchantNameKey;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (_chapterIndex < 1 || _chapterIndex > 8)
            {
                report.Error("SHP_CHAPTER_INVALID", $"章节序号必须在 1–8，当前 {_chapterIndex}。", Id, fieldName: "chapterIndex");
            }

            if (!string.IsNullOrWhiteSpace(_mapId) && !IdRules.IsValidId(DefinitionKind.Map, _mapId))
            {
                report.Error("SHP_MAP_ID_PATTERN", $"地图 ID '{_mapId}' 不符合地图命名规则（CH01_MAP01）。", Id, fieldName: "mapId");
            }

            if (ItemIds.Length == 0 && RecipeIds.Length == 0)
            {
                report.Warn("SHP_EMPTY", "商店没有出售任何东西，可能还没填。", Id, fieldName: "itemIds");
            }

            for (var i = 0; i < ItemIds.Length; i++)
            {
                var id = ItemIds[i];
                if (!IdRules.IsValidId(DefinitionKind.Item, id)
                    && !IdRules.IsValidId(DefinitionKind.Equipment, id)
                    && !IdRules.IsValidId(DefinitionKind.Sutra, id))
                {
                    report.Error("SHP_ITEM_ID_PATTERN", $"商品 ID '{id}' 命名不合法。", Id, fieldName: "itemIds");
                }
            }

            for (var i = 0; i < RecipeIds.Length; i++)
            {
                if (!IdRules.IsValidId(DefinitionKind.Recipe, RecipeIds[i]))
                {
                    report.Error("SHP_RECIPE_ID_PATTERN", $"配方 ID '{RecipeIds[i]}' 命名不合法。", Id, fieldName: "recipeIds");
                }
            }

            if (_priceMultiplier <= 0f)
            {
                report.Error("SHP_MULTIPLIER_INVALID", $"售价倍率必须为正，当前 {_priceMultiplier}。", Id, fieldName: "priceMultiplier");
            }

            if (_buybackMultiplier < 0f || _buybackMultiplier > 1f)
            {
                report.Error("SHP_BUYBACK_INVALID", $"回收价倍率必须落在 [0,1]，当前 {_buybackMultiplier}。", Id, fieldName: "buybackMultiplier");
            }

            if (string.IsNullOrWhiteSpace(_merchantNameKey))
            {
                report.Warn("SHP_MERCHANT_KEY_MISSING", "未指定商人名键，商店标题会在界面中留空。", Id, fieldName: "merchantNameKey");
            }
        }
    }
}
