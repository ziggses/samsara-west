using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Data;
using SamsaraWest.Editor;
using UnityEditor;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 选目标的敌我判据：列／排技能的阵营读技能表里的显式字段，不再靠威力猜。
    /// </summary>
    /// <remarks>
    /// 换判据这种事必须有账：老判据是「威力大于 0 就当攻击技」，于是「给同排加护盾」这类技能
    /// 永远做不出来。下面这组断言锁的就是「字段说了算、威力不再参与」，最后一条还钉住真表里的数据。
    /// </remarks>
    [TestFixture]
    public sealed class BattleTargetingTests
    {
        [Test]
        public void 列与排技能的敌我看字段_不再看威力()
        {
            using var lab = new BattleLab();

            var columnStrike = lab.AttackSkill("SKL_COL_DMG", power: 20, target: TargetRule.Column);
            BattleLab.SetPrivate(columnStrike, "_hostileOnly", true);
            Assert.IsTrue(
                BattleTargeting.RequiresHostilePrimary(columnStrike),
                "标了敌对的列攻，主目标必须从敌营里挑。");

            BattleLab.SetPrivate(columnStrike, "_hostileOnly", false);
            Assert.IsFalse(
                BattleTargeting.RequiresHostilePrimary(columnStrike),
                "字段关掉之后，哪怕它是纯伤害技能也不能再当成敌对向。");

            var rowHeal = lab.HealingSkill("SKL_ROW_HEAL", 20, TargetRule.Row);
            Assert.IsFalse(BattleTargeting.RequiresHostilePrimary(rowHeal), "排治疗以我方为中心。");
        }

        [Test]
        public void 单体与全体规则的阵营由目标规则自己决定()
        {
            using var lab = new BattleLab();

            Assert.IsTrue(BattleTargeting.RequiresHostilePrimary(
                lab.AttackSkill("SKL_ONE", target: TargetRule.SingleEnemy)));
            Assert.IsTrue(BattleTargeting.RequiresHostilePrimary(
                lab.AttackSkill("SKL_RANDOM", target: TargetRule.RandomEnemy)));
            Assert.IsFalse(BattleTargeting.RequiresHostilePrimary(
                lab.HealingSkill("SKL_ALLY", 20)));
            Assert.IsFalse(BattleTargeting.RequiresHostilePrimary(null), "空技能不能崩。");
        }

        [Test]
        public void 真表里的列攻技能都标了敌对()
        {
            Assert.IsTrue(DefinitionImportMap.TryResolve("skills", out var binding), "技能表必须在导入清单里。");

            var guids = AssetDatabase.FindAssets("SKL_BAJIE_RAKE", new[] { binding.OutputFolder });
            Assert.AreEqual(1, guids.Length, "找不到钉耙的技能资产，先跑一次数据导入。");

            var rake = AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.AreEqual(TargetRule.Column, rake.Target);
            Assert.IsTrue(
                rake.HostileOnly,
                "钉耙是打一列敌人的技能；漏了 hostileOnly，它会对着我方那一列抡。");
        }
    }
}
