using System.Collections;
using NUnit.Framework;
using SamsaraWest.Battle;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Flow;
using SamsaraWest.Narrative;
using SamsaraWest.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 「打完一场，战果留在账本里、并且能跟着存档走出去」的端到端证据。
    /// </summary>
    /// <remarks>
    /// EditMode 的 <c>BattleStoryLinkTests</c> 管这条线的规矩（四种收场各记各的、
    /// 撤退不记成战败、没有遭遇 ID 就不记）。这一组管另一半：在引导器装出来的真服务、
    /// 真数据表、真战斗之下，一场真打出来的战斗也必须把战果留在账本里，
    /// 而且<b>一行代码都不由测试来搬</b>——测试只负责开战与推进。
    /// 最后一条把上一轮的存档接线接上：战果要能被存档带走。
    /// </remarks>
    public sealed class BattleStoryWiringTests
    {
        private const string BootstrapSceneName = "Bootstrap";

        /// <summary>maps.csv 里 CH01_MAP01 绑定的那场遭遇；首章野外图唯一的一条。</summary>
        private const string EncounterId = "ENC_CH01_001";

        private const string WonKey = "flag.battle.enc_ch01_001.won";
        private const string LostKey = "flag.battle.enc_ch01_001.lost";
        private const string RetreatedKey = "flag.battle.enc_ch01_001.retreated";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameLog.DisableFileSink();
            GameLog.Reset();

            var existing = GameBootstrap.Instance;
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
                yield return null;
            }

            if (GameServices.IsReady)
            {
                GameServices.Registry.Clear();
                GameServices.Uninstall();
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 战果是写进账本的：清掉它，别污染同一场的其它用例。
            // 用 TryResolve 而不是 Resolve：用例中途失败时服务可能没装上，清理不该再抛一次。
            if (GameServices.IsReady)
            {
                if (GameServices.Registry.TryResolve(out IStoryState story))
                {
                    story.SetValue(WonKey, 0);
                    story.SetValue(LostKey, 0);
                    story.SetValue(RetreatedKey, 0);
                }

                if (GameServices.Registry.TryResolve(out IBattleService battle) && battle.HasActiveBattle)
                {
                    battle.EndBattle();
                }
            }

            var bootstrap = GameBootstrap.Instance;
            if (bootstrap != null)
            {
                Object.Destroy(bootstrap.gameObject);
                yield return null;
            }

            if (GameServices.IsReady)
            {
                GameServices.Registry.Clear();
                GameServices.Uninstall();
            }
        }

        [UnityTest]
        public IEnumerator RealBattle_LeavesItsOutcomeInTheLedger()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var battle = registry.Resolve<IBattleService>();
            var story = registry.Resolve<IStoryState>();

            Assert.IsNotNull(
                GameBootstrap.Instance.BattleStory,
                "战果回写线要装在引导末尾，否则打完了没人记账。");
            Assert.AreEqual(0, CountRecordedOutcomes(story), "开局前账本里不该有战果。");

            var session = StartRealBattle(registry, battle);
            session.RunToEnd();

            var outcome = session.Outcome;
            Assert.AreNotEqual(
                BattleOutcome.Ongoing,
                outcome,
                "守门的那一场必须能分出胜负——推不到结局说明战斗内核卡住了，不是这条接线的问题。");

            var key = BattleFlags.OutcomeKey(EncounterId, outcome);
            Assert.IsNotEmpty(key, "四种收场都该有对应的键。");
            Assert.AreEqual(1, story.GetValue(key), $"真打一场之后，账本里要有 {key}。");
            Assert.AreEqual(1, GameBootstrap.Instance.BattleStory.OutcomesRecorded, "记账的条数要能被数出来。");
            Assert.AreEqual(1, CountRecordedOutcomes(story), "一场仗只留一个战果键，输赢不并列。");
        }

        [UnityTest]
        public IEnumerator ForcedRetreat_IsNeverWrittenAsDefeat()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var battle = registry.Resolve<IBattleService>();
            var story = registry.Resolve<IStoryState>();

            var session = StartRealBattle(registry, battle);

            // 内核唯一对外能指定结局的入口：剧本用它把这一场收掉、回到剧情之前的节点。
            Assert.IsTrue(session.ForceRetreat(), "剧情撤退要能当场收场。");

            Assert.AreEqual(1, story.GetValue(RetreatedKey), "撤退是事实，要记下来。");
            Assert.AreEqual(
                0,
                story.GetValue(LostKey),
                "撤退不是战败。这条口径要在内核、接线、剧本三处一致，否则「输过一次」的条件会判错。");
        }

        [UnityTest]
        public IEnumerator Outcome_TravelsWithTheSave()
        {
            yield return LoadBootstrapScene();

            var registry = GameServices.Registry;
            var battle = registry.Resolve<IBattleService>();
            var story = registry.Resolve<IStoryState>();

            var session = StartRealBattle(registry, battle);
            session.RunToEnd();

            var key = BattleFlags.OutcomeKey(EncounterId, session.Outcome);
            Assert.AreEqual(1, story.GetValue(key), $"先得把战果记进账本，才谈得上带走：{key}。");

            // 上一轮的存档接线：账本整本进出存档，战果在它最认识的那几组字段里。
            // 这里只采快照，不落盘——落盘那一半由 SaveCoordinatorTests 与存档面板的用例管。
            var saves = registry.Resolve<ISaveCoordinator>();
            var data = saves.Capture();

            CollectionAssert.Contains(
                data.FlagKeys,
                key,
                "战果要能跟着存档走出去——不然读档回来，打过的仗就当没打过。");
        }

        /// <summary>按真实数据开一场：目录里查遭遇、取骨架期的队伍、组入场清单。</summary>
        private static BattleSession StartRealBattle(IServiceRegistry registry, IBattleService battle)
        {
            var definitions = registry.Resolve<IDefinitionRegistry>();

            Assert.IsTrue(
                definitions.TryGet(EncounterId, out EncounterDefinition encounter) && encounter != null,
                $"定义目录里必须有 {EncounterId}——maps.csv 的 encounterTableId 指着它。");

            var party = BattleFactory.DefaultParty(definitions);
            Assert.IsNotEmpty(party, "骨架期的队伍口径是目录里全部可操作角色。");

            var setup = BattleFactory.FromEncounter(encounter, party);
            Assert.IsTrue(setup.Validate(out var error), $"入场清单必须合法：{error}");

            var session = battle.StartBattle(setup);
            Assert.IsNotNull(session);
            return session;
        }

        /// <summary>账本里现在有几条战果（按前缀数，不数总数——账本里还有别的键）。</summary>
        private static int CountRecordedOutcomes(IStoryState story)
        {
            var count = 0;
            foreach (var pair in story.Values)
            {
                if (pair.Key.StartsWith(BattleFlags.Prefix, System.StringComparison.Ordinal) && pair.Value != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static IEnumerator LoadBootstrapScene()
        {
            // 无条件重载：SetUp 刚把场景里的引导器拆掉，只有重新加载才会再装一次服务。
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }
    }
}
