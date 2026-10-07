namespace ARDU_OTK.Shared.Contracts;

/// <summary>
/// Пользователь, как его хранит реплика стенда.
/// </summary>
/// <remarks>
/// Хеш пароля в реплику не попадает никогда: пароль даёт полный вход, и его
/// хеш на каждом стенде умножил бы число мест, откуда его можно перебрать.
/// Хеш PIN попадает только для ролей, которым разрешён вход без связи
/// (<see cref="Security.OtkRoles.CanUseOfflinePin"/>).
/// </remarks>
public sealed record UserReplica(
    Guid Id,
    string Login,
    string DisplayName,
    string Role,
    bool IsActive,
    string? OfflinePinHash,
    long Seq);

/// <summary>
/// Изменения сервера после номера <c>since</c>.
/// </summary>
/// <remarks>
/// <para>
/// Каждая запись реплицируемых таблиц сервера несёт номер изменения
/// <c>Seq</c> из одного сквозного счётчика. Стенд запоминает
/// <see cref="Seq"/> последнего ответа и в следующий раз спрашивает только
/// то, что изменилось после него. Записи приходят целиком, последняя версия
/// заменяет предыдущую: сливать поля не нужно, потому что у каждой таблицы
/// один хозяин записи — сервер.
/// </para>
/// <para>
/// 🔴 <see cref="ServerId"/> меняется, только если базу сервера создали
/// заново. Номера изменений новой базы начинаются с нуля, и стенд, спросивший
/// «после 500», молча не получил бы первых пятисот записей. Увидев чужой
/// <see cref="ServerId"/>, стенд обязан забыть свой номер и забрать всё.
/// </para>
/// </remarks>
/// <param name="HasMore">Ответ усечён по объёму; спросить снова с новым <see cref="Seq"/>.</param>
public sealed record SyncPullResponse(
    Guid ServerId,
    long Seq,
    bool HasMore,
    IReadOnlyList<UserReplica> Users,
    IReadOnlyList<ReferenceRecord> References);
