namespace OtonomAracOptimizasyonu.Models;

public enum VehicleTask
{
    NormalDrive = 0,
    RetreatingToSafeArea = 1,
    WaitingInSafeArea = 2,
    LoadingAtDepot = 3,
    GoingToPocketForYielding = RetreatingToSafeArea,
    WaitingInPocket = WaitingInSafeArea
}
