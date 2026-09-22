using System;
using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Позиция символа кабеля (SHCP2) — результат Geometry Engine (Фаза F,
    /// plan_implementation §11/§13). Создание самого символа — Фаза G.</summary>
    public sealed class CableSymbolPlacement
    {
        public string CableName;         // имя кабеля или null (бинарный случай)
        public int CableIndex = -1;      // 0-based индекс в CableLayoutModel.Cables
        public Pt Position;              // SymbolPosition, страничные координаты
    }

    /// <summary>Результат Geometry Engine: сегменты (ветви + шины + подходы; LayerName
    /// остаётся null — слой назначит Фаза G) и позиции символов. Warnings — побудительный
    /// список для log.Warn в AnalyzeAction (builder без логгера — чистый модуль).</summary>
    public sealed class CableGeometryResult
    {
        public readonly List<Seg> Segments = new List<Seg>();
        public readonly List<CableSymbolPlacement> Symbols = new List<CableSymbolPlacement>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Параметры разводки. В проде значения — из AddInConfiguration
    /// (CableBusOffsetMm/CableSymbolOffsetMm/CablePitchMm), в unit-тестах — свои.</summary>
    public sealed class CableGeometryConfig
    {
        public double BusOffsetMm = 10.0;     // отступ шины от крайних точек группы (perp)
        public double LevelPitchMm = 8.0;     // разнос уровней шин кабелей (perp; кабель idx=0 ближе)
        public double BusLiftMm = 8.0;        // подъём шины над отступом от кончиков выводов (perp; ревизия 4)
        public double SymbolOffsetMm = 10.0;  // вынос SymbolAxis за край ряда (axis)
        public double PitchMm = 20.0;         // разнос SymbolAxis нескольких кабелей
    }

    /// <summary>Geometry Engine (Фаза F, spec 2026-09-21-fase-f-cable-geometry-design.md):
    /// чистая геометрия кабельной разводки по эталону «example/Кабель с двух сторон.pdf».
    /// Оси: Horizontal — axis=X (ряд), perp=Y; Vertical — axis=Y, perp=X; верх страницы —
    /// большая Y (урок rev.7.1). Группы стороны из CableModel: Right (Top/Right, большие
    /// perp) — шина за max(perp)+Off; Left (Bottom/Left) — за min(perp)−Off; Other
    /// (Unknown) — как Right, с WARN. Вынос по оси: H → +X, V → −Y (s=±1);
    /// SymbolAxis = max(s*axis)*s + s*Offset + idx*Pitch. Символ — среднее уровней шин
    /// задействованных групп (односторонний кабель — на уровне своей шины, подход
    /// вырождается). Без EPLAN-типов и без логгера; дампы печатает AnalyzeAction.</summary>
    public static class CableGeometryBuilder
    {
        private const double EpsLen = 1e-9;   // порог вырожденного (нулевого) подхода

        /// <summary>dStripEndAxis — крайняя колонка клеммника по оси выноса
        /// (H: max X колонок K4, V: min Y); NaN → fallback на крайнюю точку
        /// кабеля + WARN (ревизия 2: сход за габаритом ряда, как на эталоне).
        /// Уровни шин разнесены по кабелям на LevelPitchMm (ревизия 2).</summary>
        public static CableGeometryResult Build(CableLayoutModel oLayout,
            ReportOrientation eOrientation, CableGeometryConfig oCfg, double dStripEndAxis)
        {
            CableGeometryResult oRes = new CableGeometryResult();
            if (oLayout == null || oCfg == null)
            {
                oRes.Warnings.Add("[GEOM] Build: layout или config == null — вычислять нечего");
                return oRes;
            }
            bool bV = eOrientation == ReportOrientation.Vertical;
            double sDir = bV ? -1.0 : 1.0;    // H → +X (как на эталоне), V → −Y

            for (int nCable = 0; nCable < oLayout.Cables.Count; nCable++)
            {
                CableModel oCable = oLayout.Cables[nCable];
                if (oCable == null) continue;
                string strName = oCable.Name ?? "<без имени>";

                List<TerminalConnectionModel> oRight = oCable.RightConnections;
                List<TerminalConnectionModel> oLeft = oCable.LeftConnections;
                List<TerminalConnectionModel> oOther = oCable.OtherConnections;
                int nTotal = CountOf(oRight) + CountOf(oLeft) + CountOf(oOther);
                if (nTotal == 0)
                {
                    oRes.Warnings.Add("[GEOM] кабель '" + strName +
                        "': 0 точек — пропущен (сегменты и символ не созданы)");
                    continue;
                }
                if (CountOf(oOther) > 0)
                    oRes.Warnings.Add("[GEOM] кабель '" + strName + "': " + CountOf(oOther) +
                        " точек Unknown-стороны (Other) — шина по общему правилу, правило не валидировано данными");

                // Уровни шин групп: Right/Other — за max(perp)+Off, Left — за min(perp)−Off.
                double dBusRight = CountOf(oRight) > 0 ? MaxPerpOf(oRight, bV) + oCfg.BusOffsetMm + oCfg.BusLiftMm + nCable * oCfg.LevelPitchMm : double.NaN;
                double dBusLeft = CountOf(oLeft) > 0 ? MinPerpOf(oLeft, bV) - (oCfg.BusOffsetMm + oCfg.BusLiftMm + nCable * oCfg.LevelPitchMm) : double.NaN;
                double dBusOther = CountOf(oOther) > 0 ? MaxPerpOf(oOther, bV) + oCfg.BusOffsetMm + oCfg.BusLiftMm + nCable * oCfg.LevelPitchMm : double.NaN;

                // Сход за габаритом ряда (ревизия 2): база — крайняя колонка клеммника
                // по оси выноса; К4 невалиден — крайняя точка кабеля + WARN.
                double dMaxSigned = double.NegativeInfinity;
                CollectMaxSigned(oRight, bV, sDir, ref dMaxSigned);
                CollectMaxSigned(oLeft, bV, sDir, ref dMaxSigned);
                CollectMaxSigned(oOther, bV, sDir, ref dMaxSigned);
                if (double.IsNaN(dStripEndAxis))
                {
                    oRes.Warnings.Add("[GEOM] кабель '" + strName +
                        "': край ряда не определён (К4) — SymbolAxis от крайней точки кабеля");
                }
                double dBaseAxis = double.IsNaN(dStripEndAxis) ? dMaxSigned * sDir : dStripEndAxis;
                double dSymbolAxis = dBaseAxis + sDir * oCfg.SymbolOffsetMm + nCable * oCfg.PitchMm;

                // Символ — среднее уровней шин задействованных групп.
                double dSymbolPerp = MeanBus(dBusRight, dBusLeft, dBusOther);

                AddGroup(oRes, oRight, dBusRight, bV, dSymbolAxis, dSymbolPerp, sDir, oCfg);
                AddGroup(oRes, oLeft, dBusLeft, bV, dSymbolAxis, dSymbolPerp, sDir, oCfg);
                AddGroup(oRes, oOther, dBusOther, bV, dSymbolAxis, dSymbolPerp, sDir, oCfg);

                oCable.SymbolPosition = PtOf(bV, dSymbolAxis, dSymbolPerp);
                CableSymbolPlacement oSym = new CableSymbolPlacement();
                oSym.CableName = oCable.Name;
                oSym.CableIndex = nCable;
                oSym.Position = oCable.SymbolPosition;
                oRes.Symbols.Add(oSym);
            }
            return oRes;
        }

        /// <summary>Сегменты одной группы стороны: ветвь на каждую точку (перпендикулярно
        /// ряду до уровня шины — ревизия 4: шина поднята ЦЕЛИКОМ на BusLiftMm, хвоста-уголка
        /// нет), шина от крайней точки группы, ПРОТИВОПОЛОЖНОЙ направлению выноса (H: min
        /// axis; V: max axis), до SymbolAxis, подход к символу
        /// (нулевой подход одностороннего кабеля не создаётся).</summary>
        private static void AddGroup(CableGeometryResult oRes, List<TerminalConnectionModel> lstGroup,
            double dBusPerp, bool bV, double dSymbolAxis, double dSymbolPerp, double sDir,
            CableGeometryConfig oCfg)
        {
            if (lstGroup == null || lstGroup.Count == 0 || double.IsNaN(dBusPerp)) return;
            double dBusStart = 0.0;
            bool bFirst = true;
            foreach (TerminalConnectionModel oM in lstGroup)
            {
                if (oM == null) continue;
                double dAxis = AxisOf(oM.ConnectionPoint, bV);
                if (bFirst) { dBusStart = dAxis; bFirst = false; }
                else dBusStart = bV ? Math.Max(dBusStart, dAxis) : Math.Min(dBusStart, dAxis);
                Seg oBranch = new Seg();
                oBranch.A = oM.ConnectionPoint;
                oBranch.B = PtOf(bV, dAxis, dBusPerp);
                oRes.Segments.Add(oBranch);
            }
            Seg oBus = new Seg();
            oBus.A = PtOf(bV, dBusStart, dBusPerp);
            oBus.B = PtOf(bV, dSymbolAxis, dBusPerp);
            oRes.Segments.Add(oBus);
            if (Math.Abs(dBusPerp - dSymbolPerp) > EpsLen)
            {
                Seg oApproach = new Seg();
                oApproach.A = PtOf(bV, dSymbolAxis, dBusPerp);
                oApproach.B = PtOf(bV, dSymbolAxis, dSymbolPerp);
                oRes.Segments.Add(oApproach);
            }
        }

        private static int CountOf(List<TerminalConnectionModel> lst)
        {
            // Только non-null — согласовано с null-guard'ами хелперов: группа из
            // одних null не должна давать CountOf > 0 (иначе -Inf-шина без WARN).
            if (lst == null) return 0;
            int nCount = 0;
            foreach (TerminalConnectionModel oM in lst)
                if (oM != null) nCount++;
            return nCount;
        }

        private static double AxisOf(Pt oPt, bool bV) { return bV ? oPt.Y : oPt.X; }
        private static double PerpOf(Pt oPt, bool bV) { return bV ? oPt.X : oPt.Y; }

        /// <summary>Страничная точка из осевых координат: H — (axis, perp), V — (perp, axis).</summary>
        private static Pt PtOf(bool bV, double dAxis, double dPerp)
        {
            return bV ? new Pt(dPerp, dAxis) : new Pt(dAxis, dPerp);
        }

        private static double MaxPerpOf(List<TerminalConnectionModel> lst, bool bV)
        {
            double dBest = double.NegativeInfinity;
            foreach (TerminalConnectionModel oM in lst)
                if (oM != null) dBest = Math.Max(dBest, PerpOf(oM.ConnectionPoint, bV));
            return dBest;
        }

        private static double MinPerpOf(List<TerminalConnectionModel> lst, bool bV)
        {
            double dBest = double.PositiveInfinity;
            foreach (TerminalConnectionModel oM in lst)
                if (oM != null) dBest = Math.Min(dBest, PerpOf(oM.ConnectionPoint, bV));
            return dBest;
        }

        private static void CollectMaxSigned(List<TerminalConnectionModel> lst, bool bV, double sDir,
            ref double dMaxSigned)
        {
            if (lst == null) return;
            foreach (TerminalConnectionModel oM in lst)
            {
                if (oM == null) continue;
                double dSigned = sDir * AxisOf(oM.ConnectionPoint, bV);
                if (dSigned > dMaxSigned) dMaxSigned = dSigned;
            }
        }

        private static double MeanBus(double dBusRight, double dBusLeft, double dBusOther)
        {
            double dSum = 0.0;
            int nCount = 0;
            if (!double.IsNaN(dBusRight)) { dSum += dBusRight; nCount++; }
            if (!double.IsNaN(dBusLeft)) { dSum += dBusLeft; nCount++; }
            if (!double.IsNaN(dBusOther)) { dSum += dBusOther; nCount++; }
            return nCount > 0 ? dSum / nCount : double.NaN;
        }
    }
}
