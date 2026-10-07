namespace ARDU_OTK.Shared.Network;

/// <summary>
/// Пакет сети ОТК: всё, что стенд обязан знать от администратора, — кто может
/// работать и по каким эталонам.
/// </summary>
/// <remarks>
/// <para>
/// Пакет — полный снимок, а не набор изменений. Обмен идёт редко и через
/// папку, которую синхронизирует сторонняя программа; пропущенная разностная
/// часть оставила бы стенд в состоянии, которого у администратора не было
/// никогда. Снимок самодостаточен: принять можно любой новее принятого.
/// </para>
/// <para>
/// 🔴 Пакет же — резервная копия сети. Переустановленный стенд
/// администратора восстанавливает пользователей и эталоны из последнего
/// выпущенного пакета, а не из памяти.
/// </para>
/// </remarks>
/// <param name="NetworkId">
/// Ключ сети. Стенд, принявший пакет одной сети, пакет другой не примет:
/// иначе две сети с одним ключом администратора (тест и цех) перетирали бы
/// друг другу эталоны.
/// </param>
/// <param name="Serial">
/// Номер выпуска. Строго растёт. Стенд не примет номер, не больший принятого:
/// старый пакет, подложенный в папку, вернул бы в обращение выведенный эталон
/// и снятого с работы человека.
/// </param>
/// <param name="IssuedBy">Кто выпустил — ФИО администратора.</param>
public sealed record NetworkSnapshot(
    Guid NetworkId,
    long Serial,
    DateTimeOffset IssuedUtc,
    string IssuedBy,
    IReadOnlyList<NetworkUser> Users,
    IReadOnlyList<NetworkReference> References);

/// <summary>Пользователь сети.</summary>
/// <param name="PasswordHash">
/// Хеш пароля (<see cref="Security.SecretHasher"/>). Сервера нет, поэтому пароль
/// проверяет сам стенд и хеш едет на каждый стенд — пароли обязаны быть
/// длинными (<see cref="Security.SecretPolicy"/>).
/// </param>
/// <param name="PinHash">Хеш PIN; учитывается только для ролей с <see cref="Security.OtkRoles.CanUseOfflinePin"/>.</param>
public sealed record NetworkUser(
    Guid Id,
    string Login,
    string DisplayName,
    string Role,
    bool IsActive,
    string? PasswordHash,
    string? PinHash);

/// <summary>Эталон сети.</summary>
/// <param name="Id">Ключ эталона во всей сети; стенды хранят его рядом со своим локальным ключом.</param>
/// <param name="PackageJson">Файл выгрузки эталона <c>.otkref</c> целиком.</param>
public sealed record NetworkReference(
    Guid Id,
    string Name,
    string ParamHash,
    DateTimeOffset? RetiredUtc,
    string PackageJson);
