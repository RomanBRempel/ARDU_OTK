using System.Net;
using System.Net.Http.Json;
using ARDU_OTK.Shared.Contracts;
using ARDU_OTK.Shared.Security;

namespace ARDU_OTK.Tests;

public sealed class AuthTests
{
    [Fact]
    public async Task Unknown_login_and_wrong_password_answer_identically()
    {
        using var server = new OtkServer();
        await server.AdminAsync();

        var unknown = await server.TryLoginAsync("nobody", "whatever-1");
        var wrong = await server.TryLoginAsync(OtkServer.AdminLogin, "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await DetailAsync(unknown), await DetailAsync(wrong));

        // traceId у каждого ответа свой, поэтому сравнивается текст отказа.
        static async Task<string?> DetailAsync(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())?.Detail;
    }

    [Fact]
    public async Task Login_locks_after_repeated_failures_and_unlocks_after_timeout()
    {
        using var server = new OtkServer();
        await server.AdminAsync();

        for (var i = 0; i < 5; i++)
        {
            await server.TryLoginAsync(OtkServer.AdminLogin, "wrong-password");
        }

        var locked = await server.TryLoginAsync(OtkServer.AdminLogin, OtkServer.AdminPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);

        server.Clock.Now += TimeSpan.FromMinutes(16);
        var unlocked = await server.TryLoginAsync(OtkServer.AdminLogin, OtkServer.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [Fact]
    public async Task Session_expires()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();

        server.Clock.Now += TimeSpan.FromHours(13);

        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Role_change_revokes_existing_sessions()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();
        var user = await OtkServer.CreateUserAsync(admin, "ivanov", OtkRoles.Controller);
        var controller = await server.LoginAsync("ivanov", "user-password-1");
        Assert.Equal(HttpStatusCode.OK, (await controller.GetAsync("/api/auth/me")).StatusCode);

        (await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest(null, OtkRoles.Operator, null)))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await controller.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Last_active_admin_cannot_be_demoted_or_disabled()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();
        var me = (await admin.GetFromJsonAsync<UserInfo>("/api/auth/me"))!;

        var demote = await admin.PatchAsJsonAsync($"/api/users/{me.Id}", new UpdateUserRequest(null, OtkRoles.Operator, null));
        var disable = await admin.PatchAsJsonAsync($"/api/users/{me.Id}", new UpdateUserRequest(null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, disable.StatusCode);
    }

    [Fact]
    public async Task Disabled_user_cannot_log_in()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();
        var user = await OtkServer.CreateUserAsync(admin, "petrov", OtkRoles.Operator);

        (await admin.PatchAsJsonAsync($"/api/users/{user.Id}", new UpdateUserRequest(null, null, false)))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await server.TryLoginAsync("petrov", "user-password-1")).StatusCode);
    }
}

public sealed class ReferenceTests
{
    [Fact]
    public async Task Only_admin_publishes_references()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();
        await OtkServer.CreateUserAsync(admin, "operator1", OtkRoles.Operator);
        await OtkServer.CreateUserAsync(admin, "control1", OtkRoles.Controller);
        var op = await server.LoginAsync("operator1", "user-password-1");
        var controller = await server.LoginAsync("control1", "user-password-1");
        var anonymous = server.CreateClient();

        var request = new PublishReferenceRequest(OtkServer.Package("Борт-А"));

        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync("/api/references", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await controller.PostAsJsonAsync("/api/references", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/references", request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/references", request)).StatusCode);
    }

    [Fact]
    public async Task Name_is_unique_across_network_case_insensitively()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();

        (await admin.PostAsJsonAsync("/api/references", new PublishReferenceRequest(OtkServer.Package("Борт-А"))))
            .EnsureSuccessStatusCode();
        var duplicate = await admin.PostAsJsonAsync("/api/references", new PublishReferenceRequest(OtkServer.Package("  борт-а ")));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Tampered_script_is_rejected()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();

        var tampered = OtkServer.Package("Борт-Б", scriptHash: new string('0', 64));
        var response = await admin.PostAsJsonAsync("/api/references", new PublishReferenceRequest(tampered));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Package_without_version_is_rejected()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();

        var json = OtkServer.Package("Борт-В").Replace("\"Version\":1,", string.Empty, StringComparison.Ordinal);
        var response = await admin.PostAsJsonAsync("/api/references", new PublishReferenceRequest(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class SyncTests
{
    [Fact]
    public async Task Pull_returns_only_changes_after_cursor()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();
        await OtkServer.CreateUserAsync(admin, "operator1", OtkRoles.Operator);
        var op = await server.LoginAsync("operator1", "user-password-1");

        var first = (await op.GetFromJsonAsync<SyncPullResponse>("/api/sync/pull?since=0"))!;
        Assert.Equal(2, first.Users.Count);
        Assert.Empty(first.References);

        var published = await admin.PostAsJsonAsync("/api/references", new PublishReferenceRequest(OtkServer.Package("Борт-А")));
        var reference = (await published.Content.ReadFromJsonAsync<ReferenceRecord>())!;

        var second = (await op.GetFromJsonAsync<SyncPullResponse>($"/api/sync/pull?since={first.Seq}"))!;
        Assert.Empty(second.Users);
        Assert.Equal(reference.Id, Assert.Single(second.References).Id);

        (await admin.PostAsync($"/api/references/{reference.Id}/retire", null)).EnsureSuccessStatusCode();

        var third = (await op.GetFromJsonAsync<SyncPullResponse>($"/api/sync/pull?since={second.Seq}"))!;
        Assert.NotNull(Assert.Single(third.References).RetiredUtc);
        Assert.Equal(first.ServerId, third.ServerId);
    }

    [Fact]
    public async Task Paged_pull_loses_nothing()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();
        for (var i = 0; i < 3; i++)
        {
            await OtkServer.CreateUserAsync(admin, $"op{i}", OtkRoles.Operator);
            (await admin.PostAsJsonAsync("/api/references", new PublishReferenceRequest(OtkServer.Package($"Эталон {i}"))))
                .EnsureSuccessStatusCode();
        }

        var seen = new List<long>();
        long since = 0;
        SyncPullResponse page;
        do
        {
            page = (await admin.GetFromJsonAsync<SyncPullResponse>($"/api/sync/pull?since={since}&limit=2"))!;
            seen.AddRange(page.Users.Select(u => u.Seq));
            seen.AddRange(page.References.Select(r => r.Seq));
            since = page.Seq;
        }
        while (page.HasMore);

        Assert.Equal(7, seen.Count); // админ + 3 оператора + 3 эталона
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task Replica_carries_pin_only_for_offline_roles_and_never_password()
    {
        using var server = new OtkServer();
        var admin = await server.AdminAsync();
        var op = await OtkServer.CreateUserAsync(admin, "operator1", OtkRoles.Operator);
        var me = (await admin.GetFromJsonAsync<UserInfo>("/api/auth/me"))!;

        (await admin.PutAsJsonAsync($"/api/users/{op.Id}/pin", new SetPinRequest("4321"))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/users/{me.Id}/pin", new SetPinRequest("1234"))).EnsureSuccessStatusCode();

        var raw = await admin.GetStringAsync("/api/sync/pull?since=0");
        var pull = System.Text.Json.JsonSerializer.Deserialize<SyncPullResponse>(raw, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        var opReplica = pull.Users.Single(u => u.Id == op.Id);
        Assert.True(SecretHasher.Verify("4321", opReplica.OfflinePinHash));
        Assert.Null(pull.Users.Single(u => u.Id == me.Id).OfflinePinHash);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pull_requires_session()
    {
        using var server = new OtkServer();
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.CreateClient().GetAsync("/api/sync/pull")).StatusCode);
    }
}

public sealed class SecretTests
{
    [Fact]
    public void Hash_verifies_only_the_original_secret()
    {
        var hash = SecretHasher.Hash("1234", iterations: 1000);

        Assert.True(SecretHasher.Verify("1234", hash));
        Assert.False(SecretHasher.Verify("1235", hash));
        Assert.False(SecretHasher.Verify("1234", hash[..^4] + "AAAA"));
        Assert.False(SecretHasher.Verify("1234", "garbage"));
        Assert.False(SecretHasher.Verify("1234", null));
    }

    [Theory]
    [InlineData("123", false)]
    [InlineData("1234", true)]
    [InlineData("12a4", false)]
    [InlineData("123456789", false)]
    public void Pin_policy(string pin, bool valid) => Assert.Equal(valid, SecretPolicy.ValidatePin(pin) is null);
}
