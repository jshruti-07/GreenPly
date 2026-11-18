using Npgsql;
using Pim.Catalog.Application.Lookups;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// The configured locales, read once and cached.
///
/// Singleton over its own <see cref="NpgsqlDataSource"/> rather than a scoped
/// reader over EF's connection, because this is consulted on every read request
/// to validate the requested display locale. A round trip per grid request to
/// re-learn a three-row table that changes when someone opens a new market
/// would be a cost paid thousands of times to avoid thinking once.
///
/// The cache has a TTL rather than being loaded at startup: a locale added by a
/// migration or by hand should appear without a restart, and a few minutes of
/// staleness on "which languages exist" harms nothing. It is deliberately NOT
/// invalidated on write — nothing in this service writes to `locale` yet, and a
/// invalidation hook with no writer to call it is dead code that looks like a
/// guarantee.
///
/// Disposable because of <see cref="_refreshLock"/>. The container owns the
/// lifetime — this is registered as a singleton, so dispose runs at host
/// shutdown and never mid-request. CA1001 is an error in this build precisely
/// so that an owned <see cref="SemaphoreSlim"/> cannot be added without
/// somebody deciding who releases it.
/// </summary>
public sealed class LocaleCatalog(NpgsqlDataSource dataSource) : ILocaleCatalog, IDisposable
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private const string Sql = """
        SELECT code, name, is_base
        FROM catalog.locale
        ORDER BY is_base DESC, code;
        """;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyList<LocaleDto>? _cached;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public async Task<IReadOnlyList<LocaleDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var snapshot = _cached;
        if (snapshot is not null && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
        {
            return snapshot;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the lock: several requests can arrive together on
            // an expired cache, and only the first should pay for the read.
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
            {
                return _cached;
            }

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            var locales = await DapperGateway.QueryOnAsync<LocaleDto>(
                connection, Sql, parameters: null, cancellationToken);

            _cached = locales;
            _cachedAt = DateTimeOffset.UtcNow;

            return locales;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<string?> GetBaseCodeAsync(CancellationToken cancellationToken)
    {
        var locales = await GetAllAsync(cancellationToken);
        return locales.FirstOrDefault(l => l.IsBase)?.Code;
    }

    public async Task<bool> ExistsAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var locales = await GetAllAsync(cancellationToken);

        // Locale codes are case-insensitive per BCP 47 ('en-gb' and 'en-GB' are
        // the same tag), so a caller must not be rejected over casing. The
        // stored spelling is what reaches the SQL — see DisplayLocale.Resolve.
        return locales.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Releases the refresh lock. Not the data source: this type is handed one
    /// it does not own, and the container disposes the singleton it created.
    /// Disposing a borrowed pool here would close connections other components
    /// are still using.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshLock.Dispose();
    }
}
