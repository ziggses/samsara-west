using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Exploration;
using SamsaraWest.Flow;
using SamsaraWest.Narrative;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 「探索里按了一次交互 → 开一段对白」这条接线。它存在的理由是两边的内核互相不认识：
    /// 探索只发一条事件，推进器只推它已经开着的那段，判据（这是不是一条对话）留给数据回答。
    /// 因此这里要锁的是<b>判据</b>与<b>筛选</b>：门和箱子发的交互事件不该被拿去问对话表。
    /// </summary>
    public sealed class ExplorationDialogueLinkTests
    {
        private const string Header =
            "id,displayNameKey,descriptionKey,tags,version,chapterIndex,inkKnotName,speakerCharacterId,lineCount," +
            "nextNodeIds,requiredStateKey,requiredOperator,requiredValue,setsStateKeys,karmaChannel,karmaDelta,bgmKey,portraitKey,isTerminal";

        private readonly List<Object> _created = new List<Object>();

        private ServiceRegistry _registry;
        private EventBus _bus;
        private IDialogueService _dialogue;
        private ExplorationDialogueLink _link;

        [SetUp]
        public void SetUp()
        {
            var catalog = ScriptableObject.CreateInstance<DefinitionCatalog>();
            _created.Add(catalog);
            catalog.SetDefinitions(new DefinitionBase[]
            {
                Define("DLG_CH01_002", "dlg.ch01.002.name", "CH01_N02_GREETER_TALK", 7),
                Define("DLG_CH01_004", "dlg.ch01.004.name", "CH01_N04_BROKEN_DRUM", 1),
            });

            catalog.Rebuild(new ValidationReport());

            _registry = new ServiceRegistry();
            _registry.Register<IEventBus>(_bus = new EventBus());
            NarrativeModule.Install(_registry, new DefinitionRegistry(catalog));

            _dialogue = _registry.Resolve<IDialogueService>();
            _link = new ExplorationDialogueLink(_bus, _dialogue);
        }

        [TearDown]
        public void TearDown()
        {
            _link?.Dispose();
            _registry?.Clear();

            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
            _registry = null;
            _bus = null;
            _dialogue = null;
            _link = null;
        }

        private DialogueDefinition Define(string id, string displayNameKey, string knotName, int lineCount)
        {
            var row = string.Join(
                ",",
                id,
                displayNameKey,
                displayNameKey + ".desc",
                "ch01;dialogue",
                "1",
                "1",
                knotName,
                string.Empty,
                lineCount.ToString(),
                string.Empty,
                string.Empty,
                "Equal",
                "0",
                string.Empty,
                string.Empty,
                "0",
                "music.ch01.field",
                string.Empty,
                "true");

            var report = new ValidationReport();
            var table = CsvParser.Parse(Header + "\n" + row + "\n", "dialogues.csv");
            var definition = (DialogueDefinition)CsvDefinitionMapper.Map(
                typeof(DialogueDefinition),
                table.Rows[0],
                table,
                report);

            Assert.AreEqual(0, report.ErrorCount, $"用例数据本身要修：{report}");
            _created.Add(definition);
            return definition;
        }

        private void Interact(string interactableId, string interactionTypeKey, string targetId) =>
            _bus.Publish(
                ExplorationEventChannel.Channel,
                new InteractionTriggeredEvent(interactableId, interactionTypeKey, targetId, GridPosition.Origin, false));

        [Test]
        public void DialogueInteraction_OpensTheDialogueNamedByTheTarget()
        {
            Interact("INT_CH01_001_GREETER_MONKEY", "interact.dialogue", "DLG_CH01_002");

            Assert.AreEqual(1, _link.Started);
            Assert.AreEqual(DialogueStartRejection.None, _link.LastRejection);
            Assert.IsTrue(_dialogue.IsActive);
            Assert.AreEqual("DLG_CH01_002", _dialogue.DialogueId);
        }

        [Test]
        public void ExamineInteraction_AlsoOpensADialogue()
        {
            // 断鼓那类环境调查走的是 interact.examine，但目标同样是一条对话。
            Interact("INT_CH01_002_BROKEN_DRUM", "interact.examine", "DLG_CH01_004");

            Assert.AreEqual(1, _link.Started);
            Assert.AreEqual("CH01_N04_BROKEN_DRUM", _dialogue.KnotName);
        }

        [Test]
        public void DoorInteraction_IsNotEvenAskedAbout()
        {
            // 门的 targetId 是一张地图。类型键先筛一道，免得每次开门都在日志里留一条
            // 「找不到对话定义 CH01_MAP02」的假警报，把真问题淹掉。
            Interact("INT_CH01_004_FRONT_GATE", "interact.door", "CH01_MAP02");

            Assert.AreEqual(0, _link.Started);
            Assert.AreEqual(DialogueStartRejection.None, _link.LastRejection, "没问过对话表，就谈不上被拒。");
            Assert.IsFalse(_dialogue.IsActive);

            // 「听了但不接」与「没在听」必须分得开：门走过去之后，搭话照旧要能开。
            Interact("INT_CH01_001_GREETER_MONKEY", "interact.dialogue", "DLG_CH01_002");

            Assert.AreEqual(1, _link.Started);
        }

        [Test]
        public void ChestInteraction_IsNotAskedAbout()
        {
            // 名单外的类型键一律不问。数据表里现在没有宝箱了（占位那一件随桃林重做一起删了），
            // 这里用一件合成交互物守住这条判据：筛的是类型键，不是「表里有没有这种东西」。
            Interact("INT_CH01_016_LAB_CHEST", "interact.chest", "LUT_ENM_LAB");

            Assert.AreEqual(0, _link.Started);
            Assert.IsFalse(_dialogue.IsActive);
        }

        [Test]
        public void ExaminedNonDialogueTarget_IsReportedAsRejected()
        {
            // 类型键在名单里，但目标不是对话——这条判据由数据回答，接线不维护白名单。
            Interact("INT_CH01_009_RUMOR", "interact.examine", "CH01_MAP02");

            Assert.AreEqual(0, _link.Started);
            Assert.AreEqual(DialogueStartRejection.UnknownDialogue, _link.LastRejection);
        }

        [Test]
        public void SecondInteractionWhileADialogueIsOpen_IsRejectedAsAlreadyActive()
        {
            Interact("INT_CH01_001_GREETER_MONKEY", "interact.dialogue", "DLG_CH01_002");
            Interact("INT_CH01_002_BROKEN_DRUM", "interact.examine", "DLG_CH01_004");

            Assert.AreEqual(1, _link.Started, "第二段对白不许把开着的这段顶掉。");
            Assert.AreEqual(DialogueStartRejection.AlreadyActive, _link.LastRejection);
            Assert.AreEqual("DLG_CH01_002", _dialogue.DialogueId);
        }

        [Test]
        public void EnteringAMap_ClosesWhateverDialogueIsOpen()
        {
            Interact("INT_CH01_001_GREETER_MONKEY", "interact.dialogue", "DLG_CH01_002");
            Assert.IsTrue(_dialogue.IsActive);

            _bus.Publish(
                ExplorationEventChannel.Channel,
                new MapEnteredEvent("CH01_MAP02", GridPosition.Origin, 60, 40, null));

            Assert.AreEqual(1, _link.Interrupted);
            Assert.IsFalse(
                _dialogue.IsActive,
                "「对白开着一半切了图」推进器不知道（它只认账本与条件），得有人替它把这段收掉；"
                + "不收的话，回到图上的第一下按键是在推进上一张图的对白。");
        }

        [Test]
        public void EnteringAMap_WithNothingOpen_CountsNothing()
        {
            _bus.Publish(
                ExplorationEventChannel.Channel,
                new MapEnteredEvent("CH01_MAP01", GridPosition.Origin, 60, 40, null));

            Assert.AreEqual(0, _link.Interrupted, "没开着对白时换图是常事（每次进图都会发生），不该记一笔。");
            Assert.AreEqual(0, _link.Started);
        }

        [Test]
        public void EnteringAMap_AfterTheDialogueEnded_CountsNothing()
        {
            Interact("INT_CH01_001_GREETER_MONKEY", "interact.dialogue", "DLG_CH01_002");
            while (_dialogue.IsActive)
            {
                _dialogue.Advance();
            }

            _bus.Publish(
                ExplorationEventChannel.Channel,
                new MapEnteredEvent("CH01_MAP02", GridPosition.Origin, 60, 40, null));

            Assert.AreEqual(0, _link.Interrupted, "念完自己收场的那些不算被换图打断。");
        }

        [Test]
        public void WithoutADialogueService_TheLinkStaysQuietInsteadOfThrowing()
        {
            using var link = new ExplorationDialogueLink(_bus, null);

            Assert.DoesNotThrow(() => Interact("INT_CH01_001_GREETER_MONKEY", "interact.dialogue", "DLG_CH01_002"));
            Assert.AreEqual(0, link.Started);
        }

        [Test]
        public void Constructor_WithoutABus_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ExplorationDialogueLink(null, _dialogue));
        }

        [Test]
        public void Dispose_IsIdempotent_AndStopsListening()
        {
            _link.Dispose();
            _link.Dispose();

            Interact("INT_CH01_001_GREETER_MONKEY", "interact.dialogue", "DLG_CH01_002");

            Assert.AreEqual(0, _link.Started, "退订之后这条线不该再听到任何交互。");
            Assert.IsFalse(_dialogue.IsActive);
        }
    }
}
