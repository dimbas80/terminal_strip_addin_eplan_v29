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
}
