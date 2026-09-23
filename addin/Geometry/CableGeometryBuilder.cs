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
    /// заходы; LayerName остаётся null — слой назначит Фаза G) и позиции
    /// символов. Warnings — побудительный список для log.Warn в AnalyzeAction
    /// (builder без логгера — чистый модуль).</summary>
    public sealed class CableGeometryResult
    {
        public readonly List<Seg> Segments = new List<Seg>();
        public readonly List<CableSymbolPlacement> Symbols = new List<CableSymbolPlacement>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Параметры разводки (rev.10.7). В проде значения — из AddInConfiguration
    /// (CableBusOffsetMm/CableLevelPitchMinMm/CableBusLiftMm/CableApproachOffsetMm/
    /// CableApproachPitchMm/CableSymbolColumnOffsetMm; размер символа A×B — из замера
    /// [SYMSIZE]), в unit-тестах — свои.</summary>
    public sealed class CableGeometryConfig
    {
        public double BusOffsetMm = 10.0;          // отступ шины от крайних точек группы (perp)
        public double LevelPitchMinMm = 8.0;       // минимум шага уровней шин (perp; правило 2: расчётный = max(min, B·min/14))
        public double BusLiftMm = 8.0;             // подъём шины над отступом от кончиков выводов (perp; ревизия 4)
        public double ApproachOffsetMm = 10.0;     // отступ вертикали-подхода от края ряда (axis)
        public double ApproachPitchMm = 8.0;       // шаг подходов соседних кабелей (axis)
        public double SymbolColumnOffsetMm = 16.0; // колонка символов за последним подходом (axis)
        public double SymbolWidthMm = 14.0;        // ширина символа A — габарит по страничной X (замер [SYMSIZE]; фолбэк 14 — CABDCP2)
        public double SymbolHeightMm = 14.0;       // высота символа B — габарит по страничной Y (замер [SYMSIZE]; входит в правило 2 и зазор/шаг колонок Vertical — rev.10.11)
    }

    /// <summary>Geometry Engine (Фаза F). Размер символа A×B — из замера [SYMSIZE]
    /// (фолбэк 14×14 — CABDCP2, rev.10.7) — управляет геометрией. Правило 2:
    /// шаг уровней шин = max(LevelPitchMinMm, B·LevelPitchMinMm/14).
    /// rev.10.11: зазор линия–символ и шаг колонок лесенки — от габарита
    /// символа ВДОЛЬ оси выноса (H — ось = страничная X → A; V — ось = Y → B;
    /// символ вставляется без поворота).
    /// rev.10.10 (шаг 4в Фазы G, решение пользователя 23.09.2026 по прогону
    /// rev.10.9): ВСЕ символы привязаны к УРОВНЯМ ШИН — синтетическая стопка и
    /// якорь упразднены. Ряд символа: односторонний — уровень первой непустой
    /// группы (Right→Left→Other, при нескольких групп WARN); двусторонний —
    /// уровень busRight, ЕСЛИ он свободен (нет другого символа на этом уровне),
    /// иначе busLeft, если свободен; оба заняты — busRight + WARN. Ряды
    /// назначаются СНАЧАЛА всем односторонним (в порядке кабелей — их ряд
    /// сдвинуть нельзя), ПОТОМ двусторонним (в порядке кабелей — есть выбор).
    /// Лесенка: порядок назначения колонок — ряд по УБЫВАНИЮ (сверху вниз;
    /// ничья — индекс кабеля: List.Sort нестабилен, сравниватель обязан его
    /// учесть), колонка = seqIdx%2 (шахматка); A×B-AABB позиции занят
    /// (IsBoxFree, касание по краю наложением НЕ считается) — следующая колонка
    /// c+1, c+2, … до MaxSymbolColumnTries; не нашлась — шахматная + WARN
    /// «размещён с наложением» (защитная сетка); проверка оси не нужна —
    /// шахматка повторяет колонки каждый 2-й слот ШТАТНО. Правило 4: конец
    /// захода/шины = ось СВОЕГО символа − sDir·(габарит по оси)/2 (rev.10.11:
    /// H — A/2, V — B/2) — у каждого кабеля свой.
    /// Оси: Horizontal — axis=X (ряд), perp=Y; Vertical — axis=Y, perp=X;
    /// верх страницы — большая Y (урок rev.7.1). Группы
    /// стороны из CableModel: Right (Top/Right, большие perp) — шина за max(perp)+Off;
    /// Left (Bottom/Left) — за min(perp)−Off; Other (Unknown) — как Right, с WARN.
    /// Подходы (rev.10.1): вертикаль кабеля i на X_i = край ряда + ApproachOffset +
    /// i·ApproachPitch (шины двусторонних заканчиваются на X_i; край ряда NaN —
    /// fallback на крайнюю точку кабеля + WARN); двусторонний — одна вертикаль
    /// между своими уровнями шин (диапазон расширяется на ряд символа) и
    /// горизонтальный заход на УРОВНЕ РЯДА; односторонний (rev.10.5) — ВСЕ
    /// шины сразу до оси своего символа минус зазор, БЕЗ джампа. Колонка SymX =
    /// последний подход двусторонних (нет — последний подход всех) + ColumnOffset;
    /// BusOffset/BusLift — без изменений относительно rev.10.5. Финальная сетка
    /// (правило 5): пересечение A×B-боксов пары символов — WARN (при работающем
    /// подборе колонок не срабатывает). Сегменты и символы выдаются В ПОРЯДКЕ
    /// КАБЕЛЕЙ (oRes.Symbols упорядочен по CableIndex). Без EPLAN-типов
    /// и без логгера; дампы печатает
    /// AnalyzeAction.</summary>
    public static class CableGeometryBuilder
    {
        private const double EpsLen = 1e-9;    // порог вырожденного (нулевого) сегмента
        private const double EpsBoxTol = 1e-6; // допуск AABB: касание боксов по краю — НЕ наложение
        // Размер символа, для которого принят базовый шаг уровней шин 8 мм (правило 2).
        private const double LevelPitchRefHeightMm = 14.0;
        // Защитная сетка подбора колонок (правило 3): путь «шахматная колонка
        // занята» (WARN) юнит-тестами не покрывается — в кейсах колонка всегда
        // находится; сетка страхует реальные прогоны.
        private const int MaxSymbolColumnTries = 16;

        /// <summary>dStripEndAxis — крайняя колонка клеммника по оси выноса
        /// (H: max X колонок K4, V: min Y); NaN → fallback на крайнюю точку
        /// кабеля + WARN (ревизия 2: сход за габаритом ряда, как на эталоне).
        /// Уровни шин разнесены по кабелям не меньше чем на LevelPitchMinMm
        /// (ревизия 2; правило 2 rev.10.7).</summary>
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

            // rev.10.7: производные геометрии от размера символа A×B (правила 2–4
            // решения пользователя 23.09.2026); rev.10.8: шаг уровней шин
            // dLevelPitch = max(min, B·min/14), не B (решение пользователя по
            // прогону rev.10.7); rev.10.11: зазор/шаг колонок — от габарита
            // символа ВДОЛЬ оси выноса (решение пользователя 23.09.2026).
            // rev.10.11: габарит символа вдоль оси выноса (символ без поворота:
            // вдоль страничной X лежит A, вдоль Y — B; в Vertical ось = Y → B).
            double dAxisExtentMm = bV ? oCfg.SymbolHeightMm : oCfg.SymbolWidthMm;
            double dGap = dAxisExtentMm / 2.0;   // зазор линия–символ (rev.10.11: от габарита по оси выноса)
            double dLevelPitch = Math.Max(oCfg.LevelPitchMinMm,   // шаг уровней шин (rev.10.8: учёт B)
                oCfg.SymbolHeightMm * oCfg.LevelPitchMinMm / LevelPitchRefHeightMm);

            // --- Проход 1: уровни шин и оси подходов всех кабелей. Расстановка
            // (rev.10.10) зависит от ВСЕХ кабелей (ряды символов — по уровням шин,
            // SymX — по последним подходам), поэтому сначала собираем данные,
            // потом расставляем. ---
            double[] arrBusRight = new double[nCount];
            double[] arrBusLeft = new double[nCount];
            double[] arrBusOther = new double[nCount];
            double[] arrX = new double[nCount];
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
                double dBusRight = CountOf(oRight) > 0 ? MaxPerpOf(oRight, bV) + oCfg.BusOffsetMm + oCfg.BusLiftMm + nCable * dLevelPitch : double.NaN;
                double dBusLeft = CountOf(oLeft) > 0 ? MinPerpOf(oLeft, bV) - (oCfg.BusOffsetMm + oCfg.BusLiftMm + nCable * dLevelPitch) : double.NaN;
                double dBusOther = CountOf(oOther) > 0 ? MaxPerpOf(oOther, bV) + oCfg.BusOffsetMm + oCfg.BusLiftMm + nCable * dLevelPitch : double.NaN;
                arrBusRight[nCable] = dBusRight;
                arrBusLeft[nCable] = dBusLeft;
                arrBusOther[nCable] = dBusOther;

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
            // за последним подходом всех).
            double dLastSigned = bHasBilateral ? dMaxBilateralSigned : dMaxApproachSigned;
            double dSymAxis = sDir * dLastSigned + sDir * oCfg.SymbolColumnOffsetMm;

            // Боксы уже размещённых символов (осевые координаты) — для эскалации
            // колонок (шаг «в») и финальной сетки (правило 5).
            List<SymbolBox> lstBoxes = new List<SymbolBox>();

            // --- Проход 2 (rev.10.10): расстановка символов ПО УРОВНЯМ ШИН.
            // Шаги: (а) ряды всех символов + занятые уровни, (б) сортировка
            // «лесенки», (в) колонки, (г) сегменты и символы в порядке кабелей. ---

            // (а) Ряды. Сначала односторонние (в порядке кабелей — их ряд сдвинуть
            // нельзя), затем двусторонние (в порядке кабелей — есть выбор).
            // Занятость уровня — уже назначенный ряд другого символа.
            double[] arrSymRow = new double[nCount];
            double[] arrSymAxis = new double[nCount];
            List<double> lstTakenLevels = new List<double>();
            for (int nCable = 0; nCable < nCount; nCable++)
            {
                arrSymRow[nCable] = double.NaN;
                arrSymAxis[nCable] = double.NaN;
            }

            // (а1) Односторонние: уровень первой непустой группы Right→Left→Other;
            // несколько групп — WARN (правило не валидировано, rev.10.5); уровень
            // занят другим символом — WARN, ряд оставлен как есть.
            for (int nCable = 0; nCable < nCount; nCable++)
            {
                if (double.IsNaN(arrX[nCable])) continue;   // null/пустой кабель из прохода 1
                CableModel oCable = oLayout.Cables[nCable];
                if (CountOf(oCable.RightConnections) > 0 && CountOf(oCable.LeftConnections) > 0)
                    continue;   // двусторонний — (а2)
                string strCableName = oCable.Name ?? "<без имени>";

                int nGroups = 0;
                double dSymPerp = double.NaN;
                if (!double.IsNaN(arrBusRight[nCable])) { dSymPerp = arrBusRight[nCable]; nGroups++; }
                if (!double.IsNaN(arrBusLeft[nCable])) { if (double.IsNaN(dSymPerp)) dSymPerp = arrBusLeft[nCable]; nGroups++; }
                if (!double.IsNaN(arrBusOther[nCable])) { if (double.IsNaN(dSymPerp)) dSymPerp = arrBusOther[nCable]; nGroups++; }
                if (nGroups > 1)
                    oRes.Warnings.Add("[GEOM] кабель '" + strCableName + "': групп сторон " +
                        nGroups + " — символ на уровне первой (Right→Left→Other); правило не валидировано данными");

                if (!IsLevelFree(lstTakenLevels, dSymPerp))
                    oRes.Warnings.Add("[GEOM] кабель '" + strCableName + "': уровень шины " +
                        dSymPerp + " занят другим символом");
                lstTakenLevels.Add(dSymPerp);
                arrSymRow[nCable] = dSymPerp;
            }

            // (а2) Двусторонние: уровень busRight, если свободен, иначе busLeft,
            // если свободен; оба заняты — busRight + WARN.
            for (int nCable = 0; nCable < nCount; nCable++)
            {
                if (double.IsNaN(arrX[nCable])) continue;   // null/пустой кабель из прохода 1
                CableModel oCable = oLayout.Cables[nCable];
                List<TerminalConnectionModel> oRight = oCable.RightConnections;
                List<TerminalConnectionModel> oLeft = oCable.LeftConnections;
                List<TerminalConnectionModel> oOther = oCable.OtherConnections;
                if (!(CountOf(oRight) > 0 && CountOf(oLeft) > 0)) continue;   // односторонний — (а1)
                string strCableName = oCable.Name ?? "<без имени>";

                double dSymPerp;
                if (!double.IsNaN(arrBusRight[nCable]) && IsLevelFree(lstTakenLevels, arrBusRight[nCable]))
                    dSymPerp = arrBusRight[nCable];
                else if (!double.IsNaN(arrBusLeft[nCable]) && IsLevelFree(lstTakenLevels, arrBusLeft[nCable]))
                    dSymPerp = arrBusLeft[nCable];
                else
                {
                    dSymPerp = arrBusRight[nCable];   // NaN невозможен: обе группы непусты
                    oRes.Warnings.Add("[GEOM] кабель '" + strCableName +
                        "': оба уровня шин заняты символами — символ на уровне Right");
                }
                lstTakenLevels.Add(dSymPerp);
                arrSymRow[nCable] = dSymPerp;
            }

            // (б) Лесенка: порядок назначения колонок — ряд по УБЫВАНИЮ (сверху
            // вниз); ничья — индекс кабеля (List.Sort нестабилен — сравниватель
            // обязан его учесть).
            List<int> lstOrder = new List<int>();
            for (int nCable = 0; nCable < nCount; nCable++)
                if (!double.IsNaN(arrSymRow[nCable])) lstOrder.Add(nCable);
            lstOrder.Sort(delegate(int nA, int nB)
            {
                int nC = arrSymRow[nB].CompareTo(arrSymRow[nA]);
                return nC != 0 ? nC : nA.CompareTo(nB);
            });

            // (в) Колонки: seqIdx%2 (шахматка); AABB позиции занят (IsBoxFree,
            // касание по краю — не наложение) — следующая колонка c+1, c+2, … до
            // MaxSymbolColumnTries; не нашлась — шахматная + WARN (защитная
            // сетка). Проверки оси нет: шахматка повторяет колонки каждый 2-й
            // слот ШТАТНО. Ограничение (отложено до реальных данных/Фазы H):
            // горизонтальный пробег шины/захода НЕ проверяется на проход через
            // чужой символ — при сближенных уровнях разных кабелей (< B/2)
            // линия может пройти рядом с чужим боксом; на текущем стенде
            // уровни разнесены на LevelPitch и пробоя нет.
            for (int nSeq = 0; nSeq < lstOrder.Count; nSeq++)
            {
                int nCable = lstOrder[nSeq];
                CableModel oCable = oLayout.Cables[nCable];
                string strCableName = oCable.Name ?? "<без имени>";

                int nCol = -1;
                for (int nTry = nSeq % 2; nTry < MaxSymbolColumnTries; nTry++)
                {
                    double dTryAxis = dSymAxis + sDir * nTry * dAxisExtentMm;   // шаг колонок = габарит по оси выноса (соседние колонки касаются краями)
                    if (IsBoxFree(lstBoxes, dTryAxis, arrSymRow[nCable],
                            oCfg.SymbolWidthMm, oCfg.SymbolHeightMm, bV))
                    {
                        nCol = nTry;
                        break;
                    }
                }
                if (nCol < 0)
                {
                    nCol = nSeq % 2;
                    oRes.Warnings.Add("[GEOM] кабель '" + strCableName +
                        "': шахматная колонка занята — размещён с наложением");
                }

                arrSymAxis[nCable] = dSymAxis + sDir * nCol * dAxisExtentMm;   // шаг колонок = габарит по оси выноса (rev.10.11)
                AddBox(lstBoxes, arrSymAxis[nCable], arrSymRow[nCable], oCable.Name);
            }

            // (г) Сегменты и символы — В ПОРЯДКЕ КАБЕЛЕЙ (oRes.Symbols упорядочен
            // по CableIndex — на этом завязаны тесты и Фаза G).
            for (int nCable = 0; nCable < nCount; nCable++)
            {
                if (double.IsNaN(arrX[nCable])) continue;   // null/пустой кабель из прохода 1
                CableModel oCable = oLayout.Cables[nCable];
                List<TerminalConnectionModel> oRight = oCable.RightConnections;
                List<TerminalConnectionModel> oLeft = oCable.LeftConnections;
                List<TerminalConnectionModel> oOther = oCable.OtherConnections;
                bool bBilateral = CountOf(oRight) > 0 && CountOf(oLeft) > 0;

                if (bBilateral)
                {
                    // Шины двустороннего заканчиваются на его подходе X_i.
                    AddGroup(oRes, oRight, arrBusRight[nCable], bV, arrX[nCable]);
                    AddGroup(oRes, oLeft, arrBusLeft[nCable], bV, arrX[nCable]);
                    AddGroup(oRes, oOther, arrBusOther[nCable], bV, arrX[nCable]);

                    // Вертикаль-подход на X_i: между нижним и верхним из уровней
                    // шин и ряда символа.
                    double dLo = arrSymRow[nCable];
                    double dHi = arrSymRow[nCable];
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
                    // Заход на УРОВНЕ РЯДА: X_i → ось символа минус зазор (габарит по оси/2, rev.10.11) —
                    // конец у КАЖДОГО кабеля свой (rev.10.7, правило 4).
                    double dEntryEnd = arrSymAxis[nCable] - sDir * dGap;
                    if (Math.Abs(dEntryEnd - arrX[nCable]) > EpsLen)
                    {
                        Seg oEntry = new Seg();
                        oEntry.A = PtOf(bV, arrX[nCable], arrSymRow[nCable]);
                        oEntry.B = PtOf(bV, dEntryEnd, arrSymRow[nCable]);
                        oRes.Segments.Add(oEntry);
                    }
                }
                else
                {
                    // Односторонний (rev.10.5): ВСЕ шины сразу до оси своего
                    // символа минус зазор (габарит по оси/2), БЕЗ подхода/захода/джампа.
                    double dBusEndAxis = arrSymAxis[nCable] - sDir * dGap;
                    AddGroup(oRes, oRight, arrBusRight[nCable], bV, dBusEndAxis);
                    AddGroup(oRes, oLeft, arrBusLeft[nCable], bV, dBusEndAxis);
                    AddGroup(oRes, oOther, arrBusOther[nCable], bV, dBusEndAxis);
                }

                oCable.SymbolPosition = PtOf(bV, arrSymAxis[nCable], arrSymRow[nCable]);
                CableSymbolPlacement oSym = new CableSymbolPlacement();
                oSym.CableName = oCable.Name;
                oSym.CableIndex = nCable;
                oSym.Position = oCable.SymbolPosition;
                oRes.Symbols.Add(oSym);
            }

            // Финальная защитная сетка (rev.10.7, правило 5): пересечение A×B-боксов
            // пары символов — WARN (при работающем подборе колонок не срабатывает).
            for (int i = 0; i < lstBoxes.Count; i++)
                for (int j = i + 1; j < lstBoxes.Count; j++)
                    if (BoxesOverlap(lstBoxes[i], lstBoxes[j], oCfg.SymbolWidthMm,
                        oCfg.SymbolHeightMm, bV))
                        oRes.Warnings.Add("[GEOM] наложение символов кабелей '" +
                            lstBoxes[i].CableName + "' и '" + lstBoxes[j].CableName +
                            "' — пересекаются боксы символов; проверьте уровни шин");
            return oRes;
        }

        /// <summary>Бокс размещённого символа в осевых координатах (axis — вдоль
        /// выноса, perp — поперёк ряда): эскалация колонок (шаг «в») и финальная
        /// сетка (правило 5). Габариты бокса по ориентации: вдоль Axis — B при
        /// Vertical, A при Horizontal (символ вставляется БЕЗ поворота).</summary>
        private sealed class SymbolBox
        {
            public double Axis;
            public double Perp;
            public string CableName;
        }

        private static void AddBox(List<SymbolBox> lstBoxes, double dAxis, double dPerp,
            string strCableName)
        {
            SymbolBox oBox = new SymbolBox();
            oBox.Axis = dAxis;
            oBox.Perp = dPerp;
            oBox.CableName = strCableName ?? "<без имени>";
            lstBoxes.Add(oBox);
        }

        /// <summary>Свободна ли точка (axis;perp) от наложений: пересечение A×B-боксов
        /// с уже размещёнными. A/B расставляются по ориентации: символ вставляется
        /// БЕЗ поворота — вдоль страницы X физически лежит A, вдоль Y — B, т.е. в
        /// осевых координатах вдоль Axis — B при Vertical (axis=Y) и A при Horizontal
        /// (axis=X), вдоль Perp — наоборот. Касание по краю (|d| ≥ размер, допуск
        /// EpsBoxTol) наложением НЕ считается (правило 3 rev.10.7).</summary>
        private static bool IsBoxFree(List<SymbolBox> lstBoxes, double dAxis, double dPerp,
            double dWidthMm, double dHeightMm, bool bV)
        {
            double dExtAxis = bV ? dHeightMm : dWidthMm;   // вдоль Axis: B при Vertical
            double dExtPerp = bV ? dWidthMm : dHeightMm;   // вдоль Perp: A при Vertical
            foreach (SymbolBox oBox in lstBoxes)
                if (Math.Abs(oBox.Axis - dAxis) < dExtAxis - EpsBoxTol &&
                    Math.Abs(oBox.Perp - dPerp) < dExtPerp - EpsBoxTol)
                    return false;
            return true;
        }

        /// <summary>Свободен ли УРОВЕНЬ (rev.10.10): среди уже назначенных рядов
        /// символов нет совпадающего (|Δperp| < EpsBoxTol). Проверка выполняется
        /// при назначении рядов, до построения боксов — колонки ещё не известны,
        /// но ряд от колонки не зависит.</summary>
        private static bool IsLevelFree(List<double> lstTakenLevels, double dPerp)
        {
            foreach (double dTaken in lstTakenLevels)
                if (Math.Abs(dTaken - dPerp) < EpsBoxTol)
                    return false;
            return true;
        }

        /// <summary>Пересечение A×B-боксов пары размещённых символов (финальная
        /// сетка, правило 5 rev.10.7). A/B расставляются по ориентации: вдоль
        /// Axis — B при Vertical, A при Horizontal (см. IsBoxFree).</summary>
        private static bool BoxesOverlap(SymbolBox oA, SymbolBox oB,
            double dWidthMm, double dHeightMm, bool bV)
        {
            double dExtAxis = bV ? dHeightMm : dWidthMm;
            double dExtPerp = bV ? dWidthMm : dHeightMm;
            return Math.Abs(oA.Axis - oB.Axis) < dExtAxis - EpsBoxTol &&
                   Math.Abs(oA.Perp - oB.Perp) < dExtPerp - EpsBoxTol;
        }

        /// <summary>Сегменты одной группы стороны: ветвь на каждую точку (перпендикулярно
        /// ряду до уровня шины — ревизия 4: шина поднята ЦЕЛИКОМ на BusLiftMm, хвоста-
        /// уголка нет), шина от крайней точки группы, ПРОТИВОПОЛОЖНОЙ направлению выноса
        /// (H: min axis; V: max axis), до dBusEndAxis (двусторонний — подход X_i,
        /// односторонний — ось своего символа минус зазор (габарит по оси/2, rev.10.11). Вертикаль-
        /// подход/заход строит Build (rev.10.1; джампы отменены rev.10.5).</summary>
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
