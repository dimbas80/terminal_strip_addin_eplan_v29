using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace MyEplanActions
{
    // rev.12.6 (Этап 8, H-3c): чистые data-классы DmRow/CableInfo/DmStripStats/
    // DmReport перенесены дословно в DmModels.cs (компилируются в чистый
    // тест-раннер tests/ вместе с MatchBuilder).

    /// <summary>
    /// Ридер Data Model — Фаза B (план: plan_stage2.md, Задача 4).
    /// Цепочка подтверждена чанками KB (www.eplan.help API 2.9):
    ///   Project.Pages: Page[] → Page.TerminalStrips: TerminalStrip[] →
    ///   TerminalStrip.Terminals: Terminal[] →
    ///   Terminal.ExternalConnections / InternalConnections: Terminal.ConnectionInfo[]
    ///   (поля Conn, ConnectionName, FunctionPinName, PinIndex) →
    ///   Terminal.Bridges: Terminal.Bridge[] → BridgeSegments (BridgedTerminal, Conn) →
    ///   rev.5.0 (кабель): Connection.ConnectionDefPoints: ConnectionDefinitionPoint[] →
    ///   ConnectionDefinitionPoint.CableDefinitionLine: Cable (Cable.Name; Connection.
    ///   CableDefinitionLine напрямую непригоден — бросает BaseException при ≠1 CDP);
    ///   №31058 CONNECTION_IS_CABLE («Соединение: Принадлежность=Кабель») — на Connection
    ///   и на CDP, bool, read-only, docs: legacy. TERMINALSTRIP_COUNTOFTERMINALS №35006.
    /// Ошибки отдельных объектов — в лог [DMERR] с инкрементом ErrCount, обход не прерывается.
    /// </summary>
    public sealed class EplanTerminalStripReader
    {
        private readonly DiagnosticLogger _log;

        // rev.12.1 (Фаза H, H-2): целевой клеммник (фильтр пробы [CBLPROP]) —
        // параметр конструктора: UI — выбор диалога, headless — константа
        // AddInConfiguration (явный аргумент в месте вызова).
        private readonly string _strTargetStripName;

        // rev.12.2 (H-2c, фикс-волна MAJOR-2): полное имя клеммника -> список страниц,
        // где оно встречено при агрегации PerStrip. Сверка [CROSS] и гейт [CROSSGATE]
        // ПОЛНОГО ИМЕНИ работают с допущением «полное имя уникально в проекте»;
        // имя на 2+ страницах ломает допущение (PerStrip/StripTerminalNames суммируют
        // дубликаты) — первый дуплет логируется WARN [DMSTRIPDUP], гейт смягчается
        // (TargetNameMultiPage). Заполняется в Read(), читается после.
        private readonly Dictionary<string, List<string>> _dicStripPages =
            new Dictionary<string, List<string>>();

        // rev.17-диагностика: номер свойства «Уровень» многоуровневого клеммника
        // (KB: eplan.help API 2.9, FunctionPropertyList.FUNC_TERMINALLEVEL = 20034;
        // Remarks: «Level of the multi-level terminal; if the value is "0", then there
        // are no multi-level terminals, but a terminal with only one level»). Читается
        // ТОЛЬКО в дампе [DLV] (DumpTerminalLinks) — в логике чтения не участвует.
        private const int PROP_TERMINAL_LEVEL = 20034;

        // rev.17 (правка поведения, 4-й проход): Side для строк, которые ридер
        // достаёт из Terminal.Pins[].Connections. РЕШЕНИЕ ЗАКАЗЧИКА: EPLAN эти
        // соединения вообще не классифицирует, метка наша собственная; выбрано
        // «Int», потому что (а) по смыслу это соединения «в щит», а не в кабель,
        // (б) геометрически все эти выводы верхние — TerminalGeometry.PointSide
        // даёт для них Top, а TerminalConnectionModelBuilder на Top тянет пул Int.
        private const string SIDE_PIN_CONN = "Int";

        public EplanTerminalStripReader(DiagnosticLogger oLogger, string strTargetStripName)
        {
            _log = oLogger;
            _strTargetStripName = strTargetStripName;
        }

        /// <summary>rev.12.2 (H-2c, фикс-волна MAJOR-2): встречается ли полное имя
        /// клеммника на НЕСКОЛЬКИХ страницах проекта ([DMSTRIPDUP] при агрегации
        /// PerStrip в Read()). true — сверка [CROSS] и гейт [CROSSGATE] по имени
        /// ненадёжны (счётчики суммируют дубликаты страниц): расходиться может не
        /// «чужая активная страница», а сам двойник имени — UI-гейт обязан
        /// смягчиться. Читается только после Read() того же экземпляра.</summary>
        public bool TargetNameMultiPage(string strStripName)
        {
            List<string> lstPages;
            return !string.IsNullOrEmpty(strStripName) &&
                _dicStripPages.TryGetValue(strStripName, out lstPages) && lstPages.Count > 1;
        }

        public DmReport Read(Project oProject)
        {
            DmReport oReport = new DmReport();
            Page[] arrPages = oProject.Pages;
            _log.Log("[INFO] --- Data Model: страниц в проекте: " + arrPages.Length + " ---");

            foreach (Page oPage in arrPages)
            {
                TerminalStrip[] arrStrips;
                try { arrStrips = oPage.TerminalStrips; }
                catch (Exception oException)
                {
                    DmErr(oReport, "Page.TerminalStrips ('" + oPage.IdentifyingName + "')", oException);
                    continue;
                }
                if (arrStrips == null || arrStrips.Length == 0) continue;

                foreach (TerminalStrip oStrip in arrStrips)
                {
                    oReport.StripCount++;
                    string strStripName = SafeText("<n/a>", () => oStrip.Name);
                    Terminal[] arrTerminals;
                    try
                    {
                        arrTerminals = oStrip.Terminals;
                        if (arrTerminals == null) arrTerminals = new Terminal[0];
                    }
                    catch (Exception oException)
                    {
                        DmErr(oReport, "TerminalStrip.Terminals ('" + strStripName + "')", oException);
                        arrTerminals = new Terminal[0];
                    }
                    string strDeclared = SafeText("", () =>
                        oStrip.Properties[Properties.TerminalStrip.TERMINALSTRIP_COUNTOFTERMINALS].ToString());
                    _log.Log("[DMSTRIP] '" + strStripName + "' страница '" + oPage.IdentifyingName +
                        "': клемм " + arrTerminals.Length +
                        (strDeclared.Length > 0 ? " (№35006=" + strDeclared + ")" : ""));
                    oReport.TerminalCount += arrTerminals.Length;
                    // rev.16.5-bis (задача 3.1-бис, шаг «части как источник»): части ОУ
                    // читаются ОДИН раз на КЛЕММНИК и кладутся в боковую таблицу
                    // DmReport.StripPartsByDt — это теперь ИСТОЧНИК шкафа и обозначения
                    // для BreakPointResolver.Decide (было: эвристика «последний дефис»
                    // по строке, которая путает дефис в имени шкафа с дефисом в
                    // обозначении). Один раз на клеммник, НЕ на клему: стенд даёт
                    // 504 клемы на 19 клеммников, а часть ОУ — свойство клеммника.
                    // Первый по имени клеммника выигрывает; при повторе (имя на двух
                    // страницах, [DMSTRIPDUP]) значения СОВПАДАЮТ, поэтому
                    // ContainsKey здесь — только чтобы не перетирать объект.
                    // ВНИМАНИЕ, «совпадают» — это НАБЛЮДЕНИЕ стенда 02.10, а не
                    // свойство (rev16.5-bis, ревью): два ОДНОИМЕННЫХ клеммника могут
                    // иметь разные №1400/№1200/№20013/№20014, и тогда первый по имени
                    // молча выигрывает, а второй выбрасывается — решения по этому
                    // клеммнику поедут по частям ДРУГОГО. Заметно это только в [NPT]:
                    // строки [NPT] печатаются на КАЖДОЕ появление имени, а таблица
                    // дедуплицирована (см. предупреждение в [NPTS-SUM] и строке
                    // [DMSTRIPDUP], где печатаются имена страниц).
                    // Заглушка SafeText("<n/a>", …) на НЕПРОЧИТАННОМ имени клеммника в таблицу
                    // частей НЕ кладётся — тот же отсев по '<', что в AddStripEnd для
                    // CableStripEnds (настоящие значения [DM] всегда начинаются с '=').
                    // Отличие только в источнике заглушки: имя клеммника читается одним
                    // SafeText, а части ОУ — другим (NameParts), поэтому при отказе
                    // чтения имени части МОГЛИ БЫ ПРОЧИТАТЬСЯ. Такой ключ Decide
                    // никогда не спросит (arrEnds '<n/a>' не содержит — тот же guard), т.е.
                    // решения не изменятся, но сводка [NPTS-SUM] посчитала бы лишний
                    // клеммник как прочитанный. ПРАВИЛО: имя не прочитано ⇒ ключа нет,
                    // и это честное «частей нет» для Decide — KeyOfDt/DesOfDt уйдут
                    // на разбор строки (фоллбэк).
                    DmStripParts oParts = ReadStripParts(oStrip);
                    bool bPartsKeyOk = !string.IsNullOrEmpty(strStripName) &&
                        strStripName[0] != '<';
                    if (oParts != null && bPartsKeyOk &&
                        !oReport.StripPartsByDt.ContainsKey(strStripName))
                        oReport.StripPartsByDt[strStripName] = oParts;
                    // Тот же объект уходит в диагностику: [NPT] печатает СЕЙЧАС
                    // именно те значения, на которых потом принимается решение
                    // (разойтись они не могут — читает один и тот же метод).
                    DumpStripNameParts(oStrip, strStripName, oParts);

                    // rev.12.2 (H-2c, MAJOR-2): учёт страниц у полного имени — базовое
                    // допущение сверки/гейта «полное имя уникально». Первое появление
                    // имени на второй странице — WARN [DMSTRIPDUP] (один раз на имя,
                    // чтобы не спамить при 3+ страницах).
                    string strPageName = SafeText("<n/a>", () => oPage.IdentifyingName);
                    List<string> lstPages;
                    if (!_dicStripPages.TryGetValue(strStripName, out lstPages))
                    {
                        lstPages = new List<string>();
                        _dicStripPages[strStripName] = lstPages;
                    }
                    if (!lstPages.Contains(strPageName))
                    {
                        lstPages.Add(strPageName);
                        if (lstPages.Count == 2)
                            _log.Warn("[DMSTRIPDUP] имя '" + strStripName + "' встречается на страницах " +
                                lstPages[0] + ", " + lstPages[1] + " — сверка/гейт по имени ненадёжны");
                    }

                    DmStripStats oStats;
                    if (!oReport.PerStrip.TryGetValue(strStripName, out oStats))
                    {
                        oStats = new DmStripStats();
                        oReport.PerStrip[strStripName] = oStats;
                    }
                    oStats.TerminalCount += arrTerminals.Length;
                    List<string> lstNames = new List<string>();
                    int nRowsBefore = oReport.Rows.Count;
                    foreach (Terminal oTerminal in arrTerminals)
                    {
                        lstNames.Add(SafeText("<n/a>", () => oTerminal.Name));
                        int nRowsBeforeTerm = oReport.Rows.Count;
                        ReadTerminal(oReport, oStrip, oTerminal);
                        DumpTerminalLinks(oReport, strStripName, oTerminal, nRowsBeforeTerm);
                    }
                    oReport.StripTerminalNames[strStripName] = lstNames;
                    for (int i = nRowsBefore; i < oReport.Rows.Count; i++)
                    {
                        DmRow oRow = oReport.Rows[i];
                        if (oRow.Side == "Ext") oStats.ExtCount++;
                        else if (oRow.Side == "Int") oStats.IntCount++;
                        else oStats.BridgeCount++;
                    }
                }
            }
            _log.Log("[CBLPROP-SUM] соединений: " + _nCblPropConns + ", свойств: " + _nCblPropValues);
            _log.Log("[PIN-SUM] связей на пинах: " + _nPinConnSeen + ", новых строк [DM]: " +
                _nPinConnNew + ", уже выведено без повтора: " + _nPinConnSkip);
            _log.Log("[JUMPER-SUM] перемычек (уникальных пар): " + _dicBridgePairs.Count +
                " — соседних " + CountPairs(false) + ", дальних " + CountPairs(true) +
                " (в геометрию НЕ попадают: рисует форма)");
            _log.Log("[JUMPER-FAR] потенциальные перемычки целевого клеммника '" +
                _strTargetStripName + "': " + _dicBridgePairsFar.Count + " — " +
                (_dicBridgePairsFar.Count == 0 ? "нет" : string.Join("; ", FarPairsSorted().ToArray())));
            _log.Log("[PROBE5-SUM] соединений: " + _nProbe5Conns + ", direct-ok: " + _nProbe5DirectOk +
                ", cdp-ok: " + _nProbe5CdpOk + ", connprop: " + _nProbe5ConnProps);
            // rev.16.4 (задача 3.1-bis): сколько кабелей получили хотя бы одну строку [DM]
            // с ПОЛНЫМ ОУ клемы — ровно число ключей CableStripEnds, и ровно
            // столько же УНИКАЛЬНЫХ кабелей стоит в логе "[BPE] '<кабель>': <dt>"
            // (grep ... | sed "s/^\[BPE\] '\([^']*\)':.*/\1/" | sort -u | wc -l).
            // ВНИМАНИЕ, инвариант НЕ «строки = ключи»: строка печатается на КАЖДЫЙ
            // новый уникальный DT клемы, поэтому число строк = сумма длин списков
            // и >= числа ключей (равенство только при одной клеме на кабель).
            // Сверять строки с [BPE-SUM] нельзя — на любом многоклемном кабеле это
            // дало бы ложную тревогу.
            // Тег прежнего [CBP-SUM] («кабелей с DT жил») исчез вместе с CableCoreEnds:
            // признак multi больше не считается по №31019/№31020 (см. DmModels.
            // CableStripEnds — почему). Сами 31019/31020 по-прежнему читаются и печатаются
            // в строке [DM] — как диагностика формата, не как источник решения.
            _log.Log("[BPE-SUM] кабелей с концами [DM]: " + oReport.CableStripEnds.Count);
            // rev16.5-bis: сколько УНИКАЛЬНЫХ ОУ клеммников получили части, нужные
            // BreakPointResolver.Decide. Считаем HasAny, а не размер таблицы: ключ с
            // пустыми частями (HasAny == false) в решении не участвует — Decide
            // откатится на разбор строки (KeyOfDt/DesOfDt). Сводка отвечает на
            // вопрос стенда «прочитали ли мы вообще то, чем думаем решить»:
            // 0 при отказе CreateAnyPropertyIdFromNumber/чтении NameParts.
            // ВНИМАНИЕ: это НЕ 19 клеммников стенда автоматически — строк [NPT]
            // тоже по одной на клеммник, но таблица дедуплицирована по имени,
            // а лог [NPT] печатается на КАЖДОЕ появление имени (две страницы).
            // ЗНАМЕНАТЕЛЬ (rev16.5-bis, Д-4): прежних двух чисел не хватало —
            // «прочитали 17 из 17» и «прочитали 17 из 19» выглядели одинаково,
            // т.е. отказ ReadStripParts (null) в сводке был НЕ виден. Теперь их
            // три: (1) клеммников с HasAny, (2) ВСЕГО клеммников в обходе —
            // oReport.StripCount, (3) ключей в таблице. ПОРЯДОК ПЕЧАТИ ПРОВЕРЕН ПО
            // КОДУ: StripCount инкрементируется в начале цикла по клеммникам (на
            // несколько строк выше), а сводка печатается ПОСЛЕ закрытия всех
            // циклов (конец Read) ⇒ значение уже итоговое, отдельный счётчик не
            // нужен. Расхождение (2) и (3) — это НОРМА: дубли имён ([DMSTRIPDUP],
            // дедуп по ContainsKey) и непрочитанные имена (guard по '<' в Read)
            // уменьшают число ключей; расхождение (1) и (2) — уже потерянные
            // чтения, их сводка и ловит.
            _log.Log("[NPTS-SUM] клеммников с частями ОУ: " + CountStripsWithParts(oReport) +
                " из " + oReport.StripCount + " в обходе (ключей прочитано: " +
                oReport.StripPartsByDt.Count + ")");
            _log.Log("[DLV-SUM] дамп связей клемм напечатан для целевого клеммника '" +
                _strTargetStripName + "' — по одной строке [DLV] на клемму");
            return oReport;
        }

        // --- rev.16.5-bis (задача 3.1-бис, шаг «части как источник»): части ОУ клеммника ---

        /// <summary>Чтение частей ОУ клеммника, нужных РЕШЕНИЮ (rev16.5-bis): №1400
        /// «Место сборки», №1200 «Место установки» и обозначение = №20013 (буквенный
        /// код) + №20014 (счётчик) СЛИТНОМ. Основание — стенд 02.10
        /// (spike/StripNamePartsSpike.cs): ОУ восстанавливается из частей на 100 %
        /// (19 из 19), т.е. '=Уст.Уст2++МестоСборки+МестоУстановки#Структура-Обозн'
        /// = {1100}.{1101} ++ {1400} + {1200} #{1600} -{20013}{20014}.
        ///
        /// ПОЧЕМУ ЭТО ЛУЧШЕ РАЗБОРА СТРОКИ: BreakPointResolver.CabinetKeyOf/
        /// DesignationOf вытаскивали те же значения эвристикой «последний дефис»,
        /// а она не различает дефис внутри имени шкафа (безопасно) и дефис внутри
        /// ОБОЗНАЧЕНИЯ (обозначение втягивается в имя шкафа ⇒ настоящий multi
        /// теряется — ограничение Ф-3 в доктрине CabinetKeyOf). По номерам граница
        /// однозначна: поля просто разные.
        ///
        /// ОДИН вызов на клеммник (не на клему) — зовётся из Read до обхода его
        /// клем; результат кладётся в DmReport.StripPartsByDt и печатается в [NPT].
        /// Весь метод обёрнут в try/catch: ОТКАЗ ЧТЕНИЯ НЕ ДОЛЖЕН Ломать обход —
        /// возвращается null, и Decide для этого DT откатывается на разбор строки
        /// (поведение ровно прежнее).
        /// Признак «пара не задана» — "" (не «—»): SafeNamePartText отдаёт EM DASH
        /// как маркер «не задано или не прочитано», а в DmStripParts пустое поле — это
        /// пустая строка (конвертация здесь, в StripPartValue). Исключение при
        /// CreateAnyPropertyIdFromNumber невозможно (метод internal static, внутри
        /// свой try/catch, вернёт null), но весь блок всё равно под try/catch.
        /// Известный и НЕ ПРОВЕРЕННЫЙ на стенде случай: код 20013 пуст, а счётчик
        /// 20014 задан ⇒ обозначение = один счётчик («1»). На стенде 02.10 у всех
        /// 19 клеммников заданы ОБА номера, поэтому этот случай инертен; правилом
        /// он не считается ошибкой — на строке вышло бы так же.</summary>
        private static DmStripParts ReadStripParts(TerminalStrip oStrip)
        {
            try
            {
                // Приведение к Function безопасно: см. п.1 в доктрине ниже.
                Function oFn = oStrip;
                DmStripParts oParts = new DmStripParts();
                oParts.Assembly = StripPartValue(oFn, 1400);
                oParts.Mounting = StripPartValue(oFn, 1200);
                oParts.Designation = StripPartValue(oFn, 20013) + StripPartValue(oFn, 20014);
                return oParts;
            }
            catch { return null; }
        }

        /// <summary>Значение одной части ОУ для DmStripParts: SafeNamePartText (тот же
        /// рабочий путь FunctionBasePropertyList[oId], что и у дампа [NPT]) +
        /// конвертация маркера «—» (U+2014) в «не задано» = "". Тримминга нет — в
        /// значениях ОУ пробелов не бывает, а молчаливый Trim спрятал бы расхождение
        /// с разбором строки, которое заметно в [NPT].
        /// NULL-БЕЗОПАСНОСТЬ (rev16.5-bis): null приводится к "" ЗДЕСЬ, поэтому
        /// конкатенация «20013 + 20014» в ReadStripParts не может склеить null со
        /// значением ('nullX1' в [NPT] и в решении) — тем более что DmStripParts
        /// объявлен публичным контейнером с public-полями.</summary>
        private static string StripPartValue(Function oFn, int nNumber)
        {
            string strValue = SafeNamePartText(oFn,
                CableSymbolCreator.CreateAnyPropertyIdFromNumber(nNumber));
            return strValue == null || strValue == "—" ? "" : strValue;
        }

        /// <summary>Число ключей StripPartsByDt с непустыми частями (HasAny) — сводка
        /// [NPTS-SUM]. Перебор вручную: LINQ в проекте нет.</summary>
        private static int CountStripsWithParts(DmReport oReport)
        {
            int nCount = 0;
            foreach (KeyValuePair<string, DmStripParts> oPair in oReport.StripPartsByDt)
                if (oPair.Value != null && oPair.Value.HasAny) nCount++;
            return nCount;
        }

        /// <summary>ДИАГНОСТИКА (не решение): одна строка [NPT] на клеммник — значимые
        /// части ОУ. rev16.5: строка читала свойства САМА; rev16.5-bis — значимые
        /// поля (№1200/№1400) берёт из УЖЕ ПРОЧИТАННОГО DmStripParts, то есть из
        /// тех же значений, на которых потом принимается решение (иначе диагностика
        /// и боевая логика разошлись бы, и расхождение не было бы видно нигде).
        /// Те же семь ячеек с теми же именами печатаются в порядке rev16.5 (НЕ
        /// закоммиченного — вся диагностика [NPT] нова в этом же диффе, поэтому
        /// слово «прежние» было неточным: прежними были только ячейки rev16.5),
        /// NPdes добавлена в раунде rev16.5-bis и печатается справа; ячейки
        /// NP1100/NP1101/NP1600/NP20013/NP20014 дочитываются здесь
        /// (SafeNamePartText по номерам), т.к. в решении они не участвуют.
        /// ВНИМАНИЕ, ПОЧЕМУ 1100/1101/1600/20013/20014 НЕ добавлены в DmStripParts
        /// (выбор сделан осознанно): нужны только для печати [NPT]. Ключ шкафа =
        /// №1400+№1200, обозначение = №20013+№20014 — больше ничего. Хранить в
        /// DmStripParts лишние номера означало бы раздуть контейнер полями, которые
        /// никто не читает, и создать соблазн «собрать из них полное ОУ» (это другая
        /// задача: NameParts собирает его по ПОЗИЦИЯМ прототипа, а не по номерам).
        /// Отсюда честная цена: 20013/20014 печатаются повторным чтением. Расхождение
        /// повторного чтения со слитым значением, на котором принято решение, было
        /// бы БЕЗОБРАЗНО видно: оно печатается отдельной ячейкой NPdes справа.
        /// ПОДТВЕРЖДЕНО ПО КОДУ (rev16.5-bis, Д-5): решение питается ТЕМ ЖЕ
        /// объектом DmStripParts, который напечатан как NPdes — его собирает
        /// ReadStripParts (один вызов на клеммник), а SafeNamePartText в
        /// DumpStripNameParts нужен ИСКЛЮЧИТЕЛЬНО ради ячеек NP20013/NP20014; в
        /// Decide (BreakPointResolver) эти номера не читаются вовсе, только
        /// oParts.Designation. Расходиться они не могут по двум причинам: (1)
        /// DumpStripNameParts получает тот же oParts из того же вызова Read
        /// (переменная передаётся параметром, а не перечитывается), (2) проход
        /// Data Model ничего не пишет в проект (только чтение), поэтому между
        /// двумя чтениями свойства измениться не могут. Если бы равенство всё же
        /// нарушилось (другая сборка, чужой писатель), это видно МГНОВЕННО и без
        /// сравнения: NP20013/NP20014 печатаются вплотную к NPdes справа.
        /// oParts == null — чтение не удалось (ReadStripParts вернул null): печатаем
        /// '&lt;не прочитаны&gt;' в значимых ячейках, НЕ маскируя отказ значениями
        /// по умолчанию — по такому логу видно, что решение по этому клеммнику
        /// пойдёт по разбору строки (фоллбэк), а не по частям.
        /// Основание самого пути чтения — проба spike/StripNamePartsSpike.cs
        /// (стенд 02.10, прогоны 17:14/17:47), три вывода:
        ///   1) TerminalStrip — потомок Function (цепочка TerminalStrip ← Function ←
        ///      FunctionBase ← SymbolReference ← Placement ← StorableObject), поэтому
        ///      Function oFn = oStrip; компилируется и отражение не нужно;
        ///   2) ExistingIds у клеммника содержит 1100/1101/1400/1600/20013/20014
        ///      (у части клеммников ещё 1200) — то есть эти части заданы и читаются;
        ///   3) индексатор FunctionBasePropertyList НЕ виден рефлексии (проба вернула
        ///      &lt;InvalidOperationException&gt; на все 90 строк [PART]) — читать можно
        ///      только компилируемым oParts[oId], как здесь.
        /// Поля из oParts печатаются «как есть» ("" ⇒ пустые кавычки), а не «—»:
        /// пустая пара — это «не задано», и в этом логе её видно как пустое
        /// значение, а не как отказ чтения. «—» остаётся маркером только там, где
        /// чтения не было вовсе (SafeNamePartText).</summary>
        private void DumpStripNameParts(TerminalStrip oStrip, string strStripName,
            DmStripParts oParts)
        {
            try
            {
                // Приведение к Function безопасно: см. п.1 в доктрине выше.
                Function oFn = oStrip;
                StringBuilder oDump = new StringBuilder();
                oDump.Append("[NPT] '" + strStripName + "'");
                // Не в решении, только диагностика — см. доктрину метода.
                oDump.Append(" NP1100='" + SafeNamePartText(oFn, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1100)) + "'");
                oDump.Append(" NP1101='" + SafeNamePartText(oFn, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1101)) + "'");
                if (oParts == null)
                {
                    // Отказ чтения: все четыре значимых поля — «не прочитаны».
                    oDump.Append(" NP1200='<не прочитаны>' NP1400='<не прочитаны>'" +
                        " NP20013='<не прочитаны>' NP20014='<не прочитаны>'");
                }
                else
                {
                    // NP1200/NP1400 — из oParts (те самые значения, что поедут в
                    // решение), печатаются «как есть»: "" ⇒ пустые кавычки.
                    oDump.Append(" NP1200='" + Dt(oParts.Mounting) + "'");
                    oDump.Append(" NP1400='" + Dt(oParts.Assembly) + "'");
                }
                oDump.Append(" NP1600='" + SafeNamePartText(oFn, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1600)) + "'");
                if (oParts != null)
                {
                    // NP20013/NP20014 печатаются РАЗДЕЛЬНО теми же номерами: формат
                    // лога не меняется (семь ячеек с теми же именами), и видно,
                    // где код, где счётчик. Это ПОВТОРНОЕ чтение только ради печати:
                    // в решении участвует слитое значение из oParts, и оно
                    // печатается отдельной ячейкой NPdes справа — расхождение
                    // повторного чтения со слитым было бы сразу видно в логе, а не
                    // спряталось бы в решении.
                    oDump.Append(" NP20013='" + SafeNamePartText(oFn, CableSymbolCreator.CreateAnyPropertyIdFromNumber(20013)) + "'");
                    oDump.Append(" NP20014='" + SafeNamePartText(oFn, CableSymbolCreator.CreateAnyPropertyIdFromNumber(20014)) + "'");
                    oDump.Append(" NPdes='" + Dt(oParts.Designation) + "'");
                }
                else
                {
                    oDump.Append(" NP20013='<не прочитаны>' NP20014='<не прочитаны>'" +
                        " NPdes='<не прочитаны>'");
                }
                _log.Log(oDump.ToString());
            }
            catch (Exception oException)
            {
                // Ошибку НЕ в DmErr: это диагностика, счётчики ErrCount трогать нельзя.
                _log.Log("[NPTERR] '" + strStripName + "': " + oException.GetType().Name +
                    ": " + oException.Message);
            }
        }

        /// <summary>Ячейка [NPT]: пустое значение — пустые кавычки (''), чтобы в логе
        /// было видно «поле не задано» и нельзя было спутать с отказом чтения
        /// ('&lt;не прочитаны&gt;' в доктрине выше). УЖЕ null-безопасен (strValue ?? ""),
        /// и это правильно: вызывается в том числе на public-поля DmStripParts,
        /// которые будущий код может занулить; пустая ячейка в логе тогда читается
        /// как «поле не задано», а падение — как отказ всего дампа.</summary>
        private static string Dt(string strValue)
        {
            return strValue ?? "";
        }

        private void ReadTerminal(DmReport oReport, TerminalStrip oStrip, Terminal oTerminal)
        {
            string strStripName = SafeText("<n/a>", () => oStrip.Name);
            string strTermName = SafeText("<n/a>", () => oTerminal.Name);

            // Дедупликация 4-го прохода: всё, что уже ушло в модель по ЭТОЙ клемме,
            // — по ссылке (lstEmitted) и по строковому ключу 31019->31020 (lstKeys).
            List<Connection> lstEmitted = new List<Connection>();
            List<string> lstKeys = new List<string>();

            try
            {
                Terminal.ConnectionInfo[] arrExt = oTerminal.ExternalConnections;
                if (arrExt != null)
                    foreach (Terminal.ConnectionInfo oInfo in arrExt)
                        AddConnRow(oReport, strStripName, strTermName, "Ext", oInfo, lstEmitted, lstKeys);
            }
            catch (Exception oException) { DmErr(oReport, "ExternalConnections ('" + strTermName + "')", oException); }

            try
            {
                Terminal.ConnectionInfo[] arrInt = oTerminal.InternalConnections;
                if (arrInt != null)
                    foreach (Terminal.ConnectionInfo oInfo in arrInt)
                        AddConnRow(oReport, strStripName, strTermName, "Int", oInfo, lstEmitted, lstKeys);
            }
            catch (Exception oException) { DmErr(oReport, "InternalConnections ('" + strTermName + "')", oException); }

            try
            {
                Terminal.Bridge[] arrBridges = oTerminal.Bridges;
                if (arrBridges != null)
                {
                    foreach (Terminal.Bridge oBridge in arrBridges)
                    {
                        if (oBridge == null || oBridge.BridgeSegments == null) continue;
                        foreach (Terminal.Bridge.BridgeInfo oSegment in oBridge.BridgeSegments)
                        {
                            DmRow oRow = new DmRow();
                            oRow.StripName = strStripName;
                            oRow.TerminalName = strTermName;
                            oRow.Side = "Bridge";
                            oRow.HasConn = oSegment.Conn != null;
                            // PeerName ДО FillCable (fix round 1): лог [CBP] внутри
                            // FillCable печатает для моста peer=<PeerName> — раньше
                            // поле заполнялось после вызова, и в [CBP] всегда было
                            // peer=?. Перестановка безопасна: FillCable читает только
                            // StripName/TerminalName/Side/ConnectionName/PeerName, и
                            // Bridge до FillCable их уже не меняет.
                            oRow.PeerName = oSegment.BridgedTerminal != null
                                ? SafeText("<n/a>", () => oSegment.BridgedTerminal.Name)
                                : null;
                            if (oRow.Side == "Bridge")   // всегда true в этом цикле, но пусть будет явно
                            {
                                // -1 = не распознан: пара в счётчики НЕ идёт (неизвестное — не «дальняя»)
                                int nMe = AnchorResolver.ParseTerminalNumber(strTermName ?? "");
                                int nPeer = AnchorResolver.ParseTerminalNumber(oRow.PeerName ?? "");
                                if (nMe >= 0 && nPeer >= 0)
                                {
                                    // Ключ пары — по возрастанию номера клеммы: та же идея, что
                                    // в MatchBuilder ([BRIDGE], сравнение по Ordinal), только по
                                    // числу — в пределах клеммника номера уникальны.
                                    int nLo = nMe < nPeer ? nMe : nPeer;
                                    int nHi = nMe < nPeer ? nPeer : nMe;
                                    string strPair = NumOf(nLo) + "↔" + NumOf(nHi);
                                    if (!_dicBridgePairs.ContainsKey(strPair))
                                        _dicBridgePairs[strPair] = nHi - nLo <= 1 ? 0 : 1;
                                    // Список [JUMPER-FAR] подписан «целевого клеммника» —
                                    // значит и набирать его надо только по нему. Общее число
                                    // дальних пар по проекту уже есть в [JUMPER-SUM]
                                    // (CountPairs(true)), так что фильтр ничего не теряет,
                                    // зато подпись перестаёт врать на проекте, где дальняя
                                    // перемычка есть и у другого клеммника.
                                    if (nHi - nLo > 1 && strStripName == _strTargetStripName
                                        && !_dicBridgePairsFar.ContainsKey(strPair))
                                        _dicBridgePairsFar[strPair] = 1;
                                }
                            }
                            if (oSegment.Conn != null)
                            {
                                FillCable(oReport, oRow, oSegment.Conn);
                                lstEmitted.Add(oSegment.Conn);
                                lstKeys.Add(ConnKey(oSegment.Conn));
                            }
                            Emit(oReport, oRow);
                        }
                    }
                }
            }
            catch (Exception oException) { DmErr(oReport, "Bridges ('" + strTermName + "')", oException); }

            // --- 4-й проход: связи, висящие на ПИНАХ клеммы ---
            //
            // ЗАЧЕМ (четвёртая дверь). Первые три прохода — это внешние, внутренние
            // и мостовые связи, которые EPLAN отдаёт САМ. Но Terminal.
            // ExternalConnections/InternalConnections НЕ отдают связи, у которых
            // второй конец — вывод прибора без ОУ (только имя пина), и ридер
            // поэтому терял их наглухо. Лечатся только через Pins[].Connections.
            //
            // ДОКАЗАТЕЛЬСТВО (стенд 03.10 13:23, клеммник '=++ШОБ+#-XT1.1',
            // 69 клемм): связей на пинах 117 (сумма по 276 пинам), строк [DM] было
            // Ext=34 + Int=37 + Bridge=38 = 109. Разница 8 — ровно по одной на
            // клеммы :42, :44, :46, :48, :51, :53, :55, :57, то есть связи +EB1,
            // -EB1, +EB2, -EB2, +EB3, +EB3, +EB4, +EB4. Клемм, где строк БОЛЬШЕ,
            // чем связей на пинах, НОЛЬ — значит три старых прохода дают строгое
            // подмножество, двойного чтения до правки не было и быть не могло.
            // Ожидаемый результат: 117 строк (Ext=34, Int=45, Bridge=38),
            // Ext+Int = 79 = числу геометрических точек, гейт [CROSSGATE] проходит.
            //
            // ПОЧЕМУ Side = SIDE_PIN_CONN ("Int"), А НЕ "Ext"/"Bridge". EPLAN эти
            // соединения вообще не классифицирует — метка наша собственная.
            // Заказчик выбрал Int: (а) по смыслу это соединения «в щит», а не в
            // кабель; (б) геометрически все эти выводы верхние — PointSide даёт
            // для них Top, а TerminalConnectionModelBuilder на Top тянет пул Int.
            // Поэтому же метка не мешает и сверке [CROSSGATE]: Ext+Int теперь
            // полны, а Bridge не сдвинулся.
            //
            // ДЕДУПЛИКАЦИЯ ДВУХУРОВНЕВАЯ, потому что неизвестно, отдаёт ли EPLAN
            // при повторном обращении ТОТ ЖЕ экземпляр Connection. Уровень 1 —
            // ReferenceEquals по списку уже выведенных связей: если EPLAN отдаёт
            // тот же объект, попадём сюда. Уровень 2 — строковый ключ
            // "31019->31020" (ConnKey): если EPLAN отдаёт новый объект с теми же
            // данными, попадём сюда. Совпадение ЛЮБОГО из двух уровней означает
            // «уже выведено» — иначе на клемме с двумя пинами, соединёнными между
            // собой, одна связь дала бы две строки. Ключ специально НЕ Connection.
            // Name: в этом репозитории он встречается только в докстрингах и не
            // проверен компиляцией, а SafeConnPropText проверен. Одинаковый ключ
            // = одна и та же связь по данным EPLAN.
            //
            // Порядок: [PINROW] печатается ДО Emit, чтобы новая строка была
            // видна отдельной строкой лога и не слилась с последующей [DM].
            // Счётчики _nPinConnSeen/New/Skip — общие на весь обход, печатаются
            // в [PIN-SUM]; инвариант Seen = New + Skip.
            try
            {
                Pin[] arrPins = oTerminal.Pins;
                if (arrPins != null)
                    for (int nPin = 0; nPin < arrPins.Length; nPin++)
                    {
                        Pin oPin = arrPins[nPin];
                        if (oPin == null) continue;
                        System.Array arrConns = oPin.Connections as System.Array;
                        if (arrConns == null) continue;
                        for (int nC = 0; nC < arrConns.Length; nC++)
                        {
                            Connection oConn = arrConns.GetValue(nC) as Connection;
                            if (oConn == null) continue;
                            _nPinConnSeen++;
                            string strKey = ConnKey(oConn);
                            bool bAlready = lstKeys.Contains(strKey);
                            for (int nE = 0; !bAlready && nE < lstEmitted.Count; nE++)
                                if (object.ReferenceEquals(lstEmitted[nE], oConn)) bAlready = true;
                            if (bAlready) { _nPinConnSkip++; continue; }
                            _nPinConnNew++;

                            DmRow oRow = new DmRow();
                            oRow.StripName = strStripName;
                            oRow.TerminalName = strTermName;
                            oRow.Side = SIDE_PIN_CONN;
                            oRow.HasConn = true;
                            oRow.ConnectionName = strKey;
                            oRow.PinName = SafeText("", () => oPin.Designation);
                            oRow.PinIndex = nPin;
                            FillCable(oReport, oRow, oConn);
                            lstEmitted.Add(oConn);
                            lstKeys.Add(strKey);
                            _log.Log("[PINROW] " + strStripName + "/" + strTermName + " pin" +
                                nPin + " conn=" + strKey);
                            Emit(oReport, oRow);
                        }
                    }
            }
            catch (Exception oException)
            {
                DmErr(oReport, "Pins[].Connections ('" + strTermName + "')", oException);
            }
        }

        /// <summary>ЧИСТО ДИАГНОСТИЧЕСКИЙ дамп связей одной клеммы целевого
        /// клеммника — строка [DLV]. ПОВЕДЕНИЕ НЕ МЕНЯЕТ: читает только, ничего
        /// не пишет в DmReport и ни на что не влияет; выход по нецелевому
        /// клеммнику — сразу, чтобы дамп печатался ровно для одного DT.
        ///
        /// ЗАЧЕМ (стенд 03.10 12:01): гейт [CROSSGATE] упал — геометрия дала 79
        /// точек подключения, а в DataModel у '=++ШОБ+#-XT1.1' Ext+Int=71; восемь
        /// «лишних» верхних выводов пришлись на клеммы 42,44,46,48,51,53,55,57,
        /// для которых ридер не создал НИ ОДНОЙ строки DmRow. Вопрос, который
        /// закрывает дамп: EPLAN ВООБЩЕ не отдаёт по этим клеммам связей (тогда
        /// правка в ридере не поможет — нет данных), EPLAN отдаёт, а ридер их
        /// теряет (правка в ридере), либо связей в проекте нет и неверно само
        /// ожидание гейта. Ответ даёт сверка трёх чисел слева (сколько ЭЛЕМЕНТОВ
        /// пришло от EPLAN по каждой из трёх коллекций) с числами справа (сколько
        /// из них ридер превратил в строки [DM]).
        ///
        /// ЧТО ПЕЧАТАЕТ: level — FUNC_TERMINALLEVEL (KB: eplan.help API 2.9,
        /// FunctionPropertyList.FUNC_TERMINALLEVEL = 20034; «0» = одноуровневая
        /// клемма, иначе номер уровня многоуровневой) — прямой признак, что клемма
        /// является под-клеммой; parent — ParentFunction != null (у главной функции
        /// возвращает NULL по документации); main — Terminal.IsMainTerminal
        /// («Determines whether terminal is main») — в связке с parent отличает
        /// под-клемму от главной клеммы; pins — длина Function.Pins («массив
        /// точек подключения функции»); разбор пинов pinN:'обозначение'/направление/
        /// c=N{31019->31020} идёт сразу за pins= и печатает Designation и Direction
        /// каждого пина и СПИСОК его Pin.Connections («Returns connections which start
        /// or end at this connection point») с 31019/31020 каждого соединения через
        /// SafeConnPropText. ЭТО ПРОВЕРКА ГИПОТЕЗЫ, а не украшение: connections могут
        /// висеть на ПИНАХ клеммы, и тогда ни ExternalConnections, ни
        /// InternalConnections их не отдают (стенд 03.10 12:36 — у клемм :42,:44,:46,:48
        /// ext=0 int=0, при том что связь к +EB1 подтверждена пользователем в
        /// навигаторе соединений EPLAN и висит на нашей клемме). Ненулевое c= при
        /// нулевых ext/int подтвердит гипотезу, нули везде — опровергнут её;
        /// ext/int/bridges/сегментов — длины ExternalConnections,
        /// InternalConnections, Bridges и BridgeSegments по всем мостам. Сегменты
        /// мостов печатаются списком (peer + наличие Conn), потому что Bridges по
        /// документации покрывает «внутренние, внешние и малые (jumper) перемычки» —
        /// их вклад в 8 «лишних» выводов может быть неочевиден.
        ///
        /// ЧТЕНИЕ СТРОК [DM]: nRowsExt/nRowsInt/nRowsBr считаются по
        /// oReport.Rows начиная с nRowsBefore, который вызывающий передаёт как
        /// число строк ДО ReadTerminal ТЕКУЩЕЙ клеммы, — то есть это вклад
        /// ровно этой клеммы, а не накопительная сумма по клеммнику.
        /// Слева ext/int/bridges/сегментов — тоже по этой клемме, поэтому обе
        /// половины строки сопоставимы напрямую.
        ///
        /// ВСЕ ЧТЕНИЯ ОБЁРНУТЫ: любой отказ (BaseException, IncorrectObjectTypeException
        /// на чтении невозможен, но подстраховка) даёт -1 и НЕ прерывает обход
        /// клеммника — дамп не должен ломать чтение.</summary>
        private void DumpTerminalLinks(DmReport oReport, string strStripName,
            Terminal oTerminal, int nRowsBefore)
        {
            if (strStripName != _strTargetStripName) return;
            string strTermName = SafeText("<n/a>", () => oTerminal.Name);

            string strLevel = SafeAnyProp(oTerminal, PROP_TERMINAL_LEVEL);

            bool bHasParent = false;
            try { bHasParent = oTerminal.ParentFunction != null; }
            catch { bHasParent = false; }

            bool bMainTerm = false;
            try { bMainTerm = oTerminal.IsMainTerminal; }
            catch { bMainTerm = false; }

            int nPins = -1;
            try
            {
                object oPinsObj = oTerminal.Pins;
                System.Array arrPins = oPinsObj as System.Array;
                if (arrPins != null) nPins = arrPins.Length;
            }
            catch { }

            int nExt = -1, nInt = -1, nBridges = -1, nSegments = 0;
            try { Terminal.ConnectionInfo[] arrExt = oTerminal.ExternalConnections; nExt = arrExt == null ? -1 : arrExt.Length; }
            catch { nExt = -1; }
            try { Terminal.ConnectionInfo[] arrInt = oTerminal.InternalConnections; nInt = arrInt == null ? -1 : arrInt.Length; }
            catch { nInt = -1; }

            StringBuilder oSegDump = new StringBuilder();
            try
            {
                Terminal.Bridge[] arrBridges = oTerminal.Bridges;
                nBridges = arrBridges == null ? -1 : arrBridges.Length;
                if (arrBridges != null)
                    for (int nB = 0; nB < arrBridges.Length; nB++)
                    {
                        if (arrBridges[nB] == null || arrBridges[nB].BridgeSegments == null) continue;
                        Terminal.Bridge.BridgeInfo[] arrSegs = arrBridges[nB].BridgeSegments;
                        for (int nS = 0; nS < arrSegs.Length; nS++)
                        {
                            nSegments++;
                            string strPeer = arrSegs[nS].BridgedTerminal == null
                                ? "<null>"
                                : SafeText("<err>", () => arrSegs[nS].BridgedTerminal.Name);
                            string strConn = arrSegs[nS].Conn == null ? "<null>" : "ok";
                            oSegDump.Append(" seg" + nB + "." + nS + ":peer=" + strPeer +
                                ",conn=" + strConn);
                        }
                    }
            }
            catch { }

            StringBuilder oPinDump = new StringBuilder();
            try
            {
                Pin[] arrPins = oTerminal.Pins;
                if (arrPins != null)
                    for (int nP = 0; nP < arrPins.Length; nP++)
                    {
                        Pin oPin = arrPins[nP];
                        string strDesig = oPin == null ? "<null>"
                            : SafeText("<err>", () => oPin.Designation);
                        string strDir = oPin == null ? "-" : SafeText("?", () => oPin.Direction.ToString());
                        int nPinConns = -1;
                        oPinDump.Append(" pin" + nP + ":'" + strDesig + "'/" + strDir + "/c=");
                        System.Array arrConns = oPin == null ? null : (oPin.Connections as System.Array);
                        if (arrConns != null)
                        {
                            nPinConns = arrConns.Length;
                            oPinDump.Append(nPinConns.ToString(CultureInfo.InvariantCulture));
                            for (int nC = 0; nC < arrConns.Length; nC++)
                            {
                                Connection oConn = arrConns.GetValue(nC) as Connection;
                                string strSrc = oConn == null ? "<null>" : SafeConnPropText(oConn, 31019);
                                string strDst = oConn == null ? "<null>" : SafeConnPropText(oConn, 31020);
                                oPinDump.Append("{" + (strSrc ?? "—") + "->" + (strDst ?? "—") + "}");
                            }
                        }
                        else
                        {
                            oPinDump.Append("err");
                        }
                    }
            }
            catch (Exception oException)
            {
                oPinDump.Append(" pinerr=" + oException.GetType().Name);
            }

            int nRowsExt = 0, nRowsInt = 0, nRowsBr = 0;
            for (int i = nRowsBefore; i < oReport.Rows.Count; i++)
            {
                if (oReport.Rows[i].Side == "Ext") nRowsExt++;
                else if (oReport.Rows[i].Side == "Int") nRowsInt++;
                else nRowsBr++;
            }

            _log.Log("[DLV] '" + strTermName + "' level=" + strLevel +
                " parent=" + (bHasParent ? "да" : "нет") +
                " main=" + (bMainTerm ? "да" : "нет") +
                " pins=" + nPins.ToString(CultureInfo.InvariantCulture) +
                "|" + oPinDump.ToString() +
                " ext=" + nExt.ToString(CultureInfo.InvariantCulture) +
                " int=" + nInt.ToString(CultureInfo.InvariantCulture) +
                " bridges=" + nBridges.ToString(CultureInfo.InvariantCulture) +
                " сегментов=" + nSegments.ToString(CultureInfo.InvariantCulture) +
                " | строк [DM]: Ext=" + nRowsExt.ToString(CultureInfo.InvariantCulture) +
                " Int=" + nRowsInt.ToString(CultureInfo.InvariantCulture) +
                " Bridge=" + nRowsBr.ToString(CultureInfo.InvariantCulture) +
                " |" + oSegDump.ToString());
        }

        /// <summary>Строка [DM] для одного ConnectionInfo (проходы Ext и Int) +
        /// регистрация связи в списках дедупликации 4-го прохода. lstEmitted и
        /// lstKeys накапливают ВСЁ, что уже ушло в модель по этой клемме (Ext,
        /// Int, мосты), чтобы 4-й проход не вывел то же самое второй раз.
        /// Регистрация делается сразу после HasConn — до FillCable/Emit: если
        /// FillCable бросит, связь всё равно уже засчитана за этот проход, иначе
        /// 4-й проход вывел бы её повторно.</summary>
        private void AddConnRow(DmReport oReport, string strStrip, string strTerm, string strSide,
            Terminal.ConnectionInfo oInfo, List<Connection> lstEmitted, List<string> lstKeys)
        {
            DmRow oRow = new DmRow();
            oRow.StripName = strStrip;
            oRow.TerminalName = strTerm;
            oRow.Side = strSide;
            oRow.HasConn = oInfo.Conn != null;
            if (oInfo.Conn != null) { lstEmitted.Add(oInfo.Conn); lstKeys.Add(ConnKey(oInfo.Conn)); }
            oRow.ConnectionName = SafeText("<n/a>", () => oInfo.ConnectionName);
            oRow.PinName = SafeText("", () => oInfo.FunctionPinName);
            oRow.PinIndex = SafeInt(-1, () => oInfo.PinIndex);
            if (oInfo.Conn != null) FillCable(oReport, oRow, oInfo.Conn);
            Emit(oReport, oRow);
        }

        /// <summary>Ключ связи для дедупликации 4-го прохода: «31019->31020».
        /// Читается проверенным SafeConnPropText; null (свойство не задано или
        /// отказ чтения) заменяется на «—», чтобы ключ никогда не был null и
        /// сравнение строк работало. Одинаковый ключ = одна и та же связь по
        /// данным EPLAN. Connection.Name как ключ НЕ используется осознанно (не
        /// проверен компиляцией в этом репозитории).</summary>
        private static string ConnKey(Connection oConn)
        {
            if (oConn == null) return "<null>";
            string strSrc = SafeConnPropText(oConn, 31019);
            string strDst = SafeConnPropText(oConn, 31020);
            return (strSrc ?? "—") + "->" + (strDst ?? "—");
        }

        /// <summary>Номер клеммы строкой для списка дальних перемычек [JUMPER-FAR].
        /// Отрицательный (не распознан) печатается как «?» — в [_JUMPER-FAR] такая
        /// пара не попадает, но подстраховка от «-1» в логе читается лучше.
        /// Инвариантная культура, как и во всех прочих числах этого файла.</summary>
        private static string NumOf(int n)
        {
            return n < 0 ? "?" : n.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Число УНИКАЛЬНЫХ пар перемычек: bFar=false — соседних (|Δ| ≤ 1),
        /// bFar=true — дальних (потенциальных). Считает по _dicBridgePairs, где
        /// значение 0 = соседняя, 1 = дальняя; ключ уже нормализован «min↔max»,
        /// поэтому каждая физическая перемычка учтена ровно один раз, несмотря на
        /// то что Terminal.Bridges отдаёт её с обоих концов. Инвариант:
        /// CountPairs(false) + CountPairs(true) == _dicBridgePairs.Count.</summary>
        private int CountPairs(bool bFar)
        {
            int n = 0;
            foreach (KeyValuePair<string, int> oKv in _dicBridgePairs)
                if ((oKv.Value == 1) == bFar) n++;
            return n;
        }

        /// <summary>Список дальних пар «min↔max» для [JUMPER-FAR], отсортированный
        /// по Ordinal — чтобы лог был детерминированным (Dictionary сам не
        /// упорядочен, а по номеру клеммы «10» встал бы раньше «9»).
        /// Копия в новый список: сортировать Keys напрямую нельзя.</summary>
        private List<string> FarPairsSorted()
        {
            List<string> lst = new List<string>(_dicBridgePairsFar.Keys);
            lst.Sort(StringComparer.Ordinal);
            return lst;
        }

        private void Emit(DmReport oReport, DmRow oRow)
        {
            oReport.Rows.Add(oRow);
            string strCable = oRow.CableName == null ? "<провод>" : oRow.CableName;
            string strProbe = " | cdp=" + FmtCdp(oRow.CdpCount) +
                " | 31058=c:" + FmtBool(oRow.IsCableConn) + " d:" + FmtBool(oRow.IsCableCdp);
            // Проба rev.16.3: №31019/№31020 печатаются в ОБЕИХ ветках [DM],
            // включая Bridge: bridge-сегмент с Conn!=null тоже проходит через
            // FillCable (строка вызова в ветке мостов), поэтому значения
            // заполнены реально. Раньше (коммит 7116917) bridge-ветка их не
            // печатала — соединения мостов выпадали из гейта шага 5 (неверный
            // «нет» по п.1 → неверная развилка).
            string strSrcDest = " | 31019='" + (oRow.CableSource ?? "—") +
                "' 31020='" + (oRow.CableDest ?? "—") + "'";
            if (oRow.Side == "Bridge")
            {
                _log.Log("[DM] " + oRow.TerminalName + " | Bridge | ->" + (oRow.PeerName ?? "?") +
                    " | conn=" + (oRow.HasConn ? "yes" : "no") + " | cable=" + strCable +
                    strProbe + strSrcDest);
            }
            else
            {
                _log.Log("[DM] " + oRow.TerminalName + " | " + oRow.Side + " | " +
                    oRow.ConnectionName + " | " + oRow.PinName + " | pin=" + oRow.PinIndex +
                    " | cable=" + strCable + strProbe + strSrcDest);
            }
            if (oRow.CableName != null)
            {
                int nCount;
                if (oReport.CableWireCounts.TryGetValue(oRow.CableName, out nCount))
                    oReport.CableWireCounts[oRow.CableName] = nCount + 1;
                else
                {
                    oReport.CableWireCounts[oRow.CableName] = 1;
                    _log.Log("[DMCABLE] кабель '" + oRow.CableName + "': первое обнаружение");
                }
            }
        }

        // --- Кабель (rev.5.0): ConnectionDefPoints -> CDP.CableDefinitionLine, №31058 ---

        private readonly Dictionary<Connection, CableInfo> _dicCableInfo =
            new Dictionary<Connection, CableInfo>();

        /// <summary>Проба кабеля соединения (результат кэшируется на прогон). Основной
        /// путь — CDP: Connection.ConnectionDefPoints → ConnectionDefinitionPoint.
        /// CableDefinitionLine (KB: Connection.CableDefinitionLine напрямую бросает
        /// BaseException при ≠1 CDP — причина «кабелей 0» в rev.4.x). Прямое свойство
        /// используется как запасной путь, его исключение НЕ логируется (ожидаемо).
        /// №31058 читается и на Connection, и на CDP: docs помечает его legacy
        /// («connection points determine»), но в GUI проекта это «Принадлежность».</summary>
        private void FillCable(DmReport oReport, DmRow oRow, Connection oConn)
        {
            CableInfo oInfo;
            if (!_dicCableInfo.TryGetValue(oConn, out oInfo))
            {
                oInfo = new CableInfo();
                try
                {
                    ConnectionDefinitionPoint[] arrCdp = oConn.ConnectionDefPoints;
                    oInfo.CdpCount = arrCdp == null ? 0 : arrCdp.Length;
                    if (arrCdp != null)
                    {
                        foreach (ConnectionDefinitionPoint oCdp in arrCdp)
                        {
                            if (oInfo.CableName == null)
                            {
                                try
                                {
                                    Cable oCable = oCdp.CableDefinitionLine;
                                    if (oCable != null) oInfo.CableName = SafeText("<n/a>", () => oCable.Name);
                                }
                                catch (Exception oException)
                                {
                                    DmErr(oReport, "CDP.CableDefinitionLine", oException);
                                }
                            }
                            if (oInfo.IsCableCdp == null) oInfo.IsCableCdp = ReadIsCable31058(oCdp);
                            if (oInfo.CableName != null && oInfo.IsCableCdp != null) break;
                        }
                    }
                }
                catch (Exception oException)
                {
                    oInfo.CdpCount = -1;
                    DmErr(oReport, "ConnectionDefPoints", oException);
                }
                if (oInfo.CableName == null)
                {
                    try
                    {
                        Cable oCable = oConn.CableDefinitionLine;
                        if (oCable != null) oInfo.CableName = SafeText("<n/a>", () => oCable.Name);
                    }
                    catch { /* ожидаемо при ≠1 CDP — основной путь выше */ }
                }
                oInfo.IsCableConn = ReadIsCable31058(oConn);
                // Проба rev.16.3 (задача 0): что фактически лежит в №31019/№31020
                // соединения жилы. Только чтение и лог — логики классификации нет.
                oInfo.CableSource = SafeConnPropText(oConn, 31019);
                oInfo.CableDest = SafeConnPropText(oConn, 31020);

                // [CBP] строка на каждое НЕПУСТОЕ значение — только здесь, в ветке
                // первого чтения соединения (дальше значения из кэша CableInfo).
                // Идентификатор соединения: у Ext/Int заполнен ConnectionName, у
                // Bridge он НЕ присваивается никогда (строка 190 — HasConn, но не
                // ConnectionName) → без fallback строки мостов печатали conn=''
                // и не коррелировались ни с чем.
                if (oInfo.CableSource != null || oInfo.CableDest != null)
                    _log.Log("[CBP] '" + oRow.TerminalName + "' conn='" +
                        (oRow.ConnectionName ?? ("peer=" + (oRow.PeerName ?? "?"))) +
                        "' side=" + oRow.Side + " cable=" + (oInfo.CableName ?? "<провод>") +
                        " 31019='" + (oInfo.CableSource ?? "—") + "'" +
                        " 31020='" + (oInfo.CableDest ?? "—") + "'");

                if (oInfo.CdpCount < 0) oReport.ConnCdpErr++;
                else if (oInfo.CdpCount == 0) oReport.ConnCdpZero++;
                else if (oInfo.CdpCount == 1) oReport.ConnCdpOne++;
                else oReport.ConnCdpMulti++;
                if (oInfo.IsCableConn == true) oReport.IsCable31058ConnTrue++;
                if (oInfo.IsCableCdp == true) oReport.IsCable31058CdpTrue++;
                _dicCableInfo[oConn] = oInfo;
                // Проба [CBLPROP] (rev.9.4): свойства кабельного соединения ЦЕЛЕВОГО
                // клеммника (№31058=true) — ищем, где живёт имя кабеля: K-кабели не
                // спарены (CableConnections=0, проба [CBL] rev.9.3), связь ищем в
                // свойствах соединения.
                if (oInfo.IsCableConn == true && oRow.StripName == _strTargetStripName)
                    ProbeCableProperties(oRow, oConn);
            }
            oRow.CableName = oInfo.CableName;
            oRow.CdpCount = oInfo.CdpCount;
            oRow.IsCableConn = oInfo.IsCableConn;
            oRow.IsCableCdp = oInfo.IsCableCdp;
            oRow.CableSource = oInfo.CableSource;
            oRow.CableDest = oInfo.CableDest;
            // rev.16.4 (задача 3.1-bis): сбор концов кабеля в DmReport.CableStripEnds.
            // Источник — ПОЛНОЕ ОУ КЛЕММНИКА этой же строки ([DM].StripName =
            // oStrip.Name), а НЕ ОУ клемы ([DM].TerminalName) и тем более НЕ
            // значение 31019/31020. Правило заказчика — про КЛЕММНИКИ («кабель
            // подключён к нескольким клеммникам»), и StripName — это ровно то
            // понятие. Побочно снимается целый класс проблем с хвостом ':пин':
            // у клемы он есть и БЫВАЕТ БУКВЕННЫМ ('=ТСН-1++ЯЧ17+#1-XB1:А' — 48
            // строк на стенде 02.10), а DesignationOf снимает хвост только когда
            // тот целиком цифровой ⇒ с ОУ клемы «свой конец» не находился вовсе
            // и кабель уходил в откат. С ОУ клеммника хвоста нет НИКОГДА, и
            // парсер вообще не участвует в разборе пина.
            // Значения 31019/31020 отброшены по другой причине: EPLAN собирает их
            // только из «идентифицирующих»
            // свойств, поэтому у приборов сегменты «++»/«+» в строку не попадают вовсе,
            // а настройка живёт в свойствах проекта ⇒ разбор строки зависел бы от
            // конфигурации чужого проекта (подробно — в DmModels.CableStripEnds).
            // Ничего нового для этого не читаем: обход и так проходит по ВСЕМ
            // клеммникам проекта (Project.Pages -> Page.TerminalStrips), т.е. строки
            // второго конца видны и без 31019/31020 — НО ТОЛЬКО если второй конец
            // сам клеммник. ПРИБОР в обход не попадает (он не клема), поэтому для
            // кабеля «клеммник → прибор» список концов [DM] содержит один элемент,
            // Decide даёт 'own-end-only', и потребитель откатывается на 20376/20377
            // (третий триггер отката в AnalyzeAction.PrepareBreakPointDecisions).
            // Это НЕ регрессия: rev16.3 по 31019/31020 видел оба конца, но полагался
            // на конфигурацию чужого проекта.
            // Стоит ПОСЛЕ присваивания oRow.StripName/CableName и ВНЕ ветки
            // первого чтения — иначе повторный вызов на тот же уникальный Connection
            // (каждый вывод/пин) ничего бы не добавлял. КЛЮЧ — СНАЧАЛА
            // oRow.ConnectionName, при пустом — oRow.CableName: полное DT кабеля
            // живёт в ConnectionName (проба rev.9.5, так же в CableLayoutBuilder:34).
            // Порядок полей ДОЛЖЕН совпадать с CableLayoutBuilder: потребитель
            // (AnalyzeAction.PrepareBreakPointDecisions) ищет
            // oDm.CableStripEnds[strCable] по oCable.Name = ConnectionName ?? CableName.
            // Фильтр — MatchBuilder.IsCableRow(oRow) (тот же предикат, по которому
            // TerminalConnectionModelBuilder помечает строку кабельной): без него в
            // словарь попали бы внутренние соединения с пустым именем кабеля
            // ('=+++#' — на стенде 02.10 таких строк 420 из 1398, и в одной группе
            // оказывались 30 клеммников из шести шкафов ⇒ ложный multi) и 'peer=…'
            // у мостов (у Bridge ConnectionName не заполняется вовсе).
            string strCableKey = oRow.ConnectionName;
            if (string.IsNullOrEmpty(strCableKey)) strCableKey = oRow.CableName;
            if (MatchBuilder.IsCableRow(oRow) && !string.IsNullOrEmpty(strCableKey))
                AddStripEnd(oReport, strCableKey, oRow.StripName);
        }

        /// <summary>rev.16.4 (задача 3.1-bis): ОДНО ПОЛНОЕ ОУ КЛЕММНИКА в
        /// DmReport.CableStripEnds по ключу-кабелю. Пустое имя кабеля (провод, не
        /// кабель) и пустое/нечитаемое имя клеммника пропускаются. Дубль по Ordinal не
        /// добавляется, поэтому строка [BPE] печатается РОВНО один раз на пару
        /// «кабель+клема», сколько бы выводов/пинов у соединения ни было.
        /// Хвоста ':пин' в значении НЕТ и быть не может — берётся ОУ клеммника
        /// ('=HII-1.1++ЯЧ67+#1-X3'), а не ОУ клемы ('…-X3:1'). Поэтому
        /// DesignationOf не участвует в разборе пина, и буквенный пин клемы
        /// ('=ТСН-1++ЯЧ17+#1-XB1:А') на «свой конец» больше не влияет: раньше
        /// хвост снимался только когда целиком цифровой, и такой клеммник давал
        /// 'own-end-missing' с откатом на 20376/20377.
        /// Формат строки [BPE] ОТЛИЧАЕТСЯ от [CBP] первого чтения соединения
        /// (задача 0) позицией двоеточия: там после имени идёт «conn=», здесь —
        /// «: » сразу, поэтому фильтр стенда '\\[BPE\\] '[^']*': ' разводит их.
        /// Печатаем на КАЖДЫЙ новый уникальный DT клемы, а не по условию Count == 1:
        /// оно сработало бы лишь на ПЕРВЫЙ уникальный DT кабеля, и перечень, требуемый
        /// потребителем, был бы неполным; детерминизм «первый по Ordinal»
        /// задаёт потребитель, не ридер.</summary>
        private void AddStripEnd(DmReport oReport, string strCable, string strDt)
        {
            if (string.IsNullOrEmpty(strCable) || string.IsNullOrEmpty(strDt)) return;
            // Заглушка SafeText на НЕПРОЧИТАННОМ имени (SafeText("<n/a>", …) в
            // ReadTerminal) в список концов попадать НЕ должна. Раньше это было
            // безопасно: значение приходило из свойства, которое могло быть null,
            // и guard выше его отбрасывал. Теперь источник — oRow.TerminalName,
            // который заглушку НЕ несёт (т.е. guard на неё мёртв), а хуже того
            // заглушка ПЕРЕВЕРНУЛА бы противоположный конец: у «<n/a>» нет '+'
            // ⇒ CabinetKeyOf = «<n/a>», DesignationOf = "" ⇒ IsStripCodeLetter("")
            // = false, а по Ordinal '<' (U+003C) < '=' (U+003D) ⇒ именно «<n/a>»
            // стал бы ПЕРВЫМ «чужим» концом и дал opposite-device (устройство)
            // вместо opposite-strip (клеммник) — неверный Kind и неверное имя
            // точки разрыва. Отсекаем все значения, начинающиеся с '<' —
            // настоящие ОУ из [DM] всегда начинаются с '='.
            if (strDt[0] == '<') return;
            List<string> lstEnds;
            if (!oReport.CableStripEnds.TryGetValue(strCable, out lstEnds))
            {
                lstEnds = new List<string>();
                oReport.CableStripEnds[strCable] = lstEnds;
            }
            // Явный Ordinal-обход, а не List.Contains: сравнение обязано быть
            // Ordinal (как Decide в BreakPointResolver), а Contains для List<string>
            // даёт то же, но это знание приходилось бы держать в голове.
            for (int i = 0; i < lstEnds.Count; i++)
                if (string.Equals(lstEnds[i], strDt, StringComparison.Ordinal)) return;
            lstEnds.Add(strDt);
            _log.Log("[BPE] '" + strCable + "': " + strDt);
        }

        /// <summary>№31058 «Соединение: Принадлежность=Кабель» (bool, read-only).
        /// null = свойство не задано/недоступно; значение читается как текст
        /// (паттерн №35006), чтобы не зависеть от незадокументированных конвертеров.</summary>
        private static bool? ReadIsCable31058(StorableObject oObject)
        {
            try
            {
                string strValue = oObject.Properties[Properties.Connection.CONNECTION_IS_CABLE].ToString();
                if (string.IsNullOrEmpty(strValue)) return null;
                strValue = strValue.Trim();
                if (strValue.Equals("True", StringComparison.OrdinalIgnoreCase) || strValue == "1") return true;
                if (strValue.Equals("False", StringComparison.OrdinalIgnoreCase) || strValue == "0") return false;
                return null;
            }
            catch { return null; }
        }

        /// <summary>Проба rev.16.3: текст произвольного свойства Connection по
        /// НОМЕРУ — тот же паттерн SafeAnyProp (id из
        /// CreateAnyPropertyIdFromNumber, чтение Properties[AnyPropertyId]), но
        /// для Connection, а не Function (у SafeAnyProp параметр Function, и
        /// Connection им не является). №31019 CONNECTION_SOURCE /
        /// №31020 CONNECTION_DESTINATION — по KB (eplan.help API 2.9,
        /// ConnectionPropertyList.CONNECTION_SOURCE/DESTINATION) строковые
        /// свойства соединения, не индексированные. null = нечитаемо (null id,
        /// исключение, IsEmpty, пустая строка); в логе — «—».
        /// ВНИМАНИЕ (проба): содержимое этих свойств на стенде НЕ проверено —
        /// неизвестно, лежит ли там DT второго клеммника; см. отчёт задачи 0.</summary>
        private static string SafeConnPropText(Connection oConn, int nNumber)
        {
            AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(nNumber);
            if (oId == null) return null;
            try
            {
                PropertyValue oValue = oConn.Properties[oId];
                if (oValue == null || oValue.IsEmpty) return null;
                string strValue = oValue.ToString();
                return strValue.Length == 0 ? null : strValue;
            }
            catch { return null; }
        }

        private static string FmtCdp(int nCdpCount)
        {
            return nCdpCount < 0 ? "err" : nCdpCount.ToString();
        }

        private static string FmtBool(bool? bValue)
        {
            return bValue == null ? "-" : (bValue.Value ? "True" : "False");
        }

        private void DmErr(DmReport oReport, string strWhere, Exception oException)
        {
            oReport.ErrCount++;
            _log.Log("[DMERR] " + strWhere + ": " + oException.GetType().Name + ": " + oException.Message);
        }

        private static string SafeText(string strFallback, Func<string> oGetter)
        {
            try { string str = oGetter(); return str == null ? strFallback : str; }
            catch { return strFallback; }
        }

        private static int SafeInt(int nFallback, Func<int> oGetter)
        {
            try { return oGetter(); }
            catch { return nFallback; }
        }

        /// <summary>Чтение одного свойства объекта для дампа [SRC-DT]:
        /// тот же паттерн SafeText — любое исключение (в т.ч. EmptyPropertyException
        /// незаданного свойства), null id или пустое значение — «—». Чтение read-only
        /// свойств допустимо. Id — CreateAnyPropertyIdFromNumber по номерам
        /// 20095/20096 (rev.11.6: выходные DT-элементы ОУ; enum Properties.Function
        /// констант DESIGNATION_* не содержит).</summary>
        private static string SafePropText(Function oCable, AnyPropertyId oId)
        {
            if (oId == null) return "—";
            try
            {
                PropertyValue oValue = oCable.Properties[oId];
                if (oValue == null || oValue.IsEmpty) return "—";
                string strValue = oValue.ToString();
                return strValue.Length == 0 ? "—" : strValue;
            }
            catch { return "—"; }
        }

        /// <summary>Чтение произвольного свойства по НОМЕРУ (rev.16.0, Task 3):
        /// обёртка SafePropText — id создаётся CreateAnyPropertyIdFromNumber
        /// (CableSymbolCreator, internal). «—» = маркер «не читается» (как во всех
        /// дампах ридера); потребитель (BlockFormatResolver) конвертирует «—» в ""
        /// для сверки значений (пустое чтение участвует в сверке как пустое).</summary>
        internal static string SafeAnyProp(Function oF, int nNumber)
        {
            AnyPropertyId oId = CableSymbolCreator.CreateAnyPropertyIdFromNumber(nNumber);
            if (oId == null) return "—";
            try
            {
                PropertyValue oValue = oF.Properties[oId];
                if (oValue == null || oValue.IsEmpty) return "—";
                string strValue = oValue.ToString();
                return strValue.Length == 0 ? "—" : strValue;
            }
            catch { return "—"; }
        }

        /// <summary>Чтение одной части из контейнера NameParts объекта для дампа
        /// [SRC-DT] (rev.11.6): FunctionBase.NameParts возвращает
        /// FunctionBasePropertyList; индексатор по AnyPropertyId (тот же путь,
        /// что WriteDeviceTagProperties/SetNamePart). Отказ get NameParts или
        /// чтения части (бросили — вся NP-часть дампа «—»), null-список,
        /// null id или пустая часть — «—».</summary>
        private static string SafeNamePartText(Function oCable, AnyPropertyId oId)
        {
            if (oId == null) return "—";
            try
            {
                FunctionBasePropertyList oParts = oCable.NameParts;
                if (oParts == null) return "—";
                string strValue = oParts[oId];
                if (strValue == null || strValue.Length == 0) return "—";
                return strValue;
            }
            catch { return "—"; }
        }

        // --- Проба чтения реальных кабелей проекта (Task 6, rev.9.3) ---

        /// <summary>Диагностическая проба чтения РЕАЛЬНЫХ кабелей проекта из DataModel
        /// (перед реальной группировкой; группировка CableLayoutBuilder не меняется).
        /// Паттерн — рабочий пример example/ShowCablesInSegment.cs:76-127:
        /// DMObjectsFinder + FunctionsFilter (Category = Function.Enums.Category.Cable) →
        /// GetFunctions → as Cable → Cable.Name (полный DT); жилы кабеля —
        /// Cable.CableConnections (Connection[], подтверждено KB, plan_stage2).
        /// Дампы: [CBL] на кабель (connections=N), [CBLCONN] на соединение —
        /// идентификатор для офлайн-сопоставления со строками [DM]
        /// (DmRow.ConnectionName): попытки по порядку Connection.Name, затем
        /// StorableObject.ToStringIdentifier(), при ошибке <err>; [CBL-SUM] — итог
        /// (0 при пустом результате/ошибке перечисления). Ошибки членов — в дампы,
        /// обход не прерывается.</summary>
        public void ReadCables(Project oProject)
        {
            int nCables = 0;
            Function[] arrFunctions;
            try
            {
                DMObjectsFinder oFinder = new DMObjectsFinder(oProject);
                FunctionsFilter oCableFilter = new FunctionsFilter();
                oCableFilter.Category = Function.Enums.Category.Cable;
                arrFunctions = oFinder.GetFunctions(oCableFilter);
            }
            catch (Exception oException)
            {
                _log.Log("[CBL-SUM] кабелей: 0");
                _log.Warn("[CBL] перечисление кабелей не удалось (DMObjectsFinder/GetFunctions): " +
                    oException.GetType().Name + ": " + oException.Message);
                return;
            }
            if (arrFunctions == null || arrFunctions.Length == 0)
            {
                _log.Log("[CBL-SUM] кабелей: 0");
                return;
            }
            int nSrcDumped = 0;
            for (int i = 0; i < arrFunctions.Length; i++)
            {
                Cable oCable = arrFunctions[i] as Cable;
                if (oCable == null) continue;
                nCables++;
                string strName = SafeText("<n/a>", () => oCable.Name);
                // rev.11.6: поиск, где эталон хранит имя устройства. Прогон
                // rev.11.5 доказал: Properties структуры 1100/1400/1600 совпали
                // с нашими, имя устройства в Properties-DESIGNATION_*
                // отсутствует (1800='—'). Теперь читаем контейнер NameParts
                // эталона — NP1800 (PRODUCT), NP1801 (SUBPRODUCT1), NP1820
                // (FULLPRODUCT), NP1829 (PRODUCT_VISIBLE) — и выходные
                // DT-элементы свойств P20095 (FUNC_IDENTNAMEPARTS) /
                // P20096 (FUNC_IDENTDEVICETAGPARTS). Первые три кабеля с
                // осмысленным именем (не '<n/a>', содержит '-'), одна строка
                // на кабель; каждое чтение — отдельный try/catch внутри
                // SafeNamePartText (NameParts) / SafePropText (Properties) → «—».
                if (nSrcDumped < 3 && strName != "<n/a>" && strName.Contains("-"))
                {
                    nSrcDumped++;
                    StringBuilder oDump = new StringBuilder();
                    oDump.Append("[INFO] [SRC-DT] '" + strName + "':");
                    oDump.Append(" NP1800='" + SafeNamePartText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1800)) + "'");
                    oDump.Append(" NP1801='" + SafeNamePartText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1801)) + "'");
                    oDump.Append(" NP1820='" + SafeNamePartText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1820)) + "'");
                    oDump.Append(" NP1829='" + SafeNamePartText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1829)) + "'");
                    // rev.11.14: подчинённый идентификатор установки эталона
                    // (SUBPLANT1 #1101) — подтверждение хранения по точкам
                    // ('HII-1.1' = 1100='HII-1' + 1101='1').
                    oDump.Append(" 1100='" + SafePropText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1100)) + "'");
                    oDump.Append(" 1101='" + SafePropText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(1101)) + "'");
                    oDump.Append(" P20095='" + SafePropText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(20095)) + "'");
                    oDump.Append(" P20096='" + SafePropText(oCable, CableSymbolCreator.CreateAnyPropertyIdFromNumber(20096)) + "'");
                    _log.Log(oDump.ToString());
                }
                try
                {
                    Connection[] arrConns = oCable.CableConnections;
                    int nConns = arrConns == null ? 0 : arrConns.Length;
                    _log.Log("[CBL] #" + i + " '" + strName + "' connections=" + nConns);
                    if (arrConns == null) continue;
                    for (int j = 0; j < arrConns.Length; j++)
                    {
                        Connection oConn = arrConns[j];
                        if (oConn == null) continue;
                        _log.Log("[CBLCONN] '" + strName + "' #" + j + ": conn='" + ConnIdOf(oConn) + "'");
                    }
                }
                catch (Exception oException)
                {
                    _log.Log("[CBL] #" + i + " '" + strName + "' connections=<err>");
                    _log.Warn("[CBL] '" + strName + "': CableConnections: " +
                        oException.GetType().Name + ": " + oException.Message);
                }
            }
            _log.Log("[CBL-SUM] кабелей: " + nCables);
        }

        /// <summary>Идентификатор соединения для [CBLCONN]: попытки по порядку —
        /// Connection.Name, затем StorableObject.ToStringIdentifier(), при ошибке
        /// &lt;err&gt;. ВАЖНО: в API 2.9 у Connection (Connection : StorableObject, docs)
        /// НЕТ свойства Name — попытка Name идёт через рефлексию (член отсутствует/
        /// бросил → универсальный ToStringIdentifier()); при появлении Name в будущих
        /// версиях дамп автоматически покажет его.</summary>
        private static string ConnIdOf(Connection oConn)
        {
            try
            {
                System.Reflection.PropertyInfo oName = oConn.GetType().GetProperty("Name");
                object oValue = oName != null ? oName.GetValue(oConn, null) : null;
                string strName = oValue as string;
                if (!string.IsNullOrEmpty(strName)) return strName;
            }
            catch { }
            return SafeText("<err>", () => oConn.ToStringIdentifier());
        }

        // --- Проба [CBLPROP] (rev.9.4): свойства кабельных соединений целевого клеммника ---

        // Счётчики пробы: соединений обработано / непустых значений выдамплено.
        private int _nCblPropConns;
        private int _nCblPropValues;

        // Счётчики 4-го прохода (Terminal.Pins[].Connections): сколько связей
        // перечислено на пинах (_nPinConnSeen), сколько из них дали новую строку
        // [DM] сверх Ext/Int/Bridges (_nPinConnNew) и сколько уже было выведено
        // этими тремя проходами, поэтому повторно не выводились (_nPinConnSkip).
        // Печатаются одной строкой [PIN-SUM]. Инвариант стенда: Seen = New + Skip.
        private int _nPinConnSeen;
        private int _nPinConnNew;
        private int _nPinConnSkip;

        // Фиксация наличия перемычек (Side=Bridge) — ТОЛЬКО учёт, БЕЗ влияния на
        // пайплайн. Решение заказчика дословно: «Форма рисует перемычки. Рисовать их
        // пайплайном не нужно. Просто фиксировать что они есть. может в будующем
        // пригодится». Поэтому здесь нет ни строки в DmReport сверх уже существующей
        // Bridge-строки, ни влияния на сопоставление — только два словаря.
        //
        // СЧИТАЕМ ПАРАМИ, А НЕ СТРОКАМИ (правка дефекта, стенд 03.10 14:19).
        // Terminal.Bridges отдаёт каждую физическую перемычку С ОБОИХ КОНЦОВ, поэто-
        // му Bridge-СТРОК ровно вдвое больше, чем перемычек: 106 строк = 53 пере-
        // мычки. Первая версия считала строки и поэтому показала «соседних 94,
        // дальних 12» и вывела каждую дальнюю пару дважды (45↔72 и 72↔45).
        //
        // Ключ — неупорядоченная пара, приведённая к виду «min↔max», поэтому оба
        // конца дают ОДИН ключ. Значение в _dicBridgePairs: 0 = соседняя
        // (|Δ| ≤ 1), 1 = дальняя (потенциальная, через пол-щита).
        // _dicBridgePairsFar — только дальние, ключ тот же нормализованный, т.е.
        // каждая дальняя пара в нём ровно одна. Пары с нераспознанным номером
        // (-1) в оба словаря НЕ идут: неизвестное — не «дальняя».
        private readonly Dictionary<string, int> _dicBridgePairs =
            new Dictionary<string, int>();
        private readonly Dictionary<string, int> _dicBridgePairsFar =
            new Dictionary<string, int>();

        // Кэши сбора AnyPropertyId (один раз за прогон на вариант; пусто после
        // неудачного сбора — проба молча не даёт строк).
        private static List<KeyValuePair<string, AnyPropertyId>> _lstCablePropIds;
        private static bool _bCablePropIdsTried;
        private static List<KeyValuePair<string, AnyPropertyId>> _lstAllPropIds;
        private static bool _bAllPropIdsTried;

        /// <summary>Сбор статических AnyPropertyId из вложенных классов
        /// Eplan.EplApi.DataModel.Properties (паттерн rev.5.4 [PH]-текстов:
        /// рефлексия по вложенным классам Properties, статические поля-идентификаторы).
        /// bCableOnly=true — только имена с "CABLE" (rev.9.4); false — ВСЕ
        /// (проба rev.9.5: полное дампирование свойств первого кабельного соединения).
        /// Ключ пары — "<NestedClass>.<Field>" для читаемости дампа.</summary>
        private static List<KeyValuePair<string, AnyPropertyId>> CollectPropIds(bool bCableOnly)
        {
            List<KeyValuePair<string, AnyPropertyId>> lstResult;
            bool bTried;
            if (bCableOnly) { lstResult = _lstCablePropIds; bTried = _bCablePropIdsTried; }
            else { lstResult = _lstAllPropIds; bTried = _bAllPropIdsTried; }
            if (bTried) return lstResult;
            lstResult = new List<KeyValuePair<string, AnyPropertyId>>();
            try
            {
                Type oPropsType = typeof(Properties);
                Type[] arrNested = oPropsType.GetNestedTypes(System.Reflection.BindingFlags.Public);
                foreach (Type oNested in arrNested)
                {
                    System.Reflection.FieldInfo[] arrFields = oNested.GetFields(
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    foreach (System.Reflection.FieldInfo oField in arrFields)
                    {
                        if (oField.FieldType != typeof(AnyPropertyId)) continue;
                        if (bCableOnly && !oField.Name.Contains("CABLE")) continue;
                        AnyPropertyId oId = oField.GetValue(null) as AnyPropertyId;
                        if (oId != null)
                            lstResult.Add(new KeyValuePair<string, AnyPropertyId>(
                                oNested.Name + "." + oField.Name, oId));
                    }
                }
            }
            catch { lstResult = new List<KeyValuePair<string, AnyPropertyId>>(); }
            if (bCableOnly) { _lstCablePropIds = lstResult; _bCablePropIdsTried = true; }
            else { _lstAllPropIds = lstResult; _bAllPropIdsTried = true; }
            return lstResult;
        }

        // Счётчики пробы rev.9.5: соединений / direct-успехов / CDP-успехов /
        // непустых свойств полного дампа (первое соединение).
        private int _nProbe5Conns;
        private int _nProbe5DirectOk;
        private int _nProbe5CdpOk;
        private int _nProbe5ConnProps;

        /// <summary>Проба [CBLPROP] (rev.9.4) + расширение rev.9.5 — на каждом
        /// кабельном соединении (№31058=true, целевой клеммник):
        /// 1) [CONNCBL] прямой путь KB п.20 — Connection.CableDefinitionLine
        ///    (бросает при ≠1 CDP — ловим);
        /// 2) [CDPCBL] перепроверка п.24 — CableDefinitionLine каждого ConnectionDefPoint;
        /// 3) [CONNPROP] полный дамп непустых свойств — ТОЛЬКО на первом соединении
        ///    (среди всех свойств ищем, где живёт полное DT кабеля);
        /// 4) [CBLPROP] (rev.9.4) — свойства с "CABLE" в имени (не менялся).
        /// try/catch на каждый член; пропуски молча (диагностика).</summary>
        private void ProbeCableProperties(DmRow oRow, Connection oConn)
        {
            _nCblPropConns++;
            _nProbe5Conns++;
            string strId = "'" + oRow.TerminalName + "' conn='" + oRow.ConnectionName + "'";

            // 1. Прямой путь (KB п.20): Connection.CableDefinitionLine.
            try
            {
                Cable oDirect = oConn.CableDefinitionLine;
                if (oDirect != null)
                {
                    _nProbe5DirectOk++;
                    _log.Log("[CONNCBL] " + strId + ": direct -> '" +
                        SafeText("<n/a>", () => oDirect.Name) + "'");
                }
                else
                {
                    _log.Log("[CONNCBL] " + strId + ": direct -> <null>");
                }
            }
            catch (Exception oException)
            {
                _log.Log("[CONNCBL] " + strId + ": direct -> <err: " + oException.GetType().Name + ">");
            }

            // 2. Путь через CDP (п.24, перепроверка): CableDefinitionLine каждого CDP.
            try
            {
                ConnectionDefinitionPoint[] arrCdp = oConn.ConnectionDefPoints;
                if (arrCdp != null)
                {
                    for (int j = 0; j < arrCdp.Length; j++)
                    {
                        ConnectionDefinitionPoint oCdp = arrCdp[j];
                        if (oCdp == null) continue;
                        try
                        {
                            Cable oCdpCable = oCdp.CableDefinitionLine;
                            if (oCdpCable != null)
                            {
                                _nProbe5CdpOk++;
                                _log.Log("[CDPCBL] " + strId + " cdp #" + j + " -> '" +
                                    SafeText("<n/a>", () => oCdpCable.Name) + "'");
                            }
                            else
                            {
                                _log.Log("[CDPCBL] " + strId + " cdp #" + j + " -> <null>");
                            }
                        }
                        catch (Exception oCdpException)
                        {
                            _log.Log("[CDPCBL] " + strId + " cdp #" + j + " -> <err: " +
                                oCdpException.GetType().Name + ">");
                        }
                    }
                }
            }
            catch (Exception oException)
            {
                _log.Log("[CDPCBL] " + strId + ": ConnectionDefPoints -> <err: " +
                    oException.GetType().Name + ">");
            }

            // 3. Полный дамп непустых свойств — только на ПЕРВОМ кабельном соединении:
            // среди ВСЕХ свойств соединения ищем, где живёт полное DT кабеля
            // (если оно хранится свойством с именем без «CABLE»).
            if (_nProbe5Conns == 1)
            {
                foreach (KeyValuePair<string, AnyPropertyId> oPair in CollectPropIds(false))
                {
                    try
                    {
                        PropertyValue oValue = oConn.Properties[oPair.Value];
                        if (oValue == null || oValue.IsEmpty) continue;
                        string strValue = oValue.ToString();
                        if (strValue.Length == 0) continue;
                        _nProbe5ConnProps++;
                        _log.Log("[CONNPROP] " + strId + ": '" + oPair.Key + "' = '" + strValue + "'");
                    }
                    catch { /* диагностика: неприменимое свойство — пропуск */ }
                }
            }

            // 4. [CBLPROP] (rev.9.4) — свойства с "CABLE" в имени (без изменений).
            foreach (KeyValuePair<string, AnyPropertyId> oPair in CollectPropIds(true))
            {
                try
                {
                    PropertyValue oValue = oConn.Properties[oPair.Value];
                    if (oValue == null || oValue.IsEmpty) continue;
                    string strValue = oValue.ToString();
                    if (strValue.Length == 0) continue;
                    _nCblPropValues++;
                    _log.Log("[CBLPROP] '" + oRow.TerminalName + "' conn='" + oRow.ConnectionName +
                        "': '" + oPair.Key + "' = '" + strValue + "'");
                }
                catch { /* диагностика: неприменимое свойство соединения — пропуск */ }
            }
        }
    }
}
