using System.ComponentModel.DataAnnotations;

namespace EQL.Models
{
    /// One row from items.csv (base, tier-0 stats as listed on eqlwiki.com).
    public class Item
    {
        public int Id { get; set; }

        [MaxLength(200)]
        public string Name { get; set; } = "";

        /// Upper-cased, trimmed name used for matching inventory lines to items.
        [MaxLength(200)]
        public string NormalizedName { get; set; } = "";

        [MaxLength(100)] public string? Slot { get; set; }
        [MaxLength(200)] public string? Class { get; set; }
        [MaxLength(200)] public string? Race { get; set; }
        [MaxLength(20)]  public string? Size { get; set; }

        public int? AC { get; set; }
        public int? HP { get; set; }
        public int? Mana { get; set; }
        public int? End { get; set; }
        public int? Str { get; set; }
        public int? Sta { get; set; }
        public int? Agi { get; set; }
        public int? Dex { get; set; }
        public int? Wis { get; set; }
        public int? Int { get; set; }
        public int? Cha { get; set; }
        public int? SvMagic { get; set; }
        public int? SvFire { get; set; }
        public int? SvCold { get; set; }
        public int? SvPoison { get; set; }
        public int? SvDisease { get; set; }
        public int? Haste { get; set; }
        public int? Dmg { get; set; }
        public int? Delay { get; set; }
        [MaxLength(50)] public string? Skill { get; set; }
        public double? Weight { get; set; }
        [MaxLength(500)] public string? Effect { get; set; }
        [MaxLength(100)] public string? Flags { get; set; }

        public static string Normalize(string name) => name.Trim().ToUpperInvariant();
    }
}
