using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARDU_OTK.Shared.Network;
using ARDU_OTK.Shared.Security;

namespace ARDU_OTK.Tests;

public sealed class NetworkPackageTests : IDisposable
{
    private readonly ECDsa _admin = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, string> _trusted;
    private readonly Guid _networkId = Guid.NewGuid();

    public NetworkPackageTests()
    {
        _trusted = new Dictionary<string, string>
        {
            [NetworkTrust.KeyIdOf(_admin)] = Convert.ToBase64String(_admin.ExportSubjectPublicKeyInfo()),
        };
    }

    public void Dispose()
    {
        _admin.Dispose();
        _stranger.Dispose();
    }

    [Fact]
    public void Signed_package_is_accepted_with_its_content()
    {
        var file = NetworkPackageFile.Sign(Snapshot(serial: 1), _admin);

        var check = NetworkPackageFile.Verify(file, _trusted, AcceptedNetworkState.None);

        Assert.Equal(NetworkPackageStatus.Accepted, check.Status);
        Assert.Equal(2, check.Snapshot!.Users.Count);
        Assert.Single(check.Snapshot.References);
    }

    [Fact]
    public void Package_signed_by_unknown_key_is_rejected()
    {
        var file = NetworkPackageFile.Sign(Snapshot(serial: 1), _stranger);

        Assert.Equal(NetworkPackageStatus.Rejected, NetworkPackageFile.Verify(file, _trusted, AcceptedNetworkState.None).Status);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        var file = JsonSerializer.Deserialize<NetworkPackageFile>(
            NetworkPackageFile.Sign(Snapshot(serial: 1), _admin), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        // Подменяем роль оператора на администратора, подпись оставляем прежней.
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(file.Payload))
            .Replace("\"role\":\"operator\"", "\"role\":\"admin\"", StringComparison.Ordinal);
        file.Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var tampered = JsonSerializer.Serialize(file, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var check = NetworkPackageFile.Verify(tampered, _trusted, AcceptedNetworkState.None);

        Assert.Equal(NetworkPackageStatus.Rejected, check.Status);
        Assert.Contains("Подпись", check.Reason);
    }

    [Fact]
    public void Older_or_same_serial_is_not_applied()
    {
        var accepted = new AcceptedNetworkState(_networkId, 5);

        Assert.Equal(NetworkPackageStatus.NotNewer,
            NetworkPackageFile.Verify(NetworkPackageFile.Sign(Snapshot(serial: 5), _admin), _trusted, accepted).Status);
        Assert.Equal(NetworkPackageStatus.NotNewer,
            NetworkPackageFile.Verify(NetworkPackageFile.Sign(Snapshot(serial: 4), _admin), _trusted, accepted).Status);
        Assert.Equal(NetworkPackageStatus.Accepted,
            NetworkPackageFile.Verify(NetworkPackageFile.Sign(Snapshot(serial: 6), _admin), _trusted, accepted).Status);
    }

    [Fact]
    public void Package_of_another_network_is_rejected()
    {
        var file = NetworkPackageFile.Sign(Snapshot(serial: 10), _admin);

        var check = NetworkPackageFile.Verify(file, _trusted, new AcceptedNetworkState(Guid.NewGuid(), 1));

        Assert.Equal(NetworkPackageStatus.Rejected, check.Status);
    }

    [Fact]
    public void Package_without_active_admin_is_rejected()
    {
        var snapshot = Snapshot(serial: 1) with
        {
            Users = [new NetworkUser(Guid.NewGuid(), "op", "Оператор", OtkRoles.Operator, true, "x", null)],
        };

        var check = NetworkPackageFile.Verify(NetworkPackageFile.Sign(snapshot, _admin), _trusted, AcceptedNetworkState.None);

        Assert.Equal(NetworkPackageStatus.Rejected, check.Status);
    }

    [Fact]
    public void Reference_with_mismatched_hash_is_rejected()
    {
        var snapshot = Snapshot(serial: 1);
        snapshot = snapshot with { References = [snapshot.References[0] with { ParamHash = new string('b', 64) }] };

        var check = NetworkPackageFile.Verify(NetworkPackageFile.Sign(snapshot, _admin), _trusted, AcceptedNetworkState.None);

        Assert.Equal(NetworkPackageStatus.Rejected, check.Status);
    }

    [Fact]
    public void Production_trust_contains_the_key_stored_on_this_admin_machine()
    {
        // Ключ администратора есть только на его компьютере; в CI и у других
        // разработчиков проверять нечего.
        if (!OperatingSystem.IsWindows() || !NetworkSigningKeyStore.Exists)
        {
            return;
        }

        using var key = NetworkSigningKeyStore.Load()!;
        Assert.True(NetworkTrust.Production.ContainsKey(NetworkTrust.KeyIdOf(key)));
    }

    private NetworkSnapshot Snapshot(long serial) => new(
        _networkId,
        serial,
        DateTimeOffset.UtcNow,
        "Админ",
        [
            new NetworkUser(Guid.NewGuid(), "admin", "Админ", OtkRoles.Admin, true, SecretHasher.Hash("admin-pass-1", 1000), null),
            new NetworkUser(Guid.NewGuid(), "op", "Оператор", OtkRoles.Operator, true, SecretHasher.Hash("op-pass-12", 1000), SecretHasher.Hash("1234", 1000)),
        ],
        [new NetworkReference(Guid.NewGuid(), "Борт-А", new string('a', 64), null, OtkServer.Package("Борт-А"))]);
}
