using System;
using System.Globalization;
using System.IO;

namespace ARDU_OTK.Services.Store;

/// <summary>
/// Единственный источник путей к пользовательским данным приложения: база
/// прогонов, резервные копии, выгруженные протоколы.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 Хранилище лежит в стабильном каталоге пользователя <b>вне каталога
/// приложения</b> — <c>%LOCALAPPDATA%\ARDU_OTK.Data</c>. Это решение о
/// сохранности данных, а не о стиле.
/// </para>
/// <para>
/// Velopack при обновлении <b>подменяет каталог приложения целиком</b> и
/// перезапускает процесс, а деинсталляция сносит корень установки
/// <c>%LOCALAPPDATA%\ARDU_OTK</c> вместе со всем, что внутри. Поэтому каталог
/// данных сделан <b>соседом</b> корня установки, а не его потомком: имя
/// намеренно отличается на суффикс <c>.Data</c>, чтобы установщик и апдейтер
/// никогда не приняли его за свой. Данные — это годы работы ОТК, каталог
/// приложения — расходный материал; их времена жизни не должны совпадать.
/// </para>
/// <para>
/// Путь <b>никогда</b> не выводится из <see cref="AppContext.BaseDirectory"/>,
/// <c>Assembly.Location</c> или <c>Process.MainModule</c>: все они
/// разрешаются внутри той самой версионной папки, которую подменяет Velopack.
/// Отказ был бы тихим — приложение стартует, показывает «прогонов ещё нет», и
/// история просто исчезает. По той же причине в пути не должно быть номера
/// версии.
/// </para>
/// <para>
/// Перемещаемый <c>%APPDATA%</c>, OneDrive и любые синхронизируемые папки
/// исключены: движок синхронизации уводит живой файл SQLite вместе с WAL
/// из-под приложения. <c>ApplicationData.Current.LocalFolder</c> недоступен —
/// сборка unpackaged (<c>WindowsPackageType=None</c>), идентичности пакета нет
/// и вызов бросает исключение.
/// </para>
/// </remarks>
public sealed class AppPaths
{
    /// <summary>Имя каталога данных — сосед корня установки, не его потомок.</summary>
    public const string StoreDirectoryName = "ARDU_OTK.Data";

    private const string DevelopmentDirectoryName = "dev";
    private const string BackupsDirectoryName = "backups";
    private const string ProtocolsDirectoryName = "protocols";
    private const string DatabaseFileName = "ardu_otk.db";

    private AppPaths(string storeRoot, bool isDevelopmentStore)
    {
        StoreRoot = storeRoot;
        IsDevelopmentStore = isDevelopmentStore;

        // Каталог разработки — подкаталог того же корня: его видно рядом с
        // боевым, он копируется и удаляется вместе с ним и не заводит второе
        // «секретное» место хранения.
        DataRoot = isDevelopmentStore
            ? Path.Combine(storeRoot, DevelopmentDirectoryName)
            : storeRoot;
    }

    /// <summary>
    /// Собирает пути под состояние установки приложения.
    /// </summary>
    /// <param name="isInstalled">
    /// Запущена ли установленная копия. Значение берётся у
    /// <c>UpdateService</c> (<c>UpdateState.NotInstalled</c> либо <c>null</c> в
    /// <c>CurrentVersion</c>), а <b>не</b> из символа сборки: Release, запущенный
    /// из <c>bin\</c>, — это тоже не установленная копия, а Debug вполне может
    /// быть установлен.
    /// </param>
    /// <param name="storeRootOverride">
    /// Переопределение корня хранилища (ключ командной строки <c>--store</c>)
    /// для стенда, который держит базу на общем ресурсе. Внимание: WAL требует
    /// разделяемой памяти и не работает по SMB/UNC — такой путь придётся
    /// открывать в режиме <c>journal_mode=DELETE</c> либо отклонять.
    /// </param>
    /// <remarks>
    /// 🔴 Неустановленная копия работает с <b>отдельным</b> хранилищем
    /// разработки. Иначе сборка разработчика запишет тестовые прогоны в
    /// доказательную базу цеха и, что хуже, мигрирует её на более новую схему —
    /// после чего установленное приложение откажется её открывать (см. проверку
    /// <c>user_version</c> в <see cref="SqliteCalibrationStore"/>), и стенд
    /// встанет до восстановления из резервной копии.
    /// </remarks>
    public static AppPaths ForInstallState(bool isInstalled, string? storeRootOverride = null)
    {
        var root = string.IsNullOrWhiteSpace(storeRootOverride)
            ? Path.Combine(GetLocalApplicationData(), StoreDirectoryName)
            : Path.GetFullPath(storeRootOverride);

        return new AppPaths(root, isDevelopmentStore: !isInstalled);
    }

    /// <summary>Корень хранилища — <c>%LOCALAPPDATA%\ARDU_OTK.Data</c>.</summary>
    public string StoreRoot { get; }

    /// <summary>
    /// Каталог, в котором лежат база и её спутники: сам корень для установленной
    /// копии, подкаталог <c>dev</c> — для запуска из каталога сборки.
    /// </summary>
    public string DataRoot { get; }

    /// <summary>
    /// Используется ли хранилище разработки. Это состояние обязано быть видно
    /// оператору (заголовок окна, «О программе»): перепутанное хранилище
    /// объясняет и «пропавшую» историю, и лишние прогоны в отчёте.
    /// </summary>
    public bool IsDevelopmentStore { get; }

    /// <summary>Файл базы. Рядом с ним живут спутники <c>-wal</c> и <c>-shm</c>.</summary>
    public string DatabaseFilePath => Path.Combine(DataRoot, DatabaseFileName);

    /// <summary>
    /// Каталог резервных копий. Лежит в хранилище, а не рядом с исполняемым
    /// файлом: копия возле <c>.exe</c> уничтожается ближайшим обновлением —
    /// ровно тогда, когда она нужнее всего.
    /// </summary>
    public string BackupsDirectory => Path.Combine(DataRoot, BackupsDirectoryName);

    /// <summary>Каталог выгруженных протоколов приёмки.</summary>
    public string ProtocolExportDirectory => Path.Combine(DataRoot, ProtocolsDirectoryName);

    /// <summary>
    /// Создаёт каталоги хранилища. Отсутствие базы — это первый запуск, а не
    /// ошибка.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(ProtocolExportDirectory);
    }

    /// <summary>Создаёт при необходимости каталог резервных копий и возвращает его.</summary>
    public string EnsureBackupsDirectory()
    {
        Directory.CreateDirectory(BackupsDirectory);
        return BackupsDirectory;
    }

    /// <summary>Создаёт при необходимости каталог протоколов и возвращает его.</summary>
    public string EnsureProtocolExportDirectory()
    {
        Directory.CreateDirectory(ProtocolExportDirectory);
        return ProtocolExportDirectory;
    }

    /// <summary>
    /// Имя файла резервной копии вида
    /// <c>ardu_otk-&lt;версия приложения&gt;-&lt;версия схемы&gt;-&lt;метка UTC&gt;.db</c>.
    /// </summary>
    /// <remarks>
    /// Версия приложения стоит в имени намеренно: хранилище открывается только
    /// этой версией или более новой, поэтому имя сразу говорит, чем копию
    /// вообще можно прочитать.
    /// </remarks>
    public string NewBackupFilePath(string appVersion, int schemaVersion, DateTimeOffset utcNow)
    {
        var stamp = utcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var name = string.Format(
            CultureInfo.InvariantCulture,
            "ardu_otk-{0}-s{1}-{2}.db",
            SanitizeForFileName(appVersion),
            schemaVersion,
            stamp);

        return Path.Combine(EnsureBackupsDirectory(), name);
    }

    /// <summary>Строка для окна диагностики: где именно лежат данные.</summary>
    public override string ToString() => IsDevelopmentStore
        ? $"{DatabaseFilePath} (хранилище разработки, не боевая база)"
        : DatabaseFilePath;

    private static string GetLocalApplicationData()
    {
        // DoNotVerify: каталог может ещё не существовать на свежем профиле —
        // это нормально, создадим сами.
        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrEmpty(local))
        {
            local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty;
        }

        if (string.IsNullOrEmpty(local))
        {
            throw new InvalidOperationException(
                "Не удалось определить %LOCALAPPDATA%: некуда положить хранилище прогонов.");
        }

        return local;
    }

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var buffer = value.Trim().ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (Array.IndexOf(invalid, buffer[i]) >= 0)
            {
                buffer[i] = '_';
            }
        }

        return new string(buffer);
    }
}
