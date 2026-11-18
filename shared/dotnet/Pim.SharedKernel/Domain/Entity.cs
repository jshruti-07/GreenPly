namespace Pim.SharedKernel.Domain;

/// <summary>
/// Base class for entities identified by a database-generated <see cref="long"/> id.
/// Equality is identity-based; transient entities (Id == 0) are never equal.
/// </summary>
public abstract class Entity
{
    /// <summary>Database identity. 0 until persisted.</summary>
    public long Id { get; protected set; }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other || GetType() != other.GetType())
        {
            return false;
        }

        if (Id == 0 || other.Id == 0)
        {
            return ReferenceEquals(this, other);
        }

        return Id == other.Id;
    }

    public override int GetHashCode() => Id == 0
        ? base.GetHashCode()
        : HashCode.Combine(GetType(), Id);
}
