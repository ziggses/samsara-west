using System.Collections;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Localization;
using SamsaraWest.Narrative;
using SamsaraWest.Save;
using SamsaraWest.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SamsaraWest.Tests.PlayMode
{
    /// <summary>
    /// 存档面板的端到端证据：在引导器装出来的真服务、真数据、真存档目录之上，
    /// 按一下存、走开、按一下读，人必须回到那张图、账本必须回到那份状态。
    /// </summary>
    /// <remarks>
    /// EditMode 已经证明搬运逻辑本身守规矩（见 <c>SaveCoordinatorTests</c>），但那时
    /// 面板、注册表、真数据表都不在场。这一组管的是另一半：那条线在真装配里接得通，
    /// 而且界面这一侧确实只认 <c>ISaveCoordinator</c> 就能把事办成。
    /// </remarks>
    public sealed class SaveScreenViewTests
    {
        private const string BootstrapSceneName = "Bootstrap";
        private const string LedgerKey = "flag.ch01.prologue_done";

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

            yield return SceneManager.LoadSceneAsync(BootstrapSceneName, LoadSceneMode.Single);

            // 面板在 AfterSceneLoad 已经自己挂上了；这里再要一次是幂等的，顺便把服务解析推进一步。
            SaveScreenView.EnsureCreated().Tick();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 诊断槽位是共享资源，比账本还难清：它落在磁盘上，会漏给同一场的其它用例，
            // 也会漏进开发者本机那份存档。所以这里必须删掉自己写进去的那一槽。
            if (GameServices.IsReady && GameServices.Registry.TryResolve(out ISaveService saves))
            {
                saves.TryDelete(SaveScreenView.DiagnosticSlot);
            }

            // 账本同样是场景级共享的：用例写过的键要写回去。
            if (GameServices.IsReady && GameServices.Registry.TryResolve(out IStoryState story))
            {
                story.SetValue(LedgerKey, 0);
            }

            if (GameServices.IsReady && GameServices.Registry.TryResolve(out IExplorationService exploration))
            {
                exploration.LeaveMap();
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
        public IEnumerator Screen_AppearsWithoutSceneWiring()
        {
            yield return null;

            var screen = SaveScreenView.Instance;

            Assert.IsNotNull(screen, "存档面板必须自己挂上来，不靠预制体与场景引用。");
            Assert.IsTrue(screen.IsVisible);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();

            Assert.AreEqual(localization.Get(LocalizationKeys.UI_SAVE_TITLE), screen.TitleLine);
            Assert.AreEqual(localization.Get(LocalizationKeys.UI_SAVE_VIEW_HINT), screen.HintLine);
            Assert.AreEqual(0, localization.MissingKeys.Count, "画出来的文案必须全部命中文本表。");
        }

        [UnityTest]
        public IEnumerator Screen_ReportsAnEmptySlotWithoutASave()
        {
            yield return null;

            var screen = SaveScreenView.Instance;
            var saves = GameServices.Registry.Resolve<ISaveService>();
            saves.TryDelete(SaveScreenView.DiagnosticSlot);
            screen.Tick();

            Assert.IsFalse(screen.LoadFromDiagnosticSlot(), "槽位空着，读档必须失败。");
            Assert.AreEqual(SaveScreenView.SlotStatus.Empty, screen.Status);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Format(LocalizationKeys.UI_SAVE_STATUS_EMPTY, SaveScreenView.DiagnosticSlot),
                screen.StatusLine,
                "「没有存档」与「读坏了」要分开说：它们要玩家做的事不一样。");
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }

        [UnityTest]
        public IEnumerator Screen_SavesAndLoadsARealRun()
        {
            yield return null;

            var screen = SaveScreenView.Instance;
            var explorationView = ExplorationScreenView.Instance;

            explorationView.Tick();
            Assert.IsTrue(explorationView.EnterDiagnosticMap(), "先用诊断入口开一张真图，再存。");

            var story = GameServices.Registry.Resolve<IStoryState>();
            story.SetValue(LedgerKey, 1);

            Assert.IsTrue(screen.SaveToDiagnosticSlot(), "存一次真档。");
            Assert.AreEqual(SaveScreenView.SlotStatus.Saved, screen.Status);
            Assert.IsTrue(GameServices.Registry.Resolve<ISaveService>().Exists(SaveScreenView.DiagnosticSlot));

            // 走开：离图 + 把账本改回去，让读档有东西可证明。
            explorationView.LeaveMap();
            story.SetValue(LedgerKey, 0);

            Assert.IsTrue(screen.LoadFromDiagnosticSlot(), "再从槽位读回来。");
            Assert.AreEqual(SaveScreenView.SlotStatus.Loaded, screen.Status);

            Assert.AreEqual(1, story.GetValue(LedgerKey), "账本跟着存档整本回来。");
            Assert.IsNotNull(explorationView.Session, "读档要把人送回存档里的那张图。");
            Assert.AreEqual(ExplorationScreenView.DiagnosticMapId, explorationView.Session.MapId);

            var localization = GameServices.Registry.Resolve<ILocalizationService>();
            Assert.AreEqual(
                localization.Format(LocalizationKeys.UI_SAVE_STATUS_LOADED, SaveScreenView.DiagnosticSlot),
                screen.StatusLine);
            Assert.AreEqual(0, localization.MissingKeys.Count);
        }
    }
}
