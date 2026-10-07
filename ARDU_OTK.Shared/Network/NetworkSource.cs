namespace ARDU_OTK.Shared.Network;

/// <summary>
/// Где лежит пакет сети: публичный репозиторий GitHub.
/// </summary>
/// <remarks>
/// <para>
/// GitHub выбран потому, что станции уже ходят туда за обновлениями
/// приложения: новый канал не требует ни установки программ, ни открытия
/// доступа. Репозиторий отдельный от кода приложения, чтобы токен
/// администратора, которым выпускается пакет, не давал права писать в код.
/// </para>
/// <para>
/// Публичность безопасна: пакет подписан (<see cref="NetworkPackageFile"/>) и
/// зашифрован кодом сети (<see cref="NetworkSeal"/>).
/// </para>
/// </remarks>
public static class NetworkSource
{
    public const string Repository = "RomanBRempel/ARDU_OTK.Network";

    public const string FilePath = "network.otknet";

    /// <summary>
    /// Чтение через API, а не через raw.githubusercontent.com: raw кэшируется
    /// до пяти минут, и только что выпущенный пакет станция не увидела бы.
    /// </summary>
    public const string ContentsUrl = "https://api.github.com/repos/" + Repository + "/contents/" + FilePath;
}
