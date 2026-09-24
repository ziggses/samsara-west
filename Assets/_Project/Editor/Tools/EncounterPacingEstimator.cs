using System;
using System.Collections.Generic;
using SamsaraWest.Battle;
using SamsaraWest.Data;

namespace SamsaraWest.Editor
{
    /// <summary>单场遭遇的节奏校算结果（v1 数值表的口径见 Docs/战斗数值-v1.md）。</summary>
    public readonly struct EncounterPacing
    {
        public EncounterPacing(
            string encounterId,
            bool isBoss,
            bool isElite,
            int enemyCount,
            int totalHealth,
            int rounds,
            int breakWindows,
            int focusDamageFirstRound,
            int totalDamage,
            int brokenDamage,
            int maxEnemyHealth,
            int peakRoundDamage)
        {
            EncounterId = encounterId;
            IsBoss = isBoss;
            IsElite = isElite;
            EnemyCount = enemyCount;
            TotalHealth = totalHealth;
            Rounds = rounds;
            BreakWindows = breakWindows;
            FocusDamageFirstRound = focusDamageFirstRound;
            TotalDamage = totalDamage;
            BrokenDamage = brokenDamage;
            MaxEnemyHealth = maxEnemyHealth;
            PeakRoundDamage = peakRoundDamage;
        }

        public string EncounterId { get; }

        public bool IsBoss { get; }

        public bool IsElite { get; }

        public int EnemyCount { get; }

        public int TotalHealth { get; }

        /// <summary>把全员打空所需的回合数。</summary>
        public int Rounds { get; }

        /// <summary>整场触发了几次破防。</summary>
        public int BreakWindows { get; }

        /// <summary>第一回合对集火目标的输出，用作「一次普攻打掉多少血」的直观口径。</summary>
        public int FocusDamageFirstRound { get; }

        public int TotalDamage { get; }

        /// <summary>其中发生在破防窗口内的伤害。</summary>
        public int BrokenDamage { get; }

        /// <summary>
        /// 破防伤害占比：说明破防在总量里占多重。
        /// v1 口径下它高达 55–68%，意味着玩家真正在打的是护体条而不是血条；
        /// 抬护体后掉到 0–44%，因此它被 <c>EncounterPacingTests</c> 钉上了「不许过半」的上限。
        /// </summary>
        public float BrokenDamageShare => TotalDamage <= 0 ? 0f : (float)BrokenDamage / TotalDamage;

        /// <summary>本场最高的单个敌人最大生命，用来把爆发伤害换算成「几成血」。</summary>
        public int MaxEnemyHealth { get; }

        /// <summary>单个敌人在一个回合内吃到过的最大伤害——Boss 阶段会不会被一回合跨过去，看的就是它。</summary>
        public int PeakRoundDamage { get; }

        /// <summary>
        /// 单回合最大爆发占该敌人最大生命的比例。
        /// Boss 阶段阈值是 100% / 70% / 40% / 15%，最窄的一段只有 25 个百分点：
        /// 一旦这个比例超过 25%，就必然有阶段被一回合整段跳过。
        /// </summary>
        public float PeakRoundHealthShare => MaxEnemyHealth <= 0 ? 0f : (float)PeakRoundDamage / MaxEnemyHealth;

        public override string ToString() =>
            $"{EncounterId}：{EnemyCount} 敌、总血 {TotalHealth}、{Rounds} 回合、破防 {BreakWindows} 次、" +
            $"首回合集火 {FocusDamageFirstRound}、单回合峰值 {PeakRoundHealthShare:P0}、破防伤害占比 {BrokenDamageShare:P0}";
    }

    /// <summary>整章的校算结果：正常算出来的遭遇 + 算不出来的原因。</summary>
    public sealed class PacingReport
    {
        public PacingReport(int chapterIndex)
        {
            ChapterIndex = chapterIndex;
        }

        public int ChapterIndex { get; }

        public List<EncounterPacing> Encounters { get; } = new List<EncounterPacing>();

        /// <summary>算不出来的遭遇（缺敌人、缺技能、回合数不收敛）。空表示校算完整。</summary>
        public List<string> Problems { get; } = new List<string>();
    }

    /// <summary>
    /// 战斗节奏校算（任务书 11 项交付物里的「输出与回合数校算」）。
    /// 它不是战斗模拟器，而是一把<b>可执行的尺子</b>：只回答「按最低配打法，这场打几回合」，
    /// 让「常规 4–6 回合、Boss 10–15 回合」这条设计目标从口头约定变成会失败的断言。
    ///
    /// 模型（刻意保持简单，宁可低估玩家输出）：
    /// 1. 队伍 = 所有 isPlayable 的角色；每人每回合出手一次；
    /// 2. 每人固定使用<b>初始技能表里第一个能造成伤害的技能</b>——这是不换招、不用灵力、
    ///    不吃 CD 的最低配打法，实战只会更快，所以它给出的是回合数上界；
    /// 3. 单体技能集火「当前第一个存活敌人」，Column 打 2 个、Row 打 3 个、AllEnemies 打全部
    ///    （阵型尚未落到数据里，先按「从前排开始数」近似）；
    /// 4. 护体值按技能 breakDamage × 段数削减，归零后进入破防，破防持续期内承伤按
    ///    DamageCalculator 的公式结算；破防回合本身不吃增伤（攒满的那一回合仍是普通回合）；
    /// 5. 不含加减速、状态、逃跑、治疗与敌方行为——它衡量的是「打空血条要多久」；
    /// 6. <b>不掷暴击</b>：暴击是随机项，而这里要给的是回合数上界，所以一律按未暴击结算
    ///    （期望收益 = 暴击率 × (暴击倍率 − 1)，首章口径下约 +5%，只会让实战更快）。
    /// </summary>
    public static class EncounterPacingEstimator
    {
        /// <summary>回合数上限，防止数据写坏时死循环。</summary>
        public const int RoundBudget = 200;

        public static PacingReport EstimateChapter(BattleConfig config, DefinitionCatalog catalog, int chapterIndex)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var report = new PacingReport(chapterIndex);
            var attackers = BuildAttackers(catalog);
            if (attackers.Count == 0)
            {
                report.Problems.Add("数据目录里没有任何 isPlayable 的角色，无法校算队伍输出。");
                return report;
            }

            var encounters = new List<EncounterDefinition>();
            foreach (var encounter in catalog.OfKind<EncounterDefinition>())
            {
                if (encounter.ChapterIndex == chapterIndex)
                {
                    encounters.Add(encounter);
                }
            }

            encounters.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
            foreach (var encounter in encounters)
            {
                if (TryEstimate(config, catalog, encounter, attackers, out var pacing, out var problem))
                {
                    report.Encounters.Add(pacing);
                }
                else
                {
                    report.Problems.Add(problem);
                }
            }

            return report;
        }

        public static bool TryEstimate(
            BattleConfig config,
            DefinitionCatalog catalog,
            EncounterDefinition encounter,
            out EncounterPacing pacing,
            out string problem)
        {
            return TryEstimate(config, catalog, encounter, BuildAttackers(catalog), out pacing, out problem);
        }

        private static bool TryEstimate(
            BattleConfig config,
            DefinitionCatalog catalog,
            EncounterDefinition encounter,
            List<Attacker> attackers,
            out EncounterPacing pacing,
            out string problem)
        {
            pacing = default;
            problem = null;

            var enemies = new List<EnemyState>();
            foreach (var enemyId in encounter.EnemyIds)
            {
                if (!catalog.TryGet<EnemyDefinition>(enemyId, out var definition))
                {
                    problem = $"{encounter.Id} 引用了不存在的敌人 {enemyId}，校算中断。";
                    return false;
                }

                enemies.Add(new EnemyState(definition));
            }

            if (enemies.Count == 0)
            {
                problem = $"{encounter.Id} 没有配置敌人，校算中断。";
                return false;
            }

            var totalHealth = 0;
            var maxEnemyHealth = 0;
            foreach (var enemy in enemies)
            {
                totalHealth += enemy.MaxHealth;
                if (enemy.MaxHealth > maxEnemyHealth)
                {
                    maxEnemyHealth = enemy.MaxHealth;
                }
            }

            var targets = new List<int>(enemies.Count);
            var roundDamage = new int[enemies.Count];
            var rounds = 0;
            var breakWindows = 0;
            var focusDamageFirstRound = 0;
            var totalDamage = 0;
            var brokenDamage = 0;
            var peakRoundDamage = 0;
            var aliveCount = enemies.Count;
            var focusIndexOfFirstRound = -1;

            while (aliveCount > 0 && rounds < RoundBudget)
            {
                rounds++;
                Array.Clear(roundDamage, 0, roundDamage.Length);

                if (rounds == 1)
                {
                    focusIndexOfFirstRound = FirstAliveIndex(enemies);
                }

                for (var i = 0; i < enemies.Count; i++)
                {
                    enemies[i].WasBroken = enemies[i].BrokenTurns > 0;
                }

                foreach (var attacker in attackers)
                {
                    var skill = attacker.Skill;
                    if (skill == null)
                    {
                        continue;
                    }

                    ResolveTargets(skill.Target, enemies, targets);
                    foreach (var index in targets)
                    {
                        var enemy = enemies[index];
                        var isBrokenHit = enemy.BrokenTurns > 0;
                        var damage = DamageCalculator.ComputeSkillTotal(
                            config,
                            skill.Power,
                            attacker.Attack,
                            enemy.Defense,
                            skill.HitCount,
                            skill.Element,
                            enemy.Element,
                            isBrokenHit,
                            enemy.MaxHealth);

                        enemy.Health -= damage;
                        totalDamage += damage;
                        roundDamage[index] += damage;
                        if (isBrokenHit)
                        {
                            brokenDamage += damage;
                        }

                        if (rounds == 1 && index == focusIndexOfFirstRound)
                        {
                            focusDamageFirstRound += damage;
                        }

                        if (enemy.Health <= 0 && enemy.Alive)
                        {
                            enemy.Alive = false;
                            aliveCount--;
                        }
                    }
                }

                // 护体值结算排在伤害之后：攒满的那一回合自己不吃增伤。
                foreach (var attacker in attackers)
                {
                    var skill = attacker.Skill;
                    if (skill == null || skill.BreakDamage <= 0)
                    {
                        continue;
                    }

                    ResolveTargets(skill.Target, enemies, targets);
                    foreach (var index in targets)
                    {
                        var enemy = enemies[index];
                        if (enemy.BrokenTurns > 0)
                        {
                            continue;
                        }

                        enemy.BreakLeft -= DamageCalculator.ComputeBreakDamage(skill.BreakDamage, skill.HitCount, 1f);
                        if (enemy.BreakLeft <= 0)
                        {
                            enemy.BrokenTurns = config.BrokenDurationTurns;
                            breakWindows++;
                        }
                    }
                }

                foreach (var enemy in enemies)
                {
                    // 只有「本回合开始时已经破防」才消耗破防时长，
                    // 这样 duration=2 恰好吃到两个完整回合的增伤，不会被差一位吃掉一回合。
                    if (!enemy.WasBroken)
                    {
                        continue;
                    }

                    enemy.BrokenTurns--;
                    if (enemy.BrokenTurns <= 0)
                    {
                        enemy.BrokenTurns = 0;
                        enemy.BreakLeft = enemy.BreakThreshold;
                    }
                }

                for (var i = 0; i < roundDamage.Length; i++)
                {
                    if (roundDamage[i] > peakRoundDamage)
                    {
                        peakRoundDamage = roundDamage[i];
                    }
                }
            }

            if (aliveCount > 0)
            {
                problem = $"{encounter.Id} 在 {RoundBudget} 回合内没有打完，数值可能写坏（一击必杀或血量极高）。";
                return false;
            }

            pacing = new EncounterPacing(
                encounter.Id,
                encounter.IsBoss,
                encounter.IsElite,
                enemies.Count,
                totalHealth,
                rounds,
                breakWindows,
                focusDamageFirstRound,
                totalDamage,
                brokenDamage,
                maxEnemyHealth,
                peakRoundDamage);
            return true;
        }

        private static List<Attacker> BuildAttackers(DefinitionCatalog catalog)
        {
            var characters = new List<CharacterDefinition>();
            foreach (var character in catalog.OfKind<CharacterDefinition>())
            {
                if (character.IsPlayable)
                {
                    characters.Add(character);
                }
            }

            characters.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));

            var attackers = new List<Attacker>(characters.Count);
            foreach (var character in characters)
            {
                attackers.Add(new Attacker(character.Attack, FindBaselineSkill(catalog, character)));
            }

            return attackers;
        }

        /// <summary>取初始技能表里第一个能造成伤害的技能作为基准输出；纯辅助（如唐僧）返回 null，按零输出计。</summary>
        private static SkillDefinition FindBaselineSkill(DefinitionCatalog catalog, CharacterDefinition character)
        {
            foreach (var skillId in character.StartingSkillIds)
            {
                if (catalog.TryGet<SkillDefinition>(skillId, out var skill) && skill.Power > 0)
                {
                    return skill;
                }
            }

            return null;
        }

        private static int FirstAliveIndex(List<EnemyState> enemies)
        {
            for (var i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].Alive)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>把技能的目标规则翻译成敌人下标列表。阵型数据到位之前，一律「从前排开始数」。</summary>
        private static void ResolveTargets(TargetRule rule, List<EnemyState> enemies, List<int> buffer)
        {
            buffer.Clear();

            var limit = rule switch
            {
                TargetRule.AllEnemies => int.MaxValue,
                TargetRule.Row => 3,
                TargetRule.Column => 2,
                TargetRule.Self => 0,
                TargetRule.SingleAlly => 0,
                TargetRule.AllAllies => 0,
                _ => 1,
            };

            for (var i = 0; i < enemies.Count && buffer.Count < limit; i++)
            {
                if (enemies[i].Alive)
                {
                    buffer.Add(i);
                }
            }
        }

        private readonly struct Attacker
        {
            public Attacker(int attack, SkillDefinition skill)
            {
                Attack = attack;
                Skill = skill;
            }

            public int Attack { get; }

            public SkillDefinition Skill { get; }
        }

        private sealed class EnemyState
        {
            public EnemyState(EnemyDefinition definition)
            {
                MaxHealth = definition.MaxHealth;
                Health = definition.MaxHealth;
                Defense = definition.Defense;
                Element = definition.Element;
                BreakThreshold = definition.BreakThreshold;
                BreakLeft = definition.BreakThreshold;
                Alive = true;
            }

            public int MaxHealth { get; }

            public int Health { get; set; }

            public int Defense { get; }

            public FiveElement Element { get; }

            public int BreakThreshold { get; }

            public int BreakLeft { get; set; }

            public int BrokenTurns { get; set; }

            public bool WasBroken { get; set; }

            public bool Alive { get; set; }
        }
    }
}
