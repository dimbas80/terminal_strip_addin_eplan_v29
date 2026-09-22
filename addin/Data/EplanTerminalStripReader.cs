using System;
using System.Collections.Generic;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace MyEplanActions
{
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
        // rev.5.0: статистика пробы CDP/№31058 по УНИКАЛЬНЫМ соединениям (кэш CableInfo).
        public int ConnCdpZero;
        public int ConnCdpOne;
        public int ConnCdpMulti;
        public int ConnCdpErr;
        public int IsCable31058ConnTrue;
        public int IsCable31058CdpTrue;
    }

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

        public EplanTerminalStripReader(DiagnosticLogger oLogger)
        {
            _log = oLogger;
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
                            if (oSegment.Conn != null) FillCable(oReport, oRow, oSegment.Conn);
                            oRow.PeerName = oSegment.BridgedTerminal != null
                                ? SafeText("<n/a>", () => oSegment.BridgedTerminal.Name)
                                : null;
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
            if (oRow.Side == "Bridge")
            {
                _log.Log("[DM] " + oRow.TerminalName + " | Bridge | ->" + (oRow.PeerName ?? "?") +
                    " | conn=" + (oRow.HasConn ? "yes" : "no") + " | cable=" + strCable + strProbe);
            }
            else
            {
                _log.Log("[DM] " + oRow.TerminalName + " | " + oRow.Side + " | " +
                    oRow.ConnectionName + " | " + oRow.PinName + " | pin=" + oRow.PinIndex +
                    " | cable=" + strCable + strProbe);
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
                if (oInfo.IsCableConn == true && oRow.StripName == AddInConfiguration.TargetStripName)
                    ProbeCableProperties(oRow, oConn);
            }
            oRow.CableName = oInfo.CableName;
            oRow.CdpCount = oInfo.CdpCount;
            oRow.IsCableConn = oInfo.IsCableConn;
            oRow.IsCableCdp = oInfo.IsCableCdp;
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
            for (int i = 0; i < arrFunctions.Length; i++)
            {
                Cable oCable = arrFunctions[i] as Cable;
                if (oCable == null) continue;
                nCables++;
                string strName = SafeText("<n/a>", () => oCable.Name);
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
