using Velopack;
using Velopack.Sources;

namespace ARDU_OTK.Services;

/// <summary>
/// Состояние цикла обновления, пригодное для показа в UI.
/// </summary>
public enum UpdateState
{
    /// <summary>Проверок ещё не было.</summary>
    Idle,

    /// <summary>Приложение запущено не из установленной копии — обновления неприменимы.</summary>
    NotInstalled,

    Checking,

    /// <summary>Установлена последняя версия.</summary>
    UpToDate,

    Downloading,

    /// <summary>Обновление скачано и ждёт перезапуска.</summary>
    ReadyToApply,

    /// <summary>Проверка не удалась (нет сети, недоступен GitHub).</summary>
    Failed,
}

/// <summary>
/// Доставка обновлений через GitHub Releases.
/// </summary>
/// <remarks>
/// Обновление никогда не применяется само по себе. Скачивание идёт в фоне и
/// безопасно в любой момент, а подмена файлов с перезапуском требует явного
/// вызова <see cref="ApplyAndRestart"/> — и он отказывает, пока
/// <see cref="IsBusy"/> сообщает о работающем стенде. Прерывать замер ради
/// обновления недопустимо: результат теряется, а деталь уже в приспособлении.
/// </remarks>
public sealed class UpdateService
{
    /// <summary>
    /// Репозиторий с релизами. Публичный, поэтому токен не нужен.
    /// Обязан совпадать с репозиторием, куда публикует workflow release.yml,
    /// иначе приложение будет молча искать обновления не там.
    /// </summary>
    private const string RepositoryUrl = "https://github.com/RomanBRempel/ARDU_OTK";

    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        State = _manager.IsInstalled ? UpdateState.Idle : UpdateState.NotInstalled;
    }

    /// <summary>
    /// Возвращает <c>true</c>, пока идёт замер и перезапуск недопустим.
    /// Устанавливается кодом стенда; по умолчанию стенд считается свободным.
    /// </summary>
    public Func<bool> IsBusy { get; set; } = static () => false;

    public UpdateState State { get; private set; }

    /// <summary>Текст последней ошибки, если <see cref="State"/> — <see cref="UpdateState.Failed"/>.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Версия установленной копии. У неустановленной сборки (запуск из
    /// каталога сборки при разработке) версии нет — Velopack узнаёт её из
    /// метаданных установки, а не из атрибутов сборки.
    /// </summary>
    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>Версия, готовая к установке, если обновление уже скачано.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public event EventHandler? StateChanged;

    /// <summary>
    /// Проверяет наличие обновления и, если оно есть, сразу скачивает его.
    /// Безопасно вызывать во время замера: файлы работающей версии не трогаются.
    /// </summary>
    /// <returns><c>true</c>, если обновление скачано и ждёт перезапуска.</returns>
    public async Task<bool> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
        {
            SetState(UpdateState.NotInstalled);
            return false;
        }

        try
        {
            SetState(UpdateState.Checking);
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                SetState(UpdateState.UpToDate);
                return false;
            }

            SetState(UpdateState.Downloading);
            await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken).ConfigureAwait(false);

            _pending = update;
            SetState(UpdateState.ReadyToApply);
            return true;
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Idle);
            return false;
        }
        catch (Exception ex)
        {
            // Отсутствие сети — штатная ситуация для цехового ПК, а не сбой:
            // приложение обязано продолжить работу без обновлений.
            LastError = ex.Message;
            SetState(UpdateState.Failed);
            return false;
        }
    }

    /// <summary>
    /// Применяет скачанное обновление и перезапускает приложение.
    /// </summary>
    /// <returns>
    /// <c>false</c>, если применять нечего или стенд занят. Отложенное
    /// обновление останется скачанным и применится при следующем вызове —
    /// как правило, при следующем запуске приложения.
    /// </returns>
    public bool ApplyAndRestart()
    {
        if (_pending is null || State != UpdateState.ReadyToApply)
        {
            return false;
        }

        if (IsBusy())
        {
            return false;
        }

        _manager.ApplyUpdatesAndRestart(_pending);
        return true;
    }

    private void SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
