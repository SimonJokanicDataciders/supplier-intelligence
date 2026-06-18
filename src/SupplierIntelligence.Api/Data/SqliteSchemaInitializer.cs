using Microsoft.EntityFrameworkCore;

namespace SupplierIntelligence.Api.Data;

public static class SqliteSchemaInitializer
{
    public static async Task EnsureResearchSchemaAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Suppliers" ADD COLUMN "IsArchived" INTEGER NOT NULL DEFAULT 0;""",
                cancellationToken);
        }
        catch (Exception exception) when (
            exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ResearchSources" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ResearchSources" PRIMARY KEY AUTOINCREMENT,
                "SupplierId" INTEGER NOT NULL,
                "SourceCheckId" INTEGER NULL,
                "SourceName" TEXT NOT NULL,
                "Url" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Relevance" TEXT NOT NULL,
                "Summary" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_ResearchSources_SourceChecks_SourceCheckId" FOREIGN KEY ("SourceCheckId") REFERENCES "SourceChecks" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_ResearchSources_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SupplierFacts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SupplierFacts" PRIMARY KEY AUTOINCREMENT,
                "SupplierId" INTEGER NOT NULL,
                "ResearchSourceId" INTEGER NULL,
                "FactType" TEXT NOT NULL,
                "Value" TEXT NOT NULL,
                "EvidenceText" TEXT NOT NULL,
                "SourceName" TEXT NOT NULL,
                "SourceUrl" TEXT NOT NULL,
                "Confidence" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_SupplierFacts_ResearchSources_ResearchSourceId" FOREIGN KEY ("ResearchSourceId") REFERENCES "ResearchSources" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_SupplierFacts_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SupplierMatchCandidates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SupplierMatchCandidates" PRIMARY KEY AUTOINCREMENT,
                "SupplierId" INTEGER NOT NULL,
                "CandidateName" TEXT NOT NULL,
                "CountryCode" TEXT NULL,
                "WebsiteUrl" TEXT NULL,
                "SourceName" TEXT NULL,
                "SourceUrl" TEXT NULL,
                "ConfidenceScore" INTEGER NOT NULL,
                "MatchReason" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "ReviewedAt" TEXT NULL,
                CONSTRAINT "FK_SupplierMatchCandidates_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "CompareBoards" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CompareBoards" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "CompareBoardSuppliers" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CompareBoardSuppliers" PRIMARY KEY AUTOINCREMENT,
                "CompareBoardId" INTEGER NOT NULL,
                "SupplierId" INTEGER NOT NULL,
                "SortOrder" INTEGER NOT NULL,
                "AddedAt" TEXT NOT NULL,
                CONSTRAINT "FK_CompareBoardSuppliers_CompareBoards_CompareBoardId" FOREIGN KEY ("CompareBoardId") REFERENCES "CompareBoards" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_CompareBoardSuppliers_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ResearchSources_SupplierId" ON "ResearchSources" ("SupplierId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ResearchSources_SourceCheckId" ON "ResearchSources" ("SourceCheckId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_SupplierFacts_SupplierId" ON "SupplierFacts" ("SupplierId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_SupplierFacts_ResearchSourceId" ON "SupplierFacts" ("ResearchSourceId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_SupplierMatchCandidates_SupplierId" ON "SupplierMatchCandidates" ("SupplierId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_CompareBoardSuppliers_CompareBoardId" ON "CompareBoardSuppliers" ("CompareBoardId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_CompareBoardSuppliers_SupplierId" ON "CompareBoardSuppliers" ("SupplierId");""",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_CompareBoardSuppliers_CompareBoardId_SupplierId" ON "CompareBoardSuppliers" ("CompareBoardId", "SupplierId");""",
            cancellationToken);
    }
}
