using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 战斗数值配置（v1）。任务书只写了五行关系、没给任何数字，
    /// 这里给出一版有依据的口径并全部外置为资产，调平衡不需要改代码、不需要重编译。
    ///
    /// 校算依据（详见 Docs/战斗数值-v1.md）：
    /// 单体普攻对同阶敌人约 3 次命中打空一条血；破防窗口内输出提升约 1.5 倍，
    /// 因此一场常规战斗期望 4–6 回合，Boss 战 10–15 回合。
    /// </summary>
    [CreateAssetMenu(fileName = "BattleConfig", menuName = "SamsaraWest/战斗数值配置 BattleConfig")]
    public sealed class BattleConfig : ScriptableObject
    {
        [Header("伤害公式：raw = power + attack * AttackScale - defense * DefenseScale")]
        [SerializeField] [Range(0f, 3f)] private float _attackScale = 1.0f;
        [SerializeField] [Range(0f, 3f)] private float _defenseScale = 0.5f;

        [Tooltip("伤害下限，保证再高的防御也不会出现 0 伤害。")]
        [SerializeField] private int _minimumDamage = 1;

        [Header("五行倍率")]
        [Tooltip("克制关系（金克木）的伤害倍率。")]
        [SerializeField] private float _restrainMultiplier = 1.5f;

        [Tooltip("被克制时的伤害倍率。")]
        [SerializeField] private float _restrainedMultiplier = 0.75f;

        [Tooltip("同属性对轰的伤害倍率。")]
        [SerializeField] private float _sameElementMultiplier = 1.0f;

        [Tooltip("任一方为无属性时的伤害倍率。")]
        [SerializeField] private float _neutralMultiplier = 1.0f;

        [Header("破防")]
        [Tooltip("防御方处于破防状态时的承伤倍率。")]
        [SerializeField] private float _brokenIncomingMultiplier = 1.5f;

        [Tooltip("破防持续时间（回合）。")]
        [SerializeField] private int _brokenDurationTurns = 2;

        [Tooltip("破防造成等于该比例最大生命值的额外伤害，给「攒破防」一个明确回报。")]
        [SerializeField] [Range(0f, 0.3f)] private float _brokenBonusHealthRatio = 0.05f;

        [Header("多段攻击")]
        [Tooltip("第 n 段的伤害衰减系数，第 1 段为 1。段数越多，总伤害越低但破防收益越高。")]
        [SerializeField] [Range(0.5f, 1f)] private float _multiHitDecay = 0.85f;

        [Header("行动顺序（ATB）：actionValue = ActionValueBase / speed")]
        [SerializeField] private int _actionValueBase = 10000;

        [Tooltip("速度上下限，防止极端数值把行动条拉爆。")]
        [SerializeField] private int _minSpeed = 1;
        [SerializeField] private int _maxSpeed = 999;

        [Tooltip("意图预告提前几个行动单位公布敌方下一步。0 表示不预告。")]
        [SerializeField] private int _intentPreviewLead = 1;

        public float AttackScale => _attackScale;

        public float DefenseScale => _defenseScale;

        public int MinimumDamage => _minimumDamage;

        public float RestrainMultiplier => _restrainMultiplier;

        public float RestrainedMultiplier => _restrainedMultiplier;

        public float SameElementMultiplier => _sameElementMultiplier;

        public float NeutralMultiplier => _neutralMultiplier;

        public float BrokenIncomingMultiplier => _brokenIncomingMultiplier;

        public int BrokenDurationTurns => _brokenDurationTurns;

        public float BrokenBonusHealthRatio => _brokenBonusHealthRatio;

        public float MultiHitDecay => _multiHitDecay;

        public int ActionValueBase => _actionValueBase;

        public int MinSpeed => _minSpeed;

        public int MaxSpeed => _maxSpeed;

        public int IntentPreviewLead => _intentPreviewLead;

        /// <summary>五行倍率。这是数值表里唯一「规则性」的一段，其余都是可调参数。</summary>
        public float GetElementMultiplier(FiveElement attacker, FiveElement defender)
        {
            if (attacker == FiveElement.None || defender == FiveElement.None)
            {
                return _neutralMultiplier;
            }

            if (attacker == defender)
            {
                return _sameElementMultiplier;
            }

            if (ElementRules.IsRestraining(attacker, defender))
            {
                return _restrainMultiplier;
            }

            if (ElementRules.IsRestraining(defender, attacker))
            {
                return _restrainedMultiplier;
            }

            return _neutralMultiplier;
        }

        /// <summary>把速度换算成行动值。值越小越先行动。</summary>
        public int GetActionValue(int speed)
        {
            var clamped = Mathf.Clamp(speed, _minSpeed, _maxSpeed);
            return Mathf.Max(1, Mathf.RoundToInt((float)_actionValueBase / clamped));
        }

        /// <summary>多段攻击第 <paramref name="hitIndex"/> 段（从 0 起）的衰减系数。</summary>
        public float GetHitDecay(int hitIndex) => Mathf.Pow(_multiHitDecay, Mathf.Max(0, hitIndex));

        /// <summary>
        /// 代码路径与测试使用的默认配置，数值与本资产的初始值保持一致。
        /// 编辑器中请优先使用资产，改这里不会影响已建好的资产。
        /// </summary>
        public static BattleConfig CreateDefault()
        {
            var config = CreateInstance<BattleConfig>();
            config.name = "BattleConfig_默认";
            return config;
        }
    }
}
