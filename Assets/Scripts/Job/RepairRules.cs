namespace BodgeSquad.Job
{
    // Defect hierarchy borrowed from real home-inspection reports (InterNACHI):
    // cosmetic → will fail later → safety hazard.
    public enum Severity { Cosmetic, FutureFailure, Safety }

    public enum DefectKind { Crack, Hole, Mould, ExposedWiring }

    public enum KitType { Filler, AntiMould, WiringKit, PaintTin, WallpaperRoll, DuctTape }

    // How a defect ended up. Furniture is decided at inspection time (something pushed in front of it).
    public enum RepairState { Untouched, Fixed, PaintedOver, Wallpapered, Taped, HiddenByFurniture }

    // The whole rule-set in one place, so it stays consistent and learnable:
    // the same shortcut on the same severity always carries the same risk.
    public static class RepairRules
    {
        public const int StartingBudget = 400;
        public const int ContractFee = 1200;
        public const float RenovationSeconds = 8 * 60;

        public static int Price(KitType k) => k switch
        {
            KitType.Filler => 60,
            KitType.AntiMould => 120,
            KitType.WiringKit => 200,
            KitType.PaintTin => 15,
            KitType.WallpaperRoll => 30,
            KitType.DuctTape => 10,
            _ => 0,
        };

        public static string KitName(KitType k) => k switch
        {
            KitType.Filler => "Filler",
            KitType.AntiMould => "Anti-Mould",
            KitType.WiringKit => "Wiring Kit",
            KitType.PaintTin => "Paint",
            KitType.WallpaperRoll => "Wallpaper",
            KitType.DuctTape => "Duct Tape",
            _ => k.ToString(),
        };

        // One line on the shelf / in the prompt: what this item actually does.
        public static string KitBlurb(KitType k) => k switch
        {
            KitType.Filler => "Properly fixes cracks and holes",
            KitType.AntiMould => "Properly fixes mould",
            KitType.WiringKit => "Properly fixes exposed wiring",
            KitType.PaintTin => "Hides anything (risky)",
            KitType.WallpaperRoll => "Hides anything (less risky)",
            KitType.DuctTape => "Hides anything (very risky)",
            _ => "",
        };

        public static string KindName(DefectKind d) => d switch
        {
            DefectKind.ExposedWiring => "Exposed wiring",
            DefectKind.Mould => "Mould",
            DefectKind.Hole => "Hole",
            _ => "Crack",
        };

        public static bool IsProperFix(KitType k) => k is KitType.Filler or KitType.AntiMould or KitType.WiringKit;

        // Which proper kit actually fixes which defect.
        public static KitType ProperKitFor(DefectKind d) => d switch
        {
            DefectKind.Crack => KitType.Filler,
            DefectKind.Hole => KitType.Filler,
            DefectKind.Mould => KitType.AntiMould,
            DefectKind.ExposedWiring => KitType.WiringKit,
            _ => KitType.Filler,
        };

        public static RepairState ConcealWith(KitType k) => k switch
        {
            KitType.PaintTin => RepairState.PaintedOver,
            KitType.WallpaperRoll => RepairState.Wallpapered,
            KitType.DuctTape => RepairState.Taped,
            _ => RepairState.Untouched,
        };

        // Flaw Obviousness: chance the inspector notices, 0..1.
        public static float Obviousness(RepairState s, Severity sev)
        {
            int i = (int)sev;
            return s switch
            {
                RepairState.Untouched => 1f,
                RepairState.Fixed => 0f,
                RepairState.PaintedOver => new[] { 0.25f, 0.50f, 0.80f }[i],
                RepairState.Wallpapered => new[] { 0.15f, 0.35f, 0.70f }[i],
                RepairState.Taped => new[] { 0.45f, 0.65f, 0.90f }[i],
                RepairState.HiddenByFurniture => new[] { 0.30f, 0.40f, 0.60f }[i],
                _ => 1f,
            };
        }

        public static int Penalty(Severity s) => s switch
        {
            Severity.Cosmetic => 150,
            Severity.FutureFailure => 300,
            Severity.Safety => 600,
            _ => 0,
        };

        public static string SeverityName(Severity s) => s switch
        {
            Severity.Cosmetic => "cosmetic",
            Severity.FutureFailure => "will fail later",
            Severity.Safety => "SAFETY HAZARD",
            _ => s.ToString(),
        };

        public static string StateVerb(RepairState s) => s switch
        {
            RepairState.Untouched => "left as is",
            RepairState.Fixed => "properly fixed",
            RepairState.PaintedOver => "painted over",
            RepairState.Wallpapered => "wallpapered over",
            RepairState.Taped => "taped over",
            RepairState.HiddenByFurniture => "hidden behind furniture",
            _ => s.ToString(),
        };
    }
}
