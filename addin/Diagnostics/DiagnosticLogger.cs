using System;
using System.Collections.Generic;   // rev.16.14: List<string> в SetLogDirCandidates (без EPLAN!)
using System.Text;
using System.IO;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>
    /// Диагностический логгер Этапа 2 — порт логгера rev.13 из
    /// spike/TerminalStripReportSpike.cs (Log/Summarize/Fail/SaveLog),
    /// выделенный в самостоятельный класс (план: plan_stage2.md, Задача 1).
    /// DLL исполняется из ShadowCopy, путь папки лога задаётся явно.
    /// rev.16.7 (03.10, решение заказчика): ДВА РЕЖИМА ЛОГА — обычный (в файл
    /// идут только ошибки и предупреждения) и отладка (полный лог, как было).
    /// Переключение — ключ LogMode в файле настроек (AddInSettings), UI не
    /// тронут. Гейт стоит в Log(): он «горячий» (диагностика фаз идёт оттуда),
    /// а Warn/Error/Fail вызывают AppendAlways напрямую и потому не теряются
    /// НИ В ОДНОМ режиме; шапка (=== title === / Запуск / Режим лога) и строка
    /// «Лог сохранён» пишутся через AppendAlways ВСЕГДА — без них по файлу
    /// непонятно, каким прогоном и в каком режиме он записан.
    /// rev.16.7-бис (03.10, дефект гейта): ИНВАРИАНТ ПРЕФИКСА — строка, начинающаяся
    /// с «[WARN]»/«[ERROR]», пишется в Log() ВСЕГДА, независимо от режима.
    /// Без него 6 существующих вызовов, передающих важные каналы ТЕКСТОМ с
    /// префиксом через Log(), молча исчезали в обычном режиме.
    /// rev.16.7-тер (03.10, дефект гейта — ревью): инвариант ПРЕФИКСА НЕ
    /// покрывал реальность проекта. Важные отказы пишутся ДОМЕННЫМИ тегами —
    /// [UIERR] ×13, [DMERR], [NPTERR], [MDDIRS] (16 мест), и все они терялись в
    /// обычном режиме: требование «ни одно предупреждение и ни одна ошибка не
    /// теряются» не выполнялось. ИНВАРИАНТ ВАЖНОГО КАНАЛА: строка важна, если её
    /// ПЕРВЫЙ тег (текст между «[» и «]» в начале строки) содержит ERR, WARN
    /// или FAIL (OrdinalIgnoreCase) либо тег входит в IMPORTANT_TAGS_NO_ERR
    /// ([MDDIRS]). Плюс: предупреждение с тегом [WARN], дошедшее до файла через
    /// Log(), попадает и в список финального окна (WarningCount/WarningsText) —
    /// окно показывает ВСЕ предупреждения файла, а не только те, что прошли
    /// через Warn(). Плюс: Error() больше не дописывает вторую копию «[ERROR]»,
    /// а блок предупреждений окна собирает чистая статическая функция
    /// BuildWarningsBlock (усечение до WARNINGS_SHOWN + «…и ещё N, см. лог.»).
    /// Подробности и перечень мест — в докстринге Log().
    /// rev.16.13 (03.10, решение заказчика): ФИНАЛЬНОЕ ОКНО ПОКАЗЫВАЕТ ТОЛЬКО
    /// ОШИБКИ. Предупреждения в его проекте — это особенности данных и формы,
    /// а не отказ построения: EPLAN строит ЛИШНЮЮ колонку K4 под второй вывод с
    /// одной стороны («[K4] точка не привязалась к колонке», «[MATCH] колонок
    /// К4 (16) != клемм в DM (15)»), и для заказчика это норма. Поэтому в окне
    /// такие строки только мешали («отчёт построился с ошибками» при отчёте,
    /// который построился), а в логе они остаются целиком: лог — место разбора,
    /// окно — место вердикта. ЧТО СДЕЛАНО: (1) новый список ошибок
    /// (ErrorCount/ErrorText), который наполняется из ОБЕИХ каналов ошибки —
    /// Error() и Fail() (через приватную AddError, как AddWarning у
    /// предупреждений; Error() намеренно НЕ зовёт Fail(), иначе ошибка дала бы
    /// две строки в файле — rev.16.7-тер); (2) BuildWarningsBlock переименована в
    /// нейтральную BuildNoticeBlock(заголовок, …): блок ошибок собирается той
    /// же функцией с подписью «Ошибок: N:», копипасты нет. На WarningCount/
    /// WarningsText/Failed/InfoLinesDropped ноль влияния: предупреждения
    /// копятся и пишутся в лог ровно как раньше.
    /// </summary>
    public sealed class DiagnosticLogger
    {
        // Каталог лога берётся ТОЛЬКО из кандидатов, заданных перед прогоном
        // (папка «Сценарии» ИЗ НАСТРОЕК EPLAN, см. ниже): первый существующий
        // каталог, а если ни один не существует — создаётся первый кандидат.
        // Крайний случай, когда кандидатов нет вовсе (настройки EPLAN не
        // отдались), — %TEMP%: лог и файл настроек должны лечь КУДА УГОДНО, даже
        // если папка «Сценарии» недоступна (её может не быть, каталог может быть
        // на сети или под запретом).
        // rev.16.14 (04.10, решение заказчика): захардкоженные пути удалены из
        // проекта ЦЕЛИКОМ, а не оставлены фолбэком — писать в папку, которой на
        // другой машине нет, бессмысленно, а «сработало бы на машине заказчика»
        // не должно выглядеть как рабочая настройка.

        // rev.16.14 (04.10): кандидаты каталога, ЗАДАННЫЕ ПЕРЕД ПРОГОНОМ (из
        // настроек EPLAN: ProjectManager.Paths.Scripts + подпапка
        // AddInConfiguration.LogSubFolderName, см. LogDirResolver). Пока null —
        // кандидатов нет, и ResolveLogPath уходит в %TEMP%. Список приходит
        // готовым и нормализованным извне (AnalyzeAction.Execute собирает его ДО
        // загрузки настроек — иначе лог записался бы мимо каталога «Сценарии»).
        // ПОЧЕМУ ЗДЕСЬ НЕТ ВЫЗОВА LogDirResolver.BuildCandidates: этот файл входит
        // в консольный раннер tests/build_tests.bat, где нет EPLAN-DLL, а
        // LogDirResolver.cs (единственный вызов ProjectManager) в список исходников
        // раннера НЕ добавлен и добавлен быть не может — ссылка на него дала бы
        // CS0246 в тестовой сборке. Поэтому нормализация (пропуск null/пустых,
        // дубли по OrdinalIgnoreCase) повторена здесь, а на стороне EPLAN-ветки
        // всё равно всегда вызывается LogDirResolver.BuildCandidates.
        private string[] _arrLogDirCandidates = null;

        private const string LOG_FILE_NAME = "terminal_strip_addin.log";

        private readonly StringBuilder _log = new StringBuilder();
        private readonly StringBuilder _summary = new StringBuilder();

        // rev.16.7: список предупреждений БЕЗ префикса [WARN] — для финального
        // окна («Предупреждений: N» + перечисление). В лог уходит копия с
        // префиксом через AppendAlways; окну префикс не нужен.
        private readonly StringBuilder _warnings = new StringBuilder();
        private int _nWarningCount = 0;
        private bool _bFailed = false;

        // rev.16.13 (03.10, решение заказчика): список ОШИБОК для финального окна
        // («Ошибок: N» + перечисление) — по образцу _warnings, но БЕЗ префикса
        // [ERROR] (окну он не нужен, в файле он остаётся). Наполняется из обоих
        // каналов ошибки: Error() и Fail() — они не вызывают друг друга (иначе
        // дубль [ERROR], rev.16.7-тер), поэтому список собирает AddError.
        // Окно показывает ТОЛЬКО ошибки: предупреждения в его проекте — норма
        // данных (лишняя колонка K4 под второй вывод с одной стороны), в логе
        // они остаются целиком.
        private readonly StringBuilder _errors = new StringBuilder();
        private int _nErrorCount = 0;

        // rev.16.7: активный режим лога. Дефолт true — старый BeginRun(title,
        // stamp) и любые будущие вызовы, не задавшие режим явно, ведут себя
        // как до rev.16.7 (полный лог). Информация в обычном режиме не пишется,
        // но СЧИТАЕТСЯ: молча выброшенные строки не должны выглядеть как
        // «логгер ничего не делал».
        private bool _bDebugMode = true;
        private int _nInfoDropped = 0;

        // rev.14.14: автосброс лога — периодичность FlushPartial (строк буфера)
        // и счётчик неслитых строк. Крэш-сессии теряли лог полностью (SaveLog —
        // единственная точка записи); FlushPartial пишет хвост буфера на диск.
        private const int FLUSH_EVERY_LINES = 250;
        private int _nUnflushed = 0;

        /// <summary>rev.16.7: старый вызов = отладочный режим (поведение до
        /// rev.16.7). Новые вызовы должны передавать режим явно.</summary>
        public void BeginRun(string strTitle, string strBuildStamp)
        {
            BeginRun(strTitle, strBuildStamp, true);
        }

        /// <summary>rev.16.7: старт прогона в заданном режиме. Шапка — всегда,
        /// BUILD_STAMP и две строки сборки — через Log(), т.е. попадают в файл
        /// только в отладке (в обычном режиме штамп не нужен: он не про этот
        /// прогон, а про сборку, а её раз видно в файле настроек).</summary>
        public void BeginRun(string strTitle, string strBuildStamp, bool bDebugMode)
        {
            _bDebugMode = bDebugMode;

            _log.Length = 0;
            _summary.Length = 0;
            _warnings.Length = 0;
            _nWarningCount = 0;
            // rev.16.13: список ошибок окна сбрасывается на каждом прогоне так же,
            // как список предупреждений — иначе ошибки прошлого прогона висели бы
            // в окне следующего.
            _errors.Length = 0;
            _nErrorCount = 0;
            _bFailed = false;
            _nInfoDropped = 0;
            _nUnflushed = 0;   // rev.14.14: счётчик автосброса — с чистого листа

            AppendAlways("=== " + strTitle + " ===");
            AppendAlways("Запуск: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            AppendAlways("Режим лога: " + (bDebugMode
                ? "отладка (полный лог)"
                : "обычный (только ошибки и предупреждения)"));
            Log("[INFO] BUILD_STAMP: " + strBuildStamp);
            try
            {
                string strAsmPath = GetType().Assembly.Location;
                Log("[INFO] Сборка: " + strAsmPath);
                Log("[INFO] Сборка от: " + File.GetLastWriteTime(strAsmPath));
            }
            catch { }
        }

        /// <summary>rev.16.7: единственная точка записи в буфер лога — мимо
        /// гейта режима. Тело перенесено из Log() без изменений: автосброс
        /// каждые FLUSH_EVERY_LINES строк (хвост буфера уходит на диск до
        /// конца прогона) вёл себя и ведёт себя одинаково.</summary>
        private void AppendAlways(string strText)
        {
            _log.AppendLine(strText);
            ++_nUnflushed;
            if (_nUnflushed >= FLUSH_EVERY_LINES)
                FlushPartial();
        }

        // rev.16.7-бис (03.10): префиксы важных каналов, передаваемых Log() ТЕКСТОМ.
        // ERROR_PREFIX пишет Error(); WARN_TAG/WARN_PREFIX — тег предупреждения
        // и полная форма префикса, которую пишет Warn().
        private const string ERROR_PREFIX = "[ERROR]";
        private const string WARN_TAG = "[WARN]";
        private const string WARN_PREFIX = WARN_TAG + " ";

        // rev.16.7-тер (03.10, дефект гейта): признаки ОТКАЗА внутри ПЕРВОГО
        // тега. Сравнение — OrdinalIgnoreCase (регистр тега в проекте смысла не
        // несёт), тег берётся ТОЛЬКО в начале строки.
        private static readonly string[] IMPORTANT_TAG_MARKS =
            new string[] { "ERR", "WARN", "FAIL" };

        // rev.16.7-тер: важные теги БЕЗ ERR/WARN/FAIL — доменные теги, которые
        // по смыслу несут отказ. Список рядом с ERROR_PREFIX/WARN_PREFIX, стиль
        // соседей. Сейчас один: [MDDIRS] «Settings() отказ — …»
        // (SymbolBrowserDialog:2569).
        private static readonly string[] IMPORTANT_TAGS_NO_ERR =
            new string[] { "[MDDIRS]" };

        /// <summary>rev.16.7: обычный канал (диагностика фаз). Отладка — пишет;
        /// обычный режим — пишет ТОЛЬКО важные каналы (см. инвариант ниже), всё
        /// остальное считает (InfoLinesDropped), файл остаётся читаемым
        /// (ошибки/предупреждения, шапка). Строка приходит всегда непустая и не
        /// null (у всех вызовов — конкатенация литералов).</summary>
        public void Log(string strText)
        {
            // rev.16.7-тер (03.10) ИНВАРИАНТ ВАЖНОГО КАНАЛА: строка важна, если её ПЕРВЫЙ
            // тег содержит ERR / WARN / FAIL (OrdinalIgnoreCase) либо тег входит в
            // IMPORTANT_TAGS_NO_ERR ([MDDIRS]). Такая строка пишется ВСЕГДА,
            // независимо от режима, и счётчиком отброшенных НЕ считается: она
            // записана, а не потеряна — счётчик растёт только на реально потерянных
            // строках диагностики.
            // ПОЧЕМУ ПРАВИЛО, А НЕ СПИСОК КАНАЛОВ: важные каналы проекта —
            // [ERROR] и [WARN] (AnalyzeAction:169-170 и 906-907,
            // EmbeddedReportReader:132 и 288, MatchBuilder.cs:104), [UIERR] ×13
            // (AnalyzeAction:686/696/794/806/864/1450/1462/1472/1510/1522/1532/
            // 1674/1686 — «Сбор списка клеммников не удался», «Не удалось создать
            // отчёт для формы …», «Project.Pages: …»), [DMERR]
            // (EplanTerminalStripReader:1141) и [NPTERR] (:428) — обе растят
            // DmReport.ErrCount, [MDDIRS] (SymbolBrowserDialog: «Settings() отказ»
            // переведён на Warn; ещё 4 вызова Log(strMsg) с тем же тегом —
            // «не определён», отказ пути, проба каталога, итог).
            // Хардкод каждого тега = список, который надо пополнять на КАЖДЫЙ
            // новый отказ; забытый тег = молча потерянная ошибка в обычном
            // режиме — ровно тот дефект, который чинится. Признаки
            // ERR/WARN/FAIL описывают СМЫСЛ канала, поэтому новый «*ERR*» важен
            // без правки логгера. [MDDIRS] в списке, а не под правилом: тег не
            // содержит признаков отказа, но строка «Settings() отказ — …» — отказ;
            // такие доменные исключения из правила выводить нельзя.
            // Побочный эффект списка (осознанно): из 5 строк [MDDIRS] две — чистые
            // пробы и итог, они тоже переживут обычный режим.
            // ГРАНИЦЫ ПРАВИЛА: тег берётся ТОЛЬКО в начале строки — «текст
            // [ERROR] в середине» это обычная строка; пустая/null-строка не
            // важна. Регистр тега НЕ значит (OrdinalIgnoreCase).
            // ЯВНОЕ РЕШЕНИЕ (оставлено за гейтом штатно): «[MODE] headless»,
            // «[MDINFO]», «[INFO]» и прочие INFO-маркеры — по смыслу INFO: в
            // обычном режиме они отбрасываются гейтом и растят счётчик
            // InfoLinesDropped; в отладке видны как прежде.
            if (_bDebugMode || IsImportantChannel(strText))
            {
                AppendAlways(strText);
                // rev.16.7-тер: предупреждение, дошедшее до файла через Log(),
                // идёт и в список окна — иначе в файле есть [WARN], а окно
                // показывает «без предупреждений» (MAJOR ревью: MatchBuilder.cs
                // :104 «[WARN] [MATCH-SKIP] …»). Регистр тега не значит и тут:
                // [warn] — то же предупреждение, а скрыть его из списка
                // значило бы воспроизвести чинимый дефект. Остаток — без тега и
                // без разделительного пробела, как у Warn().
                string strTag = FirstTag(strText);
                if (strTag != null && string.Equals(strTag, WARN_TAG, StringComparison.OrdinalIgnoreCase))
                    AddWarning(WarnTextRest(strText, strTag));
                return;
            }
            ++_nInfoDropped;
        }

        /// <summary>rev.16.7-тер: строка важна, если её ПЕРВЫЙ тег содержит
        /// ERR/WARN/FAIL (OrdinalIgnoreCase) либо тег из IMPORTANT_TAGS_NO_ERR.
        /// Одно место правила — чтобы и гейт, и тесты читали одну формулировку.</summary>
        private static bool IsImportantChannel(string strText)
        {
            string strTag = FirstTag(strText);
            if (strTag == null)
                return false;
            for (int i = 0; i < IMPORTANT_TAGS_NO_ERR.Length; i++)
            {
                if (string.Equals(strTag, IMPORTANT_TAGS_NO_ERR[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            for (int i = 0; i < IMPORTANT_TAG_MARKS.Length; i++)
            {
                if (strTag.IndexOf(IMPORTANT_TAG_MARKS[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>rev.16.7-тер: ПЕРВЫЙ тег строки — от позиции 0 до первого
        /// ']' включительно и ТОЛЬКО если строка начинается с '['. null, если
        /// тега нет (пустая строка, начало не '[', ']' не найден). Вхождение
        /// тега В СЕРЕДИНЕ строки тегом не считается — это часть сообщения.</summary>
        private static string FirstTag(string strText)
        {
            if (string.IsNullOrEmpty(strText) || strText[0] != '[')
                return null;
            int nClose = strText.IndexOf(']');
            if (nClose < 0)
                return null;
            return strText.Substring(0, nClose + 1);
        }

        /// <summary>rev.16.7-тер: остаток строки после тега, без
        /// разделительного пробела — то, что показывает окно.</summary>
        private static string WarnTextRest(string strText, string strTag)
        {
            string strRest = strText.Substring(strTag.Length);
            if (strRest.StartsWith(" ", StringComparison.Ordinal))
                strRest = strRest.Substring(1);
            return strRest;
        }

        /// <summary>rev16.9 (03.10): строка КОНТЕКСТА прогона («шапка»:
        /// какие настройки прочитаны, откуда) — в буфер МИМО гейта режима, то
        /// есть через AppendAlways напрямую. Без префикса: это не канал ошибки
        /// и не [INFO], который счётчик отбросил бы.
        /// ПОЧЕМУ ОТДЕЛЬНЫЙ МЕТОД, А НЕ Log(): обычный режим роняет INFO, а
        /// контекст прогона и путь к файлу настроек нужны ВСЕГДА — без них
        /// причину пропавшего ключа LogMode доказать нечем: строка
        /// «[SETTINGS] load: …» идёт через Log() и в обычном режиме в файле её
        /// НЕТ, то есть диагностика молчала (дефект, из-за которого правка
        /// пользователя «исчезала» без следов).
        /// НЕ влияет ни на что: WarningCount/Failed/InfoLinesDropped/сводка не
        /// трогаются, тег в тексте (даже «[WARN]») НЕ разбирается — иначе шапка
        /// фальсифицировала бы счётчик предупреждений финального окна. Для
        /// отказа, который надо показать всегда, есть Warn()/Error().
        /// null/пустая строка не бросают (пишутся как есть).</summary>
        public void Header(string strText)
        {
            AppendAlways(strText);
        }

        /// <summary>rev.16.7: предупреждение идёт В лог мимо гейта и
        /// дополнительно копируется в список для окна (без префикса). Пустой
        /// текст в буфер пишется как раньше, но в список НЕ идёт — иначе в
        /// окне появятся пустые строки.</summary>
        public void Warn(string strText)
        {
            AppendAlways(WARN_PREFIX + strText);
            AddWarning(strText);
            FlushPartial();   // rev.14.14: Warn-канал сбрасывается сразу
        }

        /// <summary>rev.16.7-тер: единственная точка пополнения списка
        /// предупреждений: оба источника (Warn() и важная строка из Log()) идут
        /// через неё, поэтому предупреждение не может попасть в список дважды,
        /// а пустой текст не попадает ни разу.</summary>
        private void AddWarning(string strText)
        {
            if (string.IsNullOrEmpty(strText))
                return;
            _warnings.AppendLine(strText);
            ++_nWarningCount;
        }

        /// <summary>rev.16.7: ошибка — мимо гейта ВСЕГДА (режим лога не имеет
        /// права прятать сбой).
        /// rev.16.7-тер (MAJOR ревью): НЕ вызывает Fail() — тот дописывал ту
        /// же строку ВТОРЫМ [ERROR], и одна ошибка давала два трейса в файле.
        /// Семантика канала прежняя и проверена тестом (кейс 19): строка в лог
        /// одна, Failed взводится, текст попадает в сводку (Summarize зовётся
        /// напрямую — ровно как раньше это делал Fail). Fail() как отдельный
        /// публичный метод (catch + ранние выходы пайплайна) НЕ тронут.
        /// rev.16.13: канал ошибки копит текст в списке для окна (AddError) —
        /// окно показывает ТОЛЬКО ошибки, а этот канал молча в него не попадал.
        /// Строка в файле по-прежнему одна, дубля нет.</summary>
        public void Error(string strText)
        {
            AppendAlways(ERROR_PREFIX + " " + strText);
            _bFailed = true;
            AddError(strText);
            Summarize(strText);
            FlushPartial();   // rev.14.14: ошибки сбрасываются сразу
        }

        /// <summary>rev.16.13: единственная точка пополнения списка ошибок —
        /// оба канала (Error() и Fail()) идут через неё, поэтому ошибка не может
        /// попасть в список дважды, а пустой текст не попадает ни разу (как в
        /// AddWarning). Обратная сторона единой точки: ОШИБКА, эскалированная до
        /// Fail() после Warn(), попадёт в список ОШИБКИ один раз, а её
        /// предупреждение останется только в логе — это и есть смысл решения
        /// заказчика (в окне отказ, в логе обе строки).</summary>
        private void AddError(string strText)
        {
            if (string.IsNullOrEmpty(strText))
                return;
            _errors.AppendLine(strText);
            ++_nErrorCount;
        }

        /// <summary>rev.16.7: сводка идёт в _summary ВСЕГДА (окно получает её
        /// независимо от режима), в лог — через Log(), т.е. в обычном режиме
        /// сводки фаз в файле нет: это INFO, а лог там для ошибок.</summary>
        public void Summarize(string strText)
        {
            _summary.AppendLine(strText);
            Log("[INFO] " + strText);
        }

        /// <summary>rev.16.13: второй канал ошибки (catch в Execute, ранние выходы
        /// пайплайна: «Нет открытого проекта», «Нет активной страницы», …).
        /// Семантика прежняя (Failed + строка [ERROR] + сводка); ДОБАВЛЕНО
        /// копление текста в списке ошибок окна (AddError) — иначе половина
        /// отказов пайплайна была бы видна в логе, но не в окне, а окно с
        /// 03.10 показывает ТОЛЬКО ошибки.</summary>
        public void Fail(string strText)
        {
            _bFailed = true;
            AppendAlways("[ERROR] " + strText);
            AddError(strText);
            Summarize(strText);
        }

        public bool Failed { get { return _bFailed; } }

        public string SummaryText { get { return _summary.ToString(); } }

        /// <summary>rev.16.7: число предупреждений — окно пишет «Предупреждений: N».</summary>
        public int WarningCount { get { return _nWarningCount; } }

        /// <summary>rev.16.7: предупреждения БЕЗ префикса [WARN], по строке на
        /// штуку (строки уже разделены переводом строки) — тело для окна.
        /// rev.16.13: окно перестало их показывать (предупреждения = особенности
        /// данных/формы, см. решение заказчика в докстринге класса), но список и
        /// его счётчик продолжают копиться и печататься в лог: свойство живое,
        /// его читает сборка блока и тесты — а AnalyzeAction этот блок больше
        /// не печатает.</summary>
        public string WarningsText { get { return _warnings.ToString(); } }

        /// <summary>rev.16.13: число ОШИБок прогона — окно пишет «Ошибок: N» и
        /// перечисляет их. Это единственный перечень, который финальное окно
        /// показывает: по решению заказчика 03.10 предупреждения («лишняя
        /// колонка K4 под второй вывод с одной стороны» и подобные) в окне —
        /// шум, в логе они остаются целиком. Ведётся из обоих каналов ошибки
        /// (Error() и Fail()), см. AddError.</summary>
        public int ErrorCount { get { return _nErrorCount; } }

        /// <summary>rev.16.13: ошибки БЕЗ префикса [ERROR], по строке на штуку
        /// — тело для блока ошибок окна (в файле лога префикс остаётся: там он
        /// нужен читателю, в окне только шумит).</summary>
        public string ErrorText { get { return _errors.ToString(); } }

        /// <summary>rev.16.7-тер: ТЕЛО блока сообщений финального окна,
        /// вынесенное из AnalyzeAction (окно собирает строку MessageBox прямо
        /// в методе — там она была непроверяема: AnalyzeAction локально не
        /// компилируется, нужны EPLAN-DLL/WinForms).
        /// rev.16.13: переименована из BuildWarningsBlock и получила ПЕРВЫМ
        /// аргументом strTitle — подпись шапки («Предупреждений» / «Ошибок»).
        /// Одна функция на оба блока, а не две копии: правило усечения одно,
        /// тесты переиспользуются, и новая подпись не может «поехать» отдельно
        /// от старой. Правило и смысл остальных аргументов прежние.
        /// ПРАВИЛО: шапка называет ПОЛНОЕ число (nTotal), перечень усечён до
        /// nShow строк, а хвост «…и ещё N, см. лог.» (полное минус показанные)
        /// объясняет, что список не весь. Шапка полная — намеренно: усечённый
        /// счётчик занижал бы масштаб проблемы (а 300 строк [MATCHCOL] в окне
        /// нечитаемы; в обычном режиме в файле лежат только они и просят «см.
        /// лог»).
        /// nTotal &lt;= 0 → пустая строка: окно блок не печатает вовсе.</summary>
        public static string BuildNoticeBlock(string strTitle, int nTotal, string strText, int nShow)
        {
            if (nTotal <= 0)
                return "";
            string[] arrLines = (strText ?? "").Split(
                new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            StringBuilder sb = new StringBuilder();
            sb.Append(strTitle).Append(": ").Append(
                nTotal.ToString(CultureInfo.InvariantCulture)).Append(":").Append(Environment.NewLine);
            int nShown = 0;
            for (int i = 0; i < arrLines.Length && nShown < nShow; i++)
            {
                // AppendLine даёт завершающий перевод строки — Split отдаёт его
                // последним пустым элементом. Пустая строка — не сообщение.
                if (arrLines[i].Length == 0)
                    continue;
                sb.Append(arrLines[i]).Append(Environment.NewLine);
                ++nShown;
            }
            if (nTotal > nShown)
            {
                sb.Append("…и ещё ").Append((nTotal - nShown).ToString(CultureInfo.InvariantCulture))
                    .Append(", см. лог.").Append(Environment.NewLine);
            }
            return sb.ToString();
        }

        /// <summary>rev.16.7: сколько строк выбросил гейт обычного режима (в файле
        /// их нет — диагностика прогоня). Считаются ТОЛЬКО реально потерянные
        /// строки: строки с префиксом [WARN]/[ERROR] идут мимо гейта по
        /// инварианту Log() и в счётчик НЕ входят (они записаны).</summary>
        public int InfoLinesDropped { get { return _nInfoDropped; } }

        /// <summary>rev.16.7: активный режим лога (true — отладка, полный лог).</summary>
        public bool DebugMode { get { return _bDebugMode; } }

        /// <summary>rev.16.7: текст буфера лога — наблюдаемость для тестов
        /// (SummaryText/WarningsText открыты по той же причине: финальное окно
        /// и тесты должны видеть содержимое без чтения файла лога).</summary>
        public string LogText { get { return _log.ToString(); } }

        /// <summary>Сохраняет лог в UTF-8 c BOM (паттерн rev.13). Возвращает путь файла.</summary>
        public string SaveLog()
        {
            try
            {
                string strPath = ResolveLogPath();
                if (string.IsNullOrEmpty(strPath))
                    return "<не удалось сохранить лог: путь не разрешён>";
                // rev.16.7: путь к логу нужен в файле в ОБОИХ режимах (окно
                // ссылается на него) — мимо гейта, через AppendAlways.
                AppendAlways("[INFO] Лог сохранён: " + strPath);
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

        /// <summary>rev.16.14: задать кандидатов каталога лога (из настроек
        /// EPLAN). Зовётся AnalyzeAction.Execute ДО загрузки настроек. Правила
        /// прежние (первый существующий кандидат → создание первого кандидата →
        /// %TEMP%), изменился только СПИСОК кандидатов.
        /// Нормализация (пропуск null/пустых, дубли без учёта регистра) повторена
        /// локально намеренно — LogDirResolver.BuildCandidates недоступен этому
        /// файлу, он собирается консольным раннером tests/build_tests.bat без
        /// EPLAN-DLL (пояснение у поля _arrLogDirCandidates). Список из одних
        /// пустых строк ЗАПИСЫВАЕТСЯ как пустой, и тогда кандидатов нет вовсе:
        /// резолв сразу уходит в %TEMP%.</summary>
        public void SetLogDirCandidates(string[] arrDirs)
        {
            List<string> lstDirs = new List<string>();
            if (arrDirs != null)
            {
                for (int i = 0; i < arrDirs.Length; ++i)
                {
                    string strDir = arrDirs[i];
                    if (string.IsNullOrEmpty(strDir))
                        continue;
                    if (IsAlreadyIn(lstDirs, strDir))
                        continue;
                    lstDirs.Add(strDir);
                }
            }
            _arrLogDirCandidates = lstDirs.ToArray();
        }

        /// <summary>rev.16.14: есть ли уже такой каталог в списке (регистр не
        /// значит). Ручной проход, а НЕ List.Contains/Enumerable.Contains: у
        /// List версия сравнивает Ordinal, а перегрузка с IEqualityComparer живёт
        /// в LINQ, а LINQ в проекте не используется.</summary>
        private static bool IsAlreadyIn(List<string> lstDirs, string strDir)
        {
            for (int i = 0; i < lstDirs.Count; ++i)
            {
                if (string.Equals(lstDirs[i], strDir,
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>rev.16.14: кандидаты каталога, которые реально использует
        /// резолв пути: заданный список, а пока его нет (или он пуст) — ПУСТОЙ
        /// массив, то есть «путей не задано» и ResolveLogPath уходит в %TEMP%.
        /// Захардкоженных кандидатов у логгера больше нет (решение заказчика
        /// 04.10) — возвращать нечего. ВАЖНО: в файле остаётся ни одной ссылки
        /// на EPLAN API, иначе консольный раннер tests/build_tests.bat перестанет
        /// собираться (пояснение у поля _arrLogDirCandidates).</summary>
        public string[] CurrentLogDirCandidates()
        {
            if (_arrLogDirCandidates != null && _arrLogDirCandidates.Length > 0)
                return _arrLogDirCandidates;
            return new string[0];
        }

        /// <summary>rev.14.14: резолв пути файла лога (извлечено из SaveLog,
        /// логика прежняя: первый существующий кандидат → создание первого
        /// кандидата → %TEMP%). rev.16.14: перебор идёт по
        /// CurrentLogDirCandidates() (заданы из настроек EPLAN — см.
        /// SetLogDirCandidates); список пуст (настройки EPLAN не отдались) —
        /// сразу %TEMP%. Возвращает полный путь или null.</summary>
        private string ResolveLogPath()
        {
            string[] arrDirs = CurrentLogDirCandidates();
            string strDir = null;
            foreach (string strCandidate in arrDirs)
            {
                if (Directory.Exists(strCandidate))
                {
                    strDir = strCandidate;
                    break;
                }
            }
            if (string.IsNullOrEmpty(strDir) && arrDirs.Length > 0)
            {
                // Папка Add-in'а ещё не создана — создаём первый кандидат,
                // чтобы все прогоны Этапа 2 лежали в одном месте.
                try
                {
                    Directory.CreateDirectory(arrDirs[0]);
                    strDir = arrDirs[0];
                }
                catch { }
            }
            if (string.IsNullOrEmpty(strDir) || !Directory.Exists(strDir))
                strDir = Path.GetTempPath();
            if (string.IsNullOrEmpty(strDir))
                return null;
            return Path.Combine(strDir, LOG_FILE_NAME);
        }
    }
}
