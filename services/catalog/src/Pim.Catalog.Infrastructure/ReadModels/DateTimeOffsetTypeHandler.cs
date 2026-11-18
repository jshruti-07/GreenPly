using System.Data;
using Dapper;

namespace Pim.Catalog.Infrastructure.ReadModels;

/// <summary>
/// Maps PostgreSQL <c>timestamptz</c> to <see cref="DateTimeOffset"/>.
///
/// Npgsql's default CLR type for timestamptz is <see cref="DateTime"/> with
/// Kind=Utc. Dapper reads the provider default and then assigns it, so a DTO
/// declaring DateTimeOffset — which several do, because the API contract is an
/// offset-bearing timestamp — fails to materialise.
///
/// The positional code this replaced never hit it, because every one of those
/// call sites said <c>reader.GetFieldValue&lt;DateTimeOffset&gt;(i)</c>: an
/// explicit request Npgsql honours. Dapper has no such hint, so the conversion
/// has to live here.
///
/// Registered once in <c>CatalogModule</c>. One handler beats a DateTime
/// property on every row record plus a conversion at every call site, which is
/// the shape of duplication this migration exists to remove.
/// </summary>
internal sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset offset => offset,

        // Kind=Utc from timestamptz. DateTimeOffset(DateTime) reads Kind, so a
        // UTC DateTime yields a zero offset — correct, and not a reinterpretation
        // of the instant.
        DateTime { Kind: DateTimeKind.Utc } utc => new DateTimeOffset(utc, TimeSpan.Zero),

        // timestamp (no zone) would arrive Unspecified. The catalogue has none,
        // and guessing a zone for one would silently shift the instant — so
        // treat it as UTC only because that is what this schema means, and say so.
        DateTime local => new DateTimeOffset(
            DateTime.SpecifyKind(local, DateTimeKind.Utc), TimeSpan.Zero),

        _ => throw new DataException(
            $"Cannot convert {value?.GetType().Name ?? "null"} to DateTimeOffset."),
    };

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        // Npgsql wants UTC for timestamptz; sending an offset-bearing value
        // that is not UTC throws at execution time.
        parameter.Value = value.UtcDateTime;
        parameter.DbType = DbType.DateTime2;
    }
}
