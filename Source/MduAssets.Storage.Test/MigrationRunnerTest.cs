using MduAssets.Storage;

namespace MduAssets.Storage.Test;

public sealed class MigrationRunnerTest : IDisposable
{
    private readonly TempDataDirectory _temp = new();
    private readonly Database _database;

    public MigrationRunnerTest()
    {
        _database = new Database(_temp.Directory);
    }

    public void Dispose() => _temp.Dispose();

    private static Migration CreateTable(int version, string table) =>
        new(version, table, $"CREATE TABLE {table} (id INTEGER PRIMARY KEY);");

    [Fact]
    public void AppliesMigrationsInOrderOnce()
    {
        Migration[] migrations = [CreateTable(1, "first"), CreateTable(2, "second")];

        Assert.Equal([1, 2], MigrationRunner.Apply(_database, migrations));
        Assert.Empty(MigrationRunner.Apply(_database, migrations));
        Assert.Equal(["first", "schema_migrations", "second"], Tables());
    }

    [Fact]
    public void AppliesOnlyNewMigrations()
    {
        MigrationRunner.Apply(_database, [CreateTable(1, "first")]);

        Assert.Equal([2], MigrationRunner.Apply(_database, [CreateTable(1, "first"), CreateTable(2, "second")]));
    }

    [Fact]
    public void RollsBackAFailingMigration()
    {
        Migration broken = new(1, "broken", "CREATE TABLE half (id INTEGER); THIS IS NOT SQL;");

        Assert.ThrowsAny<Exception>(() => MigrationRunner.Apply(_database, [broken]));
        Assert.Equal(["schema_migrations"], Tables());
    }

    [Fact]
    public void RejectsAChangedMigration()
    {
        MigrationRunner.Apply(_database, [CreateTable(1, "first")]);

        var error = Assert.Throws<InvalidOperationException>(
            () => MigrationRunner.Apply(_database, [new Migration(1, "first", "CREATE TABLE other (id INTEGER);")]));
        Assert.Contains("was changed", error.Message);
    }

    [Fact]
    public void RejectsADatabaseFromANewerBuild()
    {
        MigrationRunner.Apply(_database, [CreateTable(1, "first"), CreateTable(2, "second")]);

        var error = Assert.Throws<InvalidOperationException>(() => MigrationRunner.Apply(_database, [CreateTable(1, "first")]));
        Assert.Contains("newer version", error.Message);
    }

    [Theory]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 1, 1 })]
    [InlineData(new[] { 1, 3 })]
    public void RejectsGapsAndDuplicates(int[] versions)
    {
        var migrations = versions.Select((v, i) => CreateTable(v, "t" + i)).ToArray();

        Assert.Throws<InvalidOperationException>(() => MigrationRunner.Apply(_database, migrations));
    }

    [Fact]
    public void EmbeddedMigrationsAreNumberedWithoutGaps()
    {
        var migrations = Migration.Embedded();

        Assert.Equal(Enumerable.Range(1, migrations.Count), migrations.Select(m => m.Version));
    }

    [Fact]
    public void UsesWriteAheadLogging()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        Assert.Equal("wal", command.ExecuteScalar());
    }

    private List<string> Tables()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }
}
