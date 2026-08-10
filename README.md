# ARDU ОТК

Настольное приложение Windows (WinUI 3, .NET 10) с доставкой и обновлением через интернет.

## Модель развёртывания

Приложение **unpackaged** (без MSIX) и **self-contained**: в установщик входят и .NET, и
Windows App SDK. На цеховом ПК не требуется ничего предварительно устанавливать и не нужны
права администратора — установка идёт в `%LocalAppData%\ARDU_OTK`.

Обновления доставляет [Velopack](https://velopack.io) через GitHub Releases репозитория
[RomanBRempel/ARDU_OTK](https://github.com/RomanBRempel/ARDU_OTK). Репозиторий публичный,
поэтому клиентам не нужен токен.

Установщик не подписан. При первой установке Windows покажет экран SmartScreen
(«Подробнее» → «Выполнить в любом случае») — один раз на машину. Обновления после этого
идут молча.

## Выпуск новой версии

```
git tag v0.2.0
git push origin v0.2.0
```

Дальше всё делает workflow [release.yml](.github/workflows/release.yml): собирает, упаковывает
и публикует GitHub Release. Версия берётся из тега, править её в файлах не нужно —
[Directory.Build.props](Directory.Build.props) содержит только значение для локальной сборки.

Тег обязан быть вида `v1.2.3` (SemVer): по нему Velopack определяет, какая версия новее.

## Как обновляются рабочие места

Приложение проверяет обновления при старте, скачивает их в фоне и применяет с перезапуском.
Применение блокируется, пока идёт замер: см. `UpdateService.IsBusy` в
[Services/UpdateService.cs](ARDU_OTK/Services/UpdateService.cs). Код стенда должен установить
этот делегат, иначе стенд всегда считается свободным и обновление может прервать работу.

Отсутствие сети — штатная ситуация: приложение сообщает об этом и продолжает работать.

Первая загрузка около 110 МБ, последующие обновления приходят дельтами.

## Локальная сборка установщика

```powershell
dotnet publish ARDU_OTK\ARDU_OTK.csproj -c Release -r win-x64 -p:Version=0.1.0 -o publish
vpk pack --packId ARDU_OTK --packVersion 0.1.0 --packDir publish --mainExe ARDU_OTK.exe --outputDir releases
```

`vpk` ставится командой `dotnet tool install -g vpk --version 1.2.0`. Версия CLI обязана
совпадать с версией пакета `Velopack` в csproj.

## Если позже появится сертификат подписи

Подпись добавляется в `vpk pack` параметрами `--signParams` без изменения остальной схемы.
Это уберёт предупреждение SmartScreen. Переход на MSIX не потребуется.
