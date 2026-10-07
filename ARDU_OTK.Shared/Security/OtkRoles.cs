namespace ARDU_OTK.Shared.Security;

/// <summary>
/// Роли сети ОТК и то, что каждая из них разрешает.
/// </summary>
/// <remarks>
/// <para>
/// Роли — фиксированный набор, а не таблица прав. Права ОТК немногочисленны и
/// жёстко связаны с регламентом: эталон заводит администратор, сдаёт плату
/// оператор, журнал смотрит контролёр. Редактируемая матрица прав дала бы
/// возможность выдать оператору право заводить эталоны одной галочкой — то
/// есть снять ровно тот контроль, ради которого сеть ОТК и заведена.
/// </para>
/// <para>
/// 🔴 Проверка роли выполняется на сервере. Стенд по роли только прячет
/// недоступное в интерфейсе; разрешение, проверенное одним стендом, —
/// это разрешение, которое стенд выдал сам себе.
/// </para>
/// </remarks>
public static class OtkRoles
{
    /// <summary>Пользователи, эталоны, журнал всех стендов.</summary>
    public const string Admin = "admin";

    /// <summary>Журнал всех стендов, без права менять эталоны и пользователей.</summary>
    public const string Controller = "controller";

    /// <summary>Прогон плат по опубликованным эталонам.</summary>
    public const string Operator = "operator";

    /// <summary>Все роли в порядке убывания полномочий.</summary>
    public static readonly IReadOnlyList<string> All = [Admin, Controller, Operator];

    /// <summary>Роль известна этой сборке.</summary>
    public static bool IsKnown(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);

    /// <summary>Может заводить, публиковать и выводить из обращения эталоны.</summary>
    public static bool CanManageReferences(string role) => role == Admin;

    /// <summary>Может заводить пользователей и менять им роли и секреты.</summary>
    public static bool CanManageUsers(string role) => role == Admin;

    /// <summary>Видит прогоны всех стендов и всех операторов.</summary>
    public static bool CanViewJournal(string role) => role is Admin or Controller;

    /// <summary>
    /// Может входить по PIN без связи с сервером.
    /// </summary>
    /// <remarks>
    /// 🔴 Только оператор. PIN короткий, а его хеш лежит в реплике на стенде:
    /// у того, кто унёс базу стенда, перебор займёт минуты. Пока PIN открывает
    /// одни прогоны, такая цена приемлема; открой он администрирование —
    /// украденная база стенда стала бы ключом к эталонам всей сети.
    /// </remarks>
    public static bool CanUseOfflinePin(string role) => role == Operator;

    /// <summary>Подпись роли для интерфейса.</summary>
    public static string Caption(string role) => role switch
    {
        Admin => "Администратор",
        Controller => "Контролёр",
        Operator => "Оператор",
        _ => role,
    };
}
