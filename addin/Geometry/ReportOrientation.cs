namespace MyEplanActions
{
    /// <summary>Ориентация формы отчёта: вдоль колонок якоря идут по X (Horizontal —
    /// текущая горизонтальная форма) или по Y (Vertical — вертикальная форма,
    /// Задача 0/3 Этапа 3). (Выделен из AnchorResolver.cs, Этап 6 / Задача 1 — чтобы
    /// чистые модули геометрии компилировались без EPLAN-зависимостей.)</summary>
    public enum ReportOrientation { Horizontal, Vertical }
}
