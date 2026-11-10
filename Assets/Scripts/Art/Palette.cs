using UnityEngine;

namespace BodgeSquad.Art
{
    // Art bible — the single source for every color in the project.
    // Style: Low-poly flat-shaded sitcom renovation: chunky primitives, cheap paint colors,
    //        everything slightly crooked.
    // Mood:  Midday sun through grimy windows: warm key light, cool blue-gray bounce in the shadows.
    public static class Palette
    {
        public static readonly Color Floor           = Hex("#9C7A54"); // cheap laminate
        public static readonly Color Walls           = Hex("#E9DFC7"); // magnolia emulsion
        public static readonly Color CharacterPrimary   = Hex("#F28C28"); // hi-vis orange
        public static readonly Color CharacterSecondary = Hex("#2F5D8A"); // overall blue
        public static readonly Color Props           = Hex("#7FA36B"); // avocado 70s green
        public static readonly Color Shadow          = Hex("#5B6478"); // damp slate
        public static readonly Color Accent          = Hex("#E63946"); // inspector red flag

        // Per-player hard-hat tints, so everyone can tell who bodged what (same saturation family)
        public static readonly Color[] PlayerTints =
        {
            Hex("#F28C28"), // P1 hi-vis orange
            Hex("#F2C230"), // P2 safety yellow
            Hex("#4FA3D9"), // P3 sky blue
            Hex("#B57EDC"), // P4 lilac
        };

        // Light story (derived from the palette family)
        public static readonly Color KeyLight   = Hex("#FFF0D6"); // warm midday sun
        public static readonly Color AmbientSky = Hex("#AFC3D9"); // cool bounce
        public static readonly Color AmbientEquator = Hex("#B8B0A0"); // neutral plaster
        public static readonly Color AmbientGround  = Hex("#4A3D30"); // dark warm floor bounce

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
