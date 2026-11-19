using EQL.Models;

namespace EQL.Services
{
    /// Picks the best item for each equipment slot, from what a character owns and from every item
    /// any of its (up to three) classes can use, so the two can be compared slot by slot.
    public static class GearOptimizer
    {
        public record Pick(Item Item, int Tier, double Score);

        public record SlotResult(
            string Slot,
            Pick? Owned,          // best owned item, at the tier the character has it
            double? OwnedAtMax,   // that same item's score at the max tier
            Pick? Best,           // best obtainable item at +0
            double? BestAtMax);   // that item's score at the max tier

        private sealed class Candidate
        {
            public required Item Item { get; init; }
            public int Tier { get; init; }
            public int Uses { get; set; }               // how many slots it can still fill
            public required HashSet<string> Tokens { get; init; }
        }

        public static List<SlotResult> Build(
            IEnumerable<(Item Item, int Tier, int Count)> owned,
            IEnumerable<Item> allItems,
            IReadOnlyCollection<string> classCodes,
            IReadOnlyDictionary<string, double> weights)
        {
            var usable = allItems.Where(i => GameRules.CanUseAny(i.Class, classCodes) && !GameRules.IsTemporary(i)).ToList();

            var ownedPool = owned
                .Where(o => GameRules.CanUseAny(o.Item.Class, classCodes))
                .Select(o => new Candidate { Item = o.Item, Tier = o.Tier, Uses = Math.Max(1, o.Count), Tokens = GameRules.SlotTokens(o.Item.Slot) })
                .Where(c => c.Tokens.Count > 0)
                .ToList();
            ownedPool.ForEach(c => c.Tokens.Add(GameRules.AnyToken));

            var bestPool = usable
                .Select(i => new Candidate { Item = i, Tier = 0, Uses = GameRules.IsLore(i) ? 1 : 2, Tokens = GameRules.SlotTokens(i.Slot) })
                .Where(c => c.Tokens.Count > 0)
                .ToList();
            bestPool.ForEach(c => c.Tokens.Add(GameRules.AnyToken));

            var ownedPicks = Assign(ownedPool, weights);
            var bestPicks = Assign(bestPool, weights);

            var results = new List<SlotResult>();
            foreach (var (slot, token) in GameRules.EquipSlots)
            {
                bool weapon = token is "PRIMARY" or "SECONDARY" or "RANGE";
                ownedPicks.TryGetValue(slot, out var o);
                bestPicks.TryGetValue(slot, out var b);
                results.Add(new SlotResult(
                    slot,
                    o,
                    o is null ? null : GameRules.Score(o.Item, GameRules.MaxTier, weights, weapon),
                    b,
                    b is null ? null : GameRules.Score(b.Item, GameRules.MaxTier, weights, weapon)));
            }
            return results;
        }

        /// Greedy slot assignment. Handles two-handers (they take Primary and leave Secondary empty)
        /// and items that fit several slots. Not a perfect global optimum, but close for gear.
        private static Dictionary<string, Pick> Assign(List<Candidate> pool, IReadOnlyDictionary<string, double> w)
        {
            var picks = new Dictionary<string, Pick>();
            int haste = 0; // best haste equipped so far (haste doesn't stack)

            double ScoreOf(Candidate c, bool weapon) => GameRules.Score(c.Item, c.Tier, w, weapon, haste);

            Candidate? BestFor(string token, Func<Candidate, bool>? filter = null)
            {
                bool weapon = token is "PRIMARY" or "SECONDARY" or "RANGE";
                return pool.Where(c => c.Uses > 0 && c.Tokens.Contains(token) && (filter?.Invoke(c) ?? true))
                           .Select(c => (c, score: ScoreOf(c, weapon)))
                           .Where(x => x.score > 0)              // leave a slot empty rather than fill it with junk
                           .OrderByDescending(x => x.score)
                           .Select(x => x.c)
                           .FirstOrDefault();
            }
            void Take(string slot, Candidate c, bool weapon)
            {
                c.Uses--;
                picks[slot] = new Pick(c.Item, c.Tier, ScoreOf(c, weapon));
                haste = Math.Max(haste, c.Item.Haste ?? 0);
            }

            // Weapons first: best 2H alone vs. best 1H primary + best secondary.
            var twoH = BestFor("PRIMARY", c => GameRules.IsTwoHanded(c.Item));
            var oneH = BestFor("PRIMARY", c => !GameRules.IsTwoHanded(c.Item));
            double twoHScore = twoH is null ? double.MinValue : ScoreOf(twoH, true);
            double comboScore = double.MinValue;
            Candidate? off = null;
            if (oneH is not null)
            {
                oneH.Uses--;   // so the off-hand can't be the same single copy
                off = BestFor("SECONDARY", c => !GameRules.IsTwoHanded(c.Item));
                oneH.Uses++;
                comboScore = ScoreOf(oneH, true) + (off is null ? 0 : ScoreOf(off, true));
            }
            if (twoH is not null && twoHScore >= comboScore)
                Take("Primary", twoH, true);
            else if (oneH is not null)
            {
                Take("Primary", oneH, true);
                if (off is not null && off.Uses > 0) Take("Secondary", off, true);
            }

            // Every other slot, scarcest first so multi-slot items aren't wasted.
            foreach (var (slot, token) in GameRules.EquipSlots
                         .Where(s => s.Token is not ("PRIMARY" or "SECONDARY" or GameRules.AnyToken))
                         .OrderBy(s => pool.Count(c => c.Tokens.Contains(s.Token))))
            {
                var c = BestFor(token);
                if (c is not null) Take(slot, c, token == "RANGE");
            }

            // Last, the two Any slots: the best leftover items of any type (stats only, no weapon swing value).
            foreach (var (slot, _) in GameRules.EquipSlots.Where(s => s.Token == GameRules.AnyToken))
            {
                var c = BestFor(GameRules.AnyToken);
                if (c is not null) Take(slot, c, false);
            }
            return picks;
        }
    }
}
