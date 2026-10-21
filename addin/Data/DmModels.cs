using System.Collections.Generic;

namespace MyEplanActions
{
    // rev.12.6 (Этап 8, H-3c): чистые data-классы итога чтения Data Model
    // перенесены дословно из EplanTerminalStripReader.cs — сам ридер тянет
    // using Eplan.EplApi.* и не компилируется в чистый тест-раннер tests/,
    // а MatchBuilder (и новый тест оверфлоу-слота rev.12.6) работают только с
    // этими классами. Поведение не менялось; /recurse:*.cs в build_addin.bat
    // подхватывает файл автоматически.

    /// <summary>Плоская строка дампа [DM]: Terminal | Side | Connection | Cable (Фаза B,
    /// plan_implementation §23). rev.5.0: добавлены проба CDP и №31058
    /// («Соединение: Принадлежность=Кабель», summary Задача 5).</summary>
    public sealed class DmRow
    {
        public string StripName;
        public string TerminalName;
        public string Side;            // "Ext" | "Int" | "Bridge"
        public string ConnectionName;  // ConnectionInfo.ConnectionName
        public string PinName;         // ConnectionInfo.FunctionPinName
        public int PinIndex;           // ConnectionInfo.PinIndex
        public string PeerName;        // для моста: BridgedTerminal
        public string CableName;       // CDP -> CableDefinitionLine -> Cable.Name (null = провод)
        public bool HasConn;           // мост: Conn != null
        public int CdpCount = -1;      // ConnectionDefPoints: 0/1/>1; -1 = проба не удалась
        public bool? IsCableConn;      // №31058 на Connection (null = свойство не задано)
        public bool? IsCableCdp;       // №31058 на первом CDP (null = нет CDP/не задано)
        public string CableSource;      // №31019 CONNECTION_SOURCE на Connection (проба rev.16.3)
        public string CableDest;        // №31020 CONNECTION_DESTINATION на Connection (проба rev.16.3)
    }

    /// <summary>Итог пробы кабеля одного Connection (rev.5.0): кэш на прогон.
    /// KB (www.eplan.help API 2.9): Connection.CableDefinitionLine бросает BaseException
    /// при ≠1 CDP («Different count than 1») — основной путь: ConnectionDefPoints →
    /// ConnectionDefinitionPoint.CableDefinitionLine; №31058 читается на Connection и
    /// на CDP (docs помечает его legacy: «no longer in use, only old projects»).</summary>
    public sealed class CableInfo
    {
        public string CableName;
        public int CdpCount = -1;
        public bool? IsCableConn;
        public bool? IsCableCdp;
        // Проба rev.16.3: №31019/№31020 — только чтение и лог, в логике не участвуют.
        public string CableSource;
        public string CableDest;
    }

    /// <summary>Значимые части ОУ КЛЕММНИКА, прочитанные из объекта по номерам
    /// свойств (rev16.5-bis, задача 3.1-бис шаг «части как источник»). Основание —
    /// стенд 02.10 + проба spike/StripNamePartsSpike.cs: ОУ клеммника
    /// восстанавливается из частей НА 100 % (19 из 19 клеммников), т.е.
    ///   = {1100}.{1101} ++ {1400} + {1200} #{1600} -{20013}{20014}
    /// Раньше те же значения вытаскивались из СТРОКИ полного ОУ эвристикой
    /// «последний дефис» (BreakPointResolver.CabinetKeyOf/DesignationOf), а она не
    /// различает дефис внутри имени шкафа (безопасно) и дефис внутри ОБОЗНАЧЕНИЯ
    /// (ломает определение multi). По номерам граница однозначна.
    /// Пустое поле («пара не задана») — это "" и НЕ «нет данных»: отсутствие
    /// данных несут ОТДЕЛЬНЫЕ случаи — oParts == null (объект не прочитан) и
    /// HasAny == false (у клеммника не задано ни одного из трёх полей); в обоих
    /// случаях потребитель обязан откатиться на разбор строки.
    /// Заполняет EplanTerminalStripReader.ReadStripParts (один раз на клеммник,
    /// не на клему); читает BreakPointResolver.CabinetKeyOfParts /
    /// DesignationOfParts.</summary>
    public sealed class DmStripParts
    {
        // №1400 «Место сборки» (стенд: 'ЯЧ67', 'ПУ', 'ЯЧ17', …), "" если не задано.
        public string Assembly;
        // №1200 «Место установки» (часто пустое — это норма, не ошибка), "" если не задано.
        public string Mounting;
        // №20013 (буквенный код: 'X', 'XT', 'XB') + №20014 (счётчик: '1', '2', '3')
        // СЛИТНОМ, "" если не задано. Именно слитно: так это значение лежит в строке
        // ОУ, и BreakPointResolver.DesignationOf сравнивает с ним строку.
        public string Designation;
        // Инициализация в КОНСТРУКТОРЕ, а не в инициализаторе поля: соседние
        // классы файла пользуются обоими стилями, здесь выбран явный — класс
        // создаётся в трёх местах (ридер, тесты) и пустые значения обязаны быть
        // "" ВСЕГДА, иначе HasAny/конкатенация 20013+20014 уедут на null.
        public DmStripParts()
        {
            Assembly = "";
            Mounting = "";
            Designation = "";
        }
        /// <summary>Задано ли хоть одно поле. false ⇒ потребитель берёт значение из
        /// СТРОКИ (фоллбэк): читать нечего, и молча подставить "" значило бы
        /// сравнивать шкафы как «один пустой шкаф».
        /// Проверка через string.IsNullOrEmpty, а не «поле.Length > 0» (rev16.5-bis):
        /// поля public, а HasAny читается на КАЖДЫЙ DT в каждом вызове Decide — одно
        /// присваивание null в будущем коде уронило бы весь прогон целиком, а не
        /// одну проверку. Конструктор ниже страхует пустые значения на старте, но
        /// страховка не должна жить только в нём.</summary>
        public bool HasAny
        {
            get { return !string.IsNullOrEmpty(Assembly) || !string.IsNullOrEmpty(Mounting) || !string.IsNullOrEmpty(Designation); }
        }
    }

    /// <summary>Статистика одного клеммника (rev.4.1: сверка [CROSS] по-клеммнику —
    /// отчёт показывает один клеммник, сумма по проекту не годится, summary п.21).</summary>
    public sealed class DmStripStats
    {
        public int TerminalCount;
        public int ExtCount;
        public int IntCount;
        public int BridgeCount;
        public int ConnCount { get { return ExtCount + IntCount; } }
    }

    /// <summary>Итог чтения Data Model (Задача 4, план: plan_stage2.md).</summary>
    public sealed class DmReport
    {
        public readonly List<DmRow> Rows = new List<DmRow>();
        public int StripCount;
        public int TerminalCount;
        public int ErrCount;
        // По-клеммниковая статистика: полное имя клеммника -> счётчики (rev.4.1).
        public readonly Dictionary<string, DmStripStats> PerStrip =
            new Dictionary<string, DmStripStats>();
        // rev.5.1: ВСЕ имена клемм клеммника (в порядке TerminalStrip.Terminals),
        // включая клеммы без подключений — иначе сопоставление колонок К4 уезжает
        // (урок rev.5.0: 10 клемм без подключений выпадали, 61 колонка vs 50 групп).
        public readonly Dictionary<string, List<string>> StripTerminalNames =
            new Dictionary<string, List<string>>();
        // Кабель -> число жил, встреченных в подключениях клемм (первое обнаружение
        // логируется как [DMCABLE]).
        public readonly Dictionary<string, int> CableWireCounts = new Dictionary<string, int>();
        // rev.16.4 (задача 3.1-bis): полное DT кабеля -> ВСЕ ПОЛНЫЕ ОУ КЛЕММНИКОВ
        // (oRow.StripName, БЕЗ хвоста ':пин'), собранные по строкам ЭТОГО кабеля
        // (Ext/Int/Bridge). Ключ — тот же, что у CableCoreEnds в rev.16.3:
        // ConnectionName, при пустом — CableName (совпадает с CableLayoutBuilder:34).
        //
        // ПОЧЕМУ ИМЕННО ЭТОТ СЛОВАРЬ, А НЕ 31019/31020 (решение заказчика 02.10.2026):
        // EPLAN собирает 31019/31020 ТОЛЬКО из «идентифицирующих» свойств. У приборов
        // сегменты «++»/«+» помечены «описывающими» и в строку не попадают вовсе,
        // у клеммников «++» — «идентифицирующее». Настройки живут в свойствах проекта
        // и задаются пользователем ⇒ разбор такой строки зависит от конфигурации
        // чужого проекта, а восстановить ОПУЩЕННЫЙ сегмент из строки нельзя. Здесь
        // значения — полные DT из [DM], одинаковые при любых настройках.
        //
        // Порядок — первого появления, без дублей, сравнение Ordinal; сортировки нет —
        // детерминизм «первый по Ordinal» задаёт потребитель
        // BreakPointResolver.Decide(список DT). Значения — ОУ КЛЕММНИКА, поэтому хвоста
        // ':пин' в них нет вовсе: один клеммник с десятью клемами даёт ОДИН элемент
        // (дедуп по Ordinal), а буквенный пин клемы разбор не затрагивает. Поле собирается
        // ТОЛЬКО для строк MatchBuilder.IsCableRow: без фильтра в одну группу попадают
        // внутренние соединения с пустым именем кабеля ('=+++#' — на стенде 02.10 их
        // 420 из 1398 строк, а в одной группе оказывалось 30 клеммников из шести
        // шкафов ⇒ ложный multi). Кабель без строк [DM] ключом НЕ появляется —
        // это и есть сигнал к откату на 20376/20377.
        public readonly Dictionary<string, List<string>> CableStripEnds =
            new Dictionary<string, List<string>>();
        // rev16.5-bis: ПОЛНОЕ ОУ клеммника -> прочитанные части (DmStripParts:
        // №1400/№1200/№20013+№20014). Источник шкафа и обозначения для
        // BreakPointResolver.Decide — вместо эвристики «последний дефис» по строке.
        //
        // ПОЧЕМУ ОТДЕЛЬНАЯ БОКОВАЯ ТАБЛИЦА, А НЕ НОВОЕ ПОЛЕ В DmRow (решение
        // 02.10.2026): часть ОУ — свойство КЛЕММНИКА, а не строки [DM]: у одного
        // клеммника 10-500 клем (стенд: 504 клемы на 19 клеммников) и в каждой
        // строке значения повторялись бы 504 раза на один ключ. Создавать ради
        // этого новый тип строки нельзя — DmRow/CableStripEnds читает
        // MatchBuilder, а тот печатается в [BPE-SUM], в лог и в существующие
        // тесты: смена типа или имени поля сломала бы сверку «строки [DM] ↔ BPE»
        // и сверку [BPE-SUM] с числом строк лога. Боковая таблица этого не
        // трогает: не меняются ни DmRow, ни CableStripEnds, ни лог, ни тесты.
        //
        // КЛЮЧ — ПОЛНОЕ ОУ клеммника (то же значение, что oRow.StripName, которое
        // уже пишется в CableStripEnds). Оно идентифицирует клеммник однозначно
        // (дубль имени на двух страницах логируется как [DMSTRIPDUP], значения
        // частей у дублей совпадают) и, что важно, совпадает с ключами концов,
        // которые получает Decide: словарь один и тот же и для своего DT, и для
        // КАЖДОГО конца, поэтому обе стороны сравнения берутся по одному правилу.
        //
        // ПУСТОЕ ПОЛЕ ≠ «нет данных»: пустое поле — это «пара не задана»
        // (на стенде №1200 пуст у 13 клеммников из 19 — норма), а «нет данных»
        // — это отсутствие ключа (oParts == null при отказе чтения). Правило
        // выбора источника (BreakPointResolver.KeyOfDt/DesOfDt): HasAny == false
        // либо ключа нет ⇒ разбор строки.
        //
        // Заполняется ОДИН раз на клеммник (EplanTerminalStripReader.Read, до
        // обхода его клем), поэтому 504 клемы читают 19 значений, а не 504.
        public readonly Dictionary<string, DmStripParts> StripPartsByDt =
            new Dictionary<string, DmStripParts>();
        // rev.5.0: статистика пробы CDP/№31058 по УНИКАЛЬНЫМ соединениям (кэш CableInfo).
        public int ConnCdpZero;
        public int ConnCdpOne;
        public int ConnCdpMulti;
        public int ConnCdpErr;
        public int IsCable31058ConnTrue;
        public int IsCable31058CdpTrue;
    }
}
