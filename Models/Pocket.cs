namespace OtonomAracOptimizasyonu.Models;

public sealed class Pocket : VehicleStorageArea
{
    public const int PocketCapacity = 1;

    public Pocket(int positionMeters) : base(positionMeters, PocketCapacity)
    {
    }
}
