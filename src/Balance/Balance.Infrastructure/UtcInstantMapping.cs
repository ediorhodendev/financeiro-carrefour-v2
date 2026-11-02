using System.Data;
using Dapper;
namespace Balance.Infrastructure;
internal static class UtcInstantMapping
{
    internal static void Configure()=>SqlMapper.AddTypeHandler(new Handler());
    private sealed class Handler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value)=>value switch
        {
            DateTime time=>new DateTimeOffset(DateTime.SpecifyKind(time,DateTimeKind.Utc)),
            DateTimeOffset instant=>instant.ToUniversalTime(),
            _=>throw new DataException("Unexpected timestamp representation.")
        };
        public override void SetValue(IDbDataParameter parameter,DateTimeOffset value)=>parameter.Value=value.UtcDateTime;
    }
}
