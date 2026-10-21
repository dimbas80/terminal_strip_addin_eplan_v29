using System;

namespace MyEplanActions
{
    /// <summary>Чистая арифметика расстановки символа кабеля (Этап 8, задача H-4;
    /// ruling R12): компенсация визуального центра (dx,dy) при вставке и выбор
    /// слота варианта/размеров по фактической ориентации. БЕЗ EPLAN-типов —
    /// компилируется и в консольный тест-раннер tests/ (add Geometry/SymbolPlacementMath.cs).
    /// Знак компенсации (ruling R9): dx = центр_bbox.X − точка_вставки.X,
    /// dy = центр_bbox.Y − точка_вставки.Y — смещение визуального центра ОТ точки
    /// вставки; вставка: Location = desired − (dx,dy). Проверка знаками на GOST K
    /// (п.49, Δ=(0;−4)): центр на 4 мм ниже вставки → dy=−4 → Location.Y=desired+4 —
    /// круг лезет вверх на линию. Non-finite offset (NaN/±Inf) — без компенсации
    /// (det-отказ, WARN-путь вызывающего не расширяется — см. тесты).</summary>
    public static class SymbolPlacementMath
    {
        /// <summary>Компенсация центра: loc = desired − (dDx, dDy). Любой non-finite
        /// offset → идент (loc == desired, без компенсации) — детерминированно.</summary>
        public static void Compensate(double dDesiredX, double dDesiredY,
            double dDx, double dDy,
            out double dLocX, out double dLocY)
        {
            if (double.IsNaN(dDx) || double.IsInfinity(dDx) ||
                double.IsNaN(dDy) || double.IsInfinity(dDy))
            {
                dLocX = dDesiredX;
                dLocY = dDesiredY;
                return;
            }
            dLocX = dDesiredX - dDx;
            dLocY = dDesiredY - dDy;
        }

        /// <summary>Индекс варианта фактической ориентации: вертикаль → слот V,
        /// иначе → слот H (спека §2 п.7, два слота из браузера H-4).</summary>
        public static int VariantForOrientation(int nVariantH, int nVariantV, bool bVertical)
        {
            return bVertical ? nVariantV : nVariantH;
        }

        /// <summary>Пара размеров A×B варианта фактической ориентации (в конфиг
        /// геометрии: зазор/шаги — от габарита по оси выноса, rev.10.7+/10.11).</summary>
        public static void SizeForOrientation(double dWidthH, double dHeightH,
            double dWidthV, double dHeightV, bool bVertical,
            out double dWidthMm, out double dHeightMm)
        {
            if (bVertical)
            {
                dWidthMm = dWidthV;
                dHeightMm = dHeightV;
            }
            else
            {
                dWidthMm = dWidthH;
                dHeightMm = dHeightH;
            }
        }
    }
}
