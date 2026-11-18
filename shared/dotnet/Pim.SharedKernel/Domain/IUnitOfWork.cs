namespace Pim.SharedKernel.Domain;

/// <summary>Commits a business transaction; implemented per module by EF Core DbContext.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
