using System;
using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;

namespace MyEplanActions
{
    /// <summary>Создание линий-ссылок от символов кабеля (rev.11.0, решение
    /// пользователя 23.09.2026): линия от дальнего края символа наружу от
    /// клеммника (конец линии = остриё стрелки) + замкнутая PolyLine-стрелка
    /// с заливкой. Элемент — ЛОГИЧЕСКАЯ единица: на странице это два независимых
    /// объекта (Line + PolyLine, без группировки); элемент считается созданным,
    /// если создана линия (отказ стрелки — WARN, элемент не портится; стрелка
    /// без линии не создаётся). KB API 2.9 (проверено по eplan.help):
    /// PolyLine : GraphicalPlacement, Create(Page), точки — SetPointAt(int,
    /// ref PointD) (ref!); свойства Closed и IsSurfaceFilled — документировано,
    /// сеттер заливки бросает исключение на незамкнутой полилинии, поэтому
    /// IsSurfaceFilled присваивается СТРОГО после Closed=true. Перо/слой —
    /// унаследованы от GraphicalPlacement (как у Line). Отказ отдельного
    /// элемента — WARN [REF], остальные продолжают (паттерн [GRAPH]/[PREVIEW]).
    /// НЕ идемпотентно: повторный прогон дублирует объекты (очистка — Фаза I).</summary>
    public static class ReferenceArrowCreator
    {
        /// <summary>Линия-ссылка на каждый ReferenceElement: Line.Create + перо
        /// (конфиг) + слой oLayer (null — слой по умолчанию); затем PolyLine
        /// (4 точки контура, Closed, заливка). Возвращает число созданных
        /// линий-ссылок (счётчик стрелок — отдельный, в логе [REF-SUM]).</summary>
        public static int CreateReferences(Page oPage, CableGeometryResult oGeom,
            GraphicalLayer oLayer, DiagnosticLogger log)
        {
            if (oPage == null || oGeom == null)
            {
                log.Warn("[REF] CreateReferences: page или geometry == null — ссылок не создаём");
                log.Log("[INFO] [REF-SUM] линий-ссылок 0 (page или geometry == null).");
                return 0;
            }
            int nTotal = oGeom.References.Count;
            if (nTotal == 0) return 0;

            // Красное перо — как у линий разводки (GraphicLineCreator.CreateLines).
            Pen oPen = new Pen();
            oPen.ColorId = AddInConfiguration.GraphicsPenColorId;
            oPen.Width = AddInConfiguration.GraphicsPenWidthMm;
            oPen.StyleId = 0;

            int nCreated = 0;
            int nArrows = 0;
            foreach (ReferenceElement oRef in oGeom.References)
            {
                string strName = oRef.CableName ?? "<без имени>";
                bool bLineOk = false;
                try
                {
                    Line oLine = new Line();
                    oLine.Create(oPage,
                        new PointD(oRef.Line.A.X, oRef.Line.A.Y),
                        new PointD(oRef.Line.B.X, oRef.Line.B.Y));
                    oLine.Pen = oPen;
                    if (oLayer != null) oLine.Layer = oLayer;
                    bLineOk = true;
                    nCreated++;
                }
                catch (Exception oEx)
                {
                    log.Warn("[REF] '" + strName + "' #" +
                        oRef.CableIndex.ToString(CultureInfo.InvariantCulture) +
                        ": Line.Create бросил " + oEx.GetType().Name + ": " + oEx.Message);
                }
                if (bLineOk)
                {
                    // Стрелка — замкнутая PolyLine с заливкой (KB: IsSurfaceFilled
                    // ТОЛЬКО после Closed=true, иначе документированное исключение).
                    // Свежая PolyLine имеет 4 точки (пример доков ставит индексы 0..3);
                    // контур неквадратный — WARN и пропуск (защита от смены формата).
                    if (oRef.Arrow == null || oRef.Arrow.Length != 4)
                    {
                        log.Warn("[REF] '" + strName + "' #" +
                            oRef.CableIndex.ToString(CultureInfo.InvariantCulture) +
                            ": контур стрелки без 4 точек (" +
                            (oRef.Arrow == null ? "null" : oRef.Arrow.Length.ToString(CultureInfo.InvariantCulture)) +
                            ") — стрелка не создаётся");
                    }
                    else try
                    {
                        PolyLine oArrow = new PolyLine();
                        oArrow.Create(oPage);
                        for (int i = 0; i < 4; i++)
                        {
                            PointD oPt = new PointD(oRef.Arrow[i].X, oRef.Arrow[i].Y);
                            oArrow.SetPointAt(i, ref oPt);
                        }
                        oArrow.Closed = true;
                        oArrow.IsSurfaceFilled = true;
                        oArrow.Pen = oPen;
                        if (oLayer != null) oArrow.Layer = oLayer;
                        nArrows++;
                    }
                    catch (Exception oEx)
                    {
                        log.Warn("[REF] PolyLine бросил " + oEx.GetType().Name + ": " +
                            oEx.Message + " — стрелка кабеля '" + strName + "' #" +
                            oRef.CableIndex.ToString(CultureInfo.InvariantCulture) +
                            " не создана");
                    }
                }
                log.Log(string.Format(CultureInfo.InvariantCulture,
                    "[INFO] [REF] '{0}' #{1}: линия ({2:F3};{3:F3})-({4:F3};{5:F3}), стрелка @ {6}",
                    strName, oRef.CableIndex,
                    oRef.Line.A.X, oRef.Line.A.Y, oRef.Line.B.X, oRef.Line.B.Y,
                    (oRef.Arrow == null || oRef.Arrow.Length < 4)
                        ? "<нет контура>"
                        : string.Format(CultureInfo.InvariantCulture, "({0:F3};{1:F3})",
                            oRef.Arrow[0].X, oRef.Arrow[0].Y)));
            }
            log.Log("[INFO] [REF-SUM] линий-ссылок " + nCreated + " из " + nTotal +
                ", стрелок " + nArrows + " из " + nTotal +
                (oLayer != null
                    ? " (слой '" + AddInConfiguration.GraphicsLayerName + "')"
                    : " (слой по умолчанию)") + ".");
            return nCreated;
        }
    }
}
