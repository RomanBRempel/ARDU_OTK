using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARDU_OTK.Services.Fc;
using ARDU_OTK.Services.Fc.Mavlink;
using ARDU_OTK.Services.Store;

namespace ARDU_OTK.Services;

/// <summary>
/// РљРѕСЂРµРЅСЊ РєРѕРјРїРѕР·РёС†РёРё РїСЂРёР»РѕР¶РµРЅРёСЏ: РїСѓС‚Рё Рє РґР°РЅРЅС‹Рј, СЂРµРµСЃС‚СЂ РїСЂРѕРіРѕРЅРѕРІ, РѕР±РЅРѕРІР»РµРЅРёСЏ
/// Рё Р·Р°РїСѓСЃРє РїСЂРѕС†РµРґСѓСЂ СЂР°Р±РѕС‚С‹ СЃ РїРѕР»С‘С‚РЅС‹Рј РєРѕРЅС‚СЂРѕР»Р»РµСЂРѕРј.
/// </summary>
/// <remarks>
/// Р—РґРµСЃСЊ Р¶Рµ Р·Р°РјС‹РєР°РµС‚СЃСЏ Р±Р»РѕРєРёСЂРѕРІРєР° РѕР±РЅРѕРІР»РµРЅРёСЏ. <see cref="UpdateService.IsBusy"/>
/// РїРѕ СѓРјРѕР»С‡Р°РЅРёСЋ РІСЃРµРіРґР° РІРѕР·РІСЂР°С‰Р°РµС‚ В«СЃРІРѕР±РѕРґРµРЅВ», РїРѕСЌС‚РѕРјСѓ Р±РµР· СЌС‚РѕР№ СЃРІСЏР·РєРё Velopack
/// РёРјРµРµС‚ РїСЂР°РІРѕ РїРѕРґРјРµРЅРёС‚СЊ С„Р°Р№Р»С‹ Рё РїРµСЂРµР·Р°РїСѓСЃС‚РёС‚СЊ РїСЂРѕС†РµСЃСЃ РїРѕСЃСЂРµРґРё Р·Р°РїРёСЃРё РІ РїР»Р°С‚Сѓ.
/// РџРµСЂРµР·Р°РїСѓСЃРє РјРµР¶РґСѓ <c>PARAM_SET</c> Рё РµРіРѕ РїСЂРѕРІРµСЂРѕС‡РЅС‹Рј С‡С‚РµРЅРёРµРј РѕСЃС‚Р°РІР»СЏРµС‚ Р±РѕСЂС‚
/// С‡Р°СЃС‚РёС‡РЅРѕ Р·Р°РїРёСЃР°РЅРЅС‹Рј, Рё РѕРїРµСЂР°С‚РѕСЂ РЅРµ РјРѕР¶РµС‚ СѓР·РЅР°С‚СЊ, С‡С‚Рѕ СѓСЃРїРµР»Рѕ РїСЂРёРјРµРЅРёС‚СЊСЃСЏ.
/// </remarks>
public sealed class AppServices
{
    private static AppServices? _instance;

    private volatile bool _procedureRunning;

    private AppServices()
    {
        Updates = new UpdateService();

        // РџСѓС‚СЊ Рє РґР°РЅРЅС‹Рј РІС‹Р±РёСЂР°РµС‚СЃСЏ РїРѕ С„Р°РєС‚Сѓ СѓСЃС‚Р°РЅРѕРІРєРё, Р° РЅРµ РїРѕ СЃРёРјРІРѕР»Сѓ СЃР±РѕСЂРєРё:
        // Release, Р·Р°РїСѓС‰РµРЅРЅС‹Р№ РёР· bin, РѕР±СЏР·Р°РЅ СЂР°Р±РѕС‚Р°С‚СЊ СЃ РѕС‚Р»Р°РґРѕС‡РЅС‹Рј С…СЂР°РЅРёР»РёС‰РµРј.
        var isInstalled = Updates.State != UpdateState.NotInstalled;

        Paths = AppPaths.ForInstallState(isInstalled);
        Paths.EnsureCreated();

        Store = new SqliteCalibrationStore(Paths, Updates.CurrentVersion ?? "0.0.0-dev");

        Updates.IsBusy = () =>
        {
            try
            {
                return _procedureRunning || Store.HasOpenRun;
            }
            catch
            {
                // РќРµ СЃРјРѕРіР»Рё РѕРїСЂРµРґРµР»РёС‚СЊ СЃРѕСЃС‚РѕСЏРЅРёРµ вЂ” СЃС‡РёС‚Р°РµРј СЃС‚РµРЅРґ Р·Р°РЅСЏС‚С‹Рј.
                // РћС€РёР±РёС‚СЊСЃСЏ РІ СЌС‚Сѓ СЃС‚РѕСЂРѕРЅСѓ Р±РµР·РѕРїР°СЃРЅРѕ, РІ РѕР±СЂР°С‚РЅСѓСЋ вЂ” РЅРµС‚.
                return true;
            }
        };
    }

    public static AppServices Instance => _instance ??= new AppServices();

    public UpdateService Updates { get; }

    public AppPaths Paths { get; }

    public SqliteCalibrationStore Store { get; }

    /// <summary>
    /// Р“РѕС‚РѕРІРёС‚ С…СЂР°РЅРёР»РёС‰Рµ Рё Р·Р°РєСЂС‹РІР°РµС‚ РїСЂРѕРіРѕРЅС‹, Р±СЂРѕС€РµРЅРЅС‹Рµ РїСЂРµРґС‹РґСѓС‰РёРј Р·Р°РїСѓСЃРєРѕРј
    /// РїСЂРѕС†РµСЃСЃР°, вЂ” РІ С‚РѕРј С‡РёСЃР»Рµ РїСЂРµСЂРІР°РЅРЅС‹Рµ РѕР±РЅРѕРІР»РµРЅРёРµРј.
    /// </summary>
    /// <remarks>
    /// рџ”ґ Р Р°Р±РѕС‚Р° СЃ SQLite РІС‹РЅРµСЃРµРЅР° РІ РїСѓР» РїРѕС‚РѕРєРѕРІ РЅР°РјРµСЂРµРЅРЅРѕ. РџРµСЂРІРѕРµ РѕР±СЂР°С‰РµРЅРёРµ Рє
    /// РЅР°С‚РёРІРЅРѕР№ С‡Р°СЃС‚Рё SQLite РёР· UI-РїРѕС‚РѕРєР° WinUI (STA) СѓР±РёРІР°РµС‚ РїСЂРѕС†РµСЃСЃ СЃ
    /// 0xC000027B: СѓРїСЂР°РІР»СЏРµРјРѕРіРѕ РёСЃРєР»СЋС‡РµРЅРёСЏ РЅРµ РІРѕР·РЅРёРєР°РµС‚, РѕР±СЂР°Р±РѕС‚С‡РёРєРё
    /// <see cref="Application.UnhandledException"/> Рё try/catch РЅРµ СЃСЂР°Р±Р°С‚С‹РІР°СЋС‚,
    /// РІ Р¶СѓСЂРЅР°Р»Рµ Windows РѕСЃС‚Р°С‘С‚СЃСЏ С‚РѕР»СЊРєРѕ stowed exception. РџСЂРѕРІРµСЂРµРЅРѕ Р±РёСЃРµРєС†РёРµР№:
    /// С‚РѕС‚ Р¶Рµ РєРѕРґ С‡РµСЂРµР· <see cref="Task.Run(Func{Task})"/> РѕС‚СЂР°Р±Р°С‚С‹РІР°РµС‚ С€С‚Р°С‚РЅРѕ.
    /// РќРµ В«СѓРїСЂРѕС‰Р°С‚СЊВ», СѓР±РёСЂР°СЏ РѕР±С‘СЂС‚РєСѓ.
    /// </remarks>
    public Task InitializeAsync(CancellationToken ct = default) => Task.Run(
        async () =>
        {
            await Store.InitializeAsync(ct).ConfigureAwait(false);
            await Store.SweepAbandonedRunsAsync(ct).ConfigureAwait(false);
        },
        ct);

    /// <summary>Р’С‹РїРѕР»РЅСЏРµС‚ СЃРµСЂРёР№РЅСѓСЋ РєР°Р»РёР±СЂРѕРІРєСѓ РєРѕРјРїР°СЃР° РЅР° РїРѕРґРєР»СЋС‡С‘РЅРЅРѕРј Р±РѕСЂС‚Сѓ.</summary>
    public async Task<CalibrationRunResult> RunCompassCalibrationAsync(
        CalibrationRequest request,
        ICalibrationProgress progress,
        CancellationToken ct)
    {
        _procedureRunning = true;
        try
        {
            // РџРѕСЂС‚ РѕС‚РєСЂС‹РІР°РµС‚СЃСЏ РјРѕРЅРѕРїРѕР»СЊРЅРѕ: РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ РЅСѓР¶РЅРѕ
            // Р·Р°РєСЂС‹С‚СЊ, РёРЅР°С‡Рµ РїСЂРѕС†РµРґСѓСЂР° РЅРµ РѕС‚РєСЂРѕРµС‚ С‚РѕС‚ Р¶Рµ COM-РїРѕСЂС‚.
            await DisconnectAsync().ConfigureAwait(false);

            // РљР°Рє Рё InitializeAsync вЂ” С‚РѕР»СЊРєРѕ РЅРµ РІ UI-РїРѕС‚РѕРєРµ: РїСЂРѕС†РµРґСѓСЂР° РїРёС€РµС‚ РІ
            // SQLite Рё СЂР°Р±РѕС‚Р°РµС‚ СЃ РїРѕСЂС‚РѕРј, Р° РїРµСЂРІРѕРµ РѕР±СЂР°С‰РµРЅРёРµ Рє РЅР°С‚РёРІРЅРѕР№ С‡Р°СЃС‚Рё
            // SQLite РёР· STA-РїРѕС‚РѕРєР° WinUI СЂРѕРЅСЏРµС‚ РїСЂРѕС†РµСЃСЃ Р±РµР· РёСЃРєР»СЋС‡РµРЅРёСЏ.
            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        var job = new SerialCompassCalibrationJob(
                            link, Store, progress, new CalibrationTolerances());
                        return await job.RunAsync(request, ct).ConfigureAwait(false);
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// РџРѕРІС‚РѕСЂРЅРѕ Р·Р°РїРёСЃС‹РІР°РµС‚ РЅР° Р±РѕСЂС‚ СЌС‚Р°Р»РѕРЅРЅС‹Рµ Р·РЅР°С‡РµРЅРёСЏ РїР°СЂР°РјРµС‚СЂРѕРІ, РЅРµ РїСЂРѕС€РµРґС€РёС…
    /// РїСЂРѕРІРµСЂРєСѓ.
    /// </summary>
    /// <remarks>
    /// <para>
    /// рџ”ґ Р’РµСЂРґРёРєС‚ РїСЂРѕРіРѕРЅР° СЌС‚Рѕ РґРµР№СЃС‚РІРёРµ <b>РЅРµ РјРµРЅСЏРµС‚</b>. РџСЂРѕРіРѕРЅ, РЅР° РєРѕС‚РѕСЂРѕРј Р±РѕСЂС‚
    /// РЅРµ РїСЂРёРЅСЏР» Р·РЅР°С‡РµРЅРёРµ, РѕСЃС‚Р°С‘С‚СЃСЏ РїСЂРѕРІР°Р»РµРЅРЅС‹Рј: РёР·РґРµР»РёРµ СЃРґР°С‘С‚СЃСЏ РїРѕРІС‚РѕСЂРЅС‹Рј
    /// РїСЂРѕРіРѕРЅРѕРј С†РµР»РёРєРѕРј, Р° РЅРµ РїРѕРґРєСЂСѓС‚РєРѕР№ РѕС‚РґРµР»СЊРЅС‹С… РїР°СЂР°РјРµС‚СЂРѕРІ Р·Р°РґРЅРёРј С‡РёСЃР»РѕРј.
    /// РРЅР°С‡Рµ РІ СЂРµРµСЃС‚СЂРµ РїРѕСЏРІРёР»Р°СЃСЊ Р±С‹ РїР»Р°С‚Р°, В«РїСЂРѕС€РµРґС€Р°СЏВ» РїСЂРѕРІРµСЂРєСѓ, РєРѕС‚РѕСЂСѓСЋ РѕРЅР° РЅРµ
    /// РїСЂРѕС…РѕРґРёР»Р°.
    /// </para>
    /// <para>
    /// РџРѕРїС‹С‚РєР° Р»РѕР¶РёС‚СЃСЏ РІ С‚РѕС‚ Р¶Рµ РїСЂРѕРіРѕРЅ РѕС‚РґРµР»СЊРЅС‹РјРё Р·Р°РїРёСЃСЏРјРё Р°СѓРґРёС‚Р°. РњРѕР»С‡Р°Р»РёРІР°СЏ
    /// РїРµСЂРµР·Р°РїРёСЃСЊ РѕСЃС‚Р°РІРёР»Р° Р±С‹ Р±РѕСЂС‚ СЃ РёРЅС‹РјРё Р·РЅР°С‡РµРЅРёСЏРјРё, С‡РµРј С‚Рµ, РїРѕ РєРѕС‚РѕСЂС‹Рј
    /// Р·Р°РєСЂС‹С‚ РїСЂРѕРіРѕРЅ, вЂ” Рё СЂР°СЃС…РѕР¶РґРµРЅРёРµ РІСЃРїР»С‹Р»Рѕ Р±С‹ С‚РѕР»СЊРєРѕ РїСЂРё СЂР°Р·Р±РѕСЂРµ СЂРµРєР»Р°РјР°С†РёРё.
    /// </para>
    /// <para>
    /// РџРѕСЂС‚ РѕС‚РєСЂС‹РІР°РµС‚СЃСЏ Р·Р°РЅРѕРІРѕ: Рє РјРѕРјРµРЅС‚Сѓ РЅР°Р¶Р°С‚РёСЏ РєРЅРѕРїРєРё СЃРѕРµРґРёРЅРµРЅРёРµ РїСЂРѕРіРѕРЅР° СѓР¶Рµ
    /// Р·Р°РєСЂС‹С‚Рѕ, Р° РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ РјРѕРіР»Рѕ Р±С‹С‚СЊ РїРѕРґРЅСЏС‚Рѕ РЅР° С‚РѕРј Р¶Рµ РїРѕСЂС‚Сѓ вЂ” РµРіРѕ
    /// РїСЂРёС…РѕРґРёС‚СЃСЏ РїРѕРіР°СЃРёС‚СЊ, РїРѕС‚РѕРјСѓ С‡С‚Рѕ COM РѕС‚РєСЂС‹РІР°РµС‚СЃСЏ РјРѕРЅРѕРїРѕР»СЊРЅРѕ.
    /// </para>
    /// </remarks>
    /// <param name="runId">РџСЂРѕРіРѕРЅ, РІ РєРѕС‚РѕСЂС‹Р№ Р»СЏРіСѓС‚ Р·Р°РїРёСЃРё Р°СѓРґРёС‚Р°. <c>0</c> вЂ” РїСЂРѕРіРѕРЅ РЅРµ РѕС‚РєСЂС‹РІР°Р»СЃСЏ, Р°СѓРґРёС‚ РїСЂРѕРїСѓСЃРєР°РµС‚СЃСЏ.</param>
    /// <param name="portName">РџРѕСЂС‚ Р±РѕСЂС‚Р°.</param>
    /// <param name="mismatches">Р§С‚Рѕ РїРµСЂРµР·Р°РїРёСЃР°С‚СЊ.</param>
    /// <param name="progress">РљСѓРґР° СЃРѕРѕР±С‰Р°С‚СЊ С…РѕРґ; РјРѕР¶РµС‚ Р±С‹С‚СЊ <c>null</c>.</param>
    /// <param name="ct">РўРѕРєРµРЅ РѕС‚РјРµРЅС‹.</param>
    /// <returns>РСЃС…РѕРґ РїРѕ РєР°Р¶РґРѕРјСѓ РёРјРµРЅРё РІ РїРѕСЂСЏРґРєРµ РІС…РѕРґРЅРѕРіРѕ СЃРїРёСЃРєР°.</returns>
    public async Task<IReadOnlyList<ParamWriteRecord>> RewriteFromReferenceAsync(
        long runId,
        string portName,
        IReadOnlyList<ParamMismatch> mismatches,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(mismatches);

        if (mismatches.Count == 0)
        {
            return Array.Empty<ParamWriteRecord>();
        }

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var records = new List<ParamWriteRecord>(mismatches.Count);

                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException(
                                $"РџРѕСЂС‚ {portName} РѕС‚РєСЂС‹С‚, РЅРѕ HEARTBEAT РЅРµ РїРѕР»СѓС‡РµРЅ: РїРµСЂРµР·Р°РїРёСЃС‹РІР°С‚СЊ РЅРµС‡РµРіРѕ Рё РЅРµРєСѓРґР°.");
                        }

                        foreach (var mismatch in mismatches)
                        {
                            ct.ThrowIfCancellationRequested();
                            progress?.Report($"Р—Р°РїРёСЃСЊ {mismatch.Name}вЂ¦");

                            var record = await RewriteOneAsync(link, mismatch, ct).ConfigureAwait(false);
                            records.Add(record);

                            if (runId > 0)
                            {
                                try
                                {
                                    await Store.RecordWriteAsync(runId, record, ct).ConfigureAwait(false);
                                }
                                catch (CalibrationStoreException ex)
                                {
                                    // РћС‚РєР°Р· СЂРµРµСЃС‚СЂР° РЅРµ РѕС‚РјРµРЅСЏРµС‚ СѓР¶Рµ РІС‹РїРѕР»РЅРµРЅРЅСѓСЋ
                                    // Р·Р°РїРёСЃСЊ РІ Р±РѕСЂС‚ Рё РЅРµ РґРѕР»Р¶РµРЅ РµС‘ СЃРєСЂС‹С‚СЊ.
                                    progress?.Report($"РђСѓРґРёС‚ {mismatch.Name} РЅРµ Р·Р°РїРёСЃР°РЅ: {ex.Message}");
                                }
                            }
                        }
                    }

                    return (IReadOnlyList<ParamWriteRecord>)records;
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// РћРґРЅР° РїРѕРІС‚РѕСЂРЅР°СЏ Р·Р°РїРёСЃСЊ: РїРёС€РµРј Рё С‚СѓС‚ Р¶Рµ РїСЂРѕРІРµСЂСЏРµРј РЅРµР·Р°РІРёСЃРёРјС‹Рј С‡С‚РµРЅРёРµРј.
    /// </summary>
    /// <remarks>
    /// Р­С…Рѕ <c>PARAM_SET</c> РїРѕРґС‚РІРµСЂР¶РґРµРЅРёРµРј РЅРµ СЃС‡РёС‚Р°РµС‚СЃСЏ вЂ” РѕРЅРѕ С€РёСЂРѕРєРѕРІРµС‰Р°С‚РµР»СЊРЅРѕРµ
    /// Рё РїСЂРёС…РѕРґРёС‚ СЃ РЅРµРґРѕСЃС‚РѕРІРµСЂРЅС‹Рј РёРЅРґРµРєСЃРѕРј. РџСЂРѕРІР°Р»РёРІС€Р°СЏСЃСЏ РїРѕРІС‚РѕСЂРЅР°СЏ Р·Р°РїРёСЃСЊ
    /// РІРѕР·РІСЂР°С‰Р°РµС‚СЃСЏ РёСЃС…РѕРґРѕРј <see cref="WriteOutcome.Mismatch"/>, Р° РЅРµ
    /// РёСЃРєР»СЋС‡РµРЅРёРµРј: РѕРїРµСЂР°С‚РѕСЂСѓ РЅСѓР¶РµРЅ СЂРµР·СѓР»СЊС‚Р°С‚ РїРѕ РІСЃРµРј РёРјРµРЅР°Рј, Р° РЅРµ РѕСЃС‚Р°РЅРѕРІ РЅР°
    /// РїРµСЂРІРѕРј СѓРїСЂСЏРјРѕРј.
    /// </remarks>
    private static async Task<ParamWriteRecord> RewriteOneAsync(
        SerialVehicleLink link,
        ParamMismatch mismatch,
        CancellationToken ct)
    {
        double? before = null;
        try
        {
            var current = await link.ReadParamAsync(mismatch.Name, ct).ConfigureAwait(false);
            before = current.Value;

            await link.WriteParamAsync(mismatch.Name, (float)mismatch.Expected, current.Type, ct)
                .ConfigureAwait(false);

            var readBack = await link.ReadParamAsync(mismatch.Name, ct).ConfigureAwait(false);

            var accepted = ReferenceParamFile.ValuesEqual(mismatch.Expected, readBack);

            return new ParamWriteRecord(
                mismatch.Name,
                before,
                mismatch.Expected,
                readBack.Value,
                accepted ? WriteOutcome.Verified : WriteOutcome.Mismatch,
                DateTimeOffset.UtcNow);
        }
        catch (VehicleLinkException)
        {
            return new ParamWriteRecord(
                mismatch.Name,
                before,
                mismatch.Expected,
                null,
                WriteOutcome.Failed,
                DateTimeOffset.UtcNow);
        }
    }

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<IReadOnlyList<RunSummary>> LoadHistoryAsync() => Task.Run(() => Store.ListRunsAsync());

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<IReadOnlyList<CalibrationReference>> LoadReferencesAsync() =>
        Task.Run(() => Store.ListReferencesAsync());

    /// <inheritdoc cref="InitializeAsync"/>
    public Task<WorkstationSettings> LoadSettingsAsync() =>
        Task.Run(() => Store.GetWorkstationSettingsAsync());

    /// <inheritdoc cref="InitializeAsync"/>
    public Task SaveSettingsAsync(WorkstationSettings settings) =>
        Task.Run(() => Store.SaveWorkstationSettingsAsync(settings));

    // --- РЎРІСЏР·СЊ СЃ РїРѕР»С‘С‚РЅС‹Рј РєРѕРЅС‚СЂРѕР»Р»РµСЂРѕРј ------------------------------------

    private SerialVehicleLink? _link;

    /// <summary>РџРѕСЂС‚, РЅР° РєРѕС‚РѕСЂРѕРј СЃРµР№С‡Р°СЃ СѓСЃС‚Р°РЅРѕРІР»РµРЅР° СЃРІСЏР·СЊ, РёР»Рё <c>null</c>.</summary>
    public string? ConnectedPort { get; private set; }

    /// <summary>РЎС‚СЂРѕРєР° РІРµСЂСЃРёРё РїСЂРѕС€РёРІРєРё РїРѕРґРєР»СЋС‡С‘РЅРЅРѕРіРѕ Р±РѕСЂС‚Р°, РµСЃР»Рё Р±РѕСЂС‚ РµС‘ РїСЂРёСЃР»Р°Р».</summary>
    public string? ConnectedFirmware { get; private set; }

    public bool IsLinkConnected => _link is { IsConnected: true };

    /// <summary>
    /// РўРµРєСѓС‰РµРµ СЃРѕСЃС‚РѕСЏРЅРёРµ Р±РѕСЂС‚Р° РґР»СЏ РёРЅРґРёРєР°С‚РѕСЂР°. Р§С‚РµРЅРёРµ РґРµС€С‘РІРѕРµ Рё Р±РµР·РѕРїР°СЃРЅРѕРµ РёР·
    /// Р»СЋР±РѕРіРѕ РїРѕС‚РѕРєР°: РєР°РЅР°Р» СЃРєР»Р°РґС‹РІР°РµС‚ РїРѕСЃР»РµРґРЅРёРµ СЃРѕРѕР±С‰РµРЅРёСЏ РїРѕРґ Р·Р°РјРєРѕРј.
    /// </summary>
    public VehicleLiveState? LiveState => _link?.LiveState;

    /// <summary>РЎРѕСЃС‚РѕСЏРЅРёРµ СЃРІСЏР·Рё РёР·РјРµРЅРёР»РѕСЃСЊ. РњРѕР¶РµС‚ РїСЂРёР№С‚Рё РёР· Р»СЋР±РѕРіРѕ РїРѕС‚РѕРєР°.</summary>
    public event EventHandler? LinkChanged;

    /// <summary>
    /// РџРѕРґРєР»СЋС‡Р°РµС‚СЃСЏ Рє Р±РѕСЂС‚Сѓ Рё СѓРґРµСЂР¶РёРІР°РµС‚ СЃРѕРµРґРёРЅРµРЅРёРµ РґРѕ СЏРІРЅРѕРіРѕ РѕС‚РєР»СЋС‡РµРЅРёСЏ.
    /// </summary>
    /// <remarks>
    /// РџРѕСЂС‚ РѕС‚РєСЂС‹РІР°РµС‚СЃСЏ РјРѕРЅРѕРїРѕР»СЊРЅРѕ, РїРѕСЌС‚РѕРјСѓ РїРµСЂРµРґ Р·Р°РїСѓСЃРєРѕРј РїСЂРѕС†РµРґСѓСЂС‹ СЌС‚Рѕ
    /// СЃРѕРµРґРёРЅРµРЅРёРµ Р·Р°РєСЂС‹РІР°РµС‚СЃСЏ: РёРЅР°С‡Рµ РїСЂРѕС†РµРґСѓСЂР° РЅРµ СЃРјРѕР¶РµС‚ РѕС‚РєСЂС‹С‚СЊ С‚РѕС‚ Р¶Рµ РїРѕСЂС‚.
    /// </remarks>
    public Task ConnectAsync(string portName, CancellationToken ct = default) => Task.Run(
        async () =>
        {
            await DisconnectAsync().ConfigureAwait(false);

            var link = new SerialVehicleLink();
            try
            {
                await link.ConnectAsync(portName, TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
            }
            catch
            {
                await link.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _link = link;
            ConnectedPort = portName;
            ConnectedFirmware = link.FirmwareBanner;
            LinkChanged?.Invoke(this, EventArgs.Empty);
        },
        ct);

    /// <summary>
    /// РЎРЅРёРјР°РµС‚ СЌС‚Р°Р»РѕРЅ РєРѕРјРїР°СЃРѕРІ СЃ РїРѕРґРєР»СЋС‡С‘РЅРЅРѕРіРѕ РѕР±СЂР°Р·С†РѕРІРѕРіРѕ РёР·РґРµР»РёСЏ.
    /// </summary>
    /// <remarks>
    /// Р§РёС‚Р°РµС‚ РїРѕ СѓР¶Рµ РѕС‚РєСЂС‹С‚РѕРјСѓ РєР°РЅР°Р»Сѓ вЂ” СЂР°РґРё СЌС‚РѕРіРѕ Р¶РёРІРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ Рё РґРµСЂР¶РёС‚СЃСЏ.
    /// РћС‚РґРµР»СЊРЅРѕРіРѕ РїРѕРґРєР»СЋС‡РµРЅРёСЏ РЅРµ РґРµР»Р°РµС‚: РІС‚РѕСЂРѕР№ РѕС‚РєСЂС‹РІР°С‚РµР»СЊ С‚РѕРіРѕ Р¶Рµ COM-РїРѕСЂС‚Р°
    /// РїРѕР»СѓС‡РёР» Р±С‹ РѕС‚РєР°Р·, Р° Р·Р°РєСЂС‹РІР°С‚СЊ РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅС‹Р№ РєР°РЅР°Р» СЂР°РґРё СЃРЅРёРјРєР° Р·РЅР°С‡РёР»Рѕ Р±С‹
    /// РіР°СЃРёС‚СЊ РёРЅРґРёРєР°С‚РѕСЂ РІ РјРѕРјРµРЅС‚, РєРѕРіРґР° РѕРїРµСЂР°С‚РѕСЂ СЃРјРѕС‚СЂРёС‚ РЅР° Р±РѕСЂС‚.
    /// </remarks>
    /// <exception cref="VehicleLinkException">РЎРІСЏР·Рё РЅРµС‚ Р»РёР±Рѕ Р±РѕСЂС‚ РЅРµ РѕС‚РґР°Р» СЃРѕСЃС‚Р°РІ РєРѕРјРїР°СЃРѕРІ.</exception>
    public Task<CompassSnapshot> ReadCompassSnapshotAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(
        () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException(
                    "РќРµС‚ СЃРІСЏР·Рё СЃ Р±РѕСЂС‚РѕРј: СЃРЅРёРјР°С‚СЊ СЌС‚Р°Р»РѕРЅ РЅРµ СЃ С‡РµРіРѕ. РџРѕРґРєР»СЋС‡РёС‚Рµ РѕР±СЂР°Р·С†РѕРІРѕРµ РёР·РґРµР»РёРµ.");
            }

            return CompassParameterSnapshot.ReadAsync(link, progress, ct);
        },
        ct);

    /// <summary>
    /// РЎРЅРёРјР°РµС‚ СЃ РѕР±СЂР°Р·С†Р° РІСЃСЋ С‚Р°Р±Р»РёС†Сѓ РїР°СЂР°РјРµС‚СЂРѕРІ вЂ” С‚Рѕ, РёР· С‡РµРіРѕ СЃРѕР±РёСЂР°РµС‚СЃСЏ СЌС‚Р°Р»РѕРЅ.
    /// </summary>
    /// <remarks>
    /// Р§РёС‚Р°РµС‚ РїРѕ СѓР¶Рµ РѕС‚РєСЂС‹С‚РѕРјСѓ РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРјСѓ РєР°РЅР°Р»Сѓ: РІС‚РѕСЂРѕР№ РѕС‚РєСЂС‹РІР°С‚РµР»СЊ С‚РѕРіРѕ
    /// Р¶Рµ COM-РїРѕСЂС‚Р° РїРѕР»СѓС‡РёР» Р±С‹ РѕС‚РєР°Р·, Р° РіР°СЃРёС‚СЊ РєР°РЅР°Р» СЂР°РґРё СЃРЅРёРјРєР° Р·РЅР°С‡РёР»Рѕ Р±С‹
    /// РІС‹РєР»СЋС‡РёС‚СЊ РёРЅРґРёРєР°С‚РѕСЂ РІ РјРѕРјРµРЅС‚, РєРѕРіРґР° РѕРїРµСЂР°С‚РѕСЂ СЃРјРѕС‚СЂРёС‚ РЅР° Р±РѕСЂС‚.
    /// </remarks>
    /// <exception cref="VehicleLinkException">РЎРІСЏР·Рё РЅРµС‚ Р»РёР±Рѕ Р±РѕСЂС‚ РЅРµ РѕС‚РґР°Р» РЅРё РѕРґРЅРѕРіРѕ РїР°СЂР°РјРµС‚СЂР°.</exception>
    public Task<FullParameterSet> ReadAllParametersAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(
        () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException(
                    "РќРµС‚ СЃРІСЏР·Рё СЃ Р±РѕСЂС‚РѕРј: СЃРЅРёРјР°С‚СЊ СЌС‚Р°Р»РѕРЅ РЅРµ СЃ С‡РµРіРѕ. РџРѕРґРєР»СЋС‡РёС‚Рµ РѕР±СЂР°Р·С†РѕРІРѕРµ РёР·РґРµР»РёРµ.");
            }

            return link.ReadAllParamsAsync(progress, null, ct);
        },
        ct);

    // --- РџСЂРёС‘РјРєР° -----------------------------------------------------------

    private AcceptanceSession? _session;

    /// <summary>РРґС‘С‚ РїСЂРёС‘РјРєР°: РєР°РЅР°Р» Р·Р°РЅСЏС‚ СЃРµСЃСЃРёРµР№, РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ Р·Р°РєСЂС‹С‚Рѕ.</summary>
    public bool IsAcceptanceRunning => _session is not null;

    /// <summary>
    /// РќР°С‡РёРЅР°РµС‚ РїСЂРёС‘РјРєСѓ РЅР° СѓРєР°Р·Р°РЅРЅРѕРј РїРѕСЂС‚Сѓ.
    /// </summary>
    /// <remarks>
    /// рџ”ґ РќР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ РіР°СЃРёС‚СЃСЏ: COM-РїРѕСЂС‚ РѕС‚РєСЂС‹РІР°РµС‚СЃСЏ РјРѕРЅРѕРїРѕР»СЊРЅРѕ, Рё
    /// СЃРµСЃСЃРёСЏ РґРµСЂР¶РёС‚ РµРіРѕ РґРѕ РєРѕРЅС†Р° РїСЂРёС‘РјРєРё. Р”РµСЂР¶Р°С‚СЊ РєР°РЅР°Р» РѕС‚РєСЂС‹С‚С‹Рј РІСЃСЋ РїСЂРёС‘РјРєСѓ
    /// РѕР±СЏР·Р°С‚РµР»СЊРЅРѕ вЂ” РѕС‚РєСЂС‹РІР°С‚СЊ РїРѕСЂС‚ Р·Р°РЅРѕРІРѕ РЅР° РєР°Р¶РґС‹Р№ С€Р°Рі Р·РЅР°С‡РёС‚ С‚РµСЂСЏС‚СЊ РїРѕ
    /// РґРІР° РґРµСЃСЏС‚РєР° СЃРµРєСѓРЅРґ РЅР° РєР°Р¶РґРѕРј Рё Р»РѕРІРёС‚СЊ С‡СѓР¶РѕР№ Р·Р°С…РІР°С‚ РїРѕСЂС‚Р° РІ РїСЂРѕРјРµР¶СѓС‚РєР°С….
    /// </remarks>
    public async Task<AcceptanceSession> BeginAcceptanceAsync(string portName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        await EndAcceptanceAsync().ConfigureAwait(false);
        await DisconnectAsync().ConfigureAwait(false);

        _procedureRunning = true;

        var session = new AcceptanceSession();
        try
        {
            await Task.Run(() => session.ConnectAsync(portName, ct), ct).ConfigureAwait(false);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            _procedureRunning = false;
            throw;
        }

        _session = session;
        LinkChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }

    /// <summary>Р—Р°РєСЂС‹РІР°РµС‚ РїСЂРёС‘РјРєСѓ Рё РѕСЃРІРѕР±РѕР¶РґР°РµС‚ РїРѕСЂС‚.</summary>
    public async Task EndAcceptanceAsync()
    {
        var session = _session;
        _session = null;

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            _procedureRunning = false;
            LinkChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Р§РёС‚Р°РµС‚ РѕРґРёРЅ РїР°СЂР°РјРµС‚СЂ РїРѕ СѓР¶Рµ РѕС‚РєСЂС‹С‚РѕРјСѓ РЅР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРјСѓ РєР°РЅР°Р»Сѓ.</summary>
    /// <exception cref="VehicleLinkException">РЎРІСЏР·Рё РЅРµС‚ Р»РёР±Рѕ Р±РѕСЂС‚ РёРјРµРЅРё РЅРµ Р·РЅР°РµС‚.</exception>
    public Task<double> ReadParameterAsync(string name, CancellationToken ct = default) => Task.Run(
        async () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException("РќРµС‚ СЃРІСЏР·Рё СЃ Р±РѕСЂС‚РѕРј.");
            }

            var value = await link.ReadParamAsync(name, ct).ConfigureAwait(false);
            return (double)value.Value;
        },
        ct);

    /// <summary>
    /// РЎРЅРёРјР°РµС‚ СЃРєСЂРёРїС‚С‹ СЃ РїРѕРґРєР»СЋС‡С‘РЅРЅРѕРіРѕ РѕР±СЂР°Р·С†РѕРІРѕРіРѕ РёР·РґРµР»РёСЏ.
    /// </summary>
    /// <remarks>
    /// Р§РёС‚Р°РµС‚ РїРѕ СѓР¶Рµ РѕС‚РєСЂС‹С‚РѕРјСѓ РєР°РЅР°Р»Сѓ вЂ” РєР°Рє Рё СЃРЅРёРјРѕРє РєРѕРјРїР°СЃРѕРІ, Рё РїРѕ С‚РѕР№ Р¶Рµ
    /// РїСЂРёС‡РёРЅРµ: РІС‚РѕСЂРѕР№ РѕС‚РєСЂС‹РІР°С‚РµР»СЊ С‚РѕРіРѕ Р¶Рµ COM-РїРѕСЂС‚Р° РїРѕР»СѓС‡РёР» Р±С‹ РѕС‚РєР°Р·.
    /// </remarks>
    /// <param name="unreadable">РЎСЋРґР° РїРѕРїР°РґР°СЋС‚ С„Р°Р№Р»С‹, РєРѕС‚РѕСЂС‹Рµ РїСЂРѕС‡РёС‚Р°С‚СЊ РЅРµ СѓРґР°Р»РѕСЃСЊ.</param>
    /// <exception cref="VehicleLinkException">РЎРІСЏР·Рё РЅРµС‚.</exception>
    public Task<IReadOnlyList<ReferenceScript>> ReadScriptsAsync(
        List<string> unreadable,
        IProgress<string>? progress = null,
        CancellationToken ct = default) => Task.Run(
        () =>
        {
            var link = _link;
            if (link is not { IsConnected: true })
            {
                throw new VehicleLinkException(
                    "РќРµС‚ СЃРІСЏР·Рё СЃ Р±РѕСЂС‚РѕРј: СЃРЅРёРјР°С‚СЊ СЃРєСЂРёРїС‚С‹ РЅРµ СЃ С‡РµРіРѕ. РџРѕРґРєР»СЋС‡РёС‚Рµ РѕР±СЂР°Р·С†РѕРІРѕРµ РёР·РґРµР»РёРµ.");
            }

            return ScriptTransfer.ReadAllAsync(link, progress, unreadable, ct);
        },
        ct);

    /// <summary>
    /// РЎРІРµСЂСЏРµС‚ РІСЃСЋ РєРѕРЅС„РёРіСѓСЂР°С†РёСЋ С†РµР»РµРІРѕРіРѕ Р±РѕСЂС‚Р° СЃ СЌС‚Р°Р»РѕРЅРѕРј.
    /// </summary>
    /// <remarks>
    /// рџ”ґ РЎРІРµСЂСЏРµС‚СЃСЏ РІСЃС‘, С‡С‚Рѕ СЌС‚Р°Р»РѕРЅ РѕР±СЉСЏРІРёР» РєРѕРЅС‚СЂРѕР»РёСЂСѓРµРјС‹Рј, Р° РЅРµ РєРѕРјРїР°СЃРЅС‹Р№
    /// Р±Р»РѕРє. РР·РґРµР»РёРµ РѕС‚Р»РёС‡Р°РµС‚СЃСЏ РѕС‚ РёР·РґРµР»РёСЏ РєР°РЅР°Р»Р°РјРё РїСЂРёС‘РјРЅРёРєР°, РІС‹С…РѕРґР°РјРё,
    /// РїРѕР»С‘С‚РЅС‹РјРё СЂРµР¶РёРјР°РјРё Рё СЂРµРіСѓР»СЏС‚РѕСЂР°РјРё; СЃРІРµСЂРєР° РѕРґРЅРѕРіРѕ РєРѕРјРїР°СЃР° РїСЂРѕРїСѓСЃРєР°Р»Р° Р±С‹
    /// С‡СѓР¶СѓСЋ СЃР±РѕСЂРєСѓ РєР°Рє РіРѕРґРЅСѓСЋ.
    /// </remarks>
    /// <returns>РџР»Р°РЅ: С‡С‚Рѕ СЃРѕС€Р»РѕСЃСЊ, С‡С‚Рѕ СЂР°СЃС…РѕРґРёС‚СЃСЏ Рё С‡С‚Рѕ РёР· СЌС‚РѕРіРѕ РјРѕР¶РЅРѕ Р·Р°РїРёСЃР°С‚СЊ.</returns>
    public async Task<ParameterTransferPlan> CompareParametersAsync(
        string portName,
        CalibrationReference reference,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(reference);

        var expected = reference.ParseParameters().Values;
        var roles = reference.ToRoleMap();

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException($"РџРѕСЂС‚ {portName} РѕС‚РєСЂС‹С‚, РЅРѕ HEARTBEAT РЅРµ РїРѕР»СѓС‡РµРЅ.");
                        }

                        var board = await link.ReadAllParamsAsync(progress, null, ct).ConfigureAwait(false);
                        return ParameterTransfer.Plan(expected, board.Values, roles);
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// РџСЂРёРІРѕРґРёС‚ СЂР°СЃС…РѕРґСЏС‰РёРµСЃСЏ РїР°СЂР°РјРµС‚СЂС‹ Р±РѕСЂС‚Р° Рє СЌС‚Р°Р»РѕРЅСѓ.
    /// </summary>
    /// <remarks>
    /// рџ”ґ РљР°Р¶РґР°СЏ Р·Р°РїРёСЃСЊ РїРѕРґС‚РІРµСЂР¶РґР°РµС‚СЃСЏ РЅРµР·Р°РІРёСЃРёРјС‹Рј РѕР±СЂР°С‚РЅС‹Рј С‡С‚РµРЅРёРµРј, Р° РёСЃС…РѕРґ РїРѕ
    /// РєР°Р¶РґРѕРјСѓ РёРјРµРЅРё Р»РѕР¶РёС‚СЃСЏ РІ СЂРµРµСЃС‚СЂ СЃС‚СЂРѕРєРѕР№ Р°СѓРґРёС‚Р°. Р’РµСЂРґРёРєС‚ РїСЂРѕРіРѕРЅР° СЌС‚Рѕ
    /// РґРµР№СЃС‚РІРёРµ РЅРµ РјРµРЅСЏРµС‚: РёР·РґРµР»РёРµ СЃРґР°С‘С‚СЃСЏ РїРѕРІС‚РѕСЂРЅС‹Рј РїСЂРѕРіРѕРЅРѕРј С†РµР»РёРєРѕРј.
    /// </remarks>
    /// <param name="runId">РџСЂРѕРіРѕРЅ РґР»СЏ Р°СѓРґРёС‚Р°. <c>0</c> вЂ” РїРёСЃР°С‚СЊ Р°СѓРґРёС‚ РЅРµРєСѓРґР°.</param>
    public async Task<IReadOnlyList<ParamWriteRecord>> ApplyParametersAsync(
        long runId,
        string portName,
        IReadOnlyList<ParameterDifference> differences,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(differences);

        if (differences.Count == 0)
        {
            return Array.Empty<ParamWriteRecord>();
        }

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException($"РџРѕСЂС‚ {portName} РѕС‚РєСЂС‹С‚, РЅРѕ HEARTBEAT РЅРµ РїРѕР»СѓС‡РµРЅ.");
                        }

                        var records = await ParameterTransfer
                            .ApplyAsync(link, differences, progress, ct)
                            .ConfigureAwait(false);

                        if (runId > 0)
                        {
                            foreach (var record in records)
                            {
                                try
                                {
                                    await Store.RecordWriteAsync(runId, record, ct).ConfigureAwait(false);
                                }
                                catch (CalibrationStoreException ex)
                                {
                                    progress?.Report($"РђСѓРґРёС‚ {record.Name} РЅРµ Р·Р°РїРёСЃР°РЅ: {ex.Message}");
                                }
                            }
                        }

                        return records;
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// РЎРІРµСЂСЏРµС‚ СЃРєСЂРёРїС‚С‹ С†РµР»РµРІРѕРіРѕ Р±РѕСЂС‚Р° СЃ СЌС‚Р°Р»РѕРЅРѕРј.
    /// </summary>
    /// <remarks>
    /// РћС‚РєСЂС‹РІР°РµС‚ СЃРѕР±СЃС‚РІРµРЅРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ: СЃРІРµСЂРєР° РІС‹РїРѕР»РЅСЏРµС‚СЃСЏ РїРѕСЃР»Рµ РїСЂРѕРіРѕРЅР°,
    /// РєРѕРіРґР° РєР°РЅР°Р» РїСЂРѕС†РµРґСѓСЂС‹ СѓР¶Рµ Р·Р°РєСЂС‹С‚. РќР°Р±Р»СЋРґР°С‚РµР»СЊРЅРѕРµ СЃРѕРµРґРёРЅРµРЅРёРµ РїСЂРё СЌС‚РѕРј
    /// РіР°СЃРёС‚СЃСЏ вЂ” COM-РїРѕСЂС‚ РѕС‚РєСЂС‹РІР°РµС‚СЃСЏ РјРѕРЅРѕРїРѕР»СЊРЅРѕ.
    /// </remarks>
    /// <returns>Р Р°СЃС…РѕР¶РґРµРЅРёСЏ; РїСѓСЃС‚РѕР№ СЃРїРёСЃРѕРє вЂ” СЃРєСЂРёРїС‚С‹ Р±РѕСЂС‚Р° СЃРѕРІРїР°Р»Рё СЃ СЌС‚Р°Р»РѕРЅРѕРј.</returns>
    public async Task<IReadOnlyList<ScriptDifference>> VerifyScriptsAsync(
        string portName,
        IReadOnlyList<ReferenceScript> expected,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(expected);

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException($"РџРѕСЂС‚ {portName} РѕС‚РєСЂС‹С‚, РЅРѕ HEARTBEAT РЅРµ РїРѕР»СѓС‡РµРЅ.");
                        }

                        var unreadable = new List<string>();
                        var actual = await ScriptTransfer
                            .ReadAllAsync(link, progress, unreadable, ct)
                            .ConfigureAwait(false);

                        return ScriptTransfer.Compare(expected, actual, unreadable);
                    }
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// РџСЂРёРІРѕРґРёС‚ СЃРєСЂРёРїС‚С‹ С†РµР»РµРІРѕРіРѕ Р±РѕСЂС‚Р° Рє СЌС‚Р°Р»РѕРЅСѓ: РґРѕРїРёСЃС‹РІР°РµС‚ РЅРµРґРѕСЃС‚Р°СЋС‰РёРµ,
    /// РїРµСЂРµРїРёСЃС‹РІР°РµС‚ СЂР°Р·РѕС€РµРґС€РёРµСЃСЏ, СѓРґР°Р»СЏРµС‚ Р»РёС€РЅРёРµ.
    /// </summary>
    /// <remarks>
    /// <para>
    /// рџ”ґ Р’РµСЂРґРёРєС‚ РїСЂРѕРіРѕРЅР° СЌС‚Рѕ РґРµР№СЃС‚РІРёРµ РЅРµ РјРµРЅСЏРµС‚ вЂ” СЂРѕРІРЅРѕ РєР°Рє РїРѕРІС‚РѕСЂРЅР°СЏ Р·Р°РїРёСЃСЊ
    /// РїР°СЂР°РјРµС‚СЂРѕРІ. РР·РґРµР»РёРµ СЃРґР°С‘С‚СЃСЏ РїРѕРІС‚РѕСЂРЅС‹Рј РїСЂРѕРіРѕРЅРѕРј С†РµР»РёРєРѕРј.
    /// </para>
    /// <para>
    /// рџ”ґ Р—Р°РїРёСЃР°РЅРЅС‹Р№ СЃРєСЂРёРїС‚ РЅР°С‡РЅС‘С‚ РёСЃРїРѕР»РЅСЏС‚СЊСЃСЏ С‚РѕР»СЊРєРѕ РїРѕСЃР»Рµ РїРµСЂРµР·Р°РіСЂСѓР·РєРё Р±РѕСЂС‚Р°:
    /// Lua РїРѕРґС…РІР°С‚С‹РІР°РµС‚СЃСЏ РїСЂРё СЃС‚Р°СЂС‚Рµ. РЎРєР°Р·Р°С‚СЊ РѕР± СЌС‚РѕРј РѕР±СЏР·Р°РЅ РІС‹Р·С‹РІР°СЋС‰РёР№ вЂ”
    /// РјРѕР»С‡Р°РЅРёРµ Р·РґРµСЃСЊ РѕР·РЅР°С‡Р°Р»Рѕ Р±С‹ В«РїСЂРёРјРµРЅРµРЅРѕВ», С‡РµРіРѕ РЅРµ РїСЂРѕРёР·РѕС€Р»Рѕ.
    /// </para>
    /// </remarks>
    /// <returns>РџСѓС‚Рё, РїРѕ РєРѕС‚РѕСЂС‹Рј РґРµР№СЃС‚РІРёРµ РІС‹РїРѕР»РЅРµРЅРѕ, Рё С‚РµРєСЃС‚ РѕС‚РєР°Р·Р° РїРѕ РѕСЃС‚Р°Р»СЊРЅС‹Рј.</returns>
    public async Task<IReadOnlyList<(string Path, string? Failure)>> ApplyScriptsAsync(
        string portName,
        IReadOnlyList<ScriptDifference> differences,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentNullException.ThrowIfNull(differences);

        if (differences.Count == 0)
        {
            return Array.Empty<(string, string?)>();
        }

        _procedureRunning = true;
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    var outcomes = new List<(string Path, string? Failure)>(differences.Count);

                    var link = new SerialVehicleLink();
                    await using (link.ConfigureAwait(false))
                    {
                        await link.ConnectAsync(portName, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                        if (!link.IsConnected)
                        {
                            throw new VehicleLinkException(
                                $"РџРѕСЂС‚ {portName} РѕС‚РєСЂС‹С‚, РЅРѕ HEARTBEAT РЅРµ РїРѕР»СѓС‡РµРЅ.");
                        }

                        foreach (var difference in differences)
                        {
                            ct.ThrowIfCancellationRequested();
                            outcomes.Add(await ApplyOneScriptAsync(link, difference, progress, ct)
                                .ConfigureAwait(false));
                        }
                    }

                    return (IReadOnlyList<(string, string?)>)outcomes;
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _procedureRunning = false;
        }
    }

    /// <summary>
    /// РћРґРЅРѕ РґРµР№СЃС‚РІРёРµ РЅР°Рґ СЃРєСЂРёРїС‚РѕРј СЃ РїСЂРѕРІРµСЂРєРѕР№ РѕР±СЂР°С‚РЅС‹Рј С‡С‚РµРЅРёРµРј.
    /// </summary>
    /// <remarks>
    /// РћР±СЂР°С‚РЅРѕРµ С‡С‚РµРЅРёРµ РѕР±СЏР·Р°С‚РµР»СЊРЅРѕ Рё Р·РґРµСЃСЊ: РїРѕРґС‚РІРµСЂР¶РґРµРЅРёСЏ Р·Р°РїРёСЃРё MAVFTP
    /// РґРѕСЃС‚Р°С‚РѕС‡РЅРѕ, С‡С‚РѕР±С‹ Р·РЅР°С‚СЊ, С‡С‚Рѕ Р±РѕСЂС‚ РїСЂРёРЅСЏР» Р±Р°Р№С‚С‹, РЅРѕ РЅРµ С‡С‚РѕР±С‹ Р·РЅР°С‚СЊ, С‡С‚Рѕ
    /// РЅР° РєР°СЂС‚Рµ Р»РµР¶РёС‚ РёРјРµРЅРЅРѕ С‚РѕС‚ С„Р°Р№Р». РЎРІРµСЂРєР° РїРѕ SHA-256 РѕС‚РІРµС‡Р°РµС‚ РЅР° РІС‚РѕСЂРѕР№
    /// РІРѕРїСЂРѕСЃ.
    /// </remarks>
    private static async Task<(string Path, string? Failure)> ApplyOneScriptAsync(
        SerialVehicleLink link,
        ScriptDifference difference,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        try
        {
            if (difference.Outcome == ScriptComparison.ExtraOnBoard)
            {
                progress?.Report($"РЈРґР°Р»РµРЅРёРµ {difference.Path}вЂ¦");
                await link.RemoveFileAsync(difference.Path, ct).ConfigureAwait(false);
                return (difference.Path, null);
            }

            if (difference.Expected is not { } script)
            {
                return (difference.Path, "РЅРµС‡РµРіРѕ Р·Р°РїРёСЃС‹РІР°С‚СЊ: РІ СЌС‚Р°Р»РѕРЅРµ СЌС‚РѕРіРѕ СЃРєСЂРёРїС‚Р° РЅРµС‚");
            }

            progress?.Report($"Р—Р°РїРёСЃСЊ {difference.Path}вЂ¦");
            await link.WriteFileAsync(script.Path, script.ToBytes(), null, ct).ConfigureAwait(false);

            progress?.Report($"РЎРІРµСЂРєР° {difference.Path}вЂ¦");
            var readBack = await link.ReadFileAsync(script.Path, null, ct).ConfigureAwait(false);
            var actual = ReferenceScript.ComputeHash(readBack);

            return string.Equals(actual, script.Hash, StringComparison.Ordinal)
                ? (difference.Path, null)
                : (difference.Path,
                    $"РѕР±СЂР°С‚РЅРѕРµ С‡С‚РµРЅРёРµ РґР°Р»Рѕ РґСЂСѓРіРѕР№ С„Р°Р№Р»: СЌС‚Р°Р»РѕРЅ {ReferenceParameters.ShortHash(script.Hash)}, "
                  + $"Р±РѕСЂС‚ {ReferenceParameters.ShortHash(actual)}");
        }
        catch (Exception ex) when (ex is VehicleLinkException or InvalidDataException)
        {
            return (difference.Path, ex.Message);
        }
    }

    public Task DisconnectAsync() => Task.Run(async () =>
    {
        var link = _link;
        _link = null;
        ConnectedPort = null;
        ConnectedFirmware = null;

        if (link is not null)
        {
            await link.DisposeAsync().ConfigureAwait(false);
            LinkChanged?.Invoke(this, EventArgs.Empty);
        }
    });
}

