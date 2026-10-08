using UnityEngine;

namespace SamsaraWest.Data
{
    /// <summary>
    /// 被动：装备或经文带上身、不占行动、常驻生效的一层效果。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一层只登记<b>身份与来源</b>：它叫什么、由哪件东西带来。效果本身<b>不</b>写在这里，
    /// 而是落在 <see cref="StatusId"/> 指向的那条状态上——数值口径、每回合结算、修正聚合
    /// 全都复用状态那一套现成机制（口径见 <c>Docs/架构决策.md</c> ADR-020）。
    /// </para>
    /// <para>
    /// 于是「被动」与「状态」的分工是：状态回答「会发生什么」，被动回答「这是谁给的」。
    /// 建单位那一刻由 <c>BattleFactory</c> 逐条施加成状态，此后被动不再参与运行期。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Passive", menuName = "SamsaraWest/定义/被动 Passive")]
    public sealed class PassiveDefinition : DefinitionBase
    {
        [Tooltip("这条被动的效果载在哪条状态上。数值一律写在 statuses.csv，被动只指向它。")]
        [CsvColumn("statusId", required: true)] [SerializeField] private string _statusId;

        public override DefinitionKind Kind => DefinitionKind.Passive;

        /// <summary>承载效果的状态 ID，例如 <c>STS_STAFF_FORCE</c>。</summary>
        public string StatusId => _statusId;

        public override void Validate(ValidationReport report)
        {
            base.Validate(report);

            if (string.IsNullOrWhiteSpace(_statusId))
            {
                report.Error("PSV_STATUS_EMPTY", "被动必须指定承载效果的状态 ID。", Id, fieldName: "statusId");
            }
            else if (!IdRules.IsValidId(DefinitionKind.Status, _statusId))
            {
                report.Error("PSV_STATUS_ID_PATTERN", $"承载状态 ID '{_statusId}' 不符合状态命名规则。", Id, fieldName: "statusId");
            }
        }
    }
}
