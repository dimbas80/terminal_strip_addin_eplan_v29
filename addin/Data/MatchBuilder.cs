using System;
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Строка свода [MATCH] (Задача 5): точка подключения ↔ колонка К4 ↔
    /// клемма целевого клеммника ↔ классификация её подключений (кабель/провод).
    /// Идентичность «точка = конкретное подключение» из DM не выводится (в DM нет
    /// координат) — сопоставление через колонки К4.</summary>
    public sealed class MatchRow
    {
        public Pt Point;
        public int ColumnIndex = -1;     // -1 = точка-сирота (не привязалась к колонке)
        public double ColumnX = double.NaN;
        public string TerminalName;      // null = клемма не сопоставлена
        public int CableConnCount;
        public int WireConnCount;
        public readonly List<string> CableNames = new List<string>();
    }

    /// <summary>Дескриптор строки формы — PlaceHolderText (rev.5.3): текст + позиция.
    /// Якорь соответствия «номер клеммы ↔ колонка»: порядковое сопоставление колонок
    /// и клемм неверно, когда нумерация клеммника не совпадает с раскладкой по X
    /// (урок rev.5.2: мосты 7-8/8-9 стоят под перемычкой колонок 8/9/10).</summary>
    public sealed class PhRow
    {
        public Pt Location;
        public string Text;
        public string SourceTerminalName; // rev.6.1: полное имя Terminal-источника (SourceObject) или "" — запасной якорь
    }

    /// <summary>Группа [DM]-строк одной клеммы целевого клеммника (Ext+Int, без Bridge).</summary>
    public sealed class DmTerminalGroup
    {
        public string Name;
        public int Number = -1;          // номер клеммы (суффикс после ':' в полном имени)
        public readonly List<DmRow> Rows = new List<DmRow>();

        public int ConnCount { get { return Rows.Count; } }

        public int CableCount
        {
            get
            {
                int nCount = 0;
                foreach (DmRow oRow in Rows) if (MatchBuilder.IsCableRow(oRow)) nCount++;
                return nCount;
            }
        }

        public int WireCount { get { return ConnCount - CableCount; } }
    }

    /// <summary>Свод [MATCH] — Задача 5 (план: plan_stage2.md; решения 20.09.2026):
    /// (а) классификация «кабель/провод» — по свойству СОЕДИНЕНИЯ (решение
    /// пользователя): основной сигнал — №31058 «Соединение: Принадлежность=Кабель»,
    /// имя кабеля через CDP-пробу — доп. информация (rev.5.0–5.2, п.24);
    /// (б) rev.5.3: «номер ↔ колонка» — по якорям формы (PlaceHolderText), а не по
    /// порядку: подтверждено пользователем на клеммах 22/23 — у 22 подключений нет
    /// и выводов нет, у 23 два вывода (по 3 сегмента), при этом порядковое
    /// сопоставление приписывало её выводы клемме 22; rev.5.5: текст дескриптора —
    /// Text.Contents/GetDisplayString (docs API 2.9; TEXT-свойств у дескриптора нет —
    /// прогон rev.5.4), без якорей по-элементные таблицы не печатаются;
    /// (в) Bridge 11==11 (п.21в закрыт, п.24);
    /// (г) rev.5.6 (п.26): якорим только доминирующий ряд номеров формы; раздвоение
    /// вывода на свободный слот (мост К2 с листом в колонке без якоря) привязывается
    /// к клемме якоренного листа — встречается и сверху, и снизу.</summary>
    public static class MatchBuilder
    {
        /// <summary>Кабельное ли подключение: имя кабеля из CDP-пробы ИЛИ №31058
        /// «Принадлежность=Кабель» (на Connection либо на CDP).</summary>
        public static bool IsCableRow(DmRow oRow)
        {
            return oRow.CableName != null || oRow.IsCableConn == true || oRow.IsCableCdp == true;
        }

        public static List<MatchRow> Build(LeadAnalysis oAnalysis, DmReport oDm, List<PhRow> lstPh,
            DiagnosticLogger log)
        {
            List<MatchRow> lstRows = new List<MatchRow>();
            K4Report oK4 = oAnalysis == null ? null : oAnalysis.K4;
            if (oAnalysis == null || oAnalysis.Points.Count == 0)
            {
                log.Log("[INFO] [MATCH-SKIP] точек подключения нет — свод пропущен");
                return lstRows;
            }
            if (oK4 == null || !oK4.Valid)
            {
                // Ревью Этапа 3 (Important): точки есть, но колонки не определились —
                // это аварийный режим (весь свод теряется), поэтому WARN, а не INFO.
                log.Log("[WARN] [MATCH-SKIP] контроль К4 недоступен (стубов нет или <2 — шаг не определён): "
                    + oAnalysis.Points.Count + " точек остались без сопоставления клеммам — свод пропущен");
                return lstRows;
            }
            string strTarget = AddInConfiguration.TargetStripName;

            // --- 1. Строки [DM] целевого клеммника ---
            List<DmRow> lstConnRows = new List<DmRow>();
            List<DmRow> lstBridgeRows = new List<DmRow>();
            foreach (DmRow oRow in oDm.Rows)
            {
                if (oRow.StripName != strTarget) continue;
                if (oRow.Side == "Bridge") lstBridgeRows.Add(oRow);
                else lstConnRows.Add(oRow);
            }

            // --- 2. Группы клемм: полный список из DM + номер (суффикс имени) ---
            List<string> lstAllNames;
            bool bHaveAllNames = oDm.StripTerminalNames.TryGetValue(strTarget, out lstAllNames);
            Dictionary<string, DmTerminalGroup> dicGroups = new Dictionary<string, DmTerminalGroup>();
            List<DmTerminalGroup> lstTerminals = new List<DmTerminalGroup>();
            if (bHaveAllNames)
            {
                foreach (string strName in lstAllNames)
                {
                    if (dicGroups.ContainsKey(strName)) continue; // защита от дублей имён
                    DmTerminalGroup oNew = new DmTerminalGroup();
                    oNew.Name = strName;
                    oNew.Number = AnchorResolver.ParseTerminalNumber(strName);
                    dicGroups[strName] = oNew;
                    lstTerminals.Add(oNew);
                }
            }
            foreach (DmRow oRow in lstConnRows)
            {
                DmTerminalGroup oGroup;
                if (!dicGroups.TryGetValue(oRow.TerminalName, out oGroup))
                {
                    oGroup = new DmTerminalGroup();
                    oGroup.Name = oRow.TerminalName;
                    oGroup.Number = AnchorResolver.ParseTerminalNumber(oRow.TerminalName);
                    dicGroups[oRow.TerminalName] = oGroup;
                    lstTerminals.Add(oGroup);
                }
                oGroup.Rows.Add(oRow);
            }
            Dictionary<int, DmTerminalGroup> dicByNumber = new Dictionary<int, DmTerminalGroup>();
            foreach (DmTerminalGroup oGroup in lstTerminals)
                if (oGroup.Number >= 0 && !dicByNumber.ContainsKey(oGroup.Number))
                    dicByNumber[oGroup.Number] = oGroup;

            int nColumns = oK4.Columns.Count;

            // --- 3. Якоря «номер ↔ колонка» из дескрипторов формы (rev.5.3) ---
            // Числа не сортируем и по порядку НЕ сопоставляем: каждый якорь отвечает
            // сам за свою колонку (ближайшую вдоль оси: X при Horizontal / Y при
            // Vertical — rev.6.2).
            int[] arrColNumber = new int[nColumns];
            for (int i = 0; i < nColumns; i++) arrColNumber[i] = -1;

            // rev.6.2 (Задача 4): колонки К4 и Pos якорей лежат на оси ориентации —
            // метка оси в логах [PHCOL]/[SPLIT]/[MATCHCOL] (Horizontal — «X», как в
            // rev.6.1; Vertical — «Y»).
            string strAxis = oK4.Orientation == ReportOrientation.Vertical ? "Y" : "X";

            // Доминирующий ряд и разбор номеров — AnchorResolver (Этап 3, rev.6.0:
            // чистый перенос из MatchBuilder, поведение не меняется; rev.6.1 —
            // NumberToTerminalKey + запасной якорь [PHFB] из SourceObject; rev.6.2 —
            // ориентация из AddInConfiguration вместо хардкода Horizontal, ось —
            // внутри AnchorResolver).
            // Проверки «дальше шага»/«конфликт» [PHCOL] остались здесь — нужна геометрия К4.
            AnchorMap oMap = AnchorResolver.Build(lstPh, AddInConfiguration.Orientation,
                oDm.StripTerminalNames, strTarget, log);

            int nAnchors = 0, nAnchorCollisions = 0, nAnchorFar = 0;
            foreach (TerminalAnchor oAnchor in oMap.Anchors)
            {
                double dDist;
                int nCol = FindNearestColumn(oK4, oAnchor.Pos, out dDist);
                if (nCol < 0) continue;
                if (dDist > oK4.Pitch)
                {
                    nAnchorFar++;
                    log.Warn("[PHCOL] дескриптор '" + oAnchor.Text + "' (" + strAxis + "=" + FmtX(oAnchor.Pos) +
                        "): ближайшая колонка дальше шага (" + FmtX(dDist) + ") — якорь отброшен");
                    continue;
                }
                if (arrColNumber[nCol] >= 0 && arrColNumber[nCol] != oAnchor.Number)
                {
                    nAnchorCollisions++;
                    log.Warn("[PHCOL] колонка " + strAxis + "=" + FmtX(oK4.Columns[nCol]) + ": якоря конфликтуют (" +
                        arrColNumber[nCol] + " и " + oAnchor.Number + ") — оставлен первый");
                    continue;
                }
                if (arrColNumber[nCol] == oAnchor.Number) continue;
                arrColNumber[nCol] = oAnchor.Number;
                nAnchors++;
            }
            bool bUseAnchors = nAnchors > 0;
            log.Log("[INFO] [MATCH] целевой клеммник '" + strTarget + "': точек " + oAnalysis.Points.Count +
                ", колонок К4 " + nColumns + ", клемм в DM " + lstTerminals.Count +
                " (подключений Ext+Int " + lstConnRows.Count + "); якорей формы: " + nAnchors +
                (bUseAnchors ? " — сопоставление по якорям" : " — НЕТ, сопоставление колонок с клеммами недоступно"));
            if (!bUseAnchors)
                log.Warn("[MATCH] якорей формы не найдено — по-элементные [MATCHCOL]/[MATCH] пропущены " +
                    "(позиционного fallback нет, решение 20.09.2026); проверить чтение текста дескрипторов ([PH]/[PHPROBE])");
            if (bUseAnchors && nColumns != lstTerminals.Count)
                log.Warn("[MATCH] колонок К4 (" + nColumns + ") != клемм в DM (" + lstTerminals.Count +
                    ") — есть колонки без клеммы/клеммы без колонки (см. [MATCHCOL])");

            // --- 3б. Раздвоения вывода (rev.5.6, решение пользователя 20.09.2026):
            //         вывод может раздваиваться на две колонки — вторая колонка без
            //         стуба и номера (виртуальный слот). Геометрия раздвоения —
            //         компонента-мост К2 (неколлинеарная цепь, 2 листа), а по К2 оба
            //         листа моста = выводы ОДНОЙ клеммы: лист в якоренной колонке
            //         определяет клемму, лист в свободном слоте следует за ним.
            //         Встречается и сверху, и снизу — правило общее.
            int[] arrColTerminal = new int[nColumns];
            bool[] arrSplitCol = new bool[nColumns];
            for (int i = 0; i < nColumns; i++) arrColTerminal[i] = arrColNumber[i];
            int nSplits = 0;
            if (bUseAnchors)
            {
                Dictionary<int, List<int>> dicBridgePoints = new Dictionary<int, List<int>>();
                for (int i = 0; i < oAnalysis.Points.Count; i++)
                {
                    if (!oAnalysis.PointIsBridge[i]) continue;
                    int nComp = oAnalysis.PointComponent[i];
                    if (!dicBridgePoints.ContainsKey(nComp)) dicBridgePoints[nComp] = new List<int>();
                    dicBridgePoints[nComp].Add(i);
                }
                foreach (KeyValuePair<int, List<int>> oEntry in dicBridgePoints)
                {
                    if (oEntry.Value.Count != 2) continue; // мост = ровно 2 листа-точки
                    int nCol1 = oK4.BindIndex(oAnalysis.Points[oEntry.Value[0]]);
                    int nCol2 = oK4.BindIndex(oAnalysis.Points[oEntry.Value[1]]);
                    if (nCol1 < 0 || nCol2 < 0)
                    {
                        log.Warn("[SPLIT] мост: лист не привязался к колонке К4 — раздвоение не обрабатывалось");
                        continue;
                    }
                    if (nCol1 == nCol2)
                    {
                        if (arrColNumber[nCol1] < 0)
                            log.Warn("[SPLIT] мост: оба листа в колонке " + strAxis + "=" + FmtX(oK4.Columns[nCol1]) +
                                " без якоря — точки остаются без клеммы");
                        continue;
                    }
                    int nNum1 = arrColNumber[nCol1], nNum2 = arrColNumber[nCol2];
                    int nOwner = -1, nFreeCol = -1;
                    if (nNum1 >= 0 && nNum2 < 0) { nOwner = nNum1; nFreeCol = nCol2; }
                    else if (nNum2 >= 0 && nNum1 < 0) { nOwner = nNum2; nFreeCol = nCol1; }
                    else if (nNum1 < 0 && nNum2 < 0)
                    {
                        log.Warn("[SPLIT] мост: обе колонки листьев без якоря — точки остаются без клеммы");
                        continue;
                    }
                    else if (nNum1 != nNum2)
                    {
                        log.Warn("[SPLIT] мост: листья на якоренных колонках №" + nNum1 + " и №" + nNum2 +
                            " — оставлено по-колоночное сопоставление (К2 ждёт одну клемму — проверить форму)");
                        continue;
                    }
                    else continue; // оба листа уже на одной клемме
                    if (arrColTerminal[nFreeCol] == nOwner) continue;
                    if (arrColTerminal[nFreeCol] >= 0)
                    {
                        // Слот вдоль оси один, а раздвоения бывают и сверху, и снизу —
                        // два моста могут претендовать на один свободный слот.
                        log.Warn("[SPLIT] слот " + strAxis + "=" + FmtX(oK4.Columns[nFreeCol]) + ": уже привязан к №" +
                            arrColTerminal[nFreeCol] + ", раздвоение к №" + nOwner +
                            " отброшено (оставлен первый)");
                        continue;
                    }
                    arrColTerminal[nFreeCol] = nOwner;
                    arrSplitCol[nFreeCol] = true;
                    nSplits++;
                    log.Log("[SPLIT] раздвоение: мост, листья на колонках " + strAxis + "=" + FmtX(oK4.Columns[nCol1]) +
                        " и " + strAxis + "=" + FmtX(oK4.Columns[nCol2]) + " — колонка " + strAxis + "=" + FmtX(oK4.Columns[nFreeCol]) +
                        " привязана к клемме №" + nOwner + " (К2: мост = 2 вывода одной клеммы)");
                }
            }

            // --- 4. Таблица [MATCHCOL]: колонка ↔ (якорь/раздвоение) ↔ клемма (только
            //         при якорях — без них каждая строка была бы «точки есть, клеммы нет») ---
            if (bUseAnchors)
            {
                // Точки клеммы раздельно: «свои» (якоренные колонки с её номером) и
                // «покрытые раздвоением» (слоты из секции 3б). Сумма нужна для честного
                // сравнения с числом подключений (клемма 2 / слот 66.85); WARN по
                // колонке подавляется только раздвоением, не суммой дублирующихся
                // якорей (семантика rev.5.5 при nSplits==0 сохранена).
                Dictionary<int, int> dicOwnPts = new Dictionary<int, int>();
                Dictionary<int, int> dicSplitPts = new Dictionary<int, int>();
                for (int i = 0; i < nColumns; i++)
                {
                    if (arrColTerminal[i] < 0) continue;
                    int nPts = (oK4.Counts != null && i < oK4.Counts.Length && oK4.Counts[i] > 0) ? oK4.Counts[i] : 0;
                    Dictionary<int, int> dicTarget = arrColNumber[i] >= 0 ? dicOwnPts : dicSplitPts;
                    int nCur;
                    if (dicTarget.TryGetValue(arrColTerminal[i], out nCur))
                        dicTarget[arrColTerminal[i]] = nCur + nPts;
                    else
                        dicTarget[arrColTerminal[i]] = nPts;
                }

                for (int i = 0; i < nColumns; i++)
                {
                    int nNumber = arrColNumber[i];
                    int nTerm = arrColTerminal[i];
                    bool bSplit = arrSplitCol[i];
                    DmTerminalGroup oGroup = nTerm >= 0
                        ? FindGroupByAnchorNumber(oMap, nTerm, dicGroups, dicByNumber)
                        : null;
                    int nColPoints = (oK4.Counts != null && i < oK4.Counts.Length) ? oK4.Counts[i] : -1;
                    int nDiscard;
                    int nOwnPts = dicOwnPts.TryGetValue(nTerm, out nDiscard) ? nDiscard : 0;
                    int nSplitPts = dicSplitPts.TryGetValue(nTerm, out nDiscard) ? nDiscard : 0;
                    int nTotal = nOwnPts + nSplitPts;
                    string strLine = "[MATCHCOL] колонка " + strAxis + "=" + FmtX(oK4.Columns[i]) + " ↔ " +
                        (nNumber >= 0 ? "№" + nNumber :
                            (bSplit ? "<якоря нет, раздвоение → №" + nTerm + ">" : "<якоря нет>")) +
                        (oGroup == null
                            ? (nTerm >= 0 ? " — клеммы №" + nTerm + " нет в DM" : "")
                            : " клемма '" + oGroup.Name + "' (подключений " + oGroup.ConnCount +
                              ": кабель " + oGroup.CableCount + ", провод " + oGroup.WireCount + ")") +
                        ", точек в колонке " + (nColPoints >= 0 ? nColPoints.ToString(CultureInfo.InvariantCulture) : "?");
                    bool bHasGroup = oGroup != null;
                    if (!bHasGroup && nColPoints > 0)
                        log.Warn(strLine + " — точки есть, клеммы нет");
                    else if (bHasGroup && oGroup.ConnCount > 0 && nColPoints == 0 && nSplitPts == 0)
                        log.Warn(strLine + " — у клеммы подключения, а точек в колонке нет");
                    else if (bHasGroup && nColPoints >= 0 && nColPoints != oGroup.ConnCount &&
                        (nSplitPts == 0 || nTotal != oGroup.ConnCount))
                        log.Warn(strLine + " — число точек колонки != числу подключений клеммы");
                    else if (bHasGroup && bSplit && nColPoints != oGroup.ConnCount && nSplitPts > 0 &&
                        nTotal == oGroup.ConnCount)
                        log.Log(strLine + " (с учётом раздвоения: у клеммы всего " + nTotal + ")");
                    else
                        log.Log(strLine);
                }
                // Клеммы, чей номер не привязался ни к одной колонке (якорь или раздвоение)
                foreach (DmTerminalGroup oGroup in lstTerminals)
                {
                    if (oGroup.Number < 0) continue;
                    bool bFound = false;
                    for (int i = 0; i < nColumns; i++)
                        if (arrColTerminal[i] == oGroup.Number) { bFound = true; break; }
                    if (!bFound)
                        log.Warn("[MATCHCOL] клемма '" + oGroup.Name + "' (№" + oGroup.Number +
                            ", подключений " + oGroup.ConnCount + ") — нет якоря в форме");
                }
            }

            // --- 5. Строки [MATCH]: по точкам геометрии (строки строятся всегда —
            //         счётчик в [MATCH-SUM]; печать — только при якорях, иначе
            //         каждая строка была бы «клемма -: нет сопоставления») ---
            int nIndex = 0;
            foreach (Pt oPoint in oAnalysis.Points)
            {
                nIndex++;
                MatchRow oMatchRow = new MatchRow();
                oMatchRow.Point = oPoint;
                oMatchRow.ColumnIndex = oK4.BindIndex(oPoint);
                if (oMatchRow.ColumnIndex >= 0) oMatchRow.ColumnX = oK4.Columns[oMatchRow.ColumnIndex];
                int nTerm = oMatchRow.ColumnIndex >= 0 ? arrColTerminal[oMatchRow.ColumnIndex] : -1;
                bool bSplitPoint = oMatchRow.ColumnIndex >= 0 && arrSplitCol[oMatchRow.ColumnIndex];
                DmTerminalGroup oGroup = nTerm >= 0
                    ? FindGroupByAnchorNumber(oMap, nTerm, dicGroups, dicByNumber)
                    : null;
                if (oGroup != null)
                {
                    oMatchRow.TerminalName = oGroup.Name;
                    oMatchRow.CableConnCount = oGroup.CableCount;
                    oMatchRow.WireConnCount = oGroup.WireCount;
                    foreach (DmRow oRow in oGroup.Rows)
                        if (oRow.CableName != null && !oMatchRow.CableNames.Contains(oRow.CableName))
                            oMatchRow.CableNames.Add(oRow.CableName);
                }
                lstRows.Add(oMatchRow);
                if (!bUseAnchors) continue;

                string strLine = "[MATCH] #" + nIndex.ToString("00", CultureInfo.InvariantCulture) + ": (" +
                    oPoint.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oPoint.Y.ToString("F3", CultureInfo.InvariantCulture) + ") -> колонка " +
                    (oMatchRow.ColumnIndex >= 0 ? FmtX(oMatchRow.ColumnX) : "СИРОТА") + " клемма " +
                    (oGroup != null ? "'" + oGroup.Name + "'" +
                        (bSplitPoint ? " (раздвоение → №" + nTerm + ")" : "") : "-") + ": " +
                    (oGroup != null ? DescribeConns(oGroup) : "нет сопоставления");
                if (oMatchRow.ColumnIndex < 0) log.Warn(strLine);
                else log.Log(strLine);
            }

            // --- 6. Кросс-чек классификации: CDP-имя против №31058 ---
            int nCabX = 0, nCableByName = 0, nCableBy31058Only = 0;
            foreach (DmRow oRow in lstConnRows)
            {
                bool bByName = oRow.CableName != null;
                bool bByProp = oRow.IsCableConn == true || oRow.IsCableCdp == true;
                if (bByName) nCableByName++;
                if (bByName && !bByProp)
                {
                    nCabX++;
                    log.Warn("[CABX] клемма '" + oRow.TerminalName + "' conn '" + oRow.ConnectionName +
                        "': имя кабеля '" + oRow.CableName + "' из CDP, но №31058 c:" +
                        FmtBool(oRow.IsCableConn) + " d:" + FmtBool(oRow.IsCableCdp));
                }
                else if (!bByName && bByProp)
                {
                    nCableBy31058Only++;
                    log.Log("[CABX] клемма '" + oRow.TerminalName + "' conn '" + oRow.ConnectionName +
                        "': кабель по №31058 c:" + FmtBool(oRow.IsCableConn) + " d:" +
                        FmtBool(oRow.IsCableCdp) + " (имени нет — определения кабеля в DM отсутствуют)");
                }
            }

            // --- 7. Итоги ---
            int nCableConns = 0, nWireConns = 0;
            foreach (DmTerminalGroup oGroup in lstTerminals)
            {
                nCableConns += oGroup.CableCount;
                nWireConns += oGroup.WireCount;
            }
            log.Log("[INFO] [MATCH-SUM] точек " + lstRows.Count + ", колонок К4 " + nColumns +
                ", клемм в DM " + lstTerminals.Count + ", якорей " + nAnchors +
                " (коллизий " + nAnchorCollisions + ", дальних " + nAnchorFar + "), раздвоений моста " + nSplits +
                "; подключений целевого клеммника: Ext+Int " + lstConnRows.Count +
                " (кабельных " + nCableConns + " [по имени CDP " + nCableByName +
                ", только №31058 " + nCableBy31058Only + "], проводных " + nWireConns +
                "); сирот " + oK4.Orphans + ", перегруженных колонок " + oK4.Over +
                "; конфликтов классификации: " + nCabX);

            // --- 8. Bridge целевого клеммника: п.21в закрыт (п.24) ---
            HashSet<string> dicPairs = new HashSet<string>();
            foreach (DmRow oRow in lstBridgeRows)
            {
                if (oRow.PeerName == null) continue;
                string strPair = string.CompareOrdinal(oRow.TerminalName, oRow.PeerName) < 0
                    ? oRow.TerminalName + "↔" + oRow.PeerName
                    : oRow.PeerName + "↔" + oRow.TerminalName;
                if (dicPairs.Add(strPair))
                    log.Log("[BRIDGE] сегмент '" + oRow.TerminalName + "' ↔ '" + oRow.PeerName +
                        "' (cable=" + (oRow.CableName ?? "<провод>") + ")");
            }
            log.Log("[INFO] [BRIDGE] строк [DM] Bridge: " + lstBridgeRows.Count +
                " = сегментов " + dicPairs.Count + "; графических перемычек стубов: " +
                oAnalysis.Jumpers.Count + " (п.21в закрыт rev.5.0: L=28→3 сегмента, L=14→2, L=7→1)");

            log.Summarize("[MATCH]: строк " + lstRows.Count + " (клемм " + lstTerminals.Count +
                ", якорей " + nAnchors + ", кабельных подключений " + nCableConns +
                ", проводных " + nWireConns + ", Bridge-сегментов " + dicPairs.Count + ").");
            return lstRows;
        }

        private static int FindNearestColumn(K4Report oK4, double dX, out double dDist)
        {
            int nBest = -1;
            double dBest = double.MaxValue;
            for (int i = 0; i < oK4.Columns.Count; i++)
            {
                double d = Math.Abs(oK4.Columns[i] - dX);
                if (d < dBest) { dBest = d; nBest = i; }
            }
            dDist = nBest >= 0 ? dBest : double.MaxValue;
            return nBest;
        }

        /// <summary>Клемма по номеру якоря (rev.6.1): сначала TerminalKey — полное имя
        /// клеммы целевого клеммника из NumberToTerminalKey (первое имя при дубле
        /// номера, [KEYDUP]); нет ключа или имени в группах — прежний путь по номеру
        /// (dicByNumber, rev.6.0).</summary>
        private static DmTerminalGroup FindGroupByAnchorNumber(AnchorMap oMap, int nNumber,
            Dictionary<string, DmTerminalGroup> dicGroups, Dictionary<int, DmTerminalGroup> dicByNumber)
        {
            string strKey;
            if (oMap != null && oMap.NumberToTerminalKey != null && nNumber >= 0 &&
                oMap.NumberToTerminalKey.TryGetValue(nNumber, out strKey))
            {
                DmTerminalGroup oByKey;
                if (dicGroups.TryGetValue(strKey, out oByKey)) return oByKey;
            }
            DmTerminalGroup oGroup;
            dicByNumber.TryGetValue(nNumber, out oGroup);
            return oGroup;
        }

        /// <summary>Описание подключений клеммы: «кабель N (имена) + провод M».</summary>
        private static string DescribeConns(DmTerminalGroup oGroup)
        {
            if (oGroup.ConnCount == 0) return "подключений нет";
            List<string> lstNames = new List<string>();
            foreach (DmRow oRow in oGroup.Rows)
                if (oRow.CableName != null && !lstNames.Contains(oRow.CableName))
                    lstNames.Add(oRow.CableName);
            string strCable = "кабель " + oGroup.CableCount +
                (lstNames.Count > 0 ? " (" + string.Join(", ", lstNames.ToArray()) + ")" : "");
            string strWire = "провод " + oGroup.WireCount;
            if (oGroup.CableCount == 0) return strWire;
            if (oGroup.WireCount == 0) return strCable;
            return strCable + " + " + strWire;
        }

        private static string FmtX(double dX)
        {
            return dX.ToString("F3", CultureInfo.InvariantCulture);
        }

        /// <summary>bool? → "True"/"False"/"-" (не задано) — формат строк [DM]/[CABX].</summary>
        private static string FmtBool(bool? bValue)
        {
            return bValue == null ? "-" : (bValue.Value ? "True" : "False");
        }
    }
}
