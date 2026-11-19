using EQL.Models;

namespace EQL.Services
{
    /// Classes, equipment slots, and the EverQuest Legends item-upgrade math.
    public static class GameRules
    {
        /// Highest upgrade tier. Change to 11 when Rulers of Kunark goes live (and confirm the +11 rules).
        public static int MaxTier { get; set; } = 10;

        public static readonly (string Code, string Name)[] Classes =
        {
            ("WAR","Warrior"), ("CLR","Cleric"), ("PAL","Paladin"), ("RNG","Ranger"),
            ("SHD","Shadow Knight"), ("DRU","Druid"), ("MNK","Monk"), ("BRD","Bard"),
            ("ROG","Rogue"), ("SHM","Shaman"), ("NEC","Necromancer"), ("WIZ","Wizard"),
            ("MAG","Magician"), ("ENC","Enchanter"), ("BST","Beastlord"), ("BER","Berserker"),
        };

        /// Equipment slots in display order: (slot name, token used in the item's Slot field).
        public static readonly (string Slot, string Token)[] EquipSlots =
        {
            ("Ear 1","EAR"), ("Head","HEAD"), ("Face","FACE"), ("Ear 2","EAR"),
            ("Neck","NECK"), ("Shoulders","SHOULDERS"), ("Arms","ARMS"), ("Back","BACK"),
            ("Wrist 1","WRIST"), ("Wrist 2","WRIST"), ("Range","RANGE"), ("Hands","HANDS"),
            ("Primary","PRIMARY"), ("Secondary","SECONDARY"), ("Finger 1","FINGER"), ("Finger 2","FINGER"),
            ("Chest","CHEST"), ("Legs","LEGS"), ("Feet","FEET"), ("Waist","WAIST"), ("Ammo","AMMO"),
            // EQ Legends has two "Any" slots that take any wearable item at full value.
            ("Any 1","ANY"), ("Any 2","ANY"),
        };

        /// Token for the two EQ Legends "Any" slots (every wearable item fits them).
        public const string AnyToken = "ANY";

        private static readonly Dictionary<string, string> SlotAliases = new()
        {
            ["FINGERS"] = "FINGER", ["SHOULDER"] = "SHOULDERS", ["SECONDAY"] = "SECONDARY", ["SHIELD"] = "SECONDARY",
        };

        public static HashSet<string> SlotTokens(string? slot)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(slot)) return set;
            foreach (var t in slot.ToUpperInvariant().Split(new[] { ' ', ',', '/' }, StringSplitOptions.RemoveEmptyEntries))
                set.Add(SlotAliases.TryGetValue(t, out var a) ? a : t);
            return set;
        }

        /// Class field examples: "ALL", "WAR PAL SHD", "ALL except WAR MNK ROG", "NONE".
        public static bool CanUse(string? classField, string classCode)
        {
            if (string.IsNullOrWhiteSpace(classField)) return false;
            var tokens = classField.ToUpperInvariant().Replace(",", " ")
                                   .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Contains("NONE")) return false;
            if (tokens.FirstOrDefault() == "ALL")
            {
                int ex = tokens.IndexOf("EXCEPT");
                return ex < 0 || !tokens.Skip(ex + 1).Contains(classCode);
            }
            return tokens.Contains(classCode);
        }

        /// EQ Legends characters have up to three classes; an item is usable if any of them can use it.
        public static bool CanUseAny(string? classField, IEnumerable<string> classCodes) =>
            classCodes.Any(c => CanUse(classField, c));

        public static string ClassName(string code) =>
            Classes.FirstOrDefault(c => c.Code == code).Name ?? code;

        public static bool IsTwoHanded(Item i) => i.Skill?.StartsWith("2H", StringComparison.OrdinalIgnoreCase) == true;
        public static bool IsLore(Item i) => i.Flags?.Contains("LORE", StringComparison.OrdinalIgnoreCase) == true;
        public static bool IsTemporary(Item i) => i.Flags?.Contains("TEMPORARY", StringComparison.OrdinalIgnoreCase) == true;

        // ---------- upgrade math (eqlwiki.com/Item_Upgrade_System) ----------
        // Each tier adds a cumulative +10% to stats, rounded down, with at least +1 per tier.
        // Weapon damage gets +5% per tier. Delay is never reduced.
        // Assumptions (not stated on the wiki): negative stats and Haste are not changed by tiers.

        public static int StatAtTier(int? baseStat, int tier)
        {
            int b = baseStat ?? 0;
            if (b <= 0 || tier <= 0) return b;
            int scaled = (int)Math.Floor(b * (1 + 0.10 * tier));
            return Math.Max(scaled, b + tier);
        }

        public static int DamageAtTier(int? baseDmg, int tier)
        {
            int b = baseDmg ?? 0;
            if (b <= 0 || tier <= 0) return b;
            return (int)Math.Floor(b * (1 + 0.05 * tier));
        }

        /// Total upgrade XP needed to reach a tier: 0, 1, 3, 7, ... 1023 at +10.
        public static int XpForTier(int tier) => (1 << tier) - 1;

        // ---------- stat weights ----------

        public static readonly string[] WeightKeys =
        {
            "AC","HP","Mana","End","Str","Sta","Agi","Dex","Wis","Int","Cha",
            "SvMagic","SvFire","SvCold","SvPoison","SvDisease","Haste","WeaponRatio",
        };

        public static Dictionary<string, double> DefaultWeights(string classCode)
        {
            var w = WeightKeys.ToDictionary(k => k, _ => 0.0);
            void Set(params (string k, double v)[] kv) { foreach (var (k, v) in kv) w[k] = v; }
            void Resists(double v) { foreach (var k in new[] { "SvMagic","SvFire","SvCold","SvPoison","SvDisease" }) w[k] = v; }

            switch (classCode)
            {
                case "WAR": case "PAL": case "SHD":
                    Set(("AC",2), ("HP",1), ("Sta",1), ("Str",0.5), ("Agi",0.5), ("Dex",0.3), ("Haste",3), ("WeaponRatio",20));
                    Resists(0.3);
                    if (classCode != "WAR") Set(("Mana",0.3), ("Wis", classCode == "PAL" ? 0.5 : 0), ("Int", classCode == "SHD" ? 0.5 : 0));
                    break;
                case "MNK": case "ROG": case "RNG": case "BER": case "BST": case "BRD":
                    Set(("AC",0.5), ("HP",0.7), ("Str",1), ("Dex",1), ("Agi",0.7), ("Sta",0.5), ("Haste",3), ("WeaponRatio",30));
                    Resists(0.2);
                    if (classCode is "RNG" or "BST") Set(("Mana",0.3), ("Wis",0.5));
                    if (classCode == "BRD") Set(("Mana",0.3), ("Cha",0.8));
                    break;
                case "CLR": case "DRU": case "SHM":
                    Set(("Wis",1.5), ("Mana",1), ("HP",0.5), ("AC",0.3), ("Sta",0.3));
                    Resists(0.2);
                    break;
                default: // NEC WIZ MAG ENC
                    Set(("Int",1.5), ("Mana",1), ("HP",0.4), ("AC",0.2), ("Sta",0.2));
                    Resists(0.2);
                    if (classCode == "ENC") Set(("Cha",0.5));
                    break;
            }
            return w;
        }

        /// Stat weights for a multi-class character: the highest weight any of its classes gives each stat,
        /// so Shaman's WIS, Monk's STR/DEX and Shadow Knight's AC/HP all count.
        public static Dictionary<string, double> DefaultWeights(IEnumerable<string> classCodes)
        {
            var w = WeightKeys.ToDictionary(k => k, _ => 0.0);
            foreach (var code in classCodes.Distinct())
                foreach (var (k, v) in DefaultWeights(code))
                    w[k] = Math.Max(w[k], v);
            return w;
        }

        /// Weighted score of an item at a given tier. Haste doesn't stack in game (only the highest
        /// counts), so pass the best haste already equipped as hasteFloor to score only the improvement.
        public static double Score(Item i, int tier, IReadOnlyDictionary<string, double> w, bool weaponSlot, int hasteFloor = 0)
        {
            double W(string k) => w.TryGetValue(k, out var v) ? v : 0;
            double s =
                W("AC") * StatAtTier(i.AC, tier) + W("HP") * StatAtTier(i.HP, tier) +
                W("Mana") * StatAtTier(i.Mana, tier) + W("End") * StatAtTier(i.End, tier) +
                W("Str") * StatAtTier(i.Str, tier) + W("Sta") * StatAtTier(i.Sta, tier) +
                W("Agi") * StatAtTier(i.Agi, tier) + W("Dex") * StatAtTier(i.Dex, tier) +
                W("Wis") * StatAtTier(i.Wis, tier) + W("Int") * StatAtTier(i.Int, tier) +
                W("Cha") * StatAtTier(i.Cha, tier) +
                W("SvMagic") * StatAtTier(i.SvMagic, tier) + W("SvFire") * StatAtTier(i.SvFire, tier) +
                W("SvCold") * StatAtTier(i.SvCold, tier) + W("SvPoison") * StatAtTier(i.SvPoison, tier) +
                W("SvDisease") * StatAtTier(i.SvDisease, tier) +
                W("Haste") * Math.Max(0, (i.Haste ?? 0) - hasteFloor);

            if (weaponSlot && i.Dmg is > 0 && i.Delay is > 0)
                s += W("WeaponRatio") * 10.0 * DamageAtTier(i.Dmg, tier) / i.Delay.Value;
            return s;
        }
    }
}
