using System.ComponentModel.DataAnnotations;

namespace EQL.Models
{
    /// One line from a player's inventory file, cross-referenced to the Items table.
    public class InventoryEntry
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Character { get; set; } = "";

        /// Where the item is (e.g. "Head", "General1-Slot3", "Bank2"), if the file says.
        [MaxLength(64)]
        public string? Location { get; set; }

        /// Name exactly as it appeared in the file, e.g. "Mithril Breastplate +3".
        [MaxLength(200)]
        public string RawName { get; set; } = "";

        /// Name with the " +N" upgrade suffix removed, e.g. "Mithril Breastplate".
        [MaxLength(200)]
        public string BaseName { get; set; } = "";

        /// Upgrade tier parsed from the " +N" suffix (0 when there is none).
        public int Tier { get; set; }

        public int Count { get; set; } = 1;

        /// Matched item, or null when the name isn't in the Items table.
        public int? ItemId { get; set; }
        public Item? Item { get; set; }
    }
}
