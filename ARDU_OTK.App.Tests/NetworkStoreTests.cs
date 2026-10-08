using System.Security.Cryptography;
using ARDU_OTK.Services.Store;
using ARDU_OTK.Shared.Network;
using ARDU_OTK.Shared.Security;
using Microsoft.Data.Sqlite;

namespace ARDU_OTK.App.Tests;

/// <summary>
/// Приём пакета сети в реестр стенда: стенд администратора выпускает пакет,
/// чистый стенд его принимает.
/// </summary>
public sealed class NetworkStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "otk-app-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, string> _trusted;
    private readonly List<SqliteCalibrationStore> _stores = [];

    public NetworkStoreTests()
    {
        Directory.CreateDirectory(_root);
        _trusted = new Dictionary<string, string>
        {
            [NetworkTrust.KeyIdOf(_key)] = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()),
        };
    }

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        SqliteConnection.ClearAllPools();
        _key.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог.
        }
    }

    [Fact]
    public async Task Fresh_stand_receives_users_and_references_of_admin()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var stand = await StoreAsync("stand");

        var report = await ApplyAsync(stand, await IssueAsync(admin));

        var state = await stand.GetNetworkStateAsync();
        Assert.True(state.IsJoined);
        Assert.Equal(1, state.Serial);
        Assert.Equal(1, report.Added);
        var reference = Assert.Single(await stand.ListReferencesAsync());
        Assert.Equal("Борт-А", reference.Name);
        Assert.NotNull(reference.NetworkId);
        Assert.Equal("admin", Assert.Single(await stand.ListNetworkUsersAsync()).Login);
    }

    [Fact]
    public async Task Local_reference_outside_network_is_retired_not_deleted()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var stand = await StoreAsync("stand");
        await CreateReferenceAsync(stand, "Самодельный", "COMPASS_USE,0");

        var report = await ApplyAsync(stand, await IssueAsync(admin));

        Assert.Equal(1, report.Retired);
        var all = await stand.ListReferencesAsync(includeRetired: true);
        Assert.True(all.Single(r => r.Name == "Самодельный").IsRetired);
        Assert.Equal("Борт-А", Assert.Single(await stand.ListReferencesAsync()).Name);
    }

    [Fact]
    public async Task Same_local_reference_is_linked_and_keeps_its_identity()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var stand = await StoreAsync("stand");
        var localId = await CreateReferenceAsync(stand, "борт-а", "COMPASS_USE,1");

        var report = await ApplyAsync(stand, await IssueAsync(admin));

        Assert.Equal(1, report.Linked);
        Assert.Equal(0, report.Added);
        var reference = Assert.Single(await stand.ListReferencesAsync(includeRetired: true));
        Assert.Equal(localId, reference.Id);
        Assert.NotNull(reference.NetworkId);
    }

    [Fact]
    public async Task Different_local_reference_with_same_name_goes_to_archive_under_new_name()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var stand = await StoreAsync("stand");
        await CreateReferenceAsync(stand, "Борт-А", "COMPASS_USE,0");

        var report = await ApplyAsync(stand, await IssueAsync(admin));

        Assert.Equal("Борт-А (местный)", Assert.Single(report.RenamedLocal));
        var all = await stand.ListReferencesAsync(includeRetired: true);
        Assert.True(all.Single(r => r.Name == "Борт-А (местный)").IsRetired);
        Assert.NotNull(all.Single(r => r.Name == "Борт-А").NetworkId);
    }

    [Fact]
    public async Task Retirement_by_admin_reaches_stand_with_next_package()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var stand = await StoreAsync("stand");
        await ApplyAsync(stand, await IssueAsync(admin));

        var adminReference = Assert.Single(await admin.ListReferencesAsync());
        await admin.SetReferenceRetiredAsync(adminReference.Id, retired: true);
        await ApplyAsync(stand, await IssueAsync(admin));

        Assert.Empty(await stand.ListReferencesAsync());
        Assert.Equal(2, (await stand.GetNetworkStateAsync()).Serial);
    }

    [Fact]
    public async Task Old_package_cannot_be_replayed()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var stand = await StoreAsync("stand");
        var first = await IssueAsync(admin);
        await ApplyAsync(stand, first);
        await ApplyAsync(stand, await IssueAsync(admin));

        var check = NetworkPackageFile.Verify(first, _trusted, (await stand.GetNetworkStateAsync()).ToAccepted());

        Assert.Equal(NetworkPackageStatus.NotNewer, check.Status);
        await Assert.ThrowsAsync<CalibrationStoreException>(() => stand.ApplyNetworkSnapshotAsync(check.Snapshot!, Drafts(check.Snapshot!)));
    }

    [Fact]
    public async Task Last_admin_cannot_be_demoted()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var me = Assert.Single(await admin.ListNetworkUsersAsync());

        await Assert.ThrowsAsync<CalibrationStoreException>(() =>
            admin.SaveNetworkUserAsync(me with { Role = OtkRoles.Operator }));
    }

    [Fact]
    public async Task Last_login_survives_settings_save_and_package_apply()
    {
        var admin = await AdminWithReferenceAsync("Борт-А", "COMPASS_USE,1");
        var stand = await StoreAsync("stand");
        Assert.Equal(string.Empty, await stand.GetLastLoginAsync());

        await stand.SetLastLoginAsync(" admin ");
        await stand.SaveWorkstationSettingsAsync(await stand.GetWorkstationSettingsAsync());
        await ApplyAsync(stand, await IssueAsync(admin));

        Assert.Equal("admin", await stand.GetLastLoginAsync());
    }

    [Fact]
    public async Task Migration_from_v6_keeps_references_and_adds_network_columns()
    {
        var store = await StoreAsync("migrated");
        await CreateReferenceAsync(store, "Старый", "COMPASS_USE,1");
        store.Dispose();
        _stores.Remove(store);
        SqliteConnection.ClearAllPools();

        // Откатываем базу в форму v6: без NetworkId и NetworkUser.
        var dbPath = AppPaths.ForInstallState(isInstalled: false, Path.Combine(_root, "migrated")).DatabaseFilePath;
        await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP INDEX IX_Reference_NetworkId;
                DROP TABLE NetworkUser;
                ALTER TABLE Reference DROP COLUMN NetworkId;
                PRAGMA user_version = 6;
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var reopened = await StoreAsync("migrated");

        var reference = Assert.Single(await reopened.ListReferencesAsync());
        Assert.Equal("Старый", reference.Name);
        Assert.Null(reference.NetworkId);
        Assert.Empty(await reopened.ListNetworkUsersAsync());
    }

    // ── Помощники ────────────────────────────────────────────────────────

    private async Task<SqliteCalibrationStore> StoreAsync(string name)
    {
        var store = new SqliteCalibrationStore(
            AppPaths.ForInstallState(isInstalled: false, Path.Combine(_root, name)), "test");
        await store.InitializeAsync();
        _stores.Add(store);
        return store;
    }

    private async Task<SqliteCalibrationStore> AdminWithReferenceAsync(string referenceName, string paramLine)
    {
        var admin = await StoreAsync("admin");
        await admin.CreateNetworkAsync(new NetworkUser(
            Guid.NewGuid(), "admin", "Админ", OtkRoles.Admin, true, SecretHasher.Hash("admin-pass-1", 1000), null));
        await CreateReferenceAsync(admin, referenceName, paramLine);
        return admin;
    }

    private async Task<long> CreateReferenceAsync(SqliteCalibrationStore store, string name, string paramLine)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".param");
        await File.WriteAllTextAsync(path, paramLine + "\n");
        return await store.CreateReferenceAsync(new NewCalibrationReference(
            name, string.Empty, ReferenceParameters.Load(path), 10, 10, false, "тест"));
    }

    private async Task<string> IssueAsync(SqliteCalibrationStore admin)
    {
        var snapshot = await admin.PrepareNetworkSnapshotAsync("Админ");
        var file = NetworkPackageFile.Sign(snapshot, _key);
        await admin.CommitNetworkIssueAsync(snapshot);
        return file;
    }

    private async Task<NetworkApplyReport> ApplyAsync(SqliteCalibrationStore stand, string file)
    {
        var check = NetworkPackageFile.Verify(file, _trusted, (await stand.GetNetworkStateAsync()).ToAccepted());
        Assert.Equal(NetworkPackageStatus.Accepted, check.Status);
        return await stand.ApplyNetworkSnapshotAsync(check.Snapshot!, Drafts(check.Snapshot!));
    }

    /// <summary>Заготовки — тем же путём, что и в NetworkService.</summary>
    private static Dictionary<Guid, NewCalibrationReference> Drafts(NetworkSnapshot snapshot) =>
        snapshot.References.ToDictionary(
            static r => r.Id,
            static r => ReferencePackage.FromJson(r.PackageJson).ToDraft(r.Name.Trim()));
}
