using System.Collections.Generic;

namespace MyEplanActions
{
    /// <summary>Группировка подключений одного кабеля (Фаза E, plan_implementation §4.5).
    /// Name — имя кабеля (CableName из DM) или null, если кабель без имени (в проекте
    /// объектов-определений кабелей нет, только №31058 IsCable — бинарный случай).
    /// Left/Right — разбивка по стороне ряда (TerminalSide: Top/Right → Right,
    /// Bottom/Left → Left, Unknown → Other); SymbolPosition — точка вставки символа
    /// (NaN до Фазы F/Geometry).</summary>
    public sealed class CableModel
    {
        public string Name;                              // null = кабель без имени
        public readonly List<TerminalConnectionModel> Connections = new List<TerminalConnectionModel>();
        public readonly List<TerminalConnectionModel> LeftConnections = new List<TerminalConnectionModel>();
        public readonly List<TerminalConnectionModel> RightConnections = new List<TerminalConnectionModel>();
        public readonly List<TerminalConnectionModel> OtherConnections = new List<TerminalConnectionModel>();
        public Pt SymbolPosition = new Pt(double.NaN, double.NaN);   // NaN до Фазы F (явный сентинел — дефолт struct Pt = (0,0))
    }
}
