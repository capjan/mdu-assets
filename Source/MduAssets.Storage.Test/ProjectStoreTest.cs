using MduAssets.Storage;

namespace MduAssets.Storage.Test;

public sealed class ProjectStoreTest : IDisposable
{
    private readonly TempDataDirectory _temp = new();
    private readonly Database _database;

    public ProjectStoreTest()
    {
        _database = new Database(_temp.Directory);
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>An instance from before roles had only admins; they become owners of the existing projects.</summary>
    [Fact]
    public void UpgradingMakesExistingAdminsOwners()
    {
        var migrations = Migration.Embedded();
        MigrationRunner.Apply(_database, migrations.Where(m => m.Version <= 2).ToList());
        var users = new LegacyUsers(_database);
        users.Insert("admin", isAdmin: true);
        users.Insert("reader", isAdmin: false);

        MigrationRunner.Apply(_database, migrations);

        var projects = new ProjectStore(_database);
        var store = new UserStore(_database);
        var project = projects.Find("default")!;
        Assert.Equal(ProjectRole.Owner, projects.RoleOf(project.Id, store.Find("admin")!.Id));
        Assert.Null(projects.RoleOf(project.Id, store.Find("reader")!.Id));
        Assert.Equal("", project.Description);
        Assert.False(store.Find("admin")!.MustChangePassword);
    }

    [Fact]
    public void ListsProjectsWithTheRoleOfTheCaller()
    {
        MigrationRunner.Apply(_database, Migration.Embedded());
        var users = new UserStore(_database);
        var projects = new ProjectStore(_database);
        var ann = users.Create("ann", "hash", isAdmin: false);
        var bob = users.Create("bob", "hash", isAdmin: false);
        projects.Create("zeta", "Zeta", "", ann.Id);
        projects.Create("alpha", "Alpha", "", bob.Id);
        projects.SetRole(projects.Find("zeta")!.Id, bob.Id, ProjectRole.Viewer);

        Assert.Equal([("alpha", ProjectRole.Owner), ("zeta", ProjectRole.Viewer)],
            projects.List(bob.Id, all: false).Select(p => (p.Slug, p.Role!.Value)));
        Assert.Equal(["alpha", "default", "zeta"], projects.List(ann.Id, all: true).Select(p => p.Slug));
        Assert.Throws<InvalidOperationException>(() => projects.Create("alpha", "Again", "", ann.Id));
        Assert.Throws<LastOwnerException>(() => projects.RemoveMember(projects.Find("zeta")!.Id, ann.Id));
    }

    /// <summary>Writes users the way the schema before M3 did, without the newer columns.</summary>
    private sealed class LegacyUsers(Database database)
    {
        public void Insert(string name, bool isAdmin)
        {
            using var connection = database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO users (name, password_hash, is_admin, created_at) VALUES ($name, 'hash', $admin, '2026-01-01T00:00:00.000Z');";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$admin", isAdmin);
            command.ExecuteNonQuery();
        }
    }
}
