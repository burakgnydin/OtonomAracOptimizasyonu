namespace OtonomAracOptimizasyonu.Models;

public abstract class VehicleStorageArea
{
    private readonly HashSet<string> _vehicleIds = [];
    private readonly HashSet<string> _reservedVehicleIds = [];

    protected VehicleStorageArea(int positionMeters, int capacity)
    {
        if (positionMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positionMeters), "Konum negatif olamaz.");
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Kapasite sifirdan buyuk olmalidir.");
        }

        PositionMeters = positionMeters;
        Capacity = capacity;
    }

    public int PositionMeters { get; }

    public int Capacity { get; }

    public int Occupancy => _vehicleIds.Count;

    public int ReservationCount => _reservedVehicleIds.Count;

    public bool IsFull => Occupancy >= Capacity;

    public bool IsFullyAllocated => (Occupancy + ReservationCount) >= Capacity;

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
            throw new ArgumentException("Arac kimligi bos olamaz.", nameof(vehicleId));
        }

        return _vehicleIds.Remove(vehicleId);
    }

    public bool ContainsVehicle(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            throw new ArgumentException("Arac kimligi bos olamaz.", nameof(vehicleId));
        }

        return _vehicleIds.Contains(vehicleId);
    }

    public bool HasReservation(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            throw new ArgumentException("Arac kimligi bos olamaz.", nameof(vehicleId));
        }

        return _reservedVehicleIds.Contains(vehicleId);
    }

    public bool TryReserveSlot(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            throw new ArgumentException("Arac kimligi bos olamaz.", nameof(vehicleId));
        }

        if (_reservedVehicleIds.Contains(vehicleId))
        {
            return true;
        }

        if ((Occupancy + ReservationCount) >= Capacity)
        {
            return false;
        }

        return _reservedVehicleIds.Add(vehicleId);
    }

    public bool ReleaseReservation(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            throw new ArgumentException("Arac kimligi bos olamaz.", nameof(vehicleId));
        }

        return _reservedVehicleIds.Remove(vehicleId);
    }

    public void ResetOccupancy()
    {
        _vehicleIds.Clear();
    }
}
