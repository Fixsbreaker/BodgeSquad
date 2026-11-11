using UnityEngine;

namespace BodgeSquad.Art
{
    // Art bible — the single source for every color in the project.
    // Reference: the itch.io key art (Docs/KeyArt/). Match it, don't drift from it.
    // Style: Chunky low-poly cartoon renovation: big-headed blocky bodgers with signature hats,
    //        teal peeling wallpaper, warm wood, maroon/pink soft furnishings, duct-tape X's.
    // Mood:  Warm afternoon sun in a tired house: saturated, friendly, slightly grubby.
    public static class Palette
    {
        // House
        public static readonly Color Floor        = Hex("#C98B57"); // warm terracotta-wood floor
        public static readonly Color Walls        = Hex("#5B8E87"); // teal wallpaper
        public static readonly Color WallsDark    = Hex("#477A74"); // wallpaper stripe / shade
        public static readonly Color Plaster      = Hex("#D8B48C"); // plaster showing through peeling paper
        public static readonly Color PlasterEdge  = Hex("#A98262"); // torn edge
        public static readonly Color Wood         = Hex("#B4683D"); // doors, wardrobe, table
        public static readonly Color Trim         = Hex("#6B4330"); // frames, skirting, cornice
        public static readonly Color Sofa         = Hex("#A3404E"); // maroon sofa
        public static readonly Color Rug          = Hex("#C45A7E"); // pink rug
        public static readonly Color Leaf         = Hex("#7DAF4A"); // houseplant
        public static readonly Color Terracotta   = Hex("#C9693A"); // plant pot
        public static readonly Color Cream        = Hex("#FBF1DC"); // lampshade, mug, logo letters

        // Bodgers
        public static readonly Color CharacterPrimary   = Hex("#F28C28"); // hi-vis orange vest
        public static readonly Color CharacterSecondary = Hex("#3F5FA8"); // overalls blue
        public static readonly Color Skin        = Hex("#F5B78E");
        public static readonly Color Glove       = Hex("#4A3B3F"); // work gloves, boots
        public static readonly Color Props       = Hex("#7FA36B"); // avocado green (spray cans, tins)
        public static readonly Color Shadow      = Hex("#5B6478"); // damp slate (sky ground)
        public static readonly Color Accent      = Hex("#E2474E"); // logo red / inspector flag

        // Per-player hat tints, so everyone can tell who bodged what (same saturation family)
        public static readonly Color[] PlayerTints =
        {
            Hex("#F28C28"), // P1 hi-vis orange   — hard hat
            Hex("#F6C93B"), // P2 safety yellow   — backwards cap
            Hex("#4FA3D9"), // P3 sky blue        — beanie
            Hex("#B57EDC"), // P4 lilac           — goggles + bun
        };

        // Damage, bodges and the inspector
        public static readonly Color DamageDark  = Hex("#2B2025"); // crack / hole interior
        public static readonly Color Mould       = Hex("#5A5A2E"); // damp olive-brown
        public static readonly Color FreshPaint  = Hex("#78ABA2"); // "close enough" teal touch-up
        public static readonly Color Tape        = Hex("#A9AFB8"); // duct tape silver
        public static readonly Color Wallpaper   = Hex("#669990"); // off-roll teal wallpaper
        public static readonly Color InspectorSuit = Hex("#4F4A5E"); // plum-grey suit
        public static readonly Color InspectorHat  = Hex("#6B5E58"); // fedora
        public static readonly Color Hair        = Hex("#3A2A22"); // moustache, brows
        public static readonly Color Pass        = Hex("#7FA36B");
        public static readonly Color UiInk       = Hex("#2B2025"); // HUD panels, logo outline
        public static readonly Color UiPaper     = Hex("#FBF1DC"); // HUD text
        public static readonly Color TintYellow  = Hex("#F6C93B"); // logo yellow / mid-risk

        // Light story
        public static readonly Color KeyLight   = Hex("#FFE9C7"); // warm afternoon sun
        public static readonly Color LampLight  = Hex("#FFC98A"); // floor lamp
        public static readonly Color AmbientSky = Hex("#AFC3D9"); // cool bounce
        public static readonly Color AmbientEquator = Hex("#B8A690"); // warm plaster
        public static readonly Color AmbientGround  = Hex("#5A3F2E"); // warm floor bounce

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
