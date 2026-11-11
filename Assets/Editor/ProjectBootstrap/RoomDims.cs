namespace ProjectBootstrap
{
    // One place for the house footprint used by every bootstrap step.
    public static class RoomDims
    {
        public const float HX = 6f;          // half width  (x), wall centre
        public const float HZ = 5f;          // half depth  (z), wall centre
        public const float Height = 2.8f;
        public const float Wall = 0.15f;
        public const float InX = HX - Wall / 2f;   // inner face, x
        public const float InZ = HZ - Wall / 2f;   // inner face, z
        public const float DX = InX - 0.005f;      // where defects sit
        public const float DZ = InZ - 0.005f;
    }
}
