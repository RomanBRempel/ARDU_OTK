using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ARDU_OTK.Services.Store;
using ARDU_OTK.Shared.Network;
using ARDU_OTK.Shared.Security;

namespace ARDU_OTK.Services.Network;

/// <summary>Итог обращения к папке обмена — для сообщения оператору.</summary>
public sealed record NetworkRefreshResult(bool Applied, bool Failed, string Message);

/// <summary>
/// Сеть ОТК на стенде: приём пакета из папки обмена, вход пользователя,
/// а у администратора — ведение пользователей и выпуск пакета.
/// </summary>
/// <remarks>
/// <para>
/// Папка обмена — обычный каталог на диске. Его синхронизирует «Google Диск для
/// компьютера», но стенду это неизвестно и не важно: подойдут и сетевая папка,
/// и флешка. Доверие к пакету даёт подпись (<see cref="NetworkPackageFile"/>),
/// а не канал доставки.
/// </para>
/// <para>
/// 🔴 Каждое обращение к хранилищу — через <see cref="Task.Run(Func{Task})"/>:
/// нативная часть SQLite в UI-потоке WinUI роняет процесс без исключения
/// (см. <see cref="AppServices.InitializeAsync"/>).
/// </para>
/// </remarks>
public sealed class NetworkService
{
    /// <summary>Имя служебной папки на Google Диске.</summary>
    public const string ExchangeFolderName = "ARDU_OTK.Exchange";

    private const int MaxFailedLogins = 5;

    private static readonly TimeSpan LoginLockout = TimeSpan.FromSeconds(30);

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

    public Task<NetworkState> GetStateAsync() => Task.Run(() => _store.GetNetworkStateAsync());

    /// <summary>
    /// Папки обмена, которые удалось найти на этом компьютере: синхронизируемый
    /// «Google Диск для компьютера» монтируется отдельной буквой диска либо
    /// живёт в профиле пользователя.
    /// </summary>
    public static IReadOnlyList<string> FindExchangeDirs()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "Мой диск"));
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, "My Drive"));
                }
            }
            catch (IOException)
            {
                // Отключённый сетевой диск — не повод прерывать поиск.
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        roots.Add(Path.Combine(profile, "Google Drive"));
        roots.Add(Path.Combine(profile, "Google Drive", "Мой диск"));
        roots.Add(Path.Combine(profile, "Google Drive", "My Drive"));
        roots.Add(Path.Combine(profile, "Мой диск"));
        roots.Add(Path.Combine(profile, "My Drive"));

        return roots
            .Select(static root => Path.Combine(root, ExchangeFolderName))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Проверяет пакет в папке обмена и, если он новее принятого, применяет его.
    /// </summary>
    public Task<NetworkRefreshResult> RefreshAsync(string exchangeDir) => Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(exchangeDir) || !Directory.Exists(exchangeDir))
        {
            return new NetworkRefreshResult(false, true, $"Папка обмена не найдена: «{exchangeDir}».");
        }

        var path = Path.Combine(exchangeDir, NetworkPackageFile.FileName);
        if (!File.Exists(path))
        {
            return new NetworkRefreshResult(false, true,
                $"В папке «{exchangeDir}» нет пакета сети ({NetworkPackageFile.FileName}). "
              + "Если Google Диск ещё синхронизирует папку — подождите и проверьте снова.");
        }

        var state = await _store.GetNetworkStateAsync().ConfigureAwait(false);
        var check = NetworkPackageFile.Verify(
            await File.ReadAllTextAsync(path).ConfigureAwait(false), NetworkTrust.Production, state.ToAccepted());

        switch (check.Status)
        {
            case NetworkPackageStatus.Rejected:
                return new NetworkRefreshResult(false, true, "Пакет сети отвергнут: " + check.Reason);

            case NetworkPackageStatus.NotNewer:
                await _store.SetExchangeDirAsync(exchangeDir).ConfigureAwait(false);
                return new NetworkRefreshResult(false, false,
                    $"Новых пакетов нет: принят выпуск №{state.Serial}.");
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
                return new NetworkRefreshResult(false, true,
                    $"Пакет сети №{snapshot.Serial} не принят: эталон «{reference.Name}» негоден — {ex.Message}");
            }
        }

        var report = await _store.ApplyNetworkSnapshotAsync(snapshot, drafts).ConfigureAwait(false);
        await _store.SetExchangeDirAsync(exchangeDir).ConfigureAwait(false);

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
    });

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
    /// Заводит новую сеть на компьютере администратора и сразу выпускает первый
    /// пакет в папку обмена.
    /// </summary>
    public async Task<NetworkRefreshResult> CreateNetworkAsync(
        string exchangeDir, string login, string displayName, string password)
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
            await _store.SetExchangeDirAsync(exchangeDir).ConfigureAwait(false);
        }).ConfigureAwait(false);

        Session = admin;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return await IssueAsync().ConfigureAwait(false);
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
    /// Выпускает пакет сети в папку обмена.
    /// </summary>
    /// <remarks>
    /// Файл пишется рядом под временным именем и подменяется целиком: стенд,
    /// читающий папку в эту секунду, обязан увидеть либо старый пакет, либо
    /// новый, но не половину нового.
    /// </remarks>
    public Task<NetworkRefreshResult> IssueAsync() => Task.Run(async () =>
    {
        RequireAdmin();

        using var key = NetworkSigningKeyStore.Load()
            ?? throw new InvalidOperationException("На этом компьютере нет ключа администратора сети.");

        var state = await _store.GetNetworkStateAsync().ConfigureAwait(false);
        if (!Directory.Exists(state.ExchangeDir))
        {
            return new NetworkRefreshResult(false, true, $"Папка обмена не найдена: «{state.ExchangeDir}».");
        }

        var snapshot = await _store.PrepareNetworkSnapshotAsync(Session!.DisplayName).ConfigureAwait(false);
        var text = NetworkPackageFile.Sign(snapshot, key);

        var target = Path.Combine(state.ExchangeDir, NetworkPackageFile.FileName);
        var temp = target + ".tmp";
        await File.WriteAllTextAsync(temp, text).ConfigureAwait(false);
        File.Move(temp, target, overwrite: true);

        await _store.CommitNetworkIssueAsync(snapshot).ConfigureAwait(false);
        return new NetworkRefreshResult(true, false,
            $"Выпущен пакет сети №{snapshot.Serial}: пользователей {snapshot.Users.Count}, "
          + $"эталонов {snapshot.References.Count(static r => r.RetiredUtc is null)} действующих "
          + $"и {snapshot.References.Count(static r => r.RetiredUtc is not null)} в архиве. "
          + "Стенды примут его при следующей проверке папки.");
    });

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
}
