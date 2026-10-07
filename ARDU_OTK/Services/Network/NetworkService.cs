using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ARDU_OTK.Services.Store;
using ARDU_OTK.Shared.Network;
using ARDU_OTK.Shared.Security;

namespace ARDU_OTK.Services.Network;

/// <summary>Итог обращения к пакету сети — для сообщения оператору.</summary>
public sealed record NetworkRefreshResult(bool Applied, bool Failed, string Message);

/// <summary>
/// Сеть ОТК на стенде: приём пакета, вход пользователя, а у администратора —
/// ведение пользователей и выпуск пакета.
/// </summary>
/// <remarks>
/// <para>
/// Пакет лежит в публичном репозитории GitHub (<see cref="NetworkSource"/>).
/// Станция читает его анонимно: ни учётной записи, ни токена ей не нужно.
/// Доверие даёт подпись администратора, закрытость — код сети. Токен GitHub
/// есть только у администратора и нужен только для выпуска.
/// </para>
/// <para>
/// 🔴 Каждое обращение к хранилищу — через <see cref="Task.Run(Func{Task})"/>:
/// нативная часть SQLite в UI-потоке WinUI роняет процесс без исключения
/// (см. <see cref="AppServices.InitializeAsync"/>).
/// </para>
/// </remarks>
public sealed class NetworkService
{
    private const int MaxFailedLogins = 5;

    private static readonly TimeSpan LoginLockout = TimeSpan.FromSeconds(30);

    private static readonly HttpClient Http = CreateHttp();

    private static readonly byte[] TokenEntropy = "ARDU_OTK.github-token.v1"u8.ToArray();

    private readonly SqliteCalibrationStore _store;

    private int _failedLogins;

    private DateTimeOffset _lockedUntil;

    public NetworkService(SqliteCalibrationStore store) => _store = store;

    /// <summary>Кто работает на стенде; <c>null</c> — никто не вошёл.</summary>
    public NetworkUser? Session { get; private set; }

    /// <summary>Вошёл администратор сети.</summary>
    public bool IsAdmin => Session?.Role == OtkRoles.Admin;

    /// <summary>Вход или выход пользователя.</summary>
    public event EventHandler? SessionChanged;

    /// <summary>На этом компьютере лежит ключ подписи администратора сети.</summary>
    public static bool HasSigningKey => NetworkSigningKeyStore.Exists;

    /// <summary>На этом компьютере сохранён токен GitHub для выпуска пакета.</summary>
    public static bool HasGitHubToken => File.Exists(TokenPath);

    private static string TokenPath => Path.Combine(Path.GetDirectoryName(NetworkSigningKeyStore.KeyPath)!, "github-token.bin");

    public Task<NetworkState> GetStateAsync() => Task.Run(() => _store.GetNetworkStateAsync());

    // ── Приём пакета ─────────────────────────────────────────────────────

    /// <summary>Первое подключение стенда: скачать пакет и открыть его кодом сети.</summary>
    public Task<NetworkRefreshResult> ConnectAsync(string code) => Task.Run(async () =>
    {
        if (NetworkSeal.NormalizeCode(code) is null)
        {
            return Fail("Код сети набран неверно: 24 знака, латинские буквы и цифры.");
        }

        var (text, error) = await DownloadAsync().ConfigureAwait(false);
        return text is null ? Fail(error!) : await ApplySealedAsync(text, code).ConfigureAwait(false);
    });

    /// <summary>Проверка обновления пакета подключённым стендом.</summary>
    public Task<NetworkRefreshResult> RefreshAsync() => Task.Run(async () =>
    {
        var state = await _store.GetNetworkStateAsync().ConfigureAwait(false);
        if (state.NetworkCode.Length == 0)
        {
            return Fail("Код сети на этом стенде не сохранён: примите пакет из файла с кодом сети.");
        }

        var (text, error) = await DownloadAsync().ConfigureAwait(false);
        return text is null ? Fail(error!) : await ApplySealedAsync(text, state.NetworkCode).ConfigureAwait(false);
    });

    /// <summary>
    /// Приём пакета из файла — для стенда без интернета.
    /// </summary>
    /// <param name="code">Код сети; пусто — взять сохранённый на стенде.</param>
    public Task<NetworkRefreshResult> ImportFileAsync(string path, string? code) => Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            code = (await _store.GetNetworkStateAsync().ConfigureAwait(false)).NetworkCode;
        }

        return await ApplySealedAsync(await File.ReadAllTextAsync(path).ConfigureAwait(false), code).ConfigureAwait(false);
    });

    private async Task<NetworkRefreshResult> ApplySealedAsync(string sealedText, string? code)
    {
        var signed = NetworkSeal.TryOpen(sealedText, code, out var sealError);
        if (signed is null)
        {
            return Fail("Пакет сети не открыт: " + sealError);
        }

        var state = await _store.GetNetworkStateAsync().ConfigureAwait(false);
        var check = NetworkPackageFile.Verify(signed, NetworkTrust.Production, state.ToAccepted());

        switch (check.Status)
        {
            case NetworkPackageStatus.Rejected:
                return Fail("Пакет сети отвергнут: " + check.Reason);

            case NetworkPackageStatus.NotNewer:
                return new NetworkRefreshResult(false, false, $"Новых пакетов нет: принят выпуск №{state.Serial}.");
        }

        var snapshot = check.Snapshot!;

        // 🔴 Всё или ничего. Эталон, не прошедший полную проверку (канонический
        // хеш параметров, разбор набора), отменяет приём пакета целиком: стенд с
        // частью эталонов сети работал бы по сети, которой не существует.
        var drafts = new Dictionary<Guid, NewCalibrationReference>();
        foreach (var reference in snapshot.References)
        {
            try
            {
                drafts[reference.Id] = ReferencePackage.FromJson(reference.PackageJson).ToDraft(reference.Name.Trim());
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                return Fail($"Пакет сети №{snapshot.Serial} не принят: эталон «{reference.Name}» негоден — {ex.Message}");
            }
        }

        var report = await _store.ApplyNetworkSnapshotAsync(snapshot, drafts).ConfigureAwait(false);
        await _store.SetNetworkCodeAsync(NetworkSeal.NormalizeCode(code)!).ConfigureAwait(false);

        // Сессия могла устареть: пользователя отключили или сменили ему роль.
        if (Session is { } current)
        {
            var fresh = snapshot.Users.FirstOrDefault(u => u.Id == current.Id);
            Session = fresh is { IsActive: true } ? fresh : null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        var message = $"Принят пакет сети №{report.Serial} от {snapshot.IssuedUtc.ToLocalTime():dd.MM.yyyy HH:mm} "
            + $"(выпустил {snapshot.IssuedBy}): пользователей {report.Users}, эталонов новых {report.Added}, "
            + $"связано с местными {report.Linked}, выведено из обращения {report.Retired}.";
        if (report.RenamedLocal.Count > 0)
        {
            message += " Местные эталоны с теми же именами ушли в архив: " + string.Join(", ", report.RenamedLocal) + ".";
        }

        return new NetworkRefreshResult(true, false, message);
    }

    /// <summary>Скачивает пакет из репозитория анонимно.</summary>
    private static async Task<(string? Text, string? Error)> DownloadAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, NetworkSource.ContentsUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
            using var response = await Http.SendAsync(request).ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => (await response.Content.ReadAsStringAsync().ConfigureAwait(false), null),
                HttpStatusCode.NotFound => (null, "Пакет сети ещё не выпущен: администратор выпускает его в разделе «Сеть»."),
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests =>
                    (null, "GitHub временно ограничил запросы с этого адреса. Повторите через час или примите пакет из файла."),
                _ => (null, $"GitHub ответил {(int)response.StatusCode}. Повторите позже или примите пакет из файла."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "Нет связи с GitHub: " + ex.Message + ". Стенд работает по последнему принятому пакету.");
        }
    }

    // ── Вход ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Вход по паролю либо по PIN.
    /// </summary>
    /// <returns>Причина отказа либо <c>null</c>, если вход выполнен.</returns>
    /// <remarks>
    /// PIN проверяется только у ролей, которым он разрешён
    /// (<see cref="OtkRoles.CanUseOfflinePin"/>); у остальных введённые цифры
    /// сверяются как пароль. Неизвестный логин, отключённая учётка и неверный
    /// секрет дают один ответ.
    /// </remarks>
    public Task<string?> LoginAsync(string login, string secret) => Task.Run(async () =>
    {
        if (DateTimeOffset.UtcNow < _lockedUntil)
        {
            return $"Слишком много неудачных попыток. Подождите {(int)(_lockedUntil - DateTimeOffset.UtcNow).TotalSeconds + 1} с.";
        }

        var users = await _store.ListNetworkUsersAsync().ConfigureAwait(false);
        var user = users.FirstOrDefault(u => u.IsActive
            && string.Equals(u.Login, login?.Trim(), StringComparison.OrdinalIgnoreCase));

        var pinOk = user is not null
            && OtkRoles.CanUseOfflinePin(user.Role)
            && SecretPolicy.ValidatePin(secret) is null
            && SecretHasher.Verify(secret, user.PinHash);
        var ok = pinOk || (user is not null && SecretHasher.Verify(secret, user.PasswordHash));

        if (!ok)
        {
            if (++_failedLogins >= MaxFailedLogins)
            {
                _failedLogins = 0;
                _lockedUntil = DateTimeOffset.UtcNow + LoginLockout;
            }

            return "Неверный логин, пароль или PIN.";
        }

        _failedLogins = 0;
        Session = user;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return null;
    });

    public void Logout()
    {
        Session = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Администратор ────────────────────────────────────────────────────

    /// <summary>
    /// Заводит новую сеть на компьютере администратора: код сети, первого
    /// администратора, и — если токен GitHub уже задан — первый пакет.
    /// </summary>
    public async Task<NetworkRefreshResult> CreateNetworkAsync(string login, string displayName, string password)
    {
        if (!HasSigningKey)
        {
            throw new InvalidOperationException(
                "На этом компьютере нет ключа администратора сети — сеть заводят только на его компьютере.");
        }

        if (SecretPolicy.ValidatePassword(password) is { } passwordError)
        {
            throw new ArgumentException(passwordError);
        }

        var admin = new NetworkUser(Guid.NewGuid(), login.Trim(), displayName.Trim(), OtkRoles.Admin, true,
            SecretHasher.Hash(password), null);
        await Task.Run(async () =>
        {
            await _store.CreateNetworkAsync(admin).ConfigureAwait(false);
            await _store.SetNetworkCodeAsync(NetworkSeal.NormalizeCode(NetworkSeal.GenerateCode())!).ConfigureAwait(false);
        }).ConfigureAwait(false);

        Session = admin;
        SessionChanged?.Invoke(this, EventArgs.Empty);

        return HasGitHubToken
            ? await IssueAsync(saveTo: null).ConfigureAwait(false)
            : new NetworkRefreshResult(false, false,
                "Сеть заведена. Задайте токен GitHub в разделе «Сеть» и выпустите первый пакет.");
    }

    public Task<IReadOnlyList<NetworkUser>> ListUsersAsync() => Task.Run(() => _store.ListNetworkUsersAsync());

    /// <summary>
    /// Сохраняет пользователя. Пустой пароль или PIN — оставить прежний;
    /// <paramref name="clearPin"/> снимает PIN.
    /// </summary>
    public Task SaveUserAsync(NetworkUser user, string? newPassword, string? newPin, bool clearPin)
    {
        RequireAdmin();

        if (!string.IsNullOrEmpty(newPassword) && SecretPolicy.ValidatePassword(newPassword) is { } passwordError)
        {
            throw new ArgumentException(passwordError);
        }

        if (!string.IsNullOrEmpty(newPin) && SecretPolicy.ValidatePin(newPin) is { } pinError)
        {
            throw new ArgumentException(pinError);
        }

        var saved = user with
        {
            PasswordHash = string.IsNullOrEmpty(newPassword) ? user.PasswordHash : SecretHasher.Hash(newPassword),
            PinHash = clearPin ? null : string.IsNullOrEmpty(newPin) ? user.PinHash : SecretHasher.Hash(newPin),
        };

        if (saved.PasswordHash is null)
        {
            throw new ArgumentException("У нового пользователя должен быть пароль.");
        }

        return Task.Run(async () =>
        {
            await _store.SaveNetworkUserAsync(saved).ConfigureAwait(false);
            if (Session?.Id == saved.Id)
            {
                Session = saved;
            }
        });
    }

    /// <summary>
    /// Выпускает пакет сети: в репозиторий GitHub (если задан токен) и, по
    /// желанию, в файл для стендов без интернета.
    /// </summary>
    /// <remarks>
    /// Номер выпуска фиксируется, только если пакет хоть куда-то записан:
    /// невыложенный пакет номера не тратит.
    /// </remarks>
    public Task<NetworkRefreshResult> IssueAsync(string? saveTo) => Task.Run(async () =>
    {
        RequireAdmin();

        if (!HasGitHubToken && saveTo is null)
        {
            return Fail("Токен GitHub не задан: выложить пакет некуда. Задайте токен либо сохраните пакет в файл.");
        }

        using var key = NetworkSigningKeyStore.Load()
            ?? throw new InvalidOperationException("На этом компьютере нет ключа администратора сети.");

        var state = await _store.GetNetworkStateAsync().ConfigureAwait(false);
        var snapshot = await _store.PrepareNetworkSnapshotAsync(Session!.DisplayName).ConfigureAwait(false);
        var sealedText = NetworkSeal.Seal(NetworkPackageFile.Sign(snapshot, key), state.NetworkCode);

        var done = new List<string>();
        string? failure = null;

        if (saveTo is not null)
        {
            await File.WriteAllTextAsync(saveTo, sealedText).ConfigureAwait(false);
            done.Add("в файл " + saveTo);
        }

        if (HasGitHubToken)
        {
            failure = await PublishAsync(sealedText, snapshot.Serial).ConfigureAwait(false);
            if (failure is null)
            {
                done.Add("в репозиторий " + NetworkSource.Repository);
            }
        }

        if (done.Count == 0)
        {
            return Fail("Пакет не выпущен: " + failure);
        }

        await _store.CommitNetworkIssueAsync(snapshot).ConfigureAwait(false);
        var message = $"Выпущен пакет сети №{snapshot.Serial} ({string.Join(" и ", done)}): "
            + $"пользователей {snapshot.Users.Count}, эталонов {snapshot.References.Count(static r => r.RetiredUtc is null)} "
            + $"действующих и {snapshot.References.Count(static r => r.RetiredUtc is not null)} в архиве.";
        return failure is null
            ? new NetworkRefreshResult(true, false, message + " Стенды примут его при следующем входе.")
            : new NetworkRefreshResult(true, true, message + " Но в GitHub не выложен: " + failure);
    });

    /// <summary>Выкладывает пакет в репозиторий через GitHub Contents API.</summary>
    /// <returns>Причина отказа либо <c>null</c>.</returns>
    private static async Task<string?> PublishAsync(string sealedText, long serial)
    {
        var token = Encoding.UTF8.GetString(
            ProtectedData.Unprotect(await File.ReadAllBytesAsync(TokenPath).ConfigureAwait(false), TokenEntropy, DataProtectionScope.CurrentUser));

        try
        {
            // Замена существующего файла требует его текущий sha.
            string? sha = null;
            using (var get = Authorized(HttpMethod.Get, token))
            using (var response = await Http.SendAsync(get).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    sha = json.RootElement.GetProperty("sha").GetString();
                }
                else if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    return Explain(response.StatusCode);
                }
            }

            using var put = Authorized(HttpMethod.Put, token);
            put.Content = JsonContent.Create(new Dictionary<string, string?>
            {
                ["message"] = $"Пакет сети №{serial}",
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(sealedText)),
                ["sha"] = sha,
            });
            using var result = await Http.SendAsync(put).ConfigureAwait(false);
            return result.IsSuccessStatusCode ? null : Explain(result.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "нет связи с GitHub: " + ex.Message;
        }

        static string Explain(HttpStatusCode status) => status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            ? $"GitHub отказал ({(int)status}): токен недействителен либо не имеет права Contents: Read and write на {NetworkSource.Repository}."
            : $"GitHub ответил {(int)status}.";
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string token)
    {
        var request = new HttpRequestMessage(method, NetworkSource.ContentsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    /// <summary>
    /// Сохраняет токен GitHub, зашифрованный DPAPI под учётной записью
    /// Windows. В программу токен не зашивается: установщик лежит в открытых
    /// релизах, и токен из него достал бы кто угодно.
    /// </summary>
    public static void SaveGitHubToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
        File.WriteAllBytes(TokenPath, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token.Trim()), TokenEntropy, DataProtectionScope.CurrentUser));
    }

    /// <summary>Сохраняет резервную копию ключа подписи, зашифрованную паролем.</summary>
    public static async Task ExportKeyBackupAsync(string path, string password)
    {
        if (SecretPolicy.ValidatePassword(password) is { } error)
        {
            throw new ArgumentException(error);
        }

        using var key = NetworkSigningKeyStore.Load()
            ?? throw new InvalidOperationException("На этом компьютере нет ключа администратора сети.");
        await File.WriteAllBytesAsync(path, NetworkSigningKeyStore.ExportBackup(key, password)).ConfigureAwait(false);
    }

    private void RequireAdmin()
    {
        if (!IsAdmin)
        {
            throw new InvalidOperationException("Действие доступно только администратору сети.");
        }
    }

    private static NetworkRefreshResult Fail(string message) => new(false, true, message);

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        // GitHub API отвергает запросы без User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ARDU_OTK", "1.0"));
        return http;
    }
}
