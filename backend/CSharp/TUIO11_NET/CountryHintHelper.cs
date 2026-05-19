using System;
using System.Collections.Generic;
using System.Drawing;

/// <summary>Two-tier flag hints: tier 1 subtle (mood), tier 2 clearer after losing a life (not a giveaway).</summary>
internal static class CountryHintHelper
{
    internal enum FlagColorLayout
    {
        HorizontalStripes,
        VerticalStripes,
        CircleOnField,
        CrossOnField,
        BicolorHorizontal,
        BicolorVertical
    }

    private sealed class CountryClue
    {
        public Color[] Colors;
        public FlagColorLayout Layout;
        /// <summary>Tier 1 — indirect: palette or layout clue without spelling the answer out.</summary>
        public string HintTier1Text;
        /// <summary>Tier 2 — clearer colour names and order; still requires matching on the board.</summary>
        public string HintTier2Text;
    }

    private static readonly Dictionary<string, CountryClue> Clues =
        new Dictionary<string, CountryClue>(StringComparer.OrdinalIgnoreCase)
        {
            ["France"] = Clue(
                FlagColorLayout.VerticalStripes,
                new[] { C(0, 85, 164), Color.White, C(239, 65, 53) },
                "A flag of revolution and liberty.",
                "Blue beside red."),
            ["Italy"] = Clue(
                FlagColorLayout.VerticalStripes,
                new[] { C(0, 146, 70), Color.White, C(206, 43, 55) },
                "Nature colors around white peace.",
                "Green beside red."),
            ["Spain"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(198, 11, 30), C(255, 196, 0), C(198, 11, 30) },
                "Sunlight trapped between red walls.",
                "Mostly red and yellow."),
            ["Egypt"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(206, 17, 38), Color.White, Color.Black, C(192, 147, 0) },
                "An eagle watches from the centre.",
                "Red, white, black."),
            ["Germany"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { Color.Black, C(221, 0, 0), C(255, 206, 0) },
                "Darkness rising into gold.",
                "Black on top."),
            ["England"] = Clue(
                FlagColorLayout.CrossOnField,
                new[] { Color.White, C(200, 16, 46) },
                "A knight's cross fills the flag.",
                "Red cross on white."),
            ["Japan"] = Clue(
                FlagColorLayout.CircleOnField,
                new[] { Color.White, C(188, 0, 45) },
                "The rising sun stands alone.",
                "One red circle."),
            ["Saudi Arabia"] = Clue(
                FlagColorLayout.BicolorHorizontal,
                new[] { C(0, 108, 53), Color.White },
                "Sacred words above a sword.",
                "Green with white text."),
            ["Turkey"] = Clue(
                FlagColorLayout.CircleOnField,
                new[] { C(227, 10, 23), Color.White },
                "Moonlight and a single star.",
                "Crescent and star."),
            ["India"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(255, 153, 51), Color.White, C(19, 136, 8) },
                "A wheel balances three colors.",
                "Orange, white, green."),
            ["South Korea"] = Clue(
                FlagColorLayout.CircleOnField,
                new[] { Color.White, C(205, 46, 58), C(0, 71, 160) },
                "Balance shown in two swirling colors.",
                "Red and blue circle."),
            ["Portugal"] = Clue(
                FlagColorLayout.BicolorVertical,
                new[] { C(0, 102, 0), C(255, 0, 0) },
                "Explorer colors split in two.",
                "Green and red."),
            ["Greece"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(13, 94, 175), Color.White },
                "Sea waves turned into stripes.",
                "Blue and white."),
            ["Mexico"] = Clue(
                FlagColorLayout.VerticalStripes,
                new[] { C(0, 104, 71), Color.White, C(206, 17, 38) },
                "An eagle guards the middle.",
                "Green, white, red."),
            ["Norway"] = Clue(
                FlagColorLayout.CrossOnField,
                new[] { C(186, 12, 47), Color.White, C(0, 32, 91) },
                "A frozen land marked by a cross.",
                "Blue cross."),
            ["Thailand"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(165, 25, 49), Color.White, C(36, 73, 117) },
                "A deep blue heart between stripes.",
                "Red, white, blue."),
            ["UAE"] = Clue(
                FlagColorLayout.VerticalStripes,
                new[] { Color.FromArgb(255, 0, 0), C(0, 115, 47), Color.White, Color.Black },
                "Four united Arab colors.",
                "Red with green, white, black."),
            ["United Arab Emirates"] = Clue(
                FlagColorLayout.VerticalStripes,
                new[] { Color.FromArgb(255, 0, 0), C(0, 115, 47), Color.White, Color.Black },
                "Four united Arab colors.",
                "Red with green, white, black."),
            ["Denmark"] = Clue(
                FlagColorLayout.CrossOnField,
                new[] { C(198, 12, 48), Color.White },
                "A very old northern cross.",
                "White cross on red."),
            ["Netherlands"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(174, 28, 40), Color.White, C(33, 70, 139) },
                "Colors carried by old sailors.",
                "Red, white, blue."),
            ["Ghana"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(206, 17, 38), C(252, 209, 22), C(0, 107, 63) },
                "A black star of freedom.",
                "Red, yellow, green."),
            ["Madagascar"] = Clue(
                FlagColorLayout.BicolorVertical,
                new[] { Color.White, C(252, 209, 22), C(0, 127, 63) },
                "An island split into calm colors.",
                "White, red, green."),
            ["Indonesia"] = Clue(
                FlagColorLayout.BicolorHorizontal,
                new[] { C(255, 0, 0), Color.White },
                "Two simple layers of color.",
                "Red over white."),
            ["Belgium"] = Clue(
                FlagColorLayout.VerticalStripes,
                new[] { Color.Black, C(255, 223, 0), C(237, 41, 57) },
                "Three standing bands of power.",
                "Black, yellow, red."),
            ["Sweden"] = Clue(
                FlagColorLayout.CrossOnField,
                new[] { C(0, 106, 167), C(254, 204, 0) },
                "A golden cross in northern skies.",
                "Yellow cross."),
            ["Malaysia"] = Clue(
                FlagColorLayout.HorizontalStripes,
                new[] { C(204, 0, 0), Color.White, C(1, 0, 102) },
                "A crescent above endless stripes.",
                "Stripes and moon."),
        };

    private static Color C(int r, int g, int b) => Color.FromArgb(r, g, b);

    private static CountryClue Clue(FlagColorLayout layout, Color[] colors, string tier1, string tier2)
    {
        return new CountryClue
        {
            Layout = layout,
            Colors = colors,
            HintTier1Text = tier1,
            HintTier2Text = tier2
        };
    }

    private static CountryClue GetClue(string countryName)
    {
        if (string.IsNullOrWhiteSpace(countryName)) return null;
        CountryClue c;
        if (Clues.TryGetValue(countryName.Trim(), out c)) return c;
        return null;
    }

    internal static Color[] GetColors(string countryName) => GetClue(countryName)?.Colors;

    internal static FlagColorLayout GetColorLayout(string countryName)
    {
        CountryClue c = GetClue(countryName);
        return c != null ? c.Layout : FlagColorLayout.HorizontalStripes;
    }

    internal static bool HasAnyClue(string countryName) => GetClue(countryName) != null;

    /// <summary>Tier 1 — shown when sad/confused (subtle).</summary>
    internal static string GetHintTier1Text(string countryName)
    {
        CountryClue c = GetClue(countryName);
        if (c == null || string.IsNullOrWhiteSpace(c.HintTier1Text))
            return "Study the palette and how the colours are arranged.";
        return c.HintTier1Text;
    }

    /// <summary>Tier 2 — shown after losing a life (clearer text).</summary>
    internal static string GetHintTier2Text(string countryName)
    {
        CountryClue c = GetClue(countryName);
        if (c == null || string.IsNullOrWhiteSpace(c.HintTier2Text))
            return "Compare named colours and stripe direction to the flags shown.";
        return c.HintTier2Text;
    }
}
