using System;

namespace SamsaraWest.Battle
{
    /// <summary>
    /// 3×2 阵型里的一个格子。<see cref="Column"/> 与 <see cref="Row"/> 都从 0 起，
    /// <see cref="Row"/> 为 0 表示前排、1 表示后排。
    /// </summary>
    /// <remarks>
    /// 这是「位置语义」的唯一真源：技能的 <c>TargetRule.Column</c> / <c>Row</c>、
    /// 换位与移动、界面上的站位显示都读它，禁止各处自行用两个 int 拼位置。
    /// </remarks>
    public readonly struct FormationSlot : IEquatable<FormationSlot>
    {
        /// <summary>横向列数。三列是「前排三人」这条布阵约定的直接来源。</summary>
        public const int ColumnCount = 3;

        /// <summary>纵向排数：0 前排、1 后排。</summary>
        public const int RowCount = 2;

        /// <summary>单侧容量。遭遇表里的敌人数量不应超过它。</summary>
        public const int Capacity = ColumnCount * RowCount;

        public FormationSlot(int column, int row)
        {
            if (!IsInRange(column, row))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(row),
                    $"阵型坐标越界：列 {column}、排 {row}。合法范围是 0–{ColumnCount - 1} 列、0–{RowCount - 1} 排。");
            }

            Column = column;
            Row = row;
        }

        /// <summary>列，0–2。</summary>
        public int Column { get; }

        /// <summary>排，0 为前排、1 为后排。</summary>
        public int Row { get; }

        /// <summary>是否站在前排。前排是默认的受击面。</summary>
        public bool IsFront => Row == 0;

        /// <summary>在单侧阵型里的线性序号，前排在先、每排从左到右。</summary>
        public int Index => (Row * ColumnCount) + Column;

        public static bool IsInRange(int column, int row) =>
            column >= 0 && column < ColumnCount && row >= 0 && row < RowCount;

        /// <summary>按线性序号取格子；序号越界时返回前排第一格，避免调用方到处判空。</summary>
        public static FormationSlot FromIndex(int index)
        {
            if (index < 0 || index >= Capacity)
            {
                index = 0;
            }

            return new FormationSlot(index % ColumnCount, index / ColumnCount);
        }

        public bool Equals(FormationSlot other) => Column == other.Column && Row == other.Row;

        public override bool Equals(object obj) => obj is FormationSlot other && Equals(other);

        public override int GetHashCode() => (Row * ColumnCount) + Column;

        public override string ToString() => $"C{Column}R{Row}";

        public static bool operator ==(FormationSlot left, FormationSlot right) => left.Equals(right);

        public static bool operator !=(FormationSlot left, FormationSlot right) => !left.Equals(right);
    }

    /// <summary>
    /// 阵型落位。
    /// </summary>
    /// <remarks>
    /// 落位口径<b>已经由数据表定死</b>，这里只是执行它：
    /// <c>Data/Tables/encounters.csv</c> 的表头注释写明「enemyIds 顺序即 3×2 阵型的布阵顺序
    /// （前三个为前排，后面为后排）；formation 为策划备注用的阵型标签，运行时按 enemyIds 顺序落位」。
    /// 因此 <c>formation</c> 列（值形如 <c>2x1</c>）<b>不参与</b>落位计算，
    /// 与 <see cref="Data.EncounterDefinition.Formation"/> 字段上那句「例如 1,1;2,1;3,2（列,行）」
    /// 的写法不一致——这处不一致已在 Docs/战斗内核-v1.md 的遗留清单里登记，等人工拍板后再统一。
    /// </remarks>
    public static class BattleFormation
    {
        /// <summary>
        /// 按建队顺序落位：前三个进前排，其余进后排，每排从左到右。
        /// 超出 <see cref="FormationSlot.Capacity"/> 的部分落在最后一格（调用方应先报错，见 <see cref="BattleSetup.Validate"/>）。
        /// </summary>
        public static FormationSlot SlotForIndex(int index) =>
            FormationSlot.FromIndex(index < 0 ? 0 : index);
    }
}
