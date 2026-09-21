using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Builder CableLayoutModel (Фаза E, plan_implementation §10 «Этап 6»):
    /// группирует соединения клеммника по кабелям. Чистый модуль — только модели, без
    /// EPLAN-API. Семантика стороны = TerminalSide из модели (сторона ряда, rev.7.1):
    /// Top/Right → Right, Bottom/Left → Left, Unknown → Other. Бинарный случай: в проекте
    /// CableName == null (summary п.21б/п.24, только №31058 IsCable) → все кабельные
    /// соединения в один CableModel без имени; при появлении CableName — группировка
    /// по имени (ключ расширения заложен).</summary>
    public static class CableLayoutBuilder
    {
        public static CableLayoutModel Build(List<TerminalConnectionModel> lstModels, DiagnosticLogger log)
        {
            CableLayoutModel oLayout = new CableLayoutModel();
            if (lstModels == null || lstModels.Count == 0)
            {
                if (log != null) log.Log("[CABGROUP-SUM] соединений 0 — layout пуст");
                return oLayout;
            }

            // Группировка кабельных соединений по имени (CableName); null-имя — один
            // кабель «без имени» под ключом "": Dictionary<string,> не допускает null-ключей.
            Dictionary<string, CableModel> dicCables = new Dictionary<string, CableModel>();
            foreach (TerminalConnectionModel oM in lstModels)
            {
                if (oM == null || !oM.IsCable)
                {
                    if (oM != null) oLayout.NoCableConnections.Add(oM);
                    continue;
                }
                string strKey = oM.CableName ?? "";   // null → ключ ""; CableModel.Name хранит null
                CableModel oCable;
                if (!dicCables.TryGetValue(strKey, out oCable))
                {
                    oCable = new CableModel();
                    oCable.Name = oM.CableName;
                    dicCables[strKey] = oCable;
                    oLayout.Cables.Add(oCable);
                }
                oCable.Connections.Add(oM);
                switch (oM.Side)
                {
                    case TerminalSide.Top:
                    case TerminalSide.Right: oCable.RightConnections.Add(oM); break;
                    case TerminalSide.Bottom:
                    case TerminalSide.Left: oCable.LeftConnections.Add(oM); break;
                    default: oCable.OtherConnections.Add(oM); break;
                }
            }

            // Дамп [CABGROUP] на каждый кабель + WARN для Unknown-сторон.
            foreach (CableModel oCable in oLayout.Cables)
            {
                if (log != null)
                {
                    string strName = oCable.Name ?? "<без имени>";
                    log.Log("[CABGROUP] '" + strName + "': подключений " +
                        oCable.Connections.Count.ToString(CultureInfo.InvariantCulture) +
                        " (Left " + oCable.LeftConnections.Count.ToString(CultureInfo.InvariantCulture) +
                        ", Right " + oCable.RightConnections.Count.ToString(CultureInfo.InvariantCulture) +
                        ", Other " + oCable.OtherConnections.Count.ToString(CultureInfo.InvariantCulture) + ")");
                }
                foreach (TerminalConnectionModel oM in oCable.OtherConnections)
                    if (log != null)
                        log.Warn("[CABGROUP] кабель '" + (oCable.Name ?? "<без имени>") +
                            "': подключение клеммы '" + (oM.Terminal ?? "-") +
                            "' без определённой стороны (Unknown) — в OtherConnections");
            }

            // Итог [CABGROUP-SUM].
            if (log != null)
            {
                int nUnknown = 0;
                foreach (CableModel oCable in oLayout.Cables) nUnknown += oCable.OtherConnections.Count;
                log.Log("[CABGROUP-SUM] кабелей " + oLayout.Cables.Count.ToString(CultureInfo.InvariantCulture) +
                    ", проводных (NoCable) " + oLayout.NoCableConnections.Count.ToString(CultureInfo.InvariantCulture) +
                    ", кабельных подключений с Unknown-стороной " + nUnknown.ToString(CultureInfo.InvariantCulture) +
                    " (both-sides/multiple не воспроизведены: CableName в проекте отсутствует)");
            }
            return oLayout;
        }
    }
}
