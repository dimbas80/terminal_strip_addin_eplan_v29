using System;
using System.Collections.Generic;
using System.Globalization;

namespace MyEplanActions
{
    /// <summary>Точка в чистой геометрии детектора (аналог EPLAN PointD без зависимости от API).</summary>
    public struct Pt
    {
        public double X;
        public double Y;

        public Pt(double dX, double dY)
        {
            X = dX;
            Y = dY;
        }
    }

    /// <summary>Отрезок в чистой геометрии детектора: концы + имя слоя (trim, null если недоступен).
    /// Заполняется из EPLAN Line (GraphicalPlacement.Layer.Name) в Action'е.</summary>
    public struct Seg
    {
        public Pt A;
        public Pt B;
        public string LayerName;
    }

    /// <summary>Связная компонента отрезков — кандидат в «вывод» (порт LineComponent rev.12/13).</summary>
    public sealed class LineComponent
    {
        public readonly List<Seg> Segments = new List<Seg>();
        public readonly List<Pt> Leaves = new List<Pt>();
        public bool AllCollinear;
        public int MaxDegree;
    }

    /// <summary>Итог контроля К4 (rev.13 + Задача 5): колонки клемм, шаг сетки,
    /// привязка точек. rev.6.2 (Задача 4): колонки хранят координаты ВДОЛЬ оси
    /// ориентации (X при Horizontal / Y при Vertical), привязка — по |Δ оси| ≤
    /// полушага. Потребитель — MatchBuilder (свод [MATCH]).</summary>
    public sealed class K4Report
    {
        public readonly List<double> Columns = new List<double>();
        /// <summary>Ось, на которой лежат колонки (rev.6.2). По умолчанию
        /// Horizontal: K4Report создаётся только в CheckK4Report, где поле
        /// всегда проставляется из параметра ориентации.</summary>
        public ReportOrientation Orientation = ReportOrientation.Horizontal;
        public double Pitch;
        public double Half;
        public int[] Counts;
        public int Orphans;
        public int Over;
        /// <summary>false, если контроль пропущен (стубов &lt; 2 или шаг не определён).</summary>
        public bool Valid;

        /// <summary>Индекс колонки клеммы для точки (|Δ оси| ≤ полушага: X при
        /// Horizontal / Y при Vertical, rev.6.2), -1 = сирота.</summary>
        public int BindIndex(Pt oPoint)
        {
            if (!Valid || Columns.Count == 0) return -1;
            double dCoord = Orientation == ReportOrientation.Vertical ? oPoint.Y : oPoint.X;
            int nBest = -1;
            double dBest = double.MaxValue;
            for (int i = 0; i < Columns.Count; i++)
            {
                double d = Math.Abs(Columns[i] - dCoord);
                if (d < dBest) { dBest = d; nBest = i; }
            }
            if (nBest >= 0 && dBest <= Half) return nBest;
            return -1;
        }
    }

    /// <summary>Итог детектора выводов (Задача 3): точки подключения + маркеры + итог К4
    /// (колонки/привязка — для свода [MATCH], Задача 5).</summary>
    public sealed class LeadAnalysis
    {
        public readonly List<Pt> Points = new List<Pt>();
        public readonly List<Seg> Stubs = new List<Seg>();
        public readonly List<Seg> Jumpers = new List<Seg>();
        public int Leads;
        public int Bridges;
        public K4Report K4;

        /// <summary>Индекс компоненты для каждой точки из Points (индексы совпадают) —
        /// rev.5.6: оба листа моста (К2) принадлежат одной клемме, MatchBuilder
        /// привязывает свободный слот раздвоения к клемме якоренного листа.</summary>
        public readonly List<int> PointComponent = new List<int>();

        /// <summary>Точка — лист моста (К2); индексы совпадают с Points.</summary>
        public readonly List<bool> PointIsBridge = new List<bool>();
    }

    public static class LeadGeometry
    {
        /// <summary>Ключ точки с допуском: округление до 0.001 мм, формат "F3" (координаты в API
        /// содержат шум вида 53.3500000000001). Порт PointKey из rev.13.</summary>
        public static string PointKey(Pt oPoint)
        {
            return oPoint.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                oPoint.Y.ToString("F3", CultureInfo.InvariantCulture);
        }

        public static double PointDistance(Pt a, Pt b)
        {
            double dDx = a.X - b.X;
            double dDy = a.Y - b.Y;
            return Math.Sqrt(dDx * dDx + dDy * dDy);
        }
    }
}
