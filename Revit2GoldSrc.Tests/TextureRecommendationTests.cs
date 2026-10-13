using System;
using System.Collections.Generic;
using System.Linq;
using Revit2GoldSrc.Core;
using Revit2GoldSrc.GoldSrc;
using Xunit;
namespace Revit2GoldSrc.Tests
{
    public sealed class TextureRecommendationTests
    {
        [Theory]
        [InlineData(BimCategory.Wall, "WALL_A")]
        [InlineData(BimCategory.Floor, "FLOOR_A")]
        [InlineData(BimCategory.Column, "COLUMN_A")]
        public void PrioritizesCategoryAndIsIndependentOfCatalogOrder(BimCategory category, string expected)
        {
            var names = new[] { "BRICK_A", "TILE_A", "METAL_A", "COLUMN_A", "WALL_B", "WALL_A", "FLOOR_A", "PILLAR_A", "PLASTER_A", "FLR_A", "CONCRETE_A", "CRETE_A" };
            Assert.Equal(expected, TextureRecommendations.Recommend(names, category).Name);
            Assert.Equal(expected, TextureRecommendations.Recommend(names.AsEnumerable().Reverse(), category).Name);
            Assert.False(TextureRecommendations.Recommend(names, category).IsGenericFallback);
        }
        [Theory]
        [InlineData("+0WALL")]
        [InlineData("-0WALL")]
        [InlineData("{WALL")]
        [InlineData("~WALL")]
        [InlineData("WALL_DOOR")]
        [InlineData("WALL_SWITCH")]
        [InlineData("WALL_SIGN")]
        [InlineData("WALL_LIGHT")]
        public void PrefersNormalTexturesOverEffectsAndSigns(string effect)
        {
            var result = TextureRecommendations.Recommend(new[] { effect, "GENERIC" }, BimCategory.Wall);
            Assert.Equal("GENERIC", result.Name); Assert.True(result.IsGenericFallback);
        }
        [Fact]
        public void NeverSuggestsSpecialContentsOrNamesAbsentFromCatalog()
        {
            var names = new[] { "sky", "clip", "origin", "hint", "skip", "aaatrigger", "!WALL", "bad name", "TOO_LONG_TEXTURE_NAME", "TEST" };
            foreach (BimCategory category in Enum.GetValues(typeof(BimCategory))) Assert.Equal("TEST", TextureRecommendations.Recommend(names, category).Name);
            Assert.Throws<ArgumentException>(() => TextureRecommendations.Recommend(new[] { "sky", "clip" }, BimCategory.Wall));
        }
        [Fact]
        public void SingleTextureAndCaseInsensitiveTiesAreStable()
        {
            Assert.True(TextureRecommendations.Recommend(new[] { "TEST" }, BimCategory.Column).IsGenericFallback);
            Assert.Equal("wall_a", TextureRecommendations.Recommend(new[] { "WALL_Z", "wall_a" }, BimCategory.Wall).Name);
        }
        [Fact]
        public void EmptyOnlyPreservesChoicesAndFillsDisabledCategoriesAndEnclosure()
        {
            var current = new Dictionary<BimCategory, string?> { [BimCategory.Wall] = "MANUAL", [BimCategory.Floor] = null, [BimCategory.Column] = "" };
            var result = TextureRecommendations.Fill(new[] { "WALL_A", "FLOOR_A", "COLUMN_A" }, current, "MANUAL_ENC", TextureFillMode.EmptyOnly);
            Assert.Equal("MANUAL", result.Categories[BimCategory.Wall]); Assert.Equal("FLOOR_A", result.Categories[BimCategory.Floor]);
            Assert.Equal("COLUMN_A", result.Categories[BimCategory.Column]); Assert.Equal("MANUAL_ENC", result.Enclosure); Assert.Equal(2, result.Changed);
            Assert.Null(current[BimCategory.Floor]);
        }
        [Fact]
        public void ReplaceAllAndCancelFollowUserDecisionWithoutMutatingInput()
        {
            var current = new Dictionary<BimCategory, string?> { [BimCategory.Wall] = "MANUAL", [BimCategory.Floor] = "FLOOR_OLD", [BimCategory.Column] = null };
            var replaced = TextureRecommendations.Fill(new[] { "TEST" }, current, "MANUAL_ENC", TextureFillMode.ReplaceAll);
            Assert.Equal(4, replaced.Changed); Assert.Equal(4, replaced.GenericFallbackTargets.Count); Assert.All(replaced.Categories.Values, n => Assert.Equal("TEST", n)); Assert.Equal("TEST", replaced.Enclosure);
            var cancelled = TextureRecommendations.Fill(Array.Empty<string>(), current, "MANUAL_ENC", TextureFillMode.Cancel);
            Assert.Equal(0, cancelled.Changed); Assert.Equal("MANUAL", cancelled.Categories[BimCategory.Wall]); Assert.Null(cancelled.Categories[BimCategory.Column]); Assert.Equal("MANUAL_ENC", cancelled.Enclosure);
        }
    }
}
