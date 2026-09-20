using System;
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Строка свода [MATCH] (Задача 5): точка подключения ↔ колонка К4 ↔
    /// клемма целевого клеммника ↔ классификация её подключений (кабель/провод).
    /// Идентичность «точка = конкретное подключение» из DM не выводится (в DM нет
    /// координат) — сопоставление поэкранное: колонка К4 ↔ клемма по порядку.</summary>
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

    /// <summary>Группа [DM]-строк одной клеммы целевого клеммника (Ext+Int, без Bridge).</summary>
    public sealed class DmTerminalGroup
    {
        public string Name;
        public readonly List<DmRow> Rows = new List<DmRow>();

        public int ConnCount { get { return Rows.Count; } }

        public int CableCount
        {
            get
            {
                int nCount = 0;
                foreach (DmRow oRow in Rows) if (oRow.CableName != null) nCount++;
                return nCount;
            }
        }

        public int WireCount { get { return ConnCount - CableCount; } }
    }

    /// <summary>Свод [MATCH] — Задача 5 (план: plan_stage2.md; решения 20.09.2026):
    /// (а) классификация «кабель/провод» — по свойству СОЕДИНЕНИЯ (соединение
    /// принадлежит кабелю), а не по свойствам клеммы: основной сигнал — CDP-проба
    /// (ConnectionDefPoints → ConnectionDefinitionPoint.CableDefinitionLine → Cable.Name),
    /// кросс-чек — №31058 «Соединение: Принадлежность=Кабель» (расхождения — [CABX]);
    /// (б) разбор Bridge 11 vs 12 (summary п.21в): пары «клемма↔клемма» из [DM]-строк
    /// Bridge против графических перемычек [JUMPER].</summary>
    public static class MatchBuilder
    {
        public static List<MatchRow> Build(LeadAnalysis oAnalysis, DmReport oDm, DiagnosticLogger log)
        {
            List<MatchRow> lstRows = new List<MatchRow>();
            K4Report oK4 = oAnalysis == null ? null : oAnalysis.K4;
            if (oK4 == null || !oK4.Valid || oAnalysis.Points.Count == 0)
            {
                log.Log("[INFO] [MATCH-SKIP] контроль К4 недоступен или точек нет — свод пропущен");
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

            // --- 2. Группировка по клеммам (порядок чтения), затем сортировка по номеру ---
            Dictionary<string, DmTerminalGroup> dicGroups = new Dictionary<string, DmTerminalGroup>();
            List<DmTerminalGroup> lstTerminals = new List<DmTerminalGroup>();
            foreach (DmRow oRow in lstConnRows)
            {
                DmTerminalGroup oGroup;
                if (!dicGroups.TryGetValue(oRow.TerminalName, out oGroup))
                {
                    oGroup = new DmTerminalGroup();
                    oGroup.Name = oRow.TerminalName;
                    dicGroups[oRow.TerminalName] = oGroup;
                    lstTerminals.Add(oGroup);
                }
                oGroup.Rows.Add(oRow);
            }
            lstTerminals.Sort(delegate(DmTerminalGroup oA, DmTerminalGroup oB)
            {
                int nA, nB;
                bool bA = int.TryParse(oA.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out nA);
                bool bB = int.TryParse(oB.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out nB);
                if (bA && bB) return nA.CompareTo(nB);
                if (bA) return -1;
                if (bB) return 1;
                return string.CompareOrdinal(oA.Name, oB.Name);
            });

            int nColumns = oK4.Columns.Count;
            bool bAligned = nColumns == lstTerminals.Count;
            log.Log("[INFO] [MATCH] целевой клеммник '" + strTarget + "': точек " + oAnalysis.Points.Count +
                ", колонок К4 " + nColumns + ", клемм в DM " + lstTerminals.Count +
                " (подключений Ext+Int " + lstConnRows.Count + ")");
            if (!bAligned)
                log.Warn("[MATCH] колонок К4 (" + nColumns + ") != клемм в DM (" + lstTerminals.Count +
                    ") — сопоставление колонка↔клемма по порядку УСЛОВНОЕ, после лишней/виртуальной колонки возможен сдвиг");

            // --- 3. Таблица сопоставления колонок и клемм (+ WARN в обратную сторону:
            //         расхождение числа точек колонки и подключений клеммы — ревью rev.5.0) ---
            for (int i = 0; i < nColumns; i++)
            {
                DmTerminalGroup oGroup = i < lstTerminals.Count ? lstTerminals[i] : null;
                int nColPoints = (oK4.Counts != null && i < oK4.Counts.Length) ? oK4.Counts[i] : -1;
                string strLine = "[MATCHCOL] колонка X=" + FmtX(oK4.Columns[i]) + " ↔ " +
                    (oGroup == null
                        ? "<клеммы нет — колонка без терминала?>"
                        : "клемма '" + oGroup.Name + "' (подключений " + oGroup.ConnCount +
                          ": кабель " + oGroup.CableCount + ", провод " + oGroup.WireCount + ")") +
                    ", точек в колонке " + (nColPoints >= 0 ? nColPoints.ToString(CultureInfo.InvariantCulture) : "?");
                if (oGroup == null && nColPoints > 0)
                    log.Warn(strLine + " — точки есть, клеммы нет");
                else if (oGroup != null && oGroup.ConnCount > 0 && nColPoints == 0)
                    log.Warn(strLine + " — у клеммы подключения, а точек в колонке нет");
                else if (oGroup != null && nColPoints >= 0 && nColPoints != oGroup.ConnCount)
                    log.Warn(strLine + " — число точек колонки != числу подключений клеммы");
                else
                    log.Log(strLine);
            }
            for (int i = nColumns; i < lstTerminals.Count; i++)
                log.Log("[MATCHCOL] клемма '" + lstTerminals[i].Name + "' — без колонки К4");

            // --- 4. Строки [MATCH]: по точкам геометрии ---
            int nIndex = 0;
            foreach (Pt oPoint in oAnalysis.Points)
            {
                nIndex++;
                MatchRow oMatchRow = new MatchRow();
                oMatchRow.Point = oPoint;
                oMatchRow.ColumnIndex = oK4.BindIndex(oPoint);
                if (oMatchRow.ColumnIndex >= 0) oMatchRow.ColumnX = oK4.Columns[oMatchRow.ColumnIndex];
                DmTerminalGroup oGroup = oMatchRow.ColumnIndex >= 0 && oMatchRow.ColumnIndex < lstTerminals.Count
                    ? lstTerminals[oMatchRow.ColumnIndex]
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

                string strLine = "[MATCH] #" + nIndex.ToString("00", CultureInfo.InvariantCulture) + ": (" +
                    oPoint.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oPoint.Y.ToString("F3", CultureInfo.InvariantCulture) + ") -> колонка " +
                    (oMatchRow.ColumnIndex >= 0 ? FmtX(oMatchRow.ColumnX) : "СИРОТА") + " клемма " +
                    (oGroup != null ? "'" + oGroup.Name + "'" : "-") + ": " +
                    (oGroup != null ? DescribeConns(oGroup) : "нет сопоставления");
                if (oMatchRow.ColumnIndex < 0) log.Warn(strLine);
                else log.Log(strLine);
            }

            // --- 5. Кросс-чек классификации: CDP-кабель против №31058 ---
            int nCabX = 0;
            foreach (DmRow oRow in lstConnRows)
            {
                bool? eSignal = oRow.IsCableConn;
                bool? dSignal = oRow.IsCableCdp;
                bool? bSignal = eSignal ?? dSignal;
                if (bSignal == null) continue;
                bool bByCable = oRow.CableName != null;
                if (bSignal.Value != bByCable)
                {
                    nCabX++;
                    log.Log("[CABX] клемма '" + oRow.TerminalName + "' conn '" + oRow.ConnectionName +
                        "': cable=" + (bByCable ? oRow.CableName : "<провод>") +
                        ", №31058 c:" + FmtBool(eSignal) + " d:" + FmtBool(dSignal));
                }
            }

            // --- 6. Итоги ---
            int nCableConns = 0, nWireConns = 0;
            foreach (DmTerminalGroup oGroup in lstTerminals)
            {
                nCableConns += oGroup.CableCount;
                nWireConns += oGroup.WireCount;
            }
            log.Log("[INFO] [MATCH-SUM] точек " + lstRows.Count + ", колонок К4 " + nColumns +
                ", клемм в DM " + lstTerminals.Count + "; подключений целевого клеммника: Ext+Int " +
                lstConnRows.Count + " (кабельных " + nCableConns + ", проводных " + nWireConns +
                "); сирот " + oK4.Orphans + ", перегруженных колонок " + oK4.Over +
                "; расхождений с №31058: " + nCabX);

            // --- 7. Bridge целевого клеммника: разбор 11 vs 7 (summary п.21в) ---
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
                " = сегментов " + dicPairs.Count + " (каждый виден с обоих концов); графических " +
                "перемычек стубов: " + oAnalysis.Jumpers.Count +
                "; разность сегменты−перемычки: " + (dicPairs.Count - oAnalysis.Jumpers.Count) +
                " (п.21в, ожидание по шагу 7: 4+4+4=12 — разобрать)");

            log.Summarize("[MATCH]: строк " + lstRows.Count + " (клемм " + lstTerminals.Count +
                ", кабельных подключений " + nCableConns + ", проводных " + nWireConns +
                ", Bridge-сегментов " + dicPairs.Count + ").");
            return lstRows;
        }

        /// <summary>Описание подключений клеммы: «кабель N (имена) + провод M».</summary>
        private static string DescribeConns(DmTerminalGroup oGroup)
        {
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
    }
}
