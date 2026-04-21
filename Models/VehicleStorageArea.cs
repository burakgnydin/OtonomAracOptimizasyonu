namespace OtonomAracOptimizasyonu.Models;

public abstract class VehicleStorageArea
{
    private readonly HashSet<string> _vehicleIds = [];

    protected VehicleStorageArea(int positionMeters, int capacity)
    {
        if (positionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positionMeters), "Position cannot be negative.");
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        PositionMeters = positionMeters;
        Capacity = capacity;
    }

    public int PositionMeters { get; }

    public int Capacity { get; }

    public int Occupancy => _vehicleIds.Count;

    public bool IsFull => Occupancy >= Capacity;

    public IReadOnlyCollection<string> VehicleIds => _vehicleIds;

    public bool TryAddVehicle(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        if (IsFull)
        {
            return false;
        }

        return _vehicleIds.Add(vehicle.Id);
    }

    public bool RemoveVehicle(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            throw new ArgumentException("Vehicle id cannot be empty.", nameof(vehicleId));
        }

        return _vehicleIds.Remove(vehicleId);
    }

    public bool ContainsVehicle(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            throw new ArgumentException("Vehicle id cannot be empty.", nameof(vehicleId));
        }

        return _vehicleIds.Contains(vehicleId);
    }
}
