using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            using var stream=typeof(InitialSchema).Assembly.GetManifestResourceStream("Ledger.Infrastructure.Persistence.Schema.sql")
                ?? throw new InvalidOperationException("Embedded schema missing.");
            using var reader=new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Financial rollback requires an audited restore procedure.");
        }
    }
}
