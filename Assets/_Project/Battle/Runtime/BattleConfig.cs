using SamsaraWest.Data;
using UnityEngine;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 战斗数值配置（v1）。任务书只写了五行关系、没给任何数字，
    /// 这里给出一版有依据的口径并全部外置为资产，调平衡不需要改代码、不需要重编译。
    ///
    /// 校算依据（详见 Docs/战斗数值-v1.md）：
    /// 队伍每回合按最低配打法（初始技能表里第一个伤害技能、不换招、不吃灵力）对集火目标输出
    /// 约 80–95 点，破防窗口内约提升到 3 倍，于是第一章常规遭遇 6 回合、隐藏遭遇 4 回合、Boss 13 回合。
    /// 破防额外伤害取最大生命的 2%：这个比例按<b>段数</b>结算，4 段命中一回合就能吃掉
    /// 8% 最大生命；早期取 5% 时一回合能吃掉 Boss 六成血，Boss 阶段会被整段跳过，
    /// 回合数也不再随血量变化（详见 Docs/战斗数值-v1.md 第 7 节）。
    /// 五行含相生（守方生攻方 1.15、攻方生守方 0.85），暴击是独立的随机项、掷骰由战斗流程传入。
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

        [Tooltip("守方生攻方（金生水，攻方「借势」）的伤害倍率。")]
        [SerializeField] private float _generationUpMultiplier = 1.15f;

        [Tooltip("攻方生守方（火生土，攻方「资敌」）的伤害倍率。")]
        [SerializeField] private float _generationDownMultiplier = 0.85f;

        [Tooltip("同属性对轰的伤害倍率。")]
        [SerializeField] private float _sameElementMultiplier = 1.0f;

        [Tooltip("任一方为无属性时的伤害倍率。")]
        [SerializeField] private float _neutralMultiplier = 1.0f;

        [Header("暴击")]
        [Tooltip("暴击概率。五行克制是稳定的倍率、不是暴击：两者独立，克制不会顺带变成暴击。")]
        [SerializeField] [Range(0f, 1f)] private float _criticalChance = 0.1f;

        [Tooltip("暴击倍率，结算在整条公式的最后一道独立乘区。")]
        [SerializeField] [Range(1f, 3f)] private float _criticalMultiplier = 1.5f;

        [Header("破防")]
        [Tooltip("防御方处于破防状态时的承伤倍率。")]
        [SerializeField] private float _brokenIncomingMultiplier = 1.5f;

        [Tooltip("破防持续时间（回合）。")]
        [SerializeField] private int _brokenDurationTurns = 2;

        [Tooltip("破防造成等于该比例最大生命值的额外伤害，给「攒破防」一个明确回报。")]
        [SerializeField] [Range(0f, 0.3f)] private float _brokenBonusHealthRatio = 0.02f;

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

        public float GenerationUpMultiplier => _generationUpMultiplier;

        public float GenerationDownMultiplier => _generationDownMultiplier;

        public float SameElementMultiplier => _sameElementMultiplier;

        public float NeutralMultiplier => _neutralMultiplier;

        public float CriticalChance => _criticalChance;

        public float CriticalMultiplier => _criticalMultiplier;

        public float BrokenIncomingMultiplier => _brokenIncomingMultiplier;

        public int BrokenDurationTurns => _brokenDurationTurns;

        public float BrokenBonusHealthRatio => _brokenBonusHealthRatio;

        public float MultiHitDecay => _multiHitDecay;

        public int ActionValueBase => _actionValueBase;

        public int MinSpeed => _minSpeed;

        public int MaxSpeed => _maxSpeed;

        public int IntentPreviewLead => _intentPreviewLead;

        /// <summary>
        /// 五行倍率。这是数值表里唯一「规则性」的一段，其余都是可调参数。
        /// 相克与相生共用这一处判定：相克环与相生环把 25 种配对分完，
        /// 所以不存在「两两相安无事」的配对（详见 <see cref="ElementRules.Relate"/>）。
        /// </summary>
        public float GetElementMultiplier(FiveElement attacker, FiveElement defender)
        {
            switch (ElementRules.Relate(attacker, defender))
            {
                case ElementRelation.Restraining:
                    return _restrainMultiplier;
                case ElementRelation.GeneratedBy:
                    return _generationUpMultiplier;
                case ElementRelation.Generates:
                    return _generationDownMultiplier;
                case ElementRelation.Restrained:
                    return _restrainedMultiplier;
                case ElementRelation.Same:
                    return _sameElementMultiplier;
                default:
                    return _neutralMultiplier;
            }
        }

        /// <summary>
        /// 这个 0–1 的随机数是否触发暴击。取「小于」而不是「小于等于」，
        /// 于是 <c>CriticalChance = 0</c> 时传入 0 也必定不暴击——默认值必须是确定性的。
        /// </summary>
        public bool IsCriticalRoll(float roll) => roll < _criticalChance;

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
