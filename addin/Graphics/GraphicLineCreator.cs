using System;
using System.Collections.Generic;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;

namespace MyEplanActions
{
    /// <summary>Создание графических линий кабельной разводки по сегментам Geometry Engine
    /// (Фаза G, spec docs/superpowers/specs/2026-09-22-fase-g-graphics-design.md;
    /// мастер-план §12 — Этап 8). Линии графические, не электрические Connection.
    /// Слой — объект GraphicalLayer из дерева отчёта (имя читается через
    /// EmbeddedReportReader.LayerNameOf — прямой GraphicalLayer.Name в 2.9 не доказан;
    /// на стресс-стенде 67 линий на EPLAN100); перо — из AddInConfiguration.
    /// Отказ отдельной линии — WARN [GRAPH], остальные продолжают (паттерн [PREVIEW]).
    /// НЕ идемпотентно: повторный прогон дублирует линии (очистка — Фаза I).</summary>
    public static class GraphicLineCreator
    {
        /// <summary>Объект слоя strLayerName из дерева отчёта lstTree: первый
        /// GraphicalPlacement, чьё имя через EmbeddedReportReader.LayerNameOf (ридинг
        /// компилируется; прямой GraphicalLayer.Name в 2.9 не доказан) совпало со
        /// strLayerName (без учёта регистра). Не найден — WARN и null (линии
        /// останутся на слое по умолчанию, spec §5).</summary>
        public static GraphicalLayer ResolveLayerFromTree(List<Placement> lstTree,
            string strLayerName, DiagnosticLogger log)
        {
            if (lstTree == null || string.IsNullOrEmpty(strLayerName)) return null;
            foreach (Placement oPlacement in lstTree)
            {
                GraphicalPlacement oGraphical = oPlacement as GraphicalPlacement;
                if (oGraphical == null) continue;
                try
                {
                    if (string.Equals(EmbeddedReportReader.LayerNameOf(oGraphical), strLayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        return oGraphical.Layer;
                    }
                }
                catch { /* слой отдельного объекта недоступен — пробуем следующий */ }
            }
            log.Warn("[GRAPH] слой '" + strLayerName +
                "' не найден в дереве отчёта — линии на слое по умолчанию");
            return null;
        }

        /// <summary>Линия на каждый Seg: Create + перо (конфиг) + слой oLayer
        /// (null — слой по умолчанию). Возвращает число созданных.</summary>
        public static int CreateLines(Page oPage, CableGeometryResult oGeom,
            GraphicalLayer oLayer, DiagnosticLogger log)
        {
            if (oPage == null || oGeom == null)
            {
                log.Warn("[GRAPH] CreateLines: page или geometry == null — линий не создаём");
                log.Log("[INFO] [GRAPH-SUM] линий 0 (page или geometry == null).");
                return 0;
            }
            int nTotal = oGeom.Segments.Count;
            if (nTotal == 0) return 0;

            // Красное перо — рабочий вывод по решению пользователя (spec §2.2);
            // паттерн Pen — example/ShowCablesInSegment.cs / превью Фазы F.
            Pen oPen = new Pen();
            oPen.ColorId = AddInConfiguration.GraphicsPenColorId;
            oPen.Width = AddInConfiguration.GraphicsPenWidthMm;
            oPen.StyleId = 0;

            int nCreated = 0;
            foreach (Seg oSeg in oGeom.Segments)
            {
                try
                {
                    Line oLine = new Line();
                    oLine.Create(oPage,
                        new PointD(oSeg.A.X, oSeg.A.Y),
                        new PointD(oSeg.B.X, oSeg.B.Y));
                    oLine.Pen = oPen;
                    if (oLayer != null) oLine.Layer = oLayer;
                    nCreated++;
                }
                catch (Exception oEx)
                {
                    log.Warn("[GRAPH] Line.Create бросил " + oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            log.Log("[INFO] [GRAPH-SUM] линий " + nCreated + " из " + nTotal +
                (oLayer != null
                    ? " (слой '" + AddInConfiguration.GraphicsLayerName + "')"
                    : " (слой по умолчанию)") + ".");
            return nCreated;
        }
    }
}
