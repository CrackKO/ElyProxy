using ElyProxy.Models;

namespace ElyProxy.Services;

public static class ProbeRatingPolicy
{
    // These are product heuristics for a fresh SOCKS + TLS + partial-body transaction,
    // not TCP ping limits or a claim to implement web.dev's browser TTFB metric.
    public static string HttpsLatencyBand(int? ms) => ms == null ? "Unknown" : ms <= 800 ? "Good" : "Medium";
    public static ProbeRating Assess(ServerProbeResult result)
    {
        var controls = result.Samples.Where(s => s.Control).ToArray();
        var transfers = result.Samples.Where(s => s.Transfer).ToArray();
        var services = result.Samples.Where(s => !s.Control && !s.Transfer).ToArray();
        int controlOk = controls.Count(s => s.Success), transferOk = transfers.Count(s => s.Success);
        int expectedControls = result.Hard ? 6 : 2;
        int expectedServices = result.Hard ? 18 : 3;
        string latency = result.MedianMs is { } ms ? $"{ms} мс HTTPS" : "— мс HTTPS";
        string metrics = latency + (result.MinimumMbps is { } speed ? $" · ≥{speed:F1} Мбит/с" : "");
        string coverage = $"Контроль HTTPS {controlOk}/{expectedControls}; сайты {services.Count(s => s.Success)}/{expectedServices}; " +
            (result.Hard ? $"полные загрузки {transferOk}/2. " : "скорость не измерялась. ");
        string detail = coverage + "Рейтинг — сравнительная оценка, не вероятность успеха. Ограничение сайта не равно отказу прокси. UDP и TUN не проверены. ";
        bool restrictedControls = controls.Any(s => s.Detail.Contains("HTTP", StringComparison.OrdinalIgnoreCase) ||
            s.Detail.Contains("содержим", StringComparison.OrdinalIgnoreCase));
        if (controls.Length > 0 && controlOk == 0 && result.Successes == 0 && !restrictedControls)
            return new(0, "F", "Не работает", "High", metrics, detail + "Ни одна контрольная HTTPS-точка не ответила успешно; подключение не рекомендуется.");
        if (result.Failure != null || result.Total == 0 || result.Starts < (result.Hard ? 2 : 1) ||
            controls.Length != expectedControls || services.Length != expectedServices || result.Hard && transfers.Length != 2)
            return new(null, "?", "Нет данных", "Unknown", metrics, detail + "Проверка не завершена или запуск ядра не подтверждён; это не диагноз неисправного сервера.");
        double ratio = (double)controlOk / expectedControls;
        int responsePoints = result.MedianMs is <= 800 ? 10 : result.MedianMs is <= 1800 ? 7 : result.MedianMs is <= 3500 ? 4 : 1;
        int speedPoints = result.MinimumMbps is >= 10 ? 10 : result.MinimumMbps is >= 5 ? 8 : result.MinimumMbps is >= 2 ? 6 : result.MinimumMbps is >= .5 ? 3 : 1;
        int score = (int)Math.Round(40 * ratio +
            (result.Hard ? 25d * transferOk / 2 : 15 * ratio) +
            15d * services.Count(s => s.Success) / expectedServices + responsePoints + (result.Hard ? speedPoints : 5));
        bool stablePath = controlOk == expectedControls && (!result.Hard || transferOk == 2);
        bool slow = result.MedianMs > 1800 || result.P95Ms > 3500 || result.Hard && result.MinimumMbps < 2;
        bool limitedSites = services.Any(s => !s.Success);
        if (limitedSites) score = Math.Min(89, score);
        if (services.Count(s => s.Success) * 2 < expectedServices) score = Math.Min(69, score);
        if (!result.Hard) score = Math.Min(85, score);
        bool variable = result.Samples.Where(s => s.Success && !s.Transfer).GroupBy(s => s.Name)
            .Any(group => group.Count() >= 2 && group.Max(s => s.Milliseconds) - group.Min(s => s.Milliseconds) > 1500);
        if (!stablePath || variable) score = Math.Min(69, score);
        if (slow) score = Math.Min(74, score);
        string grade = score >= 90 ? "A" : score >= 75 ? "B" : score >= 55 ? "C" : "D";
        string status = !stablePath || variable ? "С перебоями" : slow ? "Медленно" : grade == "A" ? "Рекомендуется" : limitedSites ? "Можно*" : "Можно";
        string band = stablePath && !variable && !slow && score >= 75 ? "Good" : "Medium";
        detail += $"Баллы: контроль {40 * ratio:F0}/40, загрузка {(result.Hard ? 25d * transferOk / 2 : 15 * ratio):F0}/{(result.Hard ? 25 : 15)}, " +
            $"сайты {15d * services.Count(s => s.Success) / expectedServices:F0}/15, отклик {responsePoints}/10, скорость {(result.Hard ? speedPoints : 5)}/{(result.Hard ? 10 : 5)}. " +
            (stablePath ? "Выход через сервер подтверждён; можно пробовать подключение. " : "Были отказы контрольных запросов или загрузок; предпочтительнее сервер с A/B. ") +
            (result.Hard ? "" : "Turbo — предварительный отбор, максимум B/85. ") +
            "Ограничения: Turbo ≤85; отдельные сбои сайтов ≤89; большинство сайтов не прошли или есть перебои ≤69; медленно ≤74. " +
            "* — не все сайты подтвердили доступность, смотрите результаты ниже. Большие миллисекунды HTTPS сами по себе не делают сервер недоступным.";
        return new(score, grade, status, band, metrics, detail);
    }
}
