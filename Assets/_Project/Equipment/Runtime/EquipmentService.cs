using System;
using System.Collections.Generic;
using SamsaraWest.Core;
using SamsaraWest.Data;

namespace SamsaraWest.Equipment
{
    /// <summary>
    /// <see cref="IEquipmentService"/> 的实现：一本内存里的在身清单。
    /// </summary>
    /// <remarks>
    /// <para>它不落盘——落盘是 <c>Save</c> 的事，搬运是组合根的事（ADR-025 的手法）。
    /// 这里只回答「谁身上穿着什么」，并在被问倒的时候<b>什么也不改</b>。</para>
    ///
    /// <para><b>写入口只有一道闸</b>：<see cref="TryEquip"/> 把「成员认不认得、是不是可操作角色、
    /// 栏位认不认得、这件东西是不是装备或经文、它的栏位对不对得上、限定角色允许不允许」全查一遍，
    /// 不合格的条目进不了这本簿子。进场画像（<c>CharacterStatsResolver</c>）仍会把这些再验一遍，
    /// 那不是重复劳动：读档是另一个入口（数据可能来自手改过或有年头的文件），
    /// 纵深防御留在这里——两道都拦，任一道漏了还有另一道。</para>
    /// </remarks>
    public sealed class EquipmentService : IEquipmentService
    {
        private static readonly LoadoutEntry[] EmptyLoadout = Array.Empty<LoadoutEntry>();

        private readonly IDefinitionRegistry _definitions;

        // 外层键是成员 ID，内层键是栏位。
        // 内层用枚举而不是字符串：能进来的栏位名都已经过 EquipmentSlots.TryParse，
        // 此后排序、查空、比较都不必再解析一次字符串。
        private readonly Dictionary<string, Dictionary<EquipmentSlot, string>> _books =
            new Dictionary<string, Dictionary<EquipmentSlot, string>>(StringComparer.Ordinal);

        /// <param name="definitions">
        /// 定义目录。栏位对不对得上、限定角色允不允许、物品是不是装备或经文，都要问它，
        /// 所以这里不给「没有目录也能跑」的降级路：与其放行一条无从校验的条目（假生效），
        /// 不如在装配时就吵起来（<see cref="EquipmentModule.Install"/> 会先一步抛）。
        /// </param>
        public EquipmentService(IDefinitionRegistry definitions)
        {
            _definitions = definitions ?? throw new ArgumentNullException(
                nameof(definitions),
                "在身清单必须能查数据定义：栏位对不对得上、限定角色允不允许都要问目录。");
        }

        /// <summary>当前在身件数（所有成员加起来），只用来写日志。</summary>
        public int EquippedCount
        {
            get
            {
                var count = 0;
                foreach (var book in _books.Values)
                {
                    count += book.Count;
                }

                return count;
            }
        }

        public IReadOnlyList<EquippedItem> Snapshot
        {
            get
            {
                // 顺序定死：先成员 ID（序数），再栏位（展示顺序）。
                // 同一份状态两次采集必须得到同样的排列，否则同一身装备会写出两份「内容相同、顺序不同」的档。
                var characters = new List<string>(_books.Keys);
                characters.Sort(StringComparer.Ordinal);

                var snapshot = new List<EquippedItem>(EquippedCount);
                for (var i = 0; i < characters.Count; i++)
                {
                    var characterId = characters[i];
                    var slots = SortedSlots(_books[characterId]);
                    for (var j = 0; j < slots.Count; j++)
                    {
                        snapshot.Add(new EquippedItem(characterId, slots[j].ToString(), _books[characterId][slots[j]]));
                    }
                }

                return snapshot;
            }
        }

        public IReadOnlyList<LoadoutEntry> LoadoutOf(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) || !_books.TryGetValue(characterId, out var book) || book.Count == 0)
            {
                return EmptyLoadout;
            }

            var slots = SortedSlots(book);
            var loadout = new List<LoadoutEntry>(slots.Count);
            for (var i = 0; i < slots.Count; i++)
            {
                loadout.Add(new LoadoutEntry(slots[i].ToString(), book[slots[i]]));
            }

            return loadout;
        }

        public string ItemIn(string characterId, string slotId)
        {
            if (string.IsNullOrEmpty(characterId) || !EquipmentSlots.TryParse(slotId, out var slot))
            {
                return string.Empty;
            }

            return _books.TryGetValue(characterId, out var book) && book.TryGetValue(slot, out var itemId)
                ? itemId
                : string.Empty;
        }

        public bool TryEquip(string characterId, string slotId, string itemId, out string error)
        {
            if (!TryValidate(characterId, slotId, itemId, out var slot, out var definition, out error))
            {
                // 拦下的理由要留痕：静默失败是最难查的那种（谁都没做错，就是没穿上）。
                GameLog.Warn(LogChannel.Equipment, $"在身清单拦下一条：{error}");
                return false;
            }

            if (!_books.TryGetValue(characterId, out var book))
            {
                book = new Dictionary<EquipmentSlot, string>();
                _books[characterId] = book;
            }

            // 同一栏位后来者覆盖：存档是「一份成员 × 栏位只存一条」，内存里也照这个形状存，
            // 免得出现「存档写出来的与内存里有的不是同一件事」。
            book[slot] = itemId;
            GameLog.Debug(
                LogChannel.Equipment,
                $"{characterId} 的 {slot} 栏位换上 '{itemId}'（{definition.Kind}），当前在身 {EquippedCount} 件。");
            return true;
        }

        public bool TryUnequip(string characterId, string slotId, out string error)
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                error = "成员 ID 不能为空。";
                GameLog.Warn(LogChannel.Equipment, $"在身清单拦下一条：{error}");
                return false;
            }

            if (!EquipmentSlots.TryParse(slotId, out var slot))
            {
                error = $"栏位 '{slotId}' 认不出来。";
                GameLog.Warn(LogChannel.Equipment, $"在身清单拦下一条：{error}");
                return false;
            }

            if (!_books.TryGetValue(characterId, out var book) || !book.ContainsKey(slot))
            {
                // 不是错误，是「没有可做的事」：空栏位脱不出东西来，调用方想判断就先问 ItemIn。
                error = $"{characterId} 的 {slot} 栏位本来就是空的。";
                return false;
            }

            book.Remove(slot);
            if (book.Count == 0)
            {
                // 空簿子不留：「这个人什么都没穿」与「没有这个人」在快照上是同一件事，
                // 留着只会让存档多出几条空条目。
                _books.Remove(characterId);
            }

            error = string.Empty;
            GameLog.Debug(LogChannel.Equipment, $"{characterId} 脱下 {slot} 栏位，当前在身 {EquippedCount} 件。");
            return true;
        }

        public void Restore(IEnumerable<EquippedItem> items)
        {
            // 整本换掉：清单以外的成员簿子必须清空。读档是「换成另一份状态」，
            // 不是「在现在这一身上再穿几件」——与账本、钱袋同一口径。
            _books.Clear();
            if (items == null)
            {
                return;
            }

            var restored = 0;
            var skipped = 0;
            foreach (var entry in items)
            {
                if (TryValidate(entry.CharacterId, entry.SlotId, entry.ItemId, out var slot, out _, out var error))
                {
                    if (!_books.TryGetValue(entry.CharacterId, out var book))
                    {
                        book = new Dictionary<EquipmentSlot, string>();
                        _books[entry.CharacterId] = book;
                    }

                    book[slot] = entry.ItemId;
                    restored++;
                    continue;
                }

                // 坏条目只跳过它自己，并留下一条能追到具体 ID 的错误（与其他读档路径同一手法）。
                skipped++;
                GameLog.Error(LogChannel.Equipment, $"读档时这条在身清单进不来：{error}");
            }

            GameLog.Debug(
                LogChannel.Equipment,
                $"在身清单已整本换掉：收下 {restored} 条" + (skipped > 0 ? $"，跳过 {skipped} 条。" : "。"));
        }

        public void Clear()
        {
            if (_books.Count == 0)
            {
                return;
            }

            _books.Clear();
            GameLog.Debug(LogChannel.Equipment, "在身清单已清空。");
        }

        public void OnRegistered(IServiceRegistry registry)
        {
            GameLog.Debug(LogChannel.Equipment, $"在身清单已就绪，当前 {EquippedCount} 件在身、{_books.Count} 名成员有簿子。");
        }

        public void OnUnregistered() => Clear();

        /// <summary>
        /// 唯一的校验口径（穿上与读档共用）：过了就把解析好的栏位与定义交出去。
        /// </summary>
        private bool TryValidate(
            string characterId,
            string slotId,
            string itemId,
            out EquipmentSlot slot,
            out DefinitionBase definition,
            out string error)
        {
            slot = EquipmentSlot.None;
            definition = null;

            if (string.IsNullOrWhiteSpace(characterId))
            {
                error = "成员 ID 不能为空。";
                return false;
            }

            if (!_definitions.TryGet(characterId, out var character) || !(character is CharacterDefinition member))
            {
                error = $"'{characterId}' 不是目录里的角色，穿不上东西。";
                return false;
            }

            if (!member.IsPlayable)
            {
                error = $"'{characterId}' 不是可操作角色，在身清单只管队伍成员。";
                return false;
            }

            if (!EquipmentSlots.TryParse(slotId, out slot))
            {
                error = $"栏位 '{slotId}' 认不出来，只认装备栏位名（Weapon/Armor/…/Sutra）。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                error = "物品 ID 不能为空。";
                return false;
            }

            if (!_definitions.TryGet(itemId, out definition))
            {
                error = $"'{itemId}' 在数据目录里查不到。";
                return false;
            }

            switch (definition)
            {
                case EquipmentDefinition equipment:
                    if (equipment.Slot != slot)
                    {
                        error = $"'{itemId}' 属于 {equipment.Slot} 栏位，穿不到 {slot} 上。";
                        return false;
                    }

                    // 限定角色：空数组表示全队可装备（口径见 EquipmentDefinition.AllowedCharacterIds）。
                    var allowed = equipment.AllowedCharacterIds;
                    if (allowed.Length > 0 && Array.IndexOf(allowed, member.Id) < 0)
                    {
                        error = $"'{itemId}' 限定 {string.Join("/", allowed)} 使用，{characterId} 穿不上。";
                        return false;
                    }

                    break;

                case SutraDefinition:
                    if (slot != EquipmentSlot.Sutra)
                    {
                        error = $"'{itemId}' 是经文，只进 {EquipmentSlot.Sutra} 栏位。";
                        return false;
                    }

                    break;

                default:
                    error = $"'{itemId}' 是 {definition.Kind}，在身清单只收装备与经文。";
                    return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 按栏位<b>展示顺序</b>（武器·副手·头盔·护甲·护腕·腿部护具·法宝·经文）排出栏位。
        /// </summary>
        /// <remarks>
        /// 用展示顺序而不是枚举数值：界面、存档与进场画像看到的是同一个顺序，
        /// 顺带把「挂载技能的先来后到」也钉成确定的（技能挂载顺序由在身清单顺序决定）。
        /// </remarks>
        private static List<EquipmentSlot> SortedSlots(Dictionary<EquipmentSlot, string> book)
        {
            var slots = new List<EquipmentSlot>(book.Keys);
            slots.Sort((a, b) =>
                Array.IndexOf(EquipmentSlots.DisplayOrder, a).CompareTo(Array.IndexOf(EquipmentSlots.DisplayOrder, b)));
            return slots;
        }
    }
}
