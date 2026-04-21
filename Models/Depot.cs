namespace OtonomAracOptimizasyonu.Models;

public sealed class Depot : VehicleStorageArea
{
    public const int DepotCapacity = 3;

    public Depot(int positionMeters) : base(positionMeters, DepotCapacity)
    {
    }
}
