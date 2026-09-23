using NUnit.Framework;
using SamsaraWest.Data;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// ID 规则是「数据标识唯一且可追溯」的入口。规则必须与既有剧本约定逐字兼容，
    /// 因此这里用剧本里真实出现过的 ID 当作验收样例。
    /// </summary>
    public sealed class IdRulesTests
    {
        [TestCase(DefinitionKind.Character, "CHR_WUKONG")]
        [TestCase(DefinitionKind.Skill, "SKL_CLOUD_STEP")]
        [TestCase(DefinitionKind.Status, "STS_POISON")]
        [TestCase(DefinitionKind.Enemy, "ENM_BONE_DEMON")]
        [TestCase(DefinitionKind.Encounter, "ENC_CH01_001")]
        [TestCase(DefinitionKind.Encounter, "EENC_CH01_002")]
        [TestCase(DefinitionKind.Encounter, "BENC_CH01_003")]
        [TestCase(DefinitionKind.BossPhase, "BSP_CH01_001_P2")]
        [TestCase(DefinitionKind.Item, "ITM_HEAL_PILL")]
        [TestCase(DefinitionKind.Equipment, "EQP_SWORD_001")]
        [TestCase(DefinitionKind.Sutra, "SUT_HEART")]
        [TestCase(DefinitionKind.Quest, "QST_CH01_MAIN")]
        [TestCase(DefinitionKind.Dialogue, "DLG_CH01_004")]
        [TestCase(DefinitionKind.Map, "CH01_MAP01")]
        [TestCase(DefinitionKind.Interactable, "INT_CH01_CHEST")]
        [TestCase(DefinitionKind.LootTable, "LUT_CH01_COMMON")]
        [TestCase(DefinitionKind.Shop, "SHP_CH01_MERCHANT")]
        [TestCase(DefinitionKind.Recipe, "RCP_ELIXIR")]
        [TestCase(DefinitionKind.EndingCondition, "END_TRUE")]
        public void IsValidId_AcceptsScriptConventions(DefinitionKind kind, string id)
        {
            Assert.IsTrue(IdRules.IsValidId(kind, id), $"{kind} 应接受 {id}");
        }

        [TestCase(DefinitionKind.Character, "WUKONG")]
        [TestCase(DefinitionKind.Character, "chr_wukong")]
        [TestCase(DefinitionKind.Encounter, "ENC_CH1_1")]
        [TestCase(DefinitionKind.Encounter, "ENC_CH01_0001")]
        [TestCase(DefinitionKind.BossPhase, "BSP_CH01_001")]
        [TestCase(DefinitionKind.Map, "CH1_MAP1")]
        [TestCase(DefinitionKind.Map, "MAP_CH01_01")]
        [TestCase(DefinitionKind.Interactable, "INT_CH1_BOX")]
        [TestCase(DefinitionKind.Dialogue, "对话一")]
        public void IsValidId_RejectsMalformedIds(DefinitionKind kind, string id)
        {
            Assert.IsFalse(IdRules.IsValidId(kind, id), $"{kind} 应拒绝 {id}");
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void IsValidId_RejectsEmpty(string id)
        {
            Assert.IsFalse(IdRules.IsValidId(DefinitionKind.Item, id));
        }

        [Test]
        public void IsValidId_UnknownKind_AcceptsAnyNonEmptyId()
        {
            Assert.IsTrue(IdRules.IsValidId(DefinitionKind.Unknown, "whatever"));
            Assert.IsFalse(IdRules.IsValidId(DefinitionKind.Unknown, " "));
        }

        [Test]
        public void GetPattern_ReflectsNamingConvention()
        {
            StringAssert.Contains("END_", IdRules.GetPattern(DefinitionKind.EndingCondition));
            Assert.AreEqual("<无规则>", IdRules.GetPattern(DefinitionKind.Unknown));
        }

        [TestCase("CH01_N01_ENTRY")]
        [TestCase("CH01_N100_FIGHT")]
        public void IsValidNarrativeNodeId_AcceptsScriptNodes(string id)
        {
            Assert.IsTrue(IdRules.IsValidNarrativeNodeId(id));
        }

        [TestCase("N01_ENTRY")]
        [TestCase("CH1_N01_ENTRY")]
        [TestCase("CH01_N01")]
        public void IsValidNarrativeNodeId_RejectsMalformed(string id)
        {
            Assert.IsFalse(IdRules.IsValidNarrativeNodeId(id));
        }

        [TestCase("flag.ch01.truth_told")]
        [TestCase("relation.wukong")]
        [TestCase("karma.compassion")]
        [TestCase("ending.true")]
        public void IsValidStateKey_AcceptsScriptKeys(string key)
        {
            Assert.IsTrue(IdRules.IsValidStateKey(key));
        }

        [TestCase("flag.CH01")]
        [TestCase("Flag.ch01")]
        [TestCase("flag")]
        [TestCase("flag.")]
        [TestCase("flag.ch01.")]
        [TestCase("好感动")]
        public void IsValidStateKey_RejectsMalformed(string key)
        {
            Assert.IsFalse(IdRules.IsValidStateKey(key));
        }

        [TestCase("ui.battle.attack")]
        [TestCase("UI_BATTLE_ATTACK")]
        [TestCase("dlg.ch01.004")]
        public void IsValidLocalizationKey_AcceptsKeyWithoutChinese(string key)
        {
            Assert.IsTrue(IdRules.IsValidLocalizationKey(key));
        }

        [TestCase("这不该出现")]
        [TestCase("ui.攻击")]
        [TestCase("ui.battle.")]
        [TestCase(" . ")]
        public void IsValidLocalizationKey_RejectsMalformed(string key)
        {
            Assert.IsFalse(IdRules.IsValidLocalizationKey(key));
        }

        [Test]
        public void ContainsChinese_DetectsCjkCharacters()
        {
            Assert.IsTrue(IdRules.ContainsChinese("攻击"));
            Assert.IsTrue(IdRules.ContainsChinese("Attack 攻击"));
            Assert.IsFalse(IdRules.ContainsChinese("Attack"));
            Assert.IsFalse(IdRules.ContainsChinese(string.Empty));
            Assert.IsFalse(IdRules.ContainsChinese(null));
        }

        [TestCase("ui.battle.attack", "UI_BATTLE_ATTACK")]
        [TestCase("UI_BATTLE_ATTACK", "UI_BATTLE_ATTACK")]
        [TestCase("a--b", "A_B")]
        [TestCase(".x", "X")]
        [TestCase("dlg.ch01.004", "DLG_CH01_004")]
        [TestCase("key_", "KEY")]
        public void ToConstantName_ProducesEditorFriendlyName(string key, string expected)
        {
            Assert.AreEqual(expected, IdRules.ToConstantName(key));
        }

        [Test]
        public void ToConstantName_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, IdRules.ToConstantName(null));
            Assert.AreEqual(string.Empty, IdRules.ToConstantName(string.Empty));
        }
    }
}
