using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Economy;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 钱袋与背包的基本行为：加、扣、不够就不动，以及读档式整本替换。
    /// </summary>
    /// <remarks>
    /// 这里刻意<b>不</b>验「堆叠上限」与「格子」——那两个口径都还没定，服务也不实现它们。
    /// 用例写的是「拿光了就不再登记这件东西」这类能被反证的规则，而不是实现的内部形状。
    /// </remarks>
    internal sealed class EconomyServiceTests
    {
        private EconomyService _economy;

        [SetUp]
        public void SetUp() => _economy = new EconomyService();

        [Test]
        public void NewVault_HasNothing()
        {
            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(0, _economy.Items.Count);
            Assert.AreEqual(0, _economy.CountOf("ITM_HERB"));
        }

        [Test]
        public void AddGold_Accumulates()
        {
            _economy.AddGold(30);
            _economy.AddGold(12);

            Assert.AreEqual(42, _economy.Gold);
        }

        [Test]
        public void AddGold_IgnoresNonPositiveAmounts()
        {
            _economy.AddGold(10);
            _economy.AddGold(0);
            _economy.AddGold(-5);

            Assert.AreEqual(10, _economy.Gold);
        }

        [Test]
        public void TrySpendGold_TakesMoneyWhenThereIsEnough()
        {
            _economy.AddGold(50);

            Assert.IsTrue(_economy.TrySpendGold(30));
            Assert.AreEqual(20, _economy.Gold);
        }

        [Test]
        public void TrySpendGold_RefusesAndChangesNothingWhenShort()
        {
            _economy.AddGold(20);

            Assert.IsFalse(_economy.TrySpendGold(21));
            Assert.AreEqual(20, _economy.Gold, "钱不够时一分都不该扣。");
        }

        [Test]
        public void TrySpendGold_RefusesNonPositiveAmounts()
        {
            _economy.AddGold(20);

            Assert.IsFalse(_economy.TrySpendGold(0), "没有「花 0 块」这件事。");
            Assert.IsFalse(_economy.TrySpendGold(-3));
            Assert.AreEqual(20, _economy.Gold);
        }

        [Test]
        public void CountOf_UnknownItemIsZero() => Assert.AreEqual(0, _economy.CountOf("ITM_HERB"));

        [Test]
        public void AddItem_AccumulatesAcrossCalls()
        {
            _economy.AddItem("ITM_HERB", 2);
            _economy.AddItem("ITM_HERB", 3);

            Assert.AreEqual(5, _economy.CountOf("ITM_HERB"));
        }

        [Test]
        public void AddItem_IgnoresNonPositiveCounts()
        {
            _economy.AddItem("ITM_HERB", 0);
            _economy.AddItem("ITM_HERB", -1);

            Assert.AreEqual(0, _economy.Items.Count);
        }

        [Test]
        public void TryRemoveItem_ForgetsItemWhenLastOneIsTaken()
        {
            _economy.AddItem("ITM_HERB", 1);

            Assert.IsTrue(_economy.TryRemoveItem("ITM_HERB", 1));
            Assert.AreEqual(0, _economy.CountOf("ITM_HERB"));
            Assert.AreEqual(0, _economy.Items.Count, "「有 0 个」与「没有这件东西」是同一件事。");
        }

        [Test]
        public void TryRemoveItem_RefusesAndChangesNothingWhenShort()
        {
            _economy.AddItem("ITM_HERB", 2);

            Assert.IsFalse(_economy.TryRemoveItem("ITM_HERB", 3));
            Assert.AreEqual(2, _economy.CountOf("ITM_HERB"));
        }

        [Test]
        public void TryRemoveItem_RefusesUnknownItemAndNonPositiveCounts()
        {
            Assert.IsFalse(_economy.TryRemoveItem("ITM_HERB", 1));
            Assert.IsFalse(_economy.TryRemoveItem("ITM_HERB", 0));
            Assert.IsFalse(_economy.TryRemoveItem(null, 1));
        }

        [Test]
        public void Items_AreSortedByIdSoTwoSnapshotsMatch()
        {
            _economy.AddItem("ITM_PEACH", 1);
            _economy.AddItem("ITM_HERB", 2);
            _economy.AddItem("ITM_BONE", 3);

            var items = _economy.Items;

            Assert.AreEqual("ITM_BONE", items[0].ItemId);
            Assert.AreEqual("ITM_HERB", items[1].ItemId);
            Assert.AreEqual("ITM_PEACH", items[2].ItemId);
        }

        [Test]
        public void Restore_ReplacesTheWholeVaultInsteadOfAddingToIt()
        {
            _economy.AddGold(100);
            _economy.AddItem("ITM_HERB", 5);

            _economy.Restore(7, new[] { new ItemStack("ITM_BONE", 2) });

            Assert.AreEqual(7, _economy.Gold, "读档是换成另一份状态，不是往现在的钱袋里加。");
            Assert.AreEqual(0, _economy.CountOf("ITM_HERB"));
            Assert.AreEqual(2, _economy.CountOf("ITM_BONE"));
        }

        [Test]
        public void Restore_IgnoresNonPositiveEntriesAndNegativeGold()
        {
            _economy.Restore(-5, new[] { new ItemStack("ITM_HERB", 0), new ItemStack("ITM_BONE", -2) });

            Assert.AreEqual(0, _economy.Gold, "负数金钱不合法，退回 0 而不是留着。");
            Assert.AreEqual(0, _economy.Items.Count);
        }

        [Test]
        public void Clear_EmptiesEverything()
        {
            _economy.AddGold(9);
            _economy.AddItem("ITM_HERB", 1);

            _economy.Clear();

            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(0, _economy.Items.Count);
        }

        [Test]
        public void OnUnregistered_EmptiesTheVault()
        {
            _economy.AddGold(9);
            _economy.AddItem("ITM_HERB", 1);

            _economy.OnUnregistered();

            Assert.AreEqual(0, _economy.Gold);
            Assert.AreEqual(0, _economy.Items.Count);
        }

        [Test]
        public void EconomyModule_RegistersTheService()
        {
            var registry = new ServiceRegistry();

            EconomyModule.Install(registry);

            Assert.IsNotNull(registry.Resolve<IEconomyService>());
            Assert.AreSame(registry.Resolve<IEconomyService>(), registry.Resolve<IEconomyService>());
        }
    }
}
