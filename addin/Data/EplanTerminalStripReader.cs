using System;
using System.Collections.Generic;
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
                        ReadTerminal(oReport, oStrip, oTerminal);
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
            _log.Log("[PROBE5-SUM] соединений: " + _nProbe5Conns + ", direct-ok: " + _nProbe5DirectOk +
                ", cdp-ok: " + _nProbe5CdpOk + ", connprop: " + _nProbe5ConnProps);
            // rev.16.3 (задача 2): сколько кабелей получили хотя бы один непустой
            // DT жилы (#31019/#31020) — сходится с числом ключей CableCoreEnds и с
            // числом строк "[CBP] '<кабель>': <dt>" (одна на уникальный DT кабеля).
            _log.Log("[CBP-SUM] кабелей с DT жил: " + oReport.CableCoreEnds.Count);
            return oReport;
        }

        private void ReadTerminal(DmReport oReport, TerminalStrip oStrip, Terminal oTerminal)
        {
            string strStripName = SafeText("<n/a>", () => oStrip.Name);
            string strTermName = SafeText("<n/a>", () => oTerminal.Name);

            try
            {
                Terminal.ConnectionInfo[] arrExt = oTerminal.ExternalConnections;
                if (arrExt != null)
                    foreach (Terminal.ConnectionInfo oInfo in arrExt)
                        AddConnRow(oReport, strStripName, strTermName, "Ext", oInfo);
            }
            catch (Exception oException) { DmErr(oReport, "ExternalConnections ('" + strTermName + "')", oException); }

            try
            {
                Terminal.ConnectionInfo[] arrInt = oTerminal.InternalConnections;
                if (arrInt != null)
                    foreach (Terminal.ConnectionInfo oInfo in arrInt)
                        AddConnRow(oReport, strStripName, strTermName, "Int", oInfo);
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
                            if (oSegment.Conn != null) FillCable(oReport, oRow, oSegment.Conn);
                            Emit(oReport, oRow);
                        }
                    }
                }
            }
            catch (Exception oException) { DmErr(oReport, "Bridges ('" + strTermName + "')", oException); }
        }

        private void AddConnRow(DmReport oReport, string strStrip, string strTerm, string strSide,
            Terminal.ConnectionInfo oInfo)
        {
            DmRow oRow = new DmRow();
            oRow.StripName = strStrip;
            oRow.TerminalName = strTerm;
            oRow.Side = strSide;
            oRow.HasConn = oInfo.Conn != null;
            oRow.ConnectionName = SafeText("<n/a>", () => oInfo.ConnectionName);
            oRow.PinName = SafeText("", () => oInfo.FunctionPinName);
            oRow.PinIndex = SafeInt(-1, () => oInfo.PinIndex);
            if (oInfo.Conn != null) FillCable(oReport, oRow, oInfo.Conn);
            Emit(oReport, oRow);
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
            // rev.16.3 (задача 2): сбор DT жил по кабелю в DmReport.CableCoreEnds.
            // Стоит ПОСЛЕ присваивания oRow.CableName/31019/31020 (нужен готовый
            // oRow.CableName как ключ) и ВНЕ ветки первого чтения — иначе повторный
            // вызов на тот же уникальный Connection (каждый вывод/пин) ничего бы не
            // добавлял, а кабель без первого попадания не получил бы ключ вовсе.
            // Ключ — только oRow.CableName: у Bridge ConnectionName не заполняется
            // никогда, PeerName — другое поле.
            if (!string.IsNullOrEmpty(oRow.CableName))
            {
                AddCoreEnd(oReport, oRow.CableName, oRow.CableSource);
                AddCoreEnd(oReport, oRow.CableName, oRow.CableDest);
            }
        }

        /// <summary>rev.16.3 (задача 2): один конец жилы (#31019/#31020) в
        /// DmReport.CableCoreEnds по ключу-кабелю. Пустое имя кабеля (провод, не
        /// кабель) и пустое/нечитаемое значение свойства (null от SafeConnPropText)
        /// пропускаются. Дубль по Ordinal не добавляется, поэтому строка [CBP]
        /// печатается РОВНО один раз на пару «кабель+DT», сколько бы выводов/пинов
        /// у этого соединения ни было.
        /// Формат строки [CBP] ОТЛИЧАЕТСЯ от [CBP] первого чтения соединения
        /// (задача 0) позицией двоеточия: там после имени идёт «conn=», здесь —
        /// «: » сразу, поэтому фильтр стенда '\\[CBP\\] '[^']*': ' разводит их.
        /// Дополнение к букве брифа: бриф просил лог по условию Count == 1, но это
        /// условие срабатывает лишь на ПЕРВЫЙ уникальный DT кабеля (второй DT того
        /// же соединения даёт Count == 2) — полный перечень DT, требуемый гейтом
        /// шага 4 («со всеми DT этого кабеля»), не выполнился бы. Печатаем поэтому
        /// на КАЖДЫЙ новый уникальный DT; детерминизм «первый по Ordinal»
        /// (которого ждёт Task 3) задаёт потребитель, не ридер.</summary>
        private void AddCoreEnd(DmReport oReport, string strCable, string strDt)
        {
            if (string.IsNullOrEmpty(strCable) || string.IsNullOrEmpty(strDt)) return;
            List<string> lstEnds;
            if (!oReport.CableCoreEnds.TryGetValue(strCable, out lstEnds))
            {
                lstEnds = new List<string>();
                oReport.CableCoreEnds[strCable] = lstEnds;
            }
            // Явный Ordinal-обход, а не List.Contains: сравнение обязано быть
            // Ordinal (как Decide в BreakPointResolver), а Contains для List<string>
            // даёт то же, но это знание приходилось бы держать в голове.
            for (int i = 0; i < lstEnds.Count; i++)
                if (string.Equals(lstEnds[i], strDt, StringComparison.Ordinal)) return;
            lstEnds.Add(strDt);
            _log.Log("[CBP] '" + strCable + "': " + strDt);
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
