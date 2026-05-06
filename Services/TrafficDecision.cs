namespace OtonomAracOptimizasyonu.Services;

public enum TrafficStopReason
{
    None = 0,
    CollisionRisk = 1,
    DeadlockAvoidance = 2
}

public sealed record ManeuverDirective(
    int SafeAreaPositionMeters,
    string YieldToVehicleId,
    string SafeAreaType);

public sealed record TrafficDecision(
    bool CanMove,
    TrafficStopReason StopReason,
    string Message,
    ManeuverDirective? ManeuverDirective = null)
{
    public static TrafficDecision MoveAllowed() => new(true, TrafficStopReason.None, "Harekete izin verildi", null);

    public static TrafficDecision Stop(TrafficStopReason reason, string message) => new(false, reason, message, null);

    public static TrafficDecision StartManeuver(ManeuverDirective directive, string message) =>
        new(true, TrafficStopReason.DeadlockAvoidance, message, directive);
}
