namespace OtonomAracOptimizasyonu.Models;

public enum VehicleTask
{
    InGarage = 0,
    GoingToDepot = 1,
    LoadingAtDepot = 2,
    ReturningHome = 3,
    UnloadingAtHome = 4,
    Completed = 5,
    GoingToPocketForYielding = 6,
    WaitingInPocket = 7,
    NormalDrive = GoingToDepot,
    RetreatingToSafeArea = GoingToPocketForYielding,
    WaitingInSafeArea = WaitingInPocket
}
