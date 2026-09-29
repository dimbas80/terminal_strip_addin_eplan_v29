using System;
using System.Text;
using System.IO;

namespace MyEplanActions
{
    /// <summary>
    /// Диагностический логгер Этапа 2 — порт логгера rev.13 из
    /// spike/TerminalStripReportSpike.cs (Log/Summarize/Fail/SaveLog),
    /// выделенный в самостоятельный класс (план: plan_stage2.md, Задача 1).
    /// DLL исполняется из ShadowCopy, путь папки лога задаётся явно.
    /// </summary>
    public sealed class DiagnosticLogger
    {
        // Каталог лога: 1) LOG_DIR_OVERRIDE (если задан и существует);
        // 2) первый существующий из LOG_DIR_CANDIDATES (создаётся при первом
        // прогоне); 3) %TEMP%.
        private const string LOG_DIR_OVERRIDE = "";

        // Папка Add-in'а внутри «Сценарии» и корневая «Сценарии» на машине
        // пользователя (пути подтверждены прогонами Этапа 1). rev.12.0 (Фаза H):
        // public static readonly — ЕДИНЫЙ источник кандидатов каталога для лога
        // И файла настроек AddInSettings (ruling R1; НЕ Assembly.Location —
        // ShadowCopy, урок п.10).
        public static readonly string[] LOG_DIR_CANDIDATES = new string[]
        {
            @"D:\YandexDisk\!EPLAN\Сценарии\terminal_strip_addin",
            @"D:\YandexDisk\!EPLAN\Сценарии"
        };

        private const string LOG_FILE_NAME = "terminal_strip_addin.log";

        private readonly StringBuilder _log = new StringBuilder();
        private readonly StringBuilder _summary = new StringBuilder();
        private bool _bFailed = false;

        // rev.14.14: автосброс лога — периодичность FlushPartial (строк буфера)
        // и счётчик неслитых строк. Крэш-сессии теряли лог полностью (SaveLog —
        // единственная точка записи); FlushPartial пишет хвост буфера на диск.
        private const int FLUSH_EVERY_LINES = 250;
        private int _nUnflushed = 0;

        public void BeginRun(string strTitle, string strBuildStamp)
        {
            _log.Length = 0;
            _summary.Length = 0;
            _bFailed = false;
            _nUnflushed = 0;   // rev.14.14: счётчик автосброса — с чистого листа

            Log("=== " + strTitle + " ===");
            Log("Запуск: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("[INFO] BUILD_STAMP: " + strBuildStamp);
            try
            {
                string strAsmPath = GetType().Assembly.Location;
                Log("[INFO] Сборка: " + strAsmPath);
                Log("[INFO] Сборка от: " + File.GetLastWriteTime(strAsmPath));
            }
            catch { }
        }

        public void Log(string strText)
        {
            _log.AppendLine(strText);
            // rev.14.14: автосброс каждые FLUSH_EVERY_LINES строк — хвост буфера
            // уходит на диск до конца прогона.
            ++_nUnflushed;
            if (_nUnflushed >= FLUSH_EVERY_LINES)
                FlushPartial();
        }

        public void Warn(string strText)
        {
            Log("[WARN] " + strText);
            FlushPartial();   // rev.14.14: Warn-канал сбрасывается сразу
        }

        public void Error(string strText)
        {
            Log("[ERROR] " + strText);
            Fail(strText);
            FlushPartial();   // rev.14.14: ошибки сбрасываются сразу
        }

        public void Summarize(string strText)
        {
            _summary.AppendLine(strText);
            Log("[INFO] " + strText);
        }

        public void Fail(string strText)
        {
            _bFailed = true;
            Log("[ERROR] " + strText);
            Summarize(strText);
        }

        public bool Failed { get { return _bFailed; } }

        public string SummaryText { get { return _summary.ToString(); } }

        /// <summary>Сохраняет лог в UTF-8 c BOM (паттерн rev.13). Возвращает путь файла.</summary>
        public string SaveLog()
        {
            try
            {
                string strPath = ResolveLogPath();
                if (string.IsNullOrEmpty(strPath))
                    return "<не удалось сохранить лог: путь не разрешён>";
                Log("[INFO] Лог сохранён: " + strPath);
                File.WriteAllText(strPath, _log.ToString(), new UTF8Encoding(true));
                return strPath;
            }
            catch (Exception oException)
            {
                return "<не удалось сохранить лог: " + oException.Message + ">";
            }
        }

        /// <summary>rev.14.14: автосброс буфера лога в файл (SaveLog был единственной
        /// точкой записи — крэш-сессии теряли лог полностью). БЕЗ маркера «Лог
        /// сохранён» (не спамить; финальную запись оставляет SaveLog). Нативный AV
        /// (0xC0000005) в хуки исключений не доходит — выживает именно эта запись:
        /// хвост лога показывает последнюю пробу перед смертью.</summary>
        public void FlushPartial()
        {
            try
            {
                string strPath = ResolveLogPath();
                if (string.IsNullOrEmpty(strPath)) return;   // ревью M-1: счётчик НЕ сбрасывать — следующая попытка не через 250 строк
                File.WriteAllText(strPath, _log.ToString(), new UTF8Encoding(true));
                _nUnflushed = 0;
            }
            catch { }
        }

        /// <summary>rev.14.14: резолв пути файла лога (извлечено из SaveLog, логика
        /// прежняя: LOG_DIR_OVERRIDE → первый существующий LOG_DIR_CANDIDATES →
        /// создание первого кандидата → %TEMP%). Возвращает полный путь или null.</summary>
        private string ResolveLogPath()
        {
            string strDir = LOG_DIR_OVERRIDE;
            if (string.IsNullOrEmpty(strDir) || !Directory.Exists(strDir))
            {
                foreach (string strCandidate in LOG_DIR_CANDIDATES)
                {
                    if (Directory.Exists(strCandidate))
                    {
                        strDir = strCandidate;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(strDir))
                {
                    // Папка Add-in'а ещё не создана — создаём первый кандидат,
                    // чтобы все прогоны Этапа 2 лежали в одном месте.
                    try
                    {
                        Directory.CreateDirectory(LOG_DIR_CANDIDATES[0]);
                        strDir = LOG_DIR_CANDIDATES[0];
                    }
                    catch { }
                }
            }
            if (string.IsNullOrEmpty(strDir) || !Directory.Exists(strDir))
                strDir = Path.GetTempPath();
            if (string.IsNullOrEmpty(strDir))
                return null;
            return Path.Combine(strDir, LOG_FILE_NAME);
        }
    }
}
