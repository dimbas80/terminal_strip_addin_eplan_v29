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

    /// <summary>Результат Geometry Engine: сегменты (ветви + шины + вертикали-подходы +
    /// заходы + джампы; LayerName остаётся null — слой назначит Фаза G) и позиции
    /// символов. Warnings — побудительный список для log.Warn в AnalyzeAction
    /// (builder без логгера — чистый модуль).</summary>
    public sealed class CableGeometryResult
    {
        public readonly List<Seg> Segments = new List<Seg>();
        public readonly List<CableSymbolPlacement> Symbols = new List<CableSymbolPlacement>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Параметры разводки (rev.10.1). В проде значения — из AddInConfiguration
    /// (CableBusOffsetMm/CableLevelPitchMm/CableBusLiftMm/CableApproachOffsetMm/
    /// CableApproachPitchMm/CableSymbolColumnOffsetMm/CableSymbolGapMm/
    /// CableSymbolStackPitchMm), в unit-тестах — свои.</summary>
    public sealed class CableGeometryConfig
    {
        public double BusOffsetMm = 10.0;          // отступ шины от крайних точек группы (perp)
        public double LevelPitchMm = 8.0;          // разнос уровней шин кабелей (perp; кабель idx=0 ближе)
        public double BusLiftMm = 8.0;             // подъём шины над отступом от кончиков выводов (perp; ревизия 4)
        public double ApproachOffsetMm = 10.0;     // отступ вертикали-подхода от края ряда (axis)
        public double ApproachPitchMm = 8.0;       // шаг подходов соседних кабелей (axis)
        public double SymbolColumnOffsetMm = 16.0; // колонка символов за последним подходом (axis)
        public double SymbolGapMm = 8.0;           // зазор линия–символ (для CABDCP2; в UI позже)
        public double SymbolStackPitchMm = 16.0;   // шаг стопки символов (perp; из эталона rev.10.0)
    }

    /// <summary>Geometry Engine (Фаза F; rev.10.1 — spec
    /// 2026-09-22-fase-g-graphics-design.md §9, эталон «Кабель с двух сторон.pdf»):
    /// чистая геометрия кабельной разводки. Оси: Horizontal — axis=X (ряд), perp=Y;
    /// Vertical — axis=Y, perp=X; верх страницы — большая Y (урок rev.7.1). Группы
    /// стороны из CableModel: Right (Top/Right, большие perp) — шина за max(perp)+Off;
    /// Left (Bottom/Left) — за min(perp)−Off; Other (Unknown) — как Right, с WARN.
    /// Ревизия rev.10.1: вынос по оси H → +X, V → −Y (s=±1); подход кабеля i —
    /// вертикаль на X_i = край ряда + ApproachOffset + i·ApproachPitch (шины
    /// заканчиваются на X_i; край ряда NaN — fallback на крайнюю точку кабеля + WARN);
    /// двусторонний кабель (Right и Left непусты) — одна вертикаль на X_i между
    /// своими уровнями шин (диапазон расширяется на уровень символа Y_i) и
    /// горизонтальный заход на Y_i до SymX − Gap; односторонний — шина сразу до
    /// SymX − Gap и вертикальный джамп там до Y_i (вырожденные сегменты не создаются);
    /// колонка символов SymX = последний подход двусторонних (нет — последний подход
    /// всех) + ColumnOffset; стопка символов Y_i = anchor + (N−1−i)·StackPitch,
    /// якорь — минимальный уровень шин всех кабелей, N — число кабелей раскладки
    /// (пропущенный кабель сохраняет свой слот — как с уровнями шин). Символ —
    /// точка (SymX, Y_i). Без EPLAN-типов и без логгера; дампы печатает
    /// AnalyzeAction.</summary>
    public static class CableGeometryBuilder
    {
        private const double EpsLen = 1e-9;   // порог вырожденного (нулевого) сегмента

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
            int nCount = oLayout.Cables.Count;

            // --- Проход 1: уровни шин и оси подходов всех кабелей. Стопка и колонка
            // символов зависят от ВСЕХ кабелей (якорь — минимум по всем уровням шин,
            // SymX — максимум подходов), поэтому сначала собираем данные, потом рисуем. ---
            double[] arrBusRight = new double[nCount];
            double[] arrBusLeft = new double[nCount];
            double[] arrBusOther = new double[nCount];
            double[] arrX = new double[nCount];
            double dAnchor = double.PositiveInfinity;              // min по всем непустым уровням шин
            double dMaxApproachSigned = double.NegativeInfinity;   // последний подход (все кабели)
            double dMaxBilateralSigned = double.NegativeInfinity;  // последний подход (двусторонние)
            bool bHasBilateral = false;

            for (int nCable = 0; nCable < nCount; nCable++)
            {
                arrBusRight[nCable] = double.NaN;
                arrBusLeft[nCable] = double.NaN;
                arrBusOther[nCable] = double.NaN;
                arrX[nCable] = double.NaN;

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
                arrBusRight[nCable] = dBusRight;
                arrBusLeft[nCable] = dBusLeft;
                arrBusOther[nCable] = dBusOther;
                if (!double.IsNaN(dBusRight) && dBusRight < dAnchor) dAnchor = dBusRight;
                if (!double.IsNaN(dBusLeft) && dBusLeft < dAnchor) dAnchor = dBusLeft;
                if (!double.IsNaN(dBusOther) && dBusOther < dAnchor) dAnchor = dBusOther;

                // Ось подхода (rev.10.1): за краем ряда по оси выноса; К4 невалиден —
                // крайняя точка кабеля + WARN (подход от неё).
                double dMaxSigned = double.NegativeInfinity;
                CollectMaxSigned(oRight, bV, sDir, ref dMaxSigned);
                CollectMaxSigned(oLeft, bV, sDir, ref dMaxSigned);
                CollectMaxSigned(oOther, bV, sDir, ref dMaxSigned);
                if (double.IsNaN(dStripEndAxis))
                {
                    oRes.Warnings.Add("[GEOM] кабель '" + strName +
                        "': край ряда не определён (К4) — подход от крайней точки кабеля");
                }
                double dBaseAxis = double.IsNaN(dStripEndAxis) ? dMaxSigned * sDir : dStripEndAxis;
                double dXi = dBaseAxis + sDir * (oCfg.ApproachOffsetMm + nCable * oCfg.ApproachPitchMm);
                arrX[nCable] = dXi;
                double dSigned = sDir * dXi;
                if (dSigned > dMaxApproachSigned) dMaxApproachSigned = dSigned;
                if (CountOf(oRight) > 0 && CountOf(oLeft) > 0)
                {
                    bHasBilateral = true;
                    if (dSigned > dMaxBilateralSigned) dMaxBilateralSigned = dSigned;
                }
            }
            if (double.IsNegativeInfinity(dMaxApproachSigned))
                return oRes;   // ни одного кабеля с точками — предупреждения уже в списке

            // Колонка символов: за последним подходом двусторонних (нет двусторонних —
            // за последним подходом всех); конец линий — с зазором Gap перед символом.
            double dLastSigned = bHasBilateral ? dMaxBilateralSigned : dMaxApproachSigned;
            double dSymAxis = sDir * dLastSigned + sDir * oCfg.SymbolColumnOffsetMm;
            double dEndAxis = dSymAxis - sDir * oCfg.SymbolGapMm;

            // --- Проход 2: сегменты и символы ---
            for (int nCable = 0; nCable < nCount; nCable++)
            {
                if (double.IsNaN(arrX[nCable])) continue;   // null/пустой кабель из прохода 1
                CableModel oCable = oLayout.Cables[nCable];
                List<TerminalConnectionModel> oRight = oCable.RightConnections;
                List<TerminalConnectionModel> oLeft = oCable.LeftConnections;
                List<TerminalConnectionModel> oOther = oCable.OtherConnections;
                bool bBilateral = CountOf(oRight) > 0 && CountOf(oLeft) > 0;

                // Стопка символов: idx 0 сверху, idx N−1 на якоре (самом глубоком уровне шин).
                double dY = dAnchor + (nCount - 1 - nCable) * oCfg.SymbolStackPitchMm;

                // Шины двустороннего заканчиваются на подходе X_i, одностороннего —
                // сразу на SymX − Gap (до джампа к символу).
                double dBusEndAxis = bBilateral ? arrX[nCable] : dEndAxis;
                AddGroup(oRes, oRight, arrBusRight[nCable], bV, dBusEndAxis);
                AddGroup(oRes, oLeft, arrBusLeft[nCable], bV, dBusEndAxis);
                AddGroup(oRes, oOther, arrBusOther[nCable], bV, dBusEndAxis);

                if (bBilateral)
                {
                    // Вертикаль-подход на X_i: между нижним и верхним уровнями шин;
                    // диапазон расширяется, если уровень символа Y_i вне его.
                    double dLo = dY;
                    double dHi = dY;
                    if (!double.IsNaN(arrBusRight[nCable])) { dLo = Math.Min(dLo, arrBusRight[nCable]); dHi = Math.Max(dHi, arrBusRight[nCable]); }
                    if (!double.IsNaN(arrBusLeft[nCable])) { dLo = Math.Min(dLo, arrBusLeft[nCable]); dHi = Math.Max(dHi, arrBusLeft[nCable]); }
                    if (!double.IsNaN(arrBusOther[nCable])) { dLo = Math.Min(dLo, arrBusOther[nCable]); dHi = Math.Max(dHi, arrBusOther[nCable]); }
                    if (dHi - dLo > EpsLen)
                    {
                        Seg oRiser = new Seg();
                        oRiser.A = PtOf(bV, arrX[nCable], dLo);
                        oRiser.B = PtOf(bV, arrX[nCable], dHi);
                        oRes.Segments.Add(oRiser);
                    }
                    // Горизонтальный заход на уровне символа: X_i → SymX − Gap.
                    if (Math.Abs(dEndAxis - arrX[nCable]) > EpsLen)
                    {
                        Seg oEntry = new Seg();
                        oEntry.A = PtOf(bV, arrX[nCable], dY);
                        oEntry.B = PtOf(bV, dEndAxis, dY);
                        oRes.Segments.Add(oEntry);
                    }
                }
                else
                {
                    // Односторонний: вертикальный джамп до уровня символа на каждом
                    // уровне шины (K100: шина сверху — символ внизу стопки).
                    AddJog(oRes, arrBusRight[nCable], bV, dEndAxis, dY);
                    AddJog(oRes, arrBusLeft[nCable], bV, dEndAxis, dY);
                    AddJog(oRes, arrBusOther[nCable], bV, dEndAxis, dY);
                }

                oCable.SymbolPosition = PtOf(bV, dSymAxis, dY);
                CableSymbolPlacement oSym = new CableSymbolPlacement();
                oSym.CableName = oCable.Name;
                oSym.CableIndex = nCable;
                oSym.Position = oCable.SymbolPosition;
                oRes.Symbols.Add(oSym);
            }
            return oRes;
        }

        /// <summary>Сегменты одной группы стороны: ветвь на каждую точку (перпендикулярно
        /// ряду до уровня шины — ревизия 4: шина поднята ЦЕЛИКОМ на BusLiftMm, хвоста-
        /// уголка нет), шина от крайней точки группы, ПРОТИВОПОЛОЖНОЙ направлению выноса
        /// (H: min axis; V: max axis), до dBusEndAxis (двусторонний — подход X_i,
        /// односторонний — SymX − Gap). Вертикаль-подход/заход/джамп строит Build
        /// (rev.10.1).</summary>
        private static void AddGroup(CableGeometryResult oRes, List<TerminalConnectionModel> lstGroup,
            double dBusPerp, bool bV, double dBusEndAxis)
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
            oBus.B = PtOf(bV, dBusEndAxis, dBusPerp);
            oRes.Segments.Add(oBus);
        }

        /// <summary>Вертикальный джамп одностороннего кабеля на конце линий (SymX − Gap):
        /// от уровня шины до уровня символа; вырожденный (уровни совпали) — не создаётся.</summary>
        private static void AddJog(CableGeometryResult oRes, double dBusPerp, bool bV,
            double dJogAxis, double dY)
        {
            if (double.IsNaN(dBusPerp) || Math.Abs(dY - dBusPerp) <= EpsLen) return;
            Seg oJog = new Seg();
            oJog.A = PtOf(bV, dJogAxis, dBusPerp);
            oJog.B = PtOf(bV, dJogAxis, dY);
            oRes.Segments.Add(oJog);
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
    }
}
