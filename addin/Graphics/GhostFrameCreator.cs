using System;
using System.Globalization;
using Eplan.EplApi.Base;                  // PointD (KB 2.9: «PointD Structure»)
using Eplan.EplApi.DataModel;             // Page
using Eplan.EplApi.DataModel.Graphics;    // PolyLine, Pen

namespace MyEplanActions
{
    /// <summary>Создание/уборка рамки-призрака (Этап 8, H-5/H-6, rev.15.0; spec
    /// 2026-09-25-fase-h-ui-design.md §6): замкнутый прямоугольник-призрак под
    /// курсором интеракции (SetStaticCursor, см. InsertPointInteraction) — длинная
    /// сторона по X (Horizontal) или по Y (Vertical), размеры из GhostFrameMath
    /// (nTerminals×pitch × 2×SymbolFallbackSizeMm). Паттерн создания — ТОЧНО из
    /// addin/Graphics/ReferenceArrowCreator.cs (rev.11): PolyLine : GraphicalPlacement,
    /// Create(Page), точки SetPointAt(int, ref PointD) — REF; Closed=true СТРОГО до
    /// IsSurfaceFilled (KB: сеттер заливки на незамкнутой полилинии бросает
    /// документированное исключение; для призрака заливка НЕ нужна —
    /// IsSurfaceFilled=false СТРОГО ПОСЛЕ Closed=true (явное выключение,
    /// INFO-ревью: комментарий должен обещать ровно то, что делает код).
    /// Красное перо — та же идиома конфига
    /// (GraphicsPenColorId=1, GraphicsPenWidthMm=0.35 — rev.11 красное перо линий-
    /// ссылок/линий разводки). Слой: как у линий-ссылок по умолчанию (oLayer==null
    /// в ReferenceArrowCreator — слой не задаётся; для курсорной отрисовки слой не
    /// критичен, новые механизмы поиска слоя не придумываем). Отказ создания —
    /// null + WARN [GHOST-CREATE] (интеракция живёт
    /// без рамки — деградация штатная, спека §6).</summary>
    public static class GhostFrameCreator
    {
        /// <summary>Рамка-призрак: замкнутый PolyLine-прямоугольник с углом в
        /// (0,0) и размерами dLongMm×dShortMm; пользователь кликает точку вставки
        /// — интеракция рисует призрак под курсором (SetStaticCursor, начало
        /// локальной системы координат (0,0)). Возвращает созданный объект или
        /// null при отказе (WARN; отказ призрака НЕ ломает интеракцию). Лог
        /// [GHOST-CREATE].</summary>
        public static PolyLine CreateGhostFrame(Page oPage, ReportOrientation eOrient,
            double dLongMm, double dShortMm, DiagnosticLogger log)
        {
            if (oPage == null)
            {
                log.Warn("[GHOST-CREATE] страницы нет — призрак не создаётся (интеракция без рамки)");
                return null;
            }
            // Переносим координаты: Horizontal — длинная сторона по X; Vertical — по Y.
            double dX = eOrient == ReportOrientation.Vertical ? dShortMm : dLongMm;
            double dY = eOrient == ReportOrientation.Vertical ? dLongMm : dShortMm;
            // m1-фикс (ревью rev.15.0): объявление ДО try — в catch полусозданный
            // PolyLine (Create прошёл, следующий шаг бросил) удаляется guarded.
            PolyLine oGhost = null;
            try
            {
                // Красное перо — идиома ReferenceArrowCreator/GraphicLineCreator
                // (рев.11: красное перо линий-ссылок).
                Pen oPen = new Pen();
                oPen.ColorId = AddInConfiguration.GraphicsPenColorId;
                oPen.Width = AddInConfiguration.GraphicsPenWidthMm;
                oPen.StyleId = 0;

                oGhost = new PolyLine();
                oGhost.Create(oPage);
                // 4 точки контура (0,0)-(dX,0)-(dX,dY)-(0,dY); SetPointAt — ref! (KB).
                PointD[] arrCorners = new PointD[]
                {
                    new PointD(0.0, 0.0),
                    new PointD(dX, 0.0),
                    new PointD(dX, dY),
                    new PointD(0.0, dY)
                };
                for (int i = 0; i < 4; i++)
                {
                    PointD oPt = arrCorners[i];
                    oGhost.SetPointAt(i, ref oPt);
                }
                oGhost.Closed = true;          // СТРОГО до IsSurfaceFilled (ReferenceArrowCreator)
                oGhost.IsSurfaceFilled = false; // для призрака заливка не нужна
                oGhost.Pen = oPen;
                // Слой как у линий-ссылок при oLayer==null (по умолчанию) — не задаём.
                log.Log(string.Format(CultureInfo.InvariantCulture,
                    "[INFO] [GHOST-CREATE] призрак создан: {0:F1}×{1:F1} мм ({2}); X={3:F1} Y={4:F1}",
                    dX, dY, (eOrient == ReportOrientation.Vertical ? "Vertical" : "Horizontal"), dX, dY));
                return oGhost;
            }
            catch (Exception oEx)
            {
                // m1-фикс: половина создана и осталась на странице (урок п.48/паттерн
                // [PICK-CLEAN]) — guarded Remove; провал очистки НЕ глушит диагноз.
                try
                {
                    if (oGhost != null && oGhost.IsValid)
                    {
                        oGhost.Remove();
                        log.Log("[INFO] [GHOST-CREATE] полусозданный призрак удалён");
                    }
                }
                catch (Exception)
                {
                    // очистка полусозданного не критична — молча, диагноз ниже.
                }
                log.Warn("[GHOST-CREATE] создание призрака не удалось (" +
                    oEx.GetType().Name + ": " + oEx.Message + ") — интеракция без рамки");
                return null;
            }
        }

        /// <summary>Уборка призрака: Remove() по IsValid (KB: StorableObject.IsValid —
        /// «Determines if StorableObject is correct database object and is not
        /// deleted»), try/catch. Возвращает число удалённых (0 или 1). Лог
        /// [GHOST-CLEAN]. Вызывается хуком ВСЕГДА (и при успехе, и при отмене,
        /// и при таймауте — паттерн [PICK-CLEAN]).</summary>
        public static int RemoveGhost(PolyLine oGhost, DiagnosticLogger log)
        {
            if (oGhost == null) return 0;   // призрак не создавался — убирать нечего
            try
            {
                if (!oGhost.IsValid)
                {
                    log.Log("[INFO] [GHOST-CLEAN] IsValid=false — Remove пропущен (0 удалено)");
                    return 0;
                }
                oGhost.Remove();
                log.Log("[INFO] [GHOST-CLEAN] призрак удалён (1 удалено)");
                return 1;
            }
            catch (Exception oEx)
            {
                log.Warn("[GHOST-CLEAN] Remove призрака бросил " + oEx.GetType().Name +
                    ": " + oEx.Message);
                return 0;
            }
        }
    }
}
