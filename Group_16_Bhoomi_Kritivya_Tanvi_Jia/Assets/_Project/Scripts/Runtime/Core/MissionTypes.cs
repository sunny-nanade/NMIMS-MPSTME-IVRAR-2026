namespace RescueSim.Core
{
    public enum MissionState { Briefing, Active, Paused, Completed, Failed }

    public enum FeedbackKind { Info, Success, Warning, Mistake }

    /// <summary>Stable objective ids shared between mission data, hazards and UI.</summary>
    public static class ObjectiveIds
    {
        public const string Power = "power";
        public const string Gas = "gas";
        public const string Fire = "fire";
        public const string Route = "route";
        public const string Locate = "locate";
        public const string Evacuate = "evacuate";
        public const string Enter = "enter";
        public const string SafeRoute = "safe-route";
        public const string Equip = "equip";
    }
}
