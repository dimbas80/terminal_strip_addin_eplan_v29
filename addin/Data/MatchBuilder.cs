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
    /// сопоставление приписывало её выводы клемме 22;
    /// (в) Bridge 11==11 (п.21в закрыт, п.24).</summary>
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
                    oNew.Number = ParseTerminalNumber(strName);
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
                    oGroup.Number = ParseTerminalNumber(oRow.TerminalName);
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
            // сам за свою колонку (ближайшую по X).
            int[] arrColNumber = new int[nColumns];
            for (int i = 0; i < nColumns; i++) arrColNumber[i] = -1;
            int nAnchors = 0, nAnchorCollisions = 0, nAnchorFar = 0;
            foreach (PhRow oPh in lstPh)
            {
                if (oPh == null || double.IsNaN(oPh.Location.X)) continue;
                int nNumber;
                if (!int.TryParse(SuffixAfterColon(oPh.Text).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out nNumber)) continue;
                double dDist;
                int nCol = FindNearestColumn(oK4, oPh.Location.X, out dDist);
                if (nCol < 0) continue;
                if (dDist > oK4.Pitch)
                {
                    nAnchorFar++;
                    log.Warn("[PHCOL] дескриптор '" + oPh.Text + "' (X=" + FmtX(oPh.Location.X) +
                        "): ближайшая колонка дальше шага (" + FmtX(dDist) + ") — якорь отброшен");
                    continue;
                }
                if (arrColNumber[nCol] >= 0 && arrColNumber[nCol] != nNumber)
                {
                    nAnchorCollisions++;
                    log.Warn("[PHCOL] колонка X=" + FmtX(oK4.Columns[nCol]) + ": якоря конфликтуют (" +
                        arrColNumber[nCol] + " и " + nNumber + ") — оставлен первый");
                    continue;
                }
                if (arrColNumber[nCol] == nNumber) continue;
                arrColNumber[nCol] = nNumber;
                nAnchors++;
            }
            bool bUseAnchors = nAnchors > 0;
            log.Log("[INFO] [MATCH] целевой клеммник '" + strTarget + "': точек " + oAnalysis.Points.Count +
                ", колонок К4 " + nColumns + ", клемм в DM " + lstTerminals.Count +
                " (подключений Ext+Int " + lstConnRows.Count + "); якорей формы: " + nAnchors +
                (bUseAnchors ? " — сопоставление по якорям" : " — НЕТ, сопоставление по порядку УСЛОВНОЕ"));
            if (!bUseAnchors)
                log.Warn("[MATCH] якорей формы не найдено — колонки сопоставлены клеммам по порядку (i↔i), возможно неверное");
            if (nColumns != lstTerminals.Count)
                log.Warn("[MATCH] колонок К4 (" + nColumns + ") != клемм в DM (" + lstTerminals.Count +
                    ") — есть колонки без клеммы/клеммы без колонки (см. [MATCHCOL])");

            // --- 4. Таблица [MATCHCOL]: колонка ↔ (якорь) ↔ клемма ---
            for (int i = 0; i < nColumns; i++)
            {
                int nNumber = arrColNumber[i];
                DmTerminalGroup oGroup = null;
                if (nNumber >= 0) dicByNumber.TryGetValue(nNumber, out oGroup);
                int nColPoints = (oK4.Counts != null && i < oK4.Counts.Length) ? oK4.Counts[i] : -1;
                string strLine = "[MATCHCOL] колонка X=" + FmtX(oK4.Columns[i]) + " ↔ " +
                    (nNumber < 0 ? "<якоря нет>" : "№" + nNumber) +
                    (oGroup == null
                        ? (nNumber >= 0 ? " — клеммы №" + nNumber + " нет в DM" : "")
                        : " клемма '" + oGroup.Name + "' (подключений " + oGroup.ConnCount +
                          ": кабель " + oGroup.CableCount + ", провод " + oGroup.WireCount + ")") +
                    ", точек в колонке " + (nColPoints >= 0 ? nColPoints.ToString(CultureInfo.InvariantCulture) : "?");
                bool bHasGroup = oGroup != null;
                if (!bHasGroup && nColPoints > 0)
                    log.Warn(strLine + " — точки есть, клеммы нет");
                else if (bHasGroup && oGroup.ConnCount > 0 && nColPoints == 0)
                    log.Warn(strLine + " — у клеммы подключения, а точек в колонке нет");
                else if (bHasGroup && nColPoints >= 0 && nColPoints != oGroup.ConnCount)
                    log.Warn(strLine + " — число точек колонки != числу подключений клеммы");
                else
                    log.Log(strLine);
            }
            // Клеммы, чей номер не привязался ни к одной колонке
            bool[] arrColUsed = new bool[nColumns];
            for (int i = 0; i < nColumns; i++)
                if (arrColNumber[i] >= 0) arrColUsed[i] = true;
            foreach (DmTerminalGroup oGroup in lstTerminals)
            {
                if (oGroup.Number < 0) continue;
                bool bFound = false;
                for (int i = 0; i < nColumns; i++)
                    if (arrColNumber[i] == oGroup.Number) { bFound = true; break; }
                if (!bFound)
                    log.Warn("[MATCHCOL] клемма '" + oGroup.Name + "' (№" + oGroup.Number +
                        ", подключений " + oGroup.ConnCount + ") — нет якоря в форме");
            }

            // --- 5. Строки [MATCH]: по точкам геометрии ---
            int nIndex = 0;
            foreach (Pt oPoint in oAnalysis.Points)
            {
                nIndex++;
                MatchRow oMatchRow = new MatchRow();
                oMatchRow.Point = oPoint;
                oMatchRow.ColumnIndex = oK4.BindIndex(oPoint);
                if (oMatchRow.ColumnIndex >= 0) oMatchRow.ColumnX = oK4.Columns[oMatchRow.ColumnIndex];
                DmTerminalGroup oGroup = null;
                if (oMatchRow.ColumnIndex >= 0 && arrColNumber[oMatchRow.ColumnIndex] >= 0)
                    dicByNumber.TryGetValue(arrColNumber[oMatchRow.ColumnIndex], out oGroup);
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

        /// <summary>Число клеммы из полного имени: суффикс после последнего ':' как
        /// число, -1 если не число.</summary>
        private static int ParseTerminalNumber(string strName)
        {
            int nNumber;
            if (int.TryParse(SuffixAfterColon(strName).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out nNumber)) return nNumber;
            return -1;
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

        /// <summary>Суффикс имени после последнего ':' (номер клеммы в полном имени
        /// «=HII-1.1++ЯЧ67+#2-X2:1»); если ':' нет — имя целиком.</summary>
        private static string SuffixAfterColon(string strName)
        {
            if (strName == null) return "";
            int nPos = strName.LastIndexOf(':');
            return nPos >= 0 ? strName.Substring(nPos + 1) : strName;
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
