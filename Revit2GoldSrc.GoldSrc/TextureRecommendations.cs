using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;
namespace Revit2GoldSrc.GoldSrc
{
    public enum TextureFillMode { EmptyOnly, ReplaceAll, Cancel }
    public sealed class TextureRecommendation
    {
        public string Name { get; }
        public bool IsGenericFallback { get; }
        internal TextureRecommendation(string name, bool fallback) { Name = name; IsGenericFallback = fallback; }
    }
    public sealed class TextureFillResult
    {
        public Dictionary<BimCategory, string?> Categories { get; } = new Dictionary<BimCategory, string?>();
        public string? Enclosure { get; internal set; }
        public int Changed { get; internal set; }
        public List<string> GenericFallbackTargets { get; } = new List<string>();
    }
    public static class TextureRecommendations
    {
        public static TextureRecommendation Recommend(IEnumerable<string> names, BimCategory category)
        {
            string[] keywords;
            switch (category)
            {
                case BimCategory.Wall: keywords = new[] { "WALL", "BRICK", "PLASTER" }; break;
                case BimCategory.Floor: keywords = new[] { "FLOOR", "FLR", "TILE" }; break;
                case BimCategory.Column: keywords = new[] { "COLUMN", "PILLAR", "CONCRETE", "CRETE", "METAL" }; break;
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
            var candidates = names.Where(IsSolid).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => new { Name = n, Score = Score(n, keywords), Normal = IsNormal(n) }).ToList();
            if (candidates.Count == 0) throw new ArgumentException("El WAD no contiene texturas sólidas admitidas.");
            // Prefer a normal texture over effects; among normal textures prefer the category keywords.
            var winner = candidates.OrderByDescending(c => c.Normal).ThenByDescending(c => c.Score).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.Ordinal).First();
            return new TextureRecommendation(winner.Name, winner.Score == 0);
        }
        private static bool IsSolid(string name)
        { try { TextureMapper.ValidateSolidTexture(name); return true; } catch (ArgumentException) { return false; } }
        private static int Score(string name, string[] words)
        {
            for (int i = 0; i < words.Length; i++) if (name.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0) return words.Length - i;
            return 0;
        }
        private static bool IsNormal(string name)
        {
            return !name.StartsWith("+", StringComparison.Ordinal) && !name.StartsWith("-", StringComparison.Ordinal) && !name.StartsWith("{", StringComparison.Ordinal) &&
                name.IndexOf('~') < 0 && new[] { "SWITCH", "SWTCH", "STCH", "DOOR", "SIGN", "LIGHT", "LAMP", "GLOW" }.All(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0);
        }
        public static TextureFillResult Fill(IEnumerable<string> names, IReadOnlyDictionary<BimCategory, string?> current, string? enclosure, TextureFillMode mode)
        {
            if (!Enum.IsDefined(typeof(TextureFillMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            var result = new TextureFillResult { Enclosure = enclosure };
            foreach (BimCategory category in Enum.GetValues(typeof(BimCategory))) result.Categories[category] = current.TryGetValue(category, out string? value) ? value : null;
            if (mode == TextureFillMode.Cancel) return result;
            var available = names.ToList();
            foreach (var category in result.Categories.Keys.ToList())
            {
                if (mode == TextureFillMode.EmptyOnly && !string.IsNullOrWhiteSpace(result.Categories[category])) continue;
                var recommended = Recommend(available, category);
                if (result.Categories[category] != recommended.Name) { result.Categories[category] = recommended.Name; result.Changed++; }
                if (recommended.IsGenericFallback) result.GenericFallbackTargets.Add(category.ToString());
            }
            if (mode == TextureFillMode.ReplaceAll || string.IsNullOrWhiteSpace(enclosure))
            {
                var recommended = Recommend(available, BimCategory.Wall);
                if (enclosure != recommended.Name) { result.Enclosure = recommended.Name; result.Changed++; }
                if (recommended.IsGenericFallback) result.GenericFallbackTargets.Add("Recinto");
            }
            return result;
        }
    }
}
