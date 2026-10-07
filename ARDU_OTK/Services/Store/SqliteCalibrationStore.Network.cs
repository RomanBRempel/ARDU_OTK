using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Shared.Network;
using ARDU_OTK.Shared.Security;
using Microsoft.Data.Sqlite;

namespace ARDU_OTK.Services.Store;

/// <summary>Чем стенд связан с сетью ОТК.</summary>
/// <param name="NetworkId"><c>null</c> — стенд ещё не принял ни одного пакета сети.</param>
/// <param name="Serial">Номер последнего принятого (у администратора — выпущенного) пакета.</param>
/// <param name="NetworkCode">
/// Код сети, которым зашифрован пакет (<see cref="NetworkSeal"/>). Хранится
/// открыто: он защищает пакет в публичном репозитории, а всё, что под ним
/// лежит, стенд после приёма и так держит в своём реестре.
/// </param>
public sealed record NetworkState(
    Guid? NetworkId,
    long Serial,
    DateTimeOffset? IssuedUtc,
    string IssuedBy,
    string NetworkCode)
{
    /// <summary>Стенд подключён к сети: работать на нём можно.</summary>
    public bool IsJoined => NetworkId.HasValue;

    public AcceptedNetworkState ToAccepted() => new(NetworkId, Serial);
}

/// <summary>Что изменил приём пакета сети — для сообщения оператору.</summary>
public sealed record NetworkApplyReport(
    long Serial,
    int Users,
    int Added,
    int Linked,
    int Retired,
    IReadOnlyList<string> RenamedLocal);

/// <summary>Сеть ОТК: реплика пользователей и эталонов из пакета администратора.</summary>
public sealed partial class SqliteCalibrationStore
{
    private const string SettingNetworkId = "network.id";
    private const string SettingNetworkSerial = "network.serial";
    private const string SettingNetworkIssuedUtc = "network.issuedUtc";
    private const string SettingNetworkIssuedBy = "network.issuedBy";
    private const string SettingNetworkCode = "network.code";

    /// <summary>Состояние подключения стенда к сети.</summary>
    public async Task<NetworkState> GetNetworkStateAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var values = await ReadSettingsAsync(connection, null, "network.", ct).ConfigureAwait(false);
            return ToNetworkState(values);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Запоминает код сети.</summary>
    public async Task SetNetworkCodeAsync(string networkCode, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await UpsertSettingAsync(connection, transaction, SettingNetworkCode, networkCode,
                    FormatUtc(DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Применяет принятый пакет сети одной транзакцией.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 На подключённом стенде действуют только эталоны сети. Эталон, заведённый
    /// на стенде мимо администратора, выводится из обращения: иначе контроль
    /// распространения обходился бы кнопкой «Завести эталон» на любом стенде.
    /// Удаления нет — по такому эталону могли сдавать платы, и он остаётся в
    /// архиве для разбора.
    /// </para>
    /// <para>
    /// Местный эталон с тем же именем и тем же хешем параметров — это тот же
    /// эталон, принятый когда-то файлом: он связывается с сетью, а не
    /// дублируется, и его история прогонов сохраняется. С тем же именем, но
    /// другим содержимым — другой эталон: он уходит в архив под пометкой
    /// «местный», и имя достаётся эталону сети.
    /// </para>
    /// <para>
    /// Заготовки эталонов (<paramref name="drafts"/>) собирает вызывающий:
    /// разбор и полная проверка параметров живут в <see cref="ReferencePackage"/>,
    /// и хранилище не должно знать о нём второй раз.
    /// </para>
    /// </remarks>
    public async Task<NetworkApplyReport> ApplyNetworkSnapshotAsync(
        NetworkSnapshot snapshot,
        IReadOnlyDictionary<Guid, NewCalibrationReference> drafts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(drafts);
        ThrowIfDisposed();

        var nowUtc = FormatUtc(DateTimeOffset.UtcNow);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                // Повторная проверка номера внутри транзакции: два одновременных
                // приёма не должны откатить друг друга.
                var current = ToNetworkState(
                    await ReadSettingsAsync(connection, transaction, "network.", ct).ConfigureAwait(false));
                if ((current.NetworkId is { } currentId && currentId != snapshot.NetworkId) || snapshot.Serial <= current.Serial)
                {
                    throw new CalibrationStoreException(
                        "Пакет сети не новее принятого либо относится к другой сети — применять нечего.");
                }

                await ReplaceNetworkUsersAsync(connection, transaction, snapshot.Users, ct).ConfigureAwait(false);

                int added = 0, linked = 0;
                var renamed = new List<string>();
                foreach (var reference in snapshot.References)
                {
                    var retiredUtc = reference.RetiredUtc is { } r ? FormatUtc(r) : null;

                    if (await ScalarOrNullAsync(connection, transaction,
                            "SELECT Id FROM Reference WHERE NetworkId = $networkId;", ct,
                            ("$networkId", reference.Id.ToString())).ConfigureAwait(false) is long known)
                    {
                        await ExecuteAsync(connection, transaction,
                            "UPDATE Reference SET RetiredUtc = $retired WHERE Id = $id;", ct,
                            ("$retired", retiredUtc), ("$id", known)).ConfigureAwait(false);
                        continue;
                    }

                    var name = reference.Name.Trim();
                    var normalized = NormalizeName(name);
                    var local = await ReadLocalByNameAsync(connection, transaction, normalized, ct).ConfigureAwait(false);
                    if (local is { } same
                        && same.NetworkId is null
                        && string.Equals(same.ParamHash, reference.ParamHash, StringComparison.OrdinalIgnoreCase))
                    {
                        await ExecuteAsync(connection, transaction,
                            "UPDATE Reference SET NetworkId = $networkId, RetiredUtc = $retired WHERE Id = $id;", ct,
                            ("$networkId", reference.Id.ToString()), ("$retired", retiredUtc), ("$id", same.Id))
                            .ConfigureAwait(false);
                        linked++;
                        continue;
                    }

                    if (local is { } clash)
                    {
                        var localName = await FreeNameAsync(connection, transaction, name + " (местный)", ct)
                            .ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, """
                            UPDATE Reference SET Name = $name, NameNormalized = $normalized,
                                RetiredUtc = COALESCE(RetiredUtc, $now)
                            WHERE Id = $id;
                            """, ct,
                            ("$name", localName), ("$normalized", NormalizeName(localName)), ("$now", nowUtc), ("$id", clash.Id))
                            .ConfigureAwait(false);
                        renamed.Add(localName);
                    }

                    if (!drafts.TryGetValue(reference.Id, out var draft))
                    {
                        throw new CalibrationStoreException($"Для эталона сети «{name}» не собрана заготовка.");
                    }

                    var roleOverrides = (draft.Roles ?? ParameterRoleMap.Default)
                        .WithMotorCompTransfer(draft.TransferMotorComp)
                        .SerializeOverrides();
                    var referenceId = await InsertReferenceAsync(
                        connection, transaction, draft, name, normalized, roleOverrides, nowUtc, reference.Id, ct)
                        .ConfigureAwait(false);
                    if (retiredUtc is not null)
                    {
                        await ExecuteAsync(connection, transaction,
                            "UPDATE Reference SET RetiredUtc = $retired WHERE Id = $id;", ct,
                            ("$retired", retiredUtc), ("$id", referenceId)).ConfigureAwait(false);
                    }

                    added++;
                }

                // Всё, чего нет в пакете, из обращения выводится: и местное, и
                // снятое администратором с публикации.
                var inPackage = snapshot.References.Select(static r => r.Id.ToString()).ToHashSet(StringComparer.Ordinal);
                var retired = 0;
                foreach (var (id, networkId) in await ReadActiveReferenceKeysAsync(connection, transaction, ct).ConfigureAwait(false))
                {
                    if (networkId is null || !inPackage.Contains(networkId))
                    {
                        await ExecuteAsync(connection, transaction,
                            "UPDATE Reference SET RetiredUtc = $now WHERE Id = $id;", ct,
                            ("$now", nowUtc), ("$id", id)).ConfigureAwait(false);
                        retired++;
                    }
                }

                await UpsertSettingAsync(connection, transaction, SettingNetworkId, snapshot.NetworkId.ToString(), nowUtc, ct).ConfigureAwait(false);
                await UpsertSettingAsync(connection, transaction, SettingNetworkSerial,
                    snapshot.Serial.ToString(CultureInfo.InvariantCulture), nowUtc, ct).ConfigureAwait(false);
                await UpsertSettingAsync(connection, transaction, SettingNetworkIssuedUtc, FormatUtc(snapshot.IssuedUtc), nowUtc, ct).ConfigureAwait(false);
                await UpsertSettingAsync(connection, transaction, SettingNetworkIssuedBy, snapshot.IssuedBy, nowUtc, ct).ConfigureAwait(false);

                await CommitAsync(transaction, ct).ConfigureAwait(false);
                return new NetworkApplyReport(snapshot.Serial, snapshot.Users.Count, added, linked, retired, renamed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Пользователи сети в порядке логина.</summary>
    public async Task<IReadOnlyList<NetworkUser>> ListNetworkUsersAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            return await ReadNetworkUsersAsync(connection, null, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Администратор ────────────────────────────────────────────────────

    /// <summary>
    /// Заводит новую сеть на стенде администратора: ключ сети и первого
    /// администратора. Пакет ещё не выпущен — номер выпуска остаётся нулевым.
    /// </summary>
    public async Task CreateNetworkAsync(NetworkUser admin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ThrowIfDisposed();

        if (admin.Role != OtkRoles.Admin || !admin.IsActive)
        {
            throw new ArgumentException("Первый пользователь сети — действующий администратор.", nameof(admin));
        }

        var nowUtc = FormatUtc(DateTimeOffset.UtcNow);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var current = ToNetworkState(
                    await ReadSettingsAsync(connection, transaction, "network.", ct).ConfigureAwait(false));
                if (current.IsJoined)
                {
                    throw new CalibrationStoreException("Стенд уже подключён к сети ОТК — вторую сеть на нём не заводят.");
                }

                await ReplaceNetworkUsersAsync(connection, transaction, [admin], ct).ConfigureAwait(false);
                await UpsertSettingAsync(connection, transaction, SettingNetworkId, Guid.NewGuid().ToString(), nowUtc, ct).ConfigureAwait(false);
                await UpsertSettingAsync(connection, transaction, SettingNetworkSerial, "0", nowUtc, ct).ConfigureAwait(false);
                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Заводит или правит пользователя сети на стенде администратора.</summary>
    /// <remarks>
    /// Изменение доходит до стендов только со следующим выпуском пакета.
    /// Последнего действующего администратора понизить или отключить нельзя:
    /// следующий пакет выпустить было бы некому.
    /// </remarks>
    public async Task SaveNetworkUserAsync(NetworkUser user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(user.Login) || string.IsNullOrWhiteSpace(user.DisplayName))
        {
            throw new CalibrationStoreException("Логин и ФИО обязательны.");
        }

        if (!OtkRoles.IsKnown(user.Role))
        {
            throw new CalibrationStoreException($"Неизвестная роль «{user.Role}».");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var users = (await ReadNetworkUsersAsync(connection, transaction, ct).ConfigureAwait(false)).ToList();

                if (users.Any(u => u.Id != user.Id && string.Equals(u.Login, user.Login.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new CalibrationStoreException($"Логин «{user.Login.Trim()}» уже занят.");
                }

                users.RemoveAll(u => u.Id == user.Id);
                users.Add(user with { Login = user.Login.Trim(), DisplayName = user.DisplayName.Trim() });

                if (!users.Any(static u => u.IsActive && u.Role == OtkRoles.Admin))
                {
                    throw new CalibrationStoreException(
                        "Это последний действующий администратор: сначала назначьте другого.");
                }

                await ReplaceNetworkUsersAsync(connection, transaction, users, ct).ConfigureAwait(false);
                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Собирает следующий пакет сети из реестра администратора.
    /// </summary>
    /// <remarks>
    /// В пакет входят все действующие эталоны стенда администратора — заведённые
    /// им до этого выпуска получают ключ сети здесь же — и все выведенные,
    /// которые сеть уже знала: стенды должны узнать, что эталон выведен.
    /// Номер выпуска фиксируется только после записи файла
    /// (<see cref="CommitNetworkIssueAsync"/>): незаписанный пакет номера не тратит.
    /// </remarks>
    public async Task<NetworkSnapshot> PrepareNetworkSnapshotAsync(string issuedBy, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        List<CalibrationReference> references;
        NetworkState state;
        IReadOnlyList<NetworkUser> users;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                state = ToNetworkState(await ReadSettingsAsync(connection, transaction, "network.", ct).ConfigureAwait(false));
                if (!state.IsJoined)
                {
                    throw new CalibrationStoreException("Сеть на этом стенде не заведена.");
                }

                foreach (var (id, networkId) in await ReadActiveReferenceKeysAsync(connection, transaction, ct).ConfigureAwait(false))
                {
                    if (networkId is null)
                    {
                        await ExecuteAsync(connection, transaction,
                            "UPDATE Reference SET NetworkId = $networkId WHERE Id = $id;", ct,
                            ("$networkId", Guid.NewGuid().ToString()), ("$id", id)).ConfigureAwait(false);
                    }
                }

                users = await ReadNetworkUsersAsync(connection, transaction, ct).ConfigureAwait(false);

                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = ReferenceColumnsSql + " WHERE p.NetworkId IS NOT NULL ORDER BY p.NameNormalized;";
                    references = [];
                    await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        references.Add(ReadReference(reader));
                    }
                }

                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        var networkReferences = new List<NetworkReference>(references.Count);
        foreach (var reference in references)
        {
            var scripts = await GetReferenceScriptsAsync(reference.Id, ct).ConfigureAwait(false);
            var package = ReferencePackage.FromReference(reference, scripts, issuedBy);
            networkReferences.Add(new NetworkReference(
                reference.NetworkId!.Value, reference.Name, reference.ParamHash, reference.RetiredUtc, package.ToJson()));
        }

        return new NetworkSnapshot(
            state.NetworkId!.Value,
            state.Serial + 1,
            DateTimeOffset.UtcNow,
            issuedBy,
            users,
            networkReferences);
    }

    /// <summary>Фиксирует выпуск пакета после того, как файл записан в папку обмена.</summary>
    public async Task CommitNetworkIssueAsync(NetworkSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();

        var nowUtc = FormatUtc(DateTimeOffset.UtcNow);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await UpsertSettingAsync(connection, transaction, SettingNetworkSerial,
                    snapshot.Serial.ToString(CultureInfo.InvariantCulture), nowUtc, ct).ConfigureAwait(false);
                await UpsertSettingAsync(connection, transaction, SettingNetworkIssuedUtc, FormatUtc(snapshot.IssuedUtc), nowUtc, ct).ConfigureAwait(false);
                await UpsertSettingAsync(connection, transaction, SettingNetworkIssuedBy, snapshot.IssuedBy, nowUtc, ct).ConfigureAwait(false);
                await CommitAsync(transaction, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Внутреннее ───────────────────────────────────────────────────────

    private static NetworkState ToNetworkState(IReadOnlyDictionary<string, string> values) => new(
        values.TryGetValue(SettingNetworkId, out var id) && Guid.TryParse(id, out var networkId) ? networkId : null,
        values.TryGetValue(SettingNetworkSerial, out var serial)
            && long.TryParse(serial, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0,
        values.TryGetValue(SettingNetworkIssuedUtc, out var issued) ? ParseUtc(issued) : null,
        values.TryGetValue(SettingNetworkIssuedBy, out var by) ? by : string.Empty,
        values.TryGetValue(SettingNetworkCode, out var code) ? code : string.Empty);

    private static async Task<Dictionary<string, string>> ReadSettingsAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string prefix, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Name, SettingValue FROM Setting WHERE substr(Name, 1, length($prefix)) = $prefix;";
        AddParameter(command, "$prefix", prefix);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    private static async Task<IReadOnlyList<NetworkUser>> ReadNetworkUsersAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, Login, DisplayName, Role, IsActive, PasswordHash, PinHash
            FROM NetworkUser ORDER BY LoginNormalized;
            """;

        var users = new List<NetworkUser>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            users.Add(new NetworkUser(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4) != 0,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return users;
    }

    private static async Task ReplaceNetworkUsersAsync(
        SqliteConnection connection, SqliteTransaction transaction, IEnumerable<NetworkUser> users, CancellationToken ct)
    {
        await ExecuteAsync(connection, transaction, "DELETE FROM NetworkUser;", ct).ConfigureAwait(false);
        foreach (var user in users)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO NetworkUser (Id, Login, LoginNormalized, DisplayName, Role, IsActive, PasswordHash, PinHash)
                VALUES ($id, $login, $normalized, $name, $role, $active, $password, $pin);
                """, ct,
                ("$id", user.Id.ToString()),
                ("$login", user.Login.Trim()),
                ("$normalized", user.Login.Trim().ToUpperInvariant()),
                ("$name", user.DisplayName.Trim()),
                ("$role", user.Role),
                ("$active", user.IsActive ? 1L : 0L),
                ("$password", user.PasswordHash),
                ("$pin", user.PinHash)).ConfigureAwait(false);
        }
    }

    private static async Task<(long Id, string ParamHash, Guid? NetworkId)?> ReadLocalByNameAsync(
        SqliteConnection connection, SqliteTransaction transaction, string normalized, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, ParamHash, NetworkId FROM Reference WHERE NameNormalized = $normalized;";
        AddParameter(command, "$normalized", normalized);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return (reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)));
    }

    private static async Task<List<(long Id, string? NetworkId)>> ReadActiveReferenceKeysAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, NetworkId FROM Reference WHERE RetiredUtc IS NULL;";
        var rows = new List<(long, string?)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>
    /// Первое значение запроса либо <c>null</c>, если строк нет. Для вопросов
    /// «есть ли такая запись»; <see cref="ScalarAsync"/> на пустой ответ
    /// намеренно бросает исключение.
    /// </summary>
    private static async Task<object?> ScalarOrNullAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] args)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in args)
        {
            AddParameter(command, name, value);
        }

        var scalar = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return scalar is DBNull ? null : scalar;
    }

    /// <summary>Свободное имя: исходное либо с номером.</summary>
    private static async Task<string> FreeNameAsync(
        SqliteConnection connection, SqliteTransaction transaction, string baseName, CancellationToken ct)
    {
        var candidate = baseName;
        for (var n = 2; ; n++)
        {
            if (await ScalarOrNullAsync(connection, transaction,
                    "SELECT 1 FROM Reference WHERE NameNormalized = $normalized;", ct,
                    ("$normalized", NormalizeName(candidate))).ConfigureAwait(false) is null)
            {
                return candidate;
            }

            candidate = string.Create(CultureInfo.InvariantCulture, $"{baseName} {n}");
        }
    }
}
