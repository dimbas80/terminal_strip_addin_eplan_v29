using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Геометрическая сторона подключения относительно ряда/столбца клеммы.
    /// Horizontal-форма: Top (Y < ряда), Bottom (Y > ряда); Vertical-форма: Left (X <
    /// столбца), Right (X > столбца). Unknown — ряд/столбец не определился (Этап 5
    /// уточняет Side по правилам §9).</summary>
    public enum TerminalSide { Top, Bottom, Left, Right, Unknown }

    /// <summary>Модель конкретного подключения (Фаза D, plan_implementation §4.4).
    /// Одна точка геометрии = одно подключение клеммы; связка с конкретным DmRow —
    /// эвристика по геометрической стороне («верх ↔ Int, низ ↔ Ext» для Horizontal —
    /// данные пользователя 21.09.2026, rev.7.1; «лево ↔ Ext, право ↔ Int» для Vertical —
    /// данных нет), валидируется дампом [TCM].</summary>
    public sealed class TerminalConnectionModel
    {
        public string Terminal;          // полное имя клеммы из DM (TerminalKey) или null
        public int TerminalNumber = -1;  // номер клеммы (суффикс после ':')
        public string ConnectionName;    // DmRow.ConnectionName (null = не сопоставлено)
        public string PinName;           // DmRow.FunctionPinName
        public int PinIndex = -1;        // DmRow.PinIndex
        // Поля §4.4 мастер-плана, заполняются на Этапе 6 (ссылки назначения / обозначения):
        public string Destination;       // пункт назначения (null до Этапа 6)
        public string ConnectionDesignation; // обозначение соединения (null до Этапа 6)
        public Pt ConnectionPoint;       // точка подключения (геометрия)
        public int ColumnIndex = -1;     // колонка К4 (BindIndex); -1 = сирота
        public TerminalSide Side = TerminalSide.Unknown;
        public bool IsCable;             // кабель/провод (DmRow: CableName != null || №31058)
        public string CableName;         // DmRow.CableName (null = провод)
        public bool HasConn;             // DmRow.HasConn
        public string BridgePeer;        // DmRow.PeerName (для мостов) или null
        public bool IsBridge;            // точка — лист моста К2 (из LeadAnalysis.PointIsBridge)
    }

    /// <summary>Чистая геометрия стороны (без EPLAN): точка сверху/снизу (Horizontal)
    /// или слева/справа (Vertical) относительно координаты ряда/столбца клеммы на
    /// перпендикулярной оси. Допуск TolPerpMm — толщина ряда клемм: точки ближе
    /// TolPerpMm к ряду считаются на нём (Unknown).</summary>
    public static class TerminalGeometry
    {
        public const double TolPerpMm = 1.0;

        /// <summary>Сторона точки относительно перпендикулярной координаты ряда.
        /// dPerp = Y ряда (Horizontal) или X столбца (Vertical); bVertical — ось
        /// ориентации. Возвращает Side в осях формы (Top/Bottom для Horizontal,
        /// Left/Right для Vertical), Unknown при |Δ| ≤ TolPerpMm.
        /// rev.7.1 (пользователь, 21.09.2026): на странице EPLAN верх — БОЛЬШАЯ Y
        /// (точка вывода вверх имеет Y > Y ряда), поэтому dDelta>0 = Top (а не Bottom,
        /// как было до rev.7.1 — метки были инвертированы).</summary>
        public static TerminalSide PointSide(bool bVertical, Pt oPoint, double dPerp)
        {
            double dDelta = bVertical ? (oPoint.X - dPerp) : (oPoint.Y - dPerp);
            if (dDelta > TolPerpMm) return bVertical ? TerminalSide.Right : TerminalSide.Top;
            if (dDelta < -TolPerpMm) return bVertical ? TerminalSide.Left : TerminalSide.Bottom;
            return TerminalSide.Unknown;
        }
    }

    /// <summary>Builder TerminalConnectionModel (Фаза D): по каждой точке свода [MATCH]
    /// определяет конкретный DmRow (Connection) эвристикой по геометрической стороне и
    /// геометрическую Side. Чистый модуль — только Pt/Seg/строка, без EPLAN-API.</summary>
    public static class TerminalConnectionModelBuilder
    {
        /// <summary>Перпендикулярная координата ряда клеммы для колонки (Horizontal —
        /// Y ряда; Vertical — X столбца). Из стубов-маркеров (EPLAN100) колонки: медиана
        /// середины стубов, чья координата оси совпадает с колонкой (в пределах
        /// полушага). NaN — ряд не определился. Для Horizontal стуб вертикален
        /// (ось=X, перпендикуляр=Y); для Vertical — горизонтален (ось=Y, перпендикуляр=X).</summary>
        private static double ColumnPerpRef(LeadAnalysis oAnalysis, K4Report oK4, int nCol,
            Dictionary<int, double> oCache)
        {
            if (oK4 == null || nCol < 0 || nCol >= oK4.Columns.Count) return double.NaN;
            double dCached;
            if (oCache != null && oCache.TryGetValue(nCol, out dCached)) return dCached;
            bool bV = oK4.Orientation == ReportOrientation.Vertical;
            double dAxisCol = oK4.Columns[nCol];
            List<double> lstPerp = new List<double>();
            foreach (Seg oStub in oAnalysis.Stubs)
            {
                double dStubAxis = bV ? oStub.A.Y : oStub.A.X;      // ось стуба
                if (System.Math.Abs(dStubAxis - dAxisCol) > oK4.Half) continue;
                double dPerp = bV ? (oStub.A.X + oStub.B.X) / 2.0   // Vertical: перпендикуляр = X
                                   : (oStub.A.Y + oStub.B.Y) / 2.0; // Horizontal: перпендикуляр = Y
                lstPerp.Add(dPerp);
            }
            if (lstPerp.Count == 0) return double.NaN;
            lstPerp.Sort();
            double dResult = lstPerp[lstPerp.Count / 2];
            if (oCache != null) oCache[nCol] = dResult;
            return dResult;
        }

        /// <summary>Сборка моделей по точкам свода [MATCH]. Связка точка ↔ DmRow:
        /// Top/Right-точки ← Int-пул, Bottom/Left-точки ← Ext-пул (по порядку; rev.7.1:
        /// исправлено по данным пользователя — «верх ↔ Int, низ ↔ Ext»);
        /// Unknown/переполнение пула — по порядку из оставшихся DmRow; сироты (клемма
        /// не сопоставлена) — модель с пустым Connection. Валидируется дампом [TCM].
        /// КОНТРАКТ ПОРЯДКА: список группируется по клеммам (сироты — в конце), НЕ в
        /// порядке Points/lstMatch — потребители Фазы E/F не должны полагаться на
        /// lstTcm[i] ↔ Points[i].</summary>
        public static List<TerminalConnectionModel> Build(
            List<MatchRow> lstMatch, DmReport oDm, LeadAnalysis oAnalysis, K4Report oK4,
            string strTarget, DiagnosticLogger log)
        {
            List<TerminalConnectionModel> lstModels = new List<TerminalConnectionModel>();
            if (lstMatch == null || lstMatch.Count == 0 || oDm == null || oAnalysis == null)
                return lstModels;
            Dictionary<int, double> dicPerpCache = new Dictionary<int, double>();
            bool bV = oK4 != null && oK4.Orientation == ReportOrientation.Vertical;

            // 1) Точки по клемме (полное имя); lstMatch[i] ↔ Points[i].
            Dictionary<string, List<int>> dicByTerm = new Dictionary<string, List<int>>();
            List<int> lstOrphans = new List<int>();
            for (int i = 0; i < lstMatch.Count; i++)
            {
                string strTerm = lstMatch[i].TerminalName;
                if (strTerm == null) lstOrphans.Add(i);
                else
                {
                    List<int> lst;
                    if (!dicByTerm.TryGetValue(strTerm, out lst)) { lst = new List<int>(); dicByTerm[strTerm] = lst; }
                    lst.Add(i);
                }
            }

            // 2) DmRow целевого клеммника (без Bridge), по клемме.
            Dictionary<string, List<DmRow>> dicRowsByTerm = new Dictionary<string, List<DmRow>>();
            foreach (DmRow oRow in oDm.Rows)
            {
                if (oRow.StripName != strTarget || oRow.Side == "Bridge") continue;
                List<DmRow> lst;
                if (!dicRowsByTerm.TryGetValue(oRow.TerminalName, out lst)) { lst = new List<DmRow>(); dicRowsByTerm[oRow.TerminalName] = lst; }
                lst.Add(oRow);
            }

            // 3) По клемме: стороны точек, пулы Ext/Int, попарное назначение.
            foreach (KeyValuePair<string, List<int>> oEntry in dicByTerm)
            {
                List<DmRow> lstRows;
                dicRowsByTerm.TryGetValue(oEntry.Key, out lstRows);
                if (lstRows == null) lstRows = new List<DmRow>();

                List<TerminalSide> lstSides = new List<TerminalSide>();
                foreach (int nIdx in oEntry.Value)
                {
                    MatchRow oRow = lstMatch[nIdx];
                    double dRef = oRow.ColumnIndex >= 0
                        ? ColumnPerpRef(oAnalysis, oK4, oRow.ColumnIndex, dicPerpCache) : double.NaN;
                    lstSides.Add(double.IsNaN(dRef)
                        ? TerminalSide.Unknown
                        : TerminalGeometry.PointSide(bV, oRow.Point, dRef));
                }

                List<DmRow> lstExt = new List<DmRow>(), lstInt = new List<DmRow>();
                foreach (DmRow oRow in lstRows)
                    (oRow.Side == "Int" ? lstInt : lstExt).Add(oRow);

                List<DmRow>[] arrPool = { lstExt, lstInt };
                int[] arrPoolIdx = { 0, 0 };
                List<DmRow> lstRemain = new List<DmRow>(lstRows);
                for (int k = 0; k < oEntry.Value.Count; k++)
                {
                    int nIdx = oEntry.Value[k];
                    TerminalSide oSide = lstSides[k];
                    // rev.7.1: пул по исправленной стороне — Top(верх)↔Int, Bottom(низ)↔Ext
                    // (данные пользователя: клемма 31 — кабель внизу на Ext, провод вверху на Int);
                    // Left/Right (Vertical) — без данных, оставлено Left↔Ext, Right↔Int.
                    int nPool;
                    if (oSide == TerminalSide.Top || oSide == TerminalSide.Right) nPool = 1;
                    else if (oSide == TerminalSide.Bottom || oSide == TerminalSide.Left) nPool = 0;
                    else nPool = -1;
                    DmRow oPicked = null;
                    if (nPool >= 0 && arrPoolIdx[nPool] < arrPool[nPool].Count)
                        oPicked = arrPool[nPool][arrPoolIdx[nPool]++];
                    if (oPicked == null)
                    {
                        foreach (DmRow oRow in lstRemain)
                            if (oRow != null) { oPicked = oRow; break; }
                    }
                    if (oPicked != null) lstRemain.Remove(oPicked);
                    lstModels.Add(MakeModel(lstMatch[nIdx], oPicked, oSide, IsBridge(oAnalysis, nIdx)));
                }
            }

            // 4) Сироты (клемма не сопоставлена) — модель с пустым Connection.
            foreach (int nIdx in lstOrphans)
            {
                double dRef = lstMatch[nIdx].ColumnIndex >= 0
                    ? ColumnPerpRef(oAnalysis, oK4, lstMatch[nIdx].ColumnIndex, dicPerpCache) : double.NaN;
                lstModels.Add(MakeModel(lstMatch[nIdx], null,
                    double.IsNaN(dRef) ? TerminalSide.Unknown
                        : TerminalGeometry.PointSide(bV, lstMatch[nIdx].Point, dRef),
                    IsBridge(oAnalysis, nIdx)));
            }

            // 5) Дамп [TCM] + итог.
            int nBridgeLeaves = 0, nNoConn = 0;
            foreach (TerminalConnectionModel oM in lstModels)
            {
                if (oM.IsBridge) nBridgeLeaves++;
                if (oM.ConnectionName == null && !oM.IsBridge) nNoConn++;
                string strConn = oM.ConnectionName ?? (oM.IsBridge ? "<мост>" : "<нет сопоставления>");
                string strCable = oM.IsCable ? (oM.CableName ?? "№31058") : "<провод>";
                log.Log("[TCM] #" + oM.TerminalNumber.ToString(CultureInfo.InvariantCulture) + " '" +
                    (oM.Terminal ?? "-") + "' " + oM.Side + " (" +
                    oM.ConnectionPoint.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oM.ConnectionPoint.Y.ToString("F3", CultureInfo.InvariantCulture) +
                    ") -> conn='" + strConn + "' pin=" + oM.PinIndex.ToString(CultureInfo.InvariantCulture) +
                    " cable=" + strCable +
                    (oM.BridgePeer != null ? " bridge->" + oM.BridgePeer : "") +
                    (oM.ConnectionName == null && !oM.IsBridge ? " [СИРОТА]" : ""));
                if (oM.ConnectionName == null && !oM.IsBridge)
                    log.Warn("[TCM] точка без сопоставления (клемма '" + (oM.Terminal ?? "-") +
                        "'): связи с DmRow нет");
            }
            log.Log("[INFO] [TCM-SUM] моделей " + lstModels.Count +
                ": без сопоставления Connection " + nNoConn + ", листьев моста " + nBridgeLeaves +
                " (связка точка↔Connection — эвристика по стороне, см. [TCM])");
            return lstModels;
        }

        private static bool IsBridge(LeadAnalysis oAnalysis, int nIdx)
        {
            return oAnalysis.PointIsBridge != null && nIdx < oAnalysis.PointIsBridge.Count
                && oAnalysis.PointIsBridge[nIdx];
        }

        private static TerminalConnectionModel MakeModel(MatchRow oRow, DmRow oDmRow,
            TerminalSide oSide, bool bIsBridge)
        {
            TerminalConnectionModel oM = new TerminalConnectionModel();
            oM.Terminal = oRow.TerminalName;
            oM.TerminalNumber = AnchorResolver.ParseTerminalNumber(oRow.TerminalName ?? "");
            oM.ConnectionPoint = oRow.Point;
            oM.ColumnIndex = oRow.ColumnIndex;
            oM.Side = oSide;
            oM.IsBridge = bIsBridge;
            if (oDmRow != null)
            {
                oM.ConnectionName = oDmRow.ConnectionName;
                oM.PinName = oDmRow.PinName;
                oM.PinIndex = oDmRow.PinIndex;
                oM.IsCable = MatchBuilder.IsCableRow(oDmRow);
                oM.CableName = oDmRow.CableName;
                oM.HasConn = oDmRow.HasConn;
                oM.BridgePeer = oDmRow.PeerName;
            }
            return oM;
        }
    }
}
