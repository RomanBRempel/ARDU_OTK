using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ARDU_OTK.Shared.Contracts;
using ARDU_OTK.Shared.Security;
using Microsoft.Data.Sqlite;

namespace ARDU_OTK.Server.Data;

/// <summary>Отказ, который объясняется пользователю как есть.</summary>
public sealed class StoreRuleException(string message) : Exception(message);

/// <summary>Сессия, найденная по токену.</summary>
public sealed record SessionPrincipal(UserInfo User, Guid StationId);

/// <summary>Исход попытки входа.</summary>
public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
}

/// <summary>
/// Хранилище сервера ОТК.
/// </summary>
/// <remarks>
/// <para>
/// <b>Модель синхронизации.</b> Каждая реплицируемая запись (пользователь,
/// эталон) несёт <c>Seq</c> — номер изменения из одного сквозного счётчика
/// <c>Meta.Seq</c>. Номер выдаётся в той же транзакции, что и изменение.
/// Стенд спрашивает «всё после N» и получает записи целиком.
/// </para>
/// <para>
/// 🔴 Все записи идут через один семафор. Без него две транзакции могли бы
/// взять номера 10 и 11, а зафиксироваться в порядке 11, 10: стенд, успевший
/// между ними спросить изменения, получил бы 11, запомнил бы его и запись 10
/// не увидел бы никогда. Последовательная запись делает порядок номеров
/// порядком фиксации. Поток записей сети ОТК — единицы в минуту, так что
/// очередь ничего не стоит.
/// </para>
/// <para>
/// База лежит на локальном диске сервера: WAL не работает на сетевых ресурсах.
/// </para>
/// </remarks>
public sealed class ServerStore : IDisposable
{
    /// <summary>Версия схемы в <c>PRAGMA user_version</c>.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Неудачных попыток подряд до блокировки логина.</summary>
    public const int MaxFailedLogins = 5;

    /// <summary>На сколько блокируется логин.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Срок сессии: одна смена с запасом.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private readonly string _connectionString;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ServerStore(string databasePath, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
        _time = time;
    }

    /// <summary>Ключ этой базы — см. <see cref="SyncPullResponse.ServerId"/>.</summary>
    public Guid ServerId { get; private set; }

    public void Dispose() => _writeGate.Dispose();

    // ── Схема ────────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);

        await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;", ct);

        var version = Convert.ToInt32(await ScalarAsync(connection, null, "PRAGMA user_version;", ct), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
        {
            throw new InvalidOperationException(
                $"База сервера схемы {version}, эта сборка понимает {SchemaVersion}. Обновите сервер.");
        }

        if (version < 1)
        {
            await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            await ExecuteAsync(connection, tx, SchemaV1, ct);
            await ExecuteAsync(
                connection,
                tx,
                "INSERT INTO Meta (Key, Value) VALUES ('ServerId', $id), ('Seq', '0');",
                ct,
                ("$id", Guid.NewGuid().ToString()));
            await ExecuteAsync(connection, tx, $"PRAGMA user_version = {SchemaVersion};", ct);
            await tx.CommitAsync(ct);
        }

        ServerId = Guid.Parse((string)(await ScalarAsync(connection, null, "SELECT Value FROM Meta WHERE Key = 'ServerId';", ct))!);
    }

    private const string SchemaV1 = """
        CREATE TABLE Meta (
            Key   TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );

        CREATE TABLE User (
            Id              TEXT PRIMARY KEY,
            Login           TEXT NOT NULL,
            LoginNormalized TEXT NOT NULL UNIQUE,
            DisplayName     TEXT NOT NULL,
            Role            TEXT NOT NULL,
            IsActive        INTEGER NOT NULL,
            PasswordHash    TEXT NULL,
            PinHash         TEXT NULL,
            FailedLogins    INTEGER NOT NULL DEFAULT 0,
            LockedUntilUtc  TEXT NULL,
            CreatedUtc      TEXT NOT NULL,
            UpdatedUtc      TEXT NOT NULL,
            Seq             INTEGER NOT NULL
        );
        CREATE INDEX IX_User_Seq ON User (Seq);

        CREATE TABLE Station (
            Id          TEXT PRIMARY KEY,
            Name        TEXT NOT NULL,
            FirstSeenUtc TEXT NOT NULL,
            LastSeenUtc TEXT NOT NULL,
            LastUserId  TEXT NULL REFERENCES User (Id)
        );

        CREATE TABLE Session (
            TokenHash  TEXT PRIMARY KEY,
            UserId     TEXT NOT NULL REFERENCES User (Id),
            StationId  TEXT NOT NULL REFERENCES Station (Id),
            CreatedUtc TEXT NOT NULL,
            ExpiresUtc TEXT NOT NULL,
            RevokedUtc TEXT NULL
        );
        CREATE INDEX IX_Session_User ON Session (UserId);

        CREATE TABLE Reference (
            Id             TEXT PRIMARY KEY,
            Name           TEXT NOT NULL,
            NameNormalized TEXT NOT NULL UNIQUE,
            ParamHash      TEXT NOT NULL,
            PackageJson    TEXT NOT NULL,
            PublishedBy    TEXT NOT NULL REFERENCES User (Id),
            PublishedUtc   TEXT NOT NULL,
            RetiredUtc     TEXT NULL,
            RetiredBy      TEXT NULL REFERENCES User (Id),
            Seq            INTEGER NOT NULL
        );
        CREATE INDEX IX_Reference_Seq ON Reference (Seq);

        CREATE TABLE Audit (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            Utc       TEXT NOT NULL,
            UserId    TEXT NULL,
            StationId TEXT NULL,
            Action    TEXT NOT NULL,
            Target    TEXT NULL,
            Detail    TEXT NULL
        );
        """;

    // ── Пользователи ─────────────────────────────────────────────────────

    /// <summary>
    /// Заводит администратора либо возвращает права и пароль существующему
    /// логину. Только для командной строки сервера: это путь восстановления,
    /// когда в сети не осталось ни одного администратора, способного войти.
    /// </summary>
    public Task<UserDto> UpsertAdminAsync(string login, string displayName, string password, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) =>
        {
            RequireValid(SecretPolicy.ValidatePassword(password));
            var existing = await FindUserIdAsync(connection, tx, login, ct);
            var now = Now();
            var seq = await NextSeqAsync(connection, tx, ct);
            var hash = SecretHasher.Hash(password);

            Guid id;
            if (existing is { } found)
            {
                id = found;
                await ExecuteAsync(connection, tx, """
                    UPDATE User SET Role = $role, IsActive = 1, PasswordHash = $hash, FailedLogins = 0,
                        LockedUntilUtc = NULL, UpdatedUtc = $now, Seq = $seq
                    WHERE Id = $id;
                    """, ct, ("$role", OtkRoles.Admin), ("$hash", hash), ("$now", Iso(now)), ("$seq", seq), ("$id", id.ToString()));
                await RevokeSessionsAsync(connection, tx, id, ct);
            }
            else
            {
                id = Guid.NewGuid();
                await InsertUserAsync(connection, tx, id, login, displayName, OtkRoles.Admin, hash, now, seq, ct);
            }

            await AuditAsync(connection, tx, null, null, "user.bootstrap-admin", id.ToString(), login, ct);
            return (await ReadUserDtoAsync(connection, tx, id, ct))!;
        }, ct);

    public Task<UserDto> CreateUserAsync(CreateUserRequest request, SessionPrincipal actor, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) =>
        {
            var login = request.Login?.Trim() ?? string.Empty;
            var displayName = request.DisplayName?.Trim() ?? string.Empty;
            if (login.Length == 0 || displayName.Length == 0)
            {
                throw new StoreRuleException("Логин и ФИО обязательны.");
            }

            RequireRole(request.Role);
            RequireValid(SecretPolicy.ValidatePassword(request.Password));

            if (await FindUserIdAsync(connection, tx, login, ct) is not null)
            {
                throw new StoreRuleException($"Логин «{login}» уже занят.");
            }

            var id = Guid.NewGuid();
            await InsertUserAsync(
                connection, tx, id, login, displayName, request.Role,
                SecretHasher.Hash(request.Password), Now(), await NextSeqAsync(connection, tx, ct), ct);
            await AuditAsync(connection, tx, actor, "user.create", id.ToString(), $"{login} ({request.Role})", ct);
            return (await ReadUserDtoAsync(connection, tx, id, ct))!;
        }, ct);

    public Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, SessionPrincipal actor, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) =>
        {
            var current = await ReadUserDtoAsync(connection, tx, id, ct)
                ?? throw new KeyNotFoundException("Пользователь не найден.");

            var displayName = request.DisplayName?.Trim() is { Length: > 0 } name ? name : current.DisplayName;
            var role = request.Role ?? current.Role;
            var isActive = request.IsActive ?? current.IsActive;
            RequireRole(role);

            // 🔴 Последнего действующего администратора нельзя ни понизить, ни
            // отключить: сеть ОТК осталась бы без единственной роли, способной
            // завести эталон или вернуть права, и выйти из этого можно было бы
            // только с консоли сервера.
            var losesAdmin = current.Role == OtkRoles.Admin && current.IsActive && (role != OtkRoles.Admin || !isActive);
            if (losesAdmin && await CountActiveAdminsAsync(connection, tx, ct) <= 1)
            {
                throw new StoreRuleException("Это последний действующий администратор: сначала назначьте другого.");
            }

            await ExecuteAsync(connection, tx, """
                UPDATE User SET DisplayName = $name, Role = $role, IsActive = $active, UpdatedUtc = $now, Seq = $seq
                WHERE Id = $id;
                """, ct,
                ("$name", displayName), ("$role", role), ("$active", isActive ? 1 : 0),
                ("$now", Iso(Now())), ("$seq", await NextSeqAsync(connection, tx, ct)), ("$id", id.ToString()));

            // Сменилась роль или учётка отключена — выданные сессии несут старые
            // права. Они закрываются сразу, а не по истечении срока.
            if (role != current.Role || !isActive)
            {
                await RevokeSessionsAsync(connection, tx, id, ct);
            }

            await AuditAsync(connection, tx, actor, "user.update", id.ToString(),
                $"роль {current.Role}→{role}, активен {current.IsActive}→{isActive}", ct);
            return (await ReadUserDtoAsync(connection, tx, id, ct))!;
        }, ct);

    public Task SetPasswordAsync(Guid id, string password, SessionPrincipal actor, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) =>
        {
            RequireValid(SecretPolicy.ValidatePassword(password));
            var affected = await ExecuteAsync(connection, tx, """
                UPDATE User SET PasswordHash = $hash, FailedLogins = 0, LockedUntilUtc = NULL, UpdatedUtc = $now
                WHERE Id = $id;
                """, ct, ("$hash", SecretHasher.Hash(password)), ("$now", Iso(Now())), ("$id", id.ToString()));
            if (affected == 0)
            {
                throw new KeyNotFoundException("Пользователь не найден.");
            }

            await RevokeSessionsAsync(connection, tx, id, ct);
            await AuditAsync(connection, tx, actor, "user.password", id.ToString(), null, ct);
            return 0;
        }, ct);

    public Task SetPinAsync(Guid id, string? pin, SessionPrincipal actor, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) =>
        {
            if (pin is not null)
            {
                RequireValid(SecretPolicy.ValidatePin(pin));
            }

            // PIN уходит в реплики (для операторов), поэтому смена PIN — это
            // изменение реплицируемой записи и получает новый Seq.
            var affected = await ExecuteAsync(connection, tx, """
                UPDATE User SET PinHash = $hash, UpdatedUtc = $now, Seq = $seq WHERE Id = $id;
                """, ct,
                ("$hash", pin is null ? null : SecretHasher.Hash(pin)), ("$now", Iso(Now())),
                ("$seq", await NextSeqAsync(connection, tx, ct)), ("$id", id.ToString()));
            if (affected == 0)
            {
                throw new KeyNotFoundException("Пользователь не найден.");
            }

            await AuditAsync(connection, tx, actor, pin is null ? "user.pin-clear" : "user.pin", id.ToString(), null, ct);
            return 0;
        }, ct);

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = UserDtoSelect + " ORDER BY LoginNormalized;";
        var result = new List<UserDto>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadUserDto(reader));
        }

        return result;
    }

    // ── Вход и сессии ────────────────────────────────────────────────────

    /// <summary>
    /// Вход по паролю.
    /// </summary>
    /// <remarks>
    /// 🔴 Неизвестный логин, отключённая учётка и неверный пароль дают один и
    /// тот же ответ: сервер открыт в интернет, и различимый ответ превращал бы
    /// форму входа в справочник действующих логинов.
    /// </remarks>
    public Task<(LoginOutcome Outcome, LoginResponse? Response)> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        WriteAsync<(LoginOutcome, LoginResponse?)>(async (connection, tx) =>
        {
            var now = Now();
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                SELECT Id, Login, DisplayName, Role, IsActive, PasswordHash, FailedLogins, LockedUntilUtc
                FROM User WHERE LoginNormalized = $login;
                """;
            command.Parameters.AddWithValue("$login", Normalize(request.Login ?? string.Empty));

            Guid id;
            UserInfo info;
            bool isActive;
            string? hash;
            int failed;
            DateTimeOffset? lockedUntil;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                {
                    // Хеш считается и для несуществующего логина, чтобы время
                    // ответа не отличало «нет такого» от «не тот пароль».
                    SecretHasher.Verify(request.Password ?? string.Empty, DummyHash);
                    return (LoginOutcome.InvalidCredentials, null);
                }

                id = Guid.Parse(reader.GetString(0));
                info = new UserInfo(id, reader.GetString(1), reader.GetString(2), reader.GetString(3));
                isActive = reader.GetInt64(4) != 0;
                hash = reader.IsDBNull(5) ? null : reader.GetString(5);
                failed = reader.GetInt32(6);
                lockedUntil = reader.IsDBNull(7) ? null : ParseIso(reader.GetString(7));
            }

            if (lockedUntil is { } until && until > now)
            {
                return (LoginOutcome.LockedOut, null);
            }

            if (!SecretHasher.Verify(request.Password ?? string.Empty, hash) || !isActive)
            {
                failed++;
                var lockUntil = failed >= MaxFailedLogins ? Iso(now + LockoutDuration) : null;
                await ExecuteAsync(connection, tx,
                    "UPDATE User SET FailedLogins = $failed, LockedUntilUtc = $lock WHERE Id = $id;", ct,
                    ("$failed", lockUntil is null ? failed : 0), ("$lock", lockUntil), ("$id", id.ToString()));
                await AuditAsync(connection, tx, id, request.StationId, "auth.fail", id.ToString(),
                    lockUntil is null ? null : "логин заблокирован", ct);
                return (LoginOutcome.InvalidCredentials, null);
            }

            await ExecuteAsync(connection, tx,
                "UPDATE User SET FailedLogins = 0, LockedUntilUtc = NULL WHERE Id = $id;", ct, ("$id", id.ToString()));

            var stationName = string.IsNullOrWhiteSpace(request.StationName) ? "без имени" : request.StationName.Trim();
            await ExecuteAsync(connection, tx, """
                INSERT INTO Station (Id, Name, FirstSeenUtc, LastSeenUtc, LastUserId)
                VALUES ($id, $name, $now, $now, $user)
                ON CONFLICT (Id) DO UPDATE SET Name = $name, LastSeenUtc = $now, LastUserId = $user;
                """, ct,
                ("$id", request.StationId.ToString()), ("$name", stationName), ("$now", Iso(now)), ("$user", id.ToString()));

            var token = "otk_" + Base64Url(RandomNumberGenerator.GetBytes(32));
            var expires = now + SessionLifetime;
            await ExecuteAsync(connection, tx, """
                INSERT INTO Session (TokenHash, UserId, StationId, CreatedUtc, ExpiresUtc)
                VALUES ($hash, $user, $station, $now, $expires);
                """, ct,
                ("$hash", HashToken(token)), ("$user", id.ToString()), ("$station", request.StationId.ToString()),
                ("$now", Iso(now)), ("$expires", Iso(expires)));
            await AuditAsync(connection, tx, id, request.StationId, "auth.login", id.ToString(), stationName, ct);

            return (LoginOutcome.Success, new LoginResponse(token, expires, info));
        }, ct);

    /// <summary>Сессия по токену; <c>null</c> — токен неизвестен, отозван, истёк или учётка отключена.</summary>
    public async Task<SessionPrincipal?> FindSessionAsync(string token, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.Id, u.Login, u.DisplayName, u.Role, s.StationId
            FROM Session s JOIN User u ON u.Id = s.UserId
            WHERE s.TokenHash = $hash AND s.RevokedUtc IS NULL AND s.ExpiresUtc > $now AND u.IsActive = 1;
            """;
        command.Parameters.AddWithValue("$hash", HashToken(token));
        command.Parameters.AddWithValue("$now", Iso(Now()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new SessionPrincipal(
            new UserInfo(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)),
            Guid.Parse(reader.GetString(4)));
    }

    public Task LogoutAsync(string token, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) => await ExecuteAsync(connection, tx,
            "UPDATE Session SET RevokedUtc = $now WHERE TokenHash = $hash AND RevokedUtc IS NULL;", ct,
            ("$now", Iso(Now())), ("$hash", HashToken(token))), ct);

    // ── Эталоны ──────────────────────────────────────────────────────────

    public Task<ReferenceRecord> PublishReferenceAsync(string packageJson, SessionPrincipal actor, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) =>
        {
            var header = ReferencePackageHeader.TryParse(packageJson, out var error)
                ?? throw new StoreRuleException(error!);

            var name = header.Name.Trim();
            var taken = await ScalarAsync(connection, tx,
                "SELECT 1 FROM Reference WHERE NameNormalized = $name;", ct, ("$name", Normalize(name)));
            if (taken is not null)
            {
                // Имя уникально и среди выведенных: прогоны прошлых лет ссылаются
                // на эталон по имени, и второй «X» сделал бы их двусмысленными.
                throw new StoreRuleException($"Эталон «{name}» уже есть в сети. Опубликуйте под другим именем.");
            }

            var id = Guid.NewGuid();
            await ExecuteAsync(connection, tx, """
                INSERT INTO Reference (Id, Name, NameNormalized, ParamHash, PackageJson, PublishedBy, PublishedUtc, Seq)
                VALUES ($id, $name, $norm, $hash, $json, $by, $now, $seq);
                """, ct,
                ("$id", id.ToString()), ("$name", name), ("$norm", Normalize(name)),
                ("$hash", header.ParamHash.ToLowerInvariant()), ("$json", packageJson),
                ("$by", actor.User.Id.ToString()), ("$now", Iso(Now())), ("$seq", await NextSeqAsync(connection, tx, ct)));
            await AuditAsync(connection, tx, actor, "reference.publish", id.ToString(), name, ct);
            return (await ReadReferenceAsync(connection, tx, id, ct))!;
        }, ct);

    public Task<ReferenceRecord> SetReferenceRetiredAsync(Guid id, bool retired, SessionPrincipal actor, CancellationToken ct = default) =>
        WriteAsync(async (connection, tx) =>
        {
            var affected = await ExecuteAsync(connection, tx, """
                UPDATE Reference SET RetiredUtc = $retired, RetiredBy = $by, Seq = $seq WHERE Id = $id;
                """, ct,
                ("$retired", retired ? Iso(Now()) : null), ("$by", retired ? actor.User.Id.ToString() : null),
                ("$seq", await NextSeqAsync(connection, tx, ct)), ("$id", id.ToString()));
            if (affected == 0)
            {
                throw new KeyNotFoundException("Эталон не найден.");
            }

            await AuditAsync(connection, tx, actor, retired ? "reference.retire" : "reference.restore", id.ToString(), null, ct);
            return (await ReadReferenceAsync(connection, tx, id, ct))!;
        }, ct);

    // ── Синхронизация ────────────────────────────────────────────────────

    /// <summary>Изменения после номера <paramref name="since"/>, не больше <paramref name="limit"/> записей.</summary>
    public async Task<SyncPullResponse> PullAsync(long since, int limit, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await using var connection = await OpenAsync(ct);

        // Из каждой таблицы берётся до limit+1 записей, затем общий поток
        // режется по номеру. Курсор — номер последней отданной записи, поэтому
        // записи с номером за отрезом придут следующим запросом, а не потеряются.
        var users = new List<UserReplica>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, Login, DisplayName, Role, IsActive, PinHash, Seq FROM User
                WHERE Seq > $since ORDER BY Seq LIMIT $take;
                """;
            command.Parameters.AddWithValue("$since", since);
            command.Parameters.AddWithValue("$take", limit + 1);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var role = reader.GetString(3);
                var pin = reader.IsDBNull(5) ? null : reader.GetString(5);
                users.Add(new UserReplica(
                    Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), role,
                    reader.GetInt64(4) != 0, OtkRoles.CanUseOfflinePin(role) ? pin : null, reader.GetInt64(6)));
            }
        }

        var references = new List<ReferenceRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ReferenceSelect + " WHERE r.Seq > $since ORDER BY r.Seq LIMIT $take;";
            command.Parameters.AddWithValue("$since", since);
            command.Parameters.AddWithValue("$take", limit + 1);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                references.Add(ReadReference(reader));
            }
        }

        var merged = users.Select(static u => u.Seq).Concat(references.Select(static r => r.Seq)).Order().ToList();
        var hasMore = merged.Count > limit;
        var cursor = merged.Count == 0 ? since : merged[Math.Min(limit, merged.Count) - 1];

        return new SyncPullResponse(
            ServerId,
            cursor,
            hasMore,
            users.Where(u => u.Seq <= cursor).ToList(),
            references.Where(r => r.Seq <= cursor).ToList());
    }

    // ── Внутреннее ───────────────────────────────────────────────────────

    // Хеш, по которому сверяется пароль несуществующего логина. Ни один
    // пароль ему не соответствует: строка соли и хеша случайна на каждый запуск.
    private static readonly string DummyHash = SecretHasher.Hash(Guid.NewGuid().ToString());

    private const string UserDtoSelect = """
        SELECT Id, Login, DisplayName, Role, IsActive, PasswordHash IS NOT NULL, PinHash IS NOT NULL, CreatedUtc, UpdatedUtc
        FROM User
        """;

    private const string ReferenceSelect = """
        SELECT r.Id, r.Name, r.ParamHash, r.PackageJson, r.PublishedBy, u.DisplayName, r.PublishedUtc, r.RetiredUtc, r.Seq
        FROM Reference r JOIN User u ON u.Id = r.PublishedBy
        """;

    private async Task<T> WriteAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            var result = await action(connection, tx);
            await tx.CommitAsync(ct);
            return result;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }

    private static async Task<long> NextSeqAsync(SqliteConnection connection, SqliteTransaction tx, CancellationToken ct) =>
        Convert.ToInt64(
            await ScalarAsync(connection, tx,
                "UPDATE Meta SET Value = CAST(Value AS INTEGER) + 1 WHERE Key = 'Seq' RETURNING Value;", ct),
            CultureInfo.InvariantCulture);

    private static async Task InsertUserAsync(
        SqliteConnection connection, SqliteTransaction tx, Guid id, string login, string displayName, string role,
        string passwordHash, DateTimeOffset now, long seq, CancellationToken ct) =>
        await ExecuteAsync(connection, tx, """
            INSERT INTO User (Id, Login, LoginNormalized, DisplayName, Role, IsActive, PasswordHash, CreatedUtc, UpdatedUtc, Seq)
            VALUES ($id, $login, $norm, $name, $role, 1, $hash, $now, $now, $seq);
            """, ct,
            ("$id", id.ToString()), ("$login", login.Trim()), ("$norm", Normalize(login)),
            ("$name", displayName.Trim()), ("$role", role), ("$hash", passwordHash), ("$now", Iso(now)), ("$seq", seq));

    private static async Task<Guid?> FindUserIdAsync(SqliteConnection connection, SqliteTransaction tx, string login, CancellationToken ct) =>
        await ScalarAsync(connection, tx, "SELECT Id FROM User WHERE LoginNormalized = $login;", ct, ("$login", Normalize(login)))
            is string id ? Guid.Parse(id) : null;

    private static async Task<long> CountActiveAdminsAsync(SqliteConnection connection, SqliteTransaction tx, CancellationToken ct) =>
        Convert.ToInt64(
            await ScalarAsync(connection, tx, "SELECT COUNT(*) FROM User WHERE Role = $role AND IsActive = 1;", ct, ("$role", OtkRoles.Admin)),
            CultureInfo.InvariantCulture);

    private async Task RevokeSessionsAsync(SqliteConnection connection, SqliteTransaction tx, Guid userId, CancellationToken ct) =>
        await ExecuteAsync(connection, tx,
            "UPDATE Session SET RevokedUtc = $now WHERE UserId = $id AND RevokedUtc IS NULL;", ct,
            ("$now", Iso(Now())), ("$id", userId.ToString()));

    private static async Task<UserDto?> ReadUserDtoAsync(SqliteConnection connection, SqliteTransaction tx, Guid id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = UserDtoSelect + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUserDto(reader) : null;
    }

    private static UserDto ReadUserDto(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetInt64(4) != 0, reader.GetInt64(5) != 0, reader.GetInt64(6) != 0,
        ParseIso(reader.GetString(7)), ParseIso(reader.GetString(8)));

    private static async Task<ReferenceRecord?> ReadReferenceAsync(SqliteConnection connection, SqliteTransaction tx, Guid id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = ReferenceSelect + " WHERE r.Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadReference(reader) : null;
    }

    private static ReferenceRecord ReadReference(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        Guid.Parse(reader.GetString(4)), reader.GetString(5), ParseIso(reader.GetString(6)),
        reader.IsDBNull(7) ? null : ParseIso(reader.GetString(7)), reader.GetInt64(8));

    private Task AuditAsync(SqliteConnection connection, SqliteTransaction tx, SessionPrincipal actor, string action, string? target, string? detail, CancellationToken ct) =>
        AuditAsync(connection, tx, actor.User.Id, actor.StationId, action, target, detail, ct);

    private async Task AuditAsync(
        SqliteConnection connection, SqliteTransaction tx, Guid? userId, Guid? stationId,
        string action, string? target, string? detail, CancellationToken ct) =>
        await ExecuteAsync(connection, tx, """
            INSERT INTO Audit (Utc, UserId, StationId, Action, Target, Detail)
            VALUES ($now, $user, $station, $action, $target, $detail);
            """, ct,
            ("$now", Iso(Now())), ("$user", userId?.ToString()), ("$station", stationId?.ToString()),
            ("$action", action), ("$target", target), ("$detail", detail));

    private static void RequireRole(string? role)
    {
        if (!OtkRoles.IsKnown(role))
        {
            throw new StoreRuleException($"Неизвестная роль «{role}». Допустимы: {string.Join(", ", OtkRoles.All)}.");
        }
    }

    private static void RequireValid(string? error)
    {
        if (error is not null)
        {
            throw new StoreRuleException(error);
        }
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection, SqliteTransaction? tx, string sql, CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection, SqliteTransaction? tx, string sql, CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync(ct);
        return result is DBNull ? null : result;
    }

    private DateTimeOffset Now() => _time.GetUtcNow();

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseIso(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
