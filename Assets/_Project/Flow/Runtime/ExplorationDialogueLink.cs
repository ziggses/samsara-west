using System;
using SamsaraWest.Core;
using SamsaraWest.Exploration;
using SamsaraWest.Narrative;

namespace SamsaraWest.Flow
{
    /// <summary>
    /// 「在探索里按了一次交互 → 开一段对白」的接线。
    /// </summary>
    /// <remarks>
    /// 为什么要有这一层：探索只发一条 <c>InteractionTriggeredEvent</c>（「有人在 20,8 上按了 E，
    /// 目标是 DLG_CH01_002」），它<b>不许</b>认识 Narrative；推进器只会推进它已经开着的那段对白，
    /// 它<b>不许</b>认识探索。两端都由组合根连：Flow 是唯一同时看得见两边的层，
    /// 与 <c>ExplorationBattleLink</c>、<c>NarrativeStateLink</c> 是同一种安排。
    ///
    /// <b>判据是数据而不是类型键</b>：拿交互物的 <c>targetId</c> 去对话表里问一句「这是不是一条对话」，
    /// 是就开。<c>targetId</c> 指向地图（门）或掉落表（箱子）时问不出来，于是不必维护
    /// 「哪些交互算对白」的名单——加一种说话方式不用改内核。这与换图那条链路的判据同一口味。
    /// 但<b>类型键要筛一道</b>：门与箱子也会发交互事件，不加筛子就会为每次开箱记一条
    /// 「找不到对话定义 LUT_ENM_BANDIT」的警告，把真问题淹掉。
    ///
    /// <b>对白开着的时候还能不能走</b>：不能——挡在界面层（对白面板吃掉方向键与交互键），
    /// 所以「走到门口换图」这条路上，对白一定已经收场了。
    ///
    /// 但<b>换图本身仍然会发生</b>：诊断入口的 F4 在界面里排在模态判定之前（不然就对着一张对白按不动 F4），
    /// 于是可以「对白开着一半直接切图」。推进器不会自己知道这件事——它只认账本与条件。
    /// 所以这里盯一张图进过：<c>MapEnteredEvent</c> 一到就把开着的对白收掉。
    /// 收场用 <c>Close()</c> 而不是「读到底」：玩家没读完，不该按读完记账。
    /// </remarks>
    public sealed class ExplorationDialogueLink : IDisposable
    {
        /// <summary>会开出对白的交互类型键。其余类型（门、箱子）不往对话表上问。</summary>
        private static readonly string[] DialogueInteractionKeys =
        {
            "interact.dialogue",
            "interact.examine",
        };

        private readonly IDialogueService _dialogue;
        private readonly SubscriptionBag _subscriptions = new SubscriptionBag();
        private bool _disposed;

        /// <param name="bus">事件总线。</param>
        /// <param name="dialogue">剧情节点推进器；空表示这条接线不接对白（交互照旧只发事件）。</param>
        public ExplorationDialogueLink(IEventBus bus, IDialogueService dialogue)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _dialogue = dialogue;
            if (_dialogue == null)
            {
                GameLog.Warn(
                    LogChannel.Flow,
                    "交互对白接线没有拿到推进器，按 E 只会发出交互事件，不会开出对白。");
            }

            _subscriptions.Add(bus.Subscribe<InteractionTriggeredEvent>(
                ExplorationEventChannel.Channel,
                OnInteractionTriggered));

            _subscriptions.Add(bus.Subscribe<MapEnteredEvent>(
                ExplorationEventChannel.Channel,
                OnMapEntered));
        }

        /// <summary>开起来过几段对白。诊断与测试用。</summary>
        public int Started { get; private set; }

        /// <summary>因为换图而被收掉的对白段数。诊断与测试用。</summary>
        public int Interrupted { get; private set; }

        /// <summary>最近一次被拒的理由（成功时为 <see cref="DialogueStartRejection.None"/>）。</summary>
        public DialogueStartRejection LastRejection { get; private set; } = DialogueStartRejection.None;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscriptions.Dispose();
        }

        private void OnInteractionTriggered(InteractionTriggeredEvent gameEvent)
        {
            if (_dialogue == null || !IsDialogueInteraction(gameEvent.InteractionTypeKey))
            {
                return;
            }

            var result = _dialogue.TryStart(gameEvent.TargetId);
            LastRejection = result.Rejection;

            if (result.Started)
            {
                Started++;
                return;
            }

            GameLog.Warn(
                LogChannel.Flow,
                $"交互物 {gameEvent.InteractableId} 想开对话 '{gameEvent.TargetId}'，但没开成（{result.Rejection}）。",
                gameEvent.InteractableId);
        }

        private void OnMapEntered(MapEnteredEvent gameEvent)
        {
            if (_dialogue == null || !_dialogue.IsActive)
            {
                return;
            }

            // 没读完就中断，所以走 Close()：它不结算写入、也不沿后继续跑。
            // 「结算」代表「这段播过了」，玩家切图切走了的那半段不该拿到这份认可。
            if (_dialogue.Close())
            {
                Interrupted++;
            }
        }

        private static bool IsDialogueInteraction(string interactionTypeKey)
        {
            if (string.IsNullOrEmpty(interactionTypeKey))
            {
                return false;
            }

            for (var i = 0; i < DialogueInteractionKeys.Length; i++)
            {
                if (string.Equals(interactionTypeKey, DialogueInteractionKeys[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
