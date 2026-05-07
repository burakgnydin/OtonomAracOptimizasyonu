namespace OtonomAracOptimizasyonu.Models;

public enum VehicleTask
{
    InGarage = 0,
    GoingToDepot = 1,
    LoadingAtDepot = 2,
    ReturningHome = 3,
    UnloadingAtHome = 4,
    Completed = 5,
    ReversingToPocket = 6,
    WaitingInPocket = 7,
    WaitingInDepot = 8,
    NormalDrive = GoingToDepot,
    RetreatingToSafeArea = ReversingToPocket,
    WaitingInSafeArea = WaitingInPocket
}
