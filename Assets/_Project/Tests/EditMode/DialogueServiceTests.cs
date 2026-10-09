using System.Collections.Generic;
using NUnit.Framework;
using SamsaraWest.Core;
using SamsaraWest.Data;
using SamsaraWest.Narrative;
using UnityEngine;

namespace SamsaraWest.Tests.EditMode
{
    /// <summary>
    /// 剧情节点推进器（ADR-029）。锁三件事：
    /// <list type="number">
    /// <item>「往后走」是<b>数据说了算</b>——后继按条件挑，挑不到就收场，不是每段对白都硬接下一段；</item>
    /// <item>账本写入只发生在<b>节点读完</b>的时候，重复搭话按条件自己把自己关掉；</item>
    /// <item>被拒时带理由回来，而不是抛异常（玩家对着空气按 E 不该是崩溃）。</item>
    /// </list>
    /// 断言一律比对<b>文本键</b>（<c>dlg.ch01.002.line.1</c>）而不是汉字：推进器不产文本，
    /// 拿汉字来测等于顺带把本地化表也锁进了这一层。
    /// </summary>
    public sealed class DialogueServiceTests
    {
        private const string Header =
            "id,displayNameKey,descriptionKey,tags,version,chapterIndex,inkKnotName,speakerCharacterId,lineCount," +
            "nextNodeIds,requiredStateKey,requiredOperator,requiredValue,setsStateKeys,karmaChannel,karmaDelta,bgmKey,portraitKey,isTerminal";

        private readonly List<Object> _created = new List<Object>();

        private ServiceRegistry _registry;
        private EventBus _bus;
        private StoryState _state;
        private IDialogueService _dialogue;

        [TearDown]
        public void TearDown()
        {
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
            _state = null;
            _dialogue = null;
        }

        /// <summary>按表头顺序拼一行，避免手写逗号串时错位。</summary>
        private static string Row(
            string id,
            string displayNameKey,
            string chapterIndex = "1",
            string inkKnotName = "",
            string lineCount = "0",
            string nextNodeIds = "",
            string requiredStateKey = "",
            string requiredOperator = "Equal",
            string requiredValue = "0",
            string setsStateKeys = "",
            string karmaChannel = "",
            string karmaDelta = "0",
            string isTerminal = "false")
        {
            return string.Join(
                ",",
                id,
                displayNameKey,
                displayNameKey + ".desc",
                "ch01;dialogue",
                "1",
                chapterIndex,
                inkKnotName,
                string.Empty,
                lineCount,
                nextNodeIds,
                requiredStateKey,
                requiredOperator,
                requiredValue,
                setsStateKeys,
                karmaChannel,
                karmaDelta,
                "music.ch01.field",
                string.Empty,
                isTerminal);
        }

        private DialogueDefinition Define(string row)
        {
            var report = new ValidationReport();
            var table = CsvParser.Parse(Header + "\n" + row + "\n", "dialogues.csv");
            var definition = (DialogueDefinition)CsvDefinitionMapper.Map(
                typeof(DialogueDefinition),
                table.Rows[0],
                table,
                report);

            Assert.AreEqual(0, report.ErrorCount, $"造出来的对话定义不合法，用例数据本身要修：{report}");
            _created.Add(definition);
            return definition;
        }

        /// <summary>装一遍服务，走的是组合根那条路（账本先于推进器注册）。</summary>
        private void Install(params DialogueDefinition[] nodes)
        {
            var catalog = ScriptableObject.CreateInstance<DefinitionCatalog>();
            _created.Add(catalog);
            catalog.SetDefinitions(nodes);

            var report = new ValidationReport();
            catalog.Rebuild(report);

            _registry = new ServiceRegistry();
            _registry.Register<IEventBus>(_bus = new EventBus());
            NarrativeModule.Install(_registry, new DefinitionRegistry(catalog));

            _state = (StoryState)_registry.Resolve<IStoryState>();
            _dialogue = _registry.Resolve<IDialogueService>();
        }

        // ── 首章前山那几个节点，与 dialogues.csv 同形 ────────────────────────────────

        /// <summary>迎客猴的招呼：7 行，读完接「问伤口」。</summary>
        private static string GreeterRow => Row(
            id: "DLG_CH01_002",
            displayNameKey: "dlg.ch01.002.name",
            inkKnotName: "CH01_N02_GREETER_TALK",
            lineCount: "7",
            nextNodeIds: "CH01_N03_ASK_THE_WOUND",
            setsStateKeys: "flag.ch01.greeted_monkey");

        /// <summary>问伤口：2 行，只在「还没问过」时进得来，问过就置位，且加一点真相。</summary>
        private static string WoundRow => Row(
            id: "DLG_CH01_003",
            displayNameKey: "dlg.ch01.003.name",
            inkKnotName: "CH01_N03_ASK_THE_WOUND",
            lineCount: "2",
            requiredStateKey: "flag.ch01.monkey_questioned",
            requiredOperator: "Equal",
            requiredValue: "0",
            setsStateKeys: "flag.ch01.monkey_questioned",
            karmaChannel: "karma.truth",
            karmaDelta: "1",
            isTerminal: "true");

        private static string PeachRow => Row(
            id: "DLG_CH01_001",
            displayNameKey: "dlg.ch01.001.name",
            inkKnotName: "CH01_N01_MONKEY_PEACH",
            lineCount: "3",
            setsStateKeys: "flag.ch01.repeating_action",
            isTerminal: "true");

        private static string GateRow => Row(
            id: "DLG_CH01_007",
            displayNameKey: "dlg.ch01.007.name",
            inkKnotName: "CH01_N07_FRONT_GATE",
            lineCount: "0",
            isTerminal: "true");

        private void InstallChapterOneFrontHill() =>
            Install(
                Define(PeachRow),
                Define(GreeterRow),
                Define(WoundRow),
                Define(GateRow));

        // ── 开始 ────────────────────────────────────────────────────────────────

        [Test]
        public void Start_ReadsTheFirstLineItselfInsteadOfWaitingForAKeyPress()
        {
            InstallChapterOneFrontHill();

            var result = _dialogue.TryStart("DLG_CH01_002");

            Assert.IsTrue(result.Started);
            Assert.AreEqual(DialogueStartRejection.None, result.Rejection);
            Assert.AreEqual("CH01_N02_GREETER_TALK", result.KnotName);
            Assert.AreEqual("DLG_CH01_002", _dialogue.DialogueId);
            Assert.AreEqual("dlg.ch01.002.name", _dialogue.NodeDisplayNameKey);
            Assert.AreEqual(1, _dialogue.LineNumber, "行号从 1 数起：界面直接拿它显示「第 n/N 行」。");
            Assert.AreEqual(7, _dialogue.LineCount);
            Assert.AreEqual("dlg.ch01.002.line.1", _dialogue.LineKey);
            Assert.AreEqual("dlg.ch01.002.line.1.who", _dialogue.SpeakerKey);
            Assert.AreEqual(1, _dialogue.LinesPlayed, "第一行是开始那一刻就播掉的，否则界面会先空一屏。");
        }

        [Test]
        public void Start_UnknownId_IsRejectedWithAReason()
        {
            InstallChapterOneFrontHill();

            var result = _dialogue.TryStart("DLG_CH99_999");

            Assert.IsFalse(result.Started);
            Assert.AreEqual(DialogueStartRejection.UnknownDialogue, result.Rejection);
            Assert.IsFalse(_dialogue.IsActive);
            Assert.IsNull(_dialogue.LineKey);
        }

        [Test]
        public void Start_NonDialogueTarget_IsRejected()
        {
            InstallChapterOneFrontHill();

            // 一扇门的 targetId 是一张地图。这样「这是不是一条对话」只由数据回答，
            // 接线那边不必维护一份「哪些交互算对白」的名单。
            var result = _dialogue.TryStart("CH01_MAP02");

            Assert.IsFalse(result.Started);
            Assert.AreEqual(DialogueStartRejection.UnknownDialogue, result.Rejection);
        }

        [Test]
        public void Start_EmptyId_IsRejected()
        {
            InstallChapterOneFrontHill();

            Assert.AreEqual(DialogueStartRejection.UnknownDialogue, _dialogue.TryStart(null).Rejection);
            Assert.AreEqual(DialogueStartRejection.UnknownDialogue, _dialogue.TryStart("  ").Rejection);
        }

        [Test]
        public void Start_WhileAnotherIsOpen_IsRejected()
        {
            InstallChapterOneFrontHill();
            _dialogue.TryStart("DLG_CH01_002");

            var result = _dialogue.TryStart("DLG_CH01_001");

            Assert.IsFalse(result.Started);
            Assert.AreEqual(
                DialogueStartRejection.AlreadyActive,
                result.Rejection,
                "同屏不叠两段对白——叠了界面也不知道该画哪一段。");
            Assert.AreEqual("DLG_CH01_002", _dialogue.DialogueId, "被拒的那一次不许把开着的这段换掉。");
        }

        [Test]
        public void Start_RequirementNotMet_IsRejectedInsteadOfPlaying()
        {
            InstallChapterOneFrontHill();
            _state.SetValue("flag.ch01.monkey_questioned", 1);

            var result = _dialogue.TryStart("DLG_CH01_003");

            Assert.IsFalse(result.Started);
            Assert.AreEqual(DialogueStartRejection.RequirementNotMet, result.Rejection);
            Assert.IsFalse(_dialogue.IsActive);
        }

        [Test]
        public void Start_NodeWithoutLines_EndsOnTheSpot()
        {
            InstallChapterOneFrontHill();

            var ended = default(DialogueEndedEvent);
            var count = 0;
            using var subscription = _bus.Subscribe<DialogueEndedEvent>(
                NarrativeEventChannel.Channel,
                e =>
                {
                    ended = e;
                    count++;
                });

            var result = _dialogue.TryStart("DLG_CH01_007");

            Assert.IsTrue(result.Started, "请求本身是成立的，只是这个节点没有文本。");
            Assert.IsFalse(
                _dialogue.IsActive,
                "没有文本的节点（山门出口那一类）进来就收场，界面不该停在一条空行上等玩家按键。");
            Assert.AreEqual(1, count);
            Assert.AreEqual("DLG_CH01_007", ended.DialogueId);
            Assert.AreEqual(0, ended.LinesPlayed);
            Assert.IsTrue(ended.Completed);
        }

        // ── 推进 ────────────────────────────────────────────────────────────────

        [Test]
        public void Advance_WalksTheLinesAndReportsTheLineKeys()
        {
            InstallChapterOneFrontHill();
            _dialogue.TryStart("DLG_CH01_002");

            for (var line = 2; line <= 6; line++)
            {
                var step = _dialogue.Advance();

                Assert.IsTrue(step.Advanced);
                Assert.IsFalse(step.ChangedNode, "同一个节点内的下一行不算翻节点。");
                Assert.IsFalse(step.Ended);
                Assert.AreEqual($"dlg.ch01.002.line.{line}", _dialogue.LineKey);
                Assert.AreEqual($"dlg.ch01.002.line.{line}.who", _dialogue.SpeakerKey);
                Assert.AreEqual(line, _dialogue.LineNumber);
            }
        }

        [Test]
        public void Advance_AfterTheLastLine_ChainsIntoTheConditionedSuccessor()
        {
            InstallChapterOneFrontHill();
            _dialogue.TryStart("DLG_CH01_002");
            for (var i = 0; i < 6; i++)
            {
                _dialogue.Advance();
            }

            var step = _dialogue.Advance();

            Assert.IsTrue(step.ChangedNode, "迎客猴讲完接着问伤口，界面据此重画标题与行数。");
            Assert.IsFalse(step.Ended);
            Assert.AreEqual("CH01_N03_ASK_THE_WOUND", step.KnotName);
            Assert.AreEqual("dlg.ch01.003.line.1", _dialogue.LineKey);
            Assert.AreEqual(2, _dialogue.LineCount, "总行数必须跟着新节点走。");
            Assert.AreEqual(8, _dialogue.LinesPlayed, "本次会话跨节点累计行数。");
        }

        [Test]
        public void Advance_LastLineOfANode_WritesItsLedgerKeys()
        {
            InstallChapterOneFrontHill();
            _dialogue.TryStart("DLG_CH01_002");
            Assert.AreEqual(0, _state.GetValue("flag.ch01.greeted_monkey"), "没读完之前不许记账。");

            for (var i = 0; i < 6; i++)
            {
                _dialogue.Advance();
            }

            _dialogue.Advance();

            Assert.AreEqual(
                1,
                _state.GetValue("flag.ch01.greeted_monkey"),
                "节点读完才结算写入——写入代表「这段播过了」，不该在第一行就发生。");
        }

        [Test]
        public void Advance_TerminalNode_EndsInsteadOfFollowingItsSuccessors()
        {
            // 终局节点 + 一个明明能满足的后继：终局说了算。
            // 这正是「环境调查」要的形状——断鼓就是断鼓，不该顺势再播一段血迹的旁白。
            Install(
                Define(Row(
                    id: "DLG_CH01_004",
                    displayNameKey: "dlg.ch01.004.name",
                    inkKnotName: "CH01_N04_BROKEN_DRUM",
                    lineCount: "1",
                    nextNodeIds: "CH01_N02_GREETER_TALK",
                    isTerminal: "true")),
                Define(GreeterRow));

            Assert.IsTrue(_dialogue.TryStart("DLG_CH01_004").Started);

            var step = _dialogue.Advance();

            Assert.IsTrue(step.Ended);
            Assert.IsFalse(step.ChangedNode);
            Assert.AreEqual("CH01_N04_BROKEN_DRUM", step.KnotName, "收场时报的是这一段，不是被跳过的后继。");
            Assert.IsFalse(_dialogue.IsActive);
            Assert.IsNull(_dialogue.DialogueId, "被跳过的后继不许偷偷开起来。");
        }

        [Test]
        public void Advance_TerminalNodeStillWritesItsOwnLedgerKeys()
        {
            Install(
                Define(PeachRow),
                Define(GreeterRow));
            _dialogue.TryStart("DLG_CH01_001");

            _dialogue.Advance();
            _dialogue.Advance();
            var step = _dialogue.Advance();

            Assert.IsTrue(step.Ended);
            Assert.AreEqual(
                1,
                _state.GetValue("flag.ch01.repeating_action"),
                "就地收场不等于不结算：这一段确实是读完了的。");
        }

        [Test]
        public void Advance_WithoutAnOpenDialogue_ReportsInactiveAndDoesNothing()
        {
            InstallChapterOneFrontHill();

            var step = _dialogue.Advance();

            Assert.IsFalse(step.Advanced);
            Assert.IsFalse(step.Ended);
            Assert.IsNull(step.KnotName);
            Assert.AreEqual(0, _dialogue.LinesPlayed);
        }

        [Test]
        public void Advance_AssignsKarmaThroughTheChannel()
        {
            InstallChapterOneFrontHill();
            _dialogue.TryStart("DLG_CH01_003");
            _dialogue.Advance();

            Assert.AreEqual(0, _state.GetKarma(KarmaAxis.Truth), "没读完之前不许记心念。");

            var step = _dialogue.Advance();

            Assert.IsTrue(step.Ended);
            Assert.AreEqual(1, _state.GetKarma(KarmaAxis.Truth));
            Assert.AreEqual(1, _state.GetValue("flag.ch01.monkey_questioned"));
        }

        [Test]
        public void Advance_SecondVisit_SkipsTheQuestionBranch()
        {
            InstallChapterOneFrontHill();

            // 第一次搭话：招呼（7 行）→ 问伤口（2 行）。
            _dialogue.TryStart("DLG_CH01_002");
            for (var i = 0; i < 9; i++)
            {
                _dialogue.Advance();
            }

            Assert.IsFalse(_dialogue.IsActive, "第一次应当把两段都读完。");
            Assert.AreEqual(1, _state.GetKarma(KarmaAxis.Truth));

            // 第二次搭话：条件不再满足，同一个问题不该再问一遍。
            Assert.IsTrue(_dialogue.TryStart("DLG_CH01_002").Started);
            DialogueAdvanceResult step = default;
            for (var i = 0; i < 7; i++)
            {
                step = _dialogue.Advance();
            }

            Assert.IsTrue(step.Ended, "后继条件都不满足时就到此为止，而不是硬接一段。");
            Assert.AreEqual("CH01_N02_GREETER_TALK", step.KnotName, "收场时报的是最后读到的那一段。");
            Assert.IsFalse(_dialogue.IsActive);
            Assert.AreEqual(
                1,
                _state.GetKarma(KarmaAxis.Truth),
                "重复搭话不该再赚一点真相值——「问过一次就不再问」靠的就是这里。");
        }

        // ── 收场 ────────────────────────────────────────────────────────────────

        [Test]
        public void Close_EndsTheSessionWithoutWritesAndWithoutChaining()
        {
            InstallChapterOneFrontHill();
            _dialogue.TryStart("DLG_CH01_002");
            var ended = default(DialogueEndedEvent);
            using var subscription = _bus.Subscribe<DialogueEndedEvent>(
                NarrativeEventChannel.Channel,
                e => ended = e);

            Assert.IsTrue(_dialogue.Close());

            Assert.IsFalse(_dialogue.IsActive, "「强行收场」要真的收场：换图之后对白不该还挂着。");
            Assert.IsFalse(ended.Completed, "中断不是读完——存档与日志都得能分清这两种收场。");
            Assert.AreEqual(1, ended.LinesPlayed);
            Assert.AreEqual(
                0,
                _state.GetValue("flag.ch01.greeted_monkey"),
                "话没说完就不该记这一次的账。");
        }

        [Test]
        public void Close_WithoutAnOpenDialogue_IsFalse()
        {
            InstallChapterOneFrontHill();

            Assert.IsFalse(_dialogue.Close());
        }

        [Test]
        public void Close_ThenStartAgain_IsAllowed()
        {
            InstallChapterOneFrontHill();
            _dialogue.TryStart("DLG_CH01_002");
            _dialogue.Close();

            var result = _dialogue.TryStart("DLG_CH01_002");

            Assert.IsTrue(result.Started, "收场必须把会话清干净，否则「再搭一次话」会被自己挡在 AlreadyActive 上。");
            Assert.AreEqual(1, _dialogue.LineNumber, "重新开始要回到第一行。");
        }

        // ── 事件 ────────────────────────────────────────────────────────────────

        [Test]
        public void Events_DistinguishStartFromLineChange()
        {
            InstallChapterOneFrontHill();

            var starts = new List<DialogueStartedEvent>();
            var lines = new List<DialogueLineChangedEvent>();
            using var a = _bus.Subscribe<DialogueStartedEvent>(NarrativeEventChannel.Channel, starts.Add);
            using var b = _bus.Subscribe<DialogueLineChangedEvent>(NarrativeEventChannel.Channel, lines.Add);

            _dialogue.TryStart("DLG_CH01_002");

            Assert.AreEqual(1, starts.Count);
            Assert.AreEqual(1, lines.Count, "开始那一刻只报一行；合成一条事件的话首行会被画两次。");
            Assert.AreEqual("DLG_CH01_002", starts[0].DialogueId);
            Assert.AreEqual(7, starts[0].LineCount);
            Assert.AreEqual(0, lines[0].LineIndex, "行号在事件里从 0 数起，界面显示时加一。");
            Assert.AreEqual("dlg.ch01.002.line.1", lines[0].LineKey);
            Assert.AreEqual("dlg.ch01.002.line.1.who", lines[0].SpeakerKey);
        }

        // ── 文本键的形状 ─────────────────────────────────────────────────────────

        [Test]
        public void KeyHelpers_DeriveLineKeysFromTheDisplayNameKey()
        {
            var node = Define(GreeterRow);

            Assert.AreEqual("dlg.ch01.002", DialogueService.NodeKeyStem(node));
            Assert.AreEqual("dlg.ch01.002.line.4", DialogueService.LineKeyOf(node, 4));
            Assert.AreEqual("dlg.ch01.002.line.4.who", DialogueService.SpeakerKeyOf(node, 4));
        }

        [Test]
        public void KeyHelpers_UnnamedDisplayKey_StayEmptyInsteadOfGuessingAKey()
        {
            // 键认得出来才拼：拼不出就交空串，让本地化服务把「缺的是哪个键」报出来，
            // 而不是悄悄画出一片空白。
            Assert.AreEqual(string.Empty, DialogueService.NodeKeyStem(null));
            Assert.AreEqual(string.Empty, DialogueService.LineKeyOf(null, 1));
            Assert.AreEqual(string.Empty, DialogueService.SpeakerKeyOf(null, 1));
        }

        [Test]
        public void KeyHelpers_DisplayKeyWithoutNameSuffix_IsUsedAsIs()
        {
            var node = Define(Row(
                id: "DLG_CH01_004",
                displayNameKey: "dlg.ch01.004",
                inkKnotName: "CH01_N04_BROKEN_DRUM",
                lineCount: "1",
                isTerminal: "true"));

            Assert.AreEqual("dlg.ch01.004", DialogueService.NodeKeyStem(node));
            Assert.AreEqual("dlg.ch01.004.line.1", DialogueService.LineKeyOf(node, 1));
        }

        // ── 缺件 ────────────────────────────────────────────────────────────────

        [Test]
        public void Install_WithoutDefinitionCatalog_InstallsOnlyTheLedger()
        {
            _registry = new ServiceRegistry();
            _registry.Register<IEventBus>(_bus = new EventBus());
            NarrativeModule.Install(_registry);

            Assert.IsNotNull(_registry.Resolve<IStoryState>());
            Assert.IsFalse(
                _registry.TryResolve<IDialogueService>(out _),
                "没给定义目录就只装账本，而不是装一个读不到任何节点的空壳推进器。");
        }

        [Test]
        public void DuplicateKnotName_KeepsTheFirstForSuccessorResolution()
        {
            // 两个对话抢同一个剧本节点名。选哪一个不只是「谁赢」的问题：
            // 后继是按节点名挑的，挑错了就是「接在招呼后面的那段话，悄悄换成了另一段」。
            Install(
                Define(PeachRow),
                Define(GreeterRow),
                Define(WoundRow),
                Define(Row(
                    id: "DLG_CH01_099",
                    displayNameKey: "dlg.ch01.099.name",
                    inkKnotName: "CH01_N03_ASK_THE_WOUND",
                    lineCount: "1",
                    isTerminal: "true")));

            _dialogue.TryStart("DLG_CH01_002");
            for (var i = 0; i < 7; i++)
            {
                _dialogue.Advance();
            }

            Assert.AreEqual(
                "DLG_CH01_003",
                _dialogue.DialogueId,
                "重名的后来者被忽略：接在招呼后面的仍是先登记的那一条。");
            Assert.AreEqual(2, _dialogue.LineCount);

            // 但「按 ID 直接开」是另一件事，走的不是节点名索引：交互物拿的就是 DLG ID。
            Assert.IsTrue(_dialogue.Close());
            Assert.IsTrue(
                _dialogue.TryStart("DLG_CH01_099").Started,
                "被忽略的是它的节点名，不是它自己——两套索引各管各的。");
        }
    }
}
