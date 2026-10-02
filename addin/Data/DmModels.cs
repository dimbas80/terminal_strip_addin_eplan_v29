using System.Collections.Generic;

namespace MyEplanActions
{
    // rev.12.6 (Этап 8, H-3c): чистые data-классы итога чтения Data Model
    // перенесены дословно из EplanTerminalStripReader.cs — сам ридер тянет
    // using Eplan.EplApi.* и не компилируется в чистый тест-раннер tests/,
    // а MatchBuilder (и новый тест оверфлоу-слота rev.12.6) работают только с
    // этими классами. Поведение не менялось; /recurse:*.cs в build_addin.bat
    // подхватывает файл автоматически.

    /// <summary>Плоская строка дампа [DM]: Terminal | Side | Connection | Cable (Фаза B,
    /// plan_implementation §23). rev.5.0: добавлены проба CDP и №31058
    /// («Соединение: Принадлежность=Кабель», summary Задача 5).</summary>
    public sealed class DmRow
    {
        public string StripName;
        public string TerminalName;
        public string Side;            // "Ext" | "Int" | "Bridge"
        public string ConnectionName;  // ConnectionInfo.ConnectionName
        public string PinName;         // ConnectionInfo.FunctionPinName
        public int PinIndex;           // ConnectionInfo.PinIndex
        public string PeerName;        // для моста: BridgedTerminal
        public string CableName;       // CDP -> CableDefinitionLine -> Cable.Name (null = провод)
        public bool HasConn;           // мост: Conn != null
        public int CdpCount = -1;      // ConnectionDefPoints: 0/1/>1; -1 = проба не удалась
        public bool? IsCableConn;      // №31058 на Connection (null = свойство не задано)
        public bool? IsCableCdp;       // №31058 на первом CDP (null = нет CDP/не задано)
        public string CableSource;      // №31019 CONNECTION_SOURCE на Connection (проба rev.16.3)
        public string CableDest;        // №31020 CONNECTION_DESTINATION на Connection (проба rev.16.3)
    }

    /// <summary>Итог пробы кабеля одного Connection (rev.5.0): кэш на прогон.
    /// KB (www.eplan.help API 2.9): Connection.CableDefinitionLine бросает BaseException
    /// при ≠1 CDP («Different count than 1») — основной путь: ConnectionDefPoints →
    /// ConnectionDefinitionPoint.CableDefinitionLine; №31058 читается на Connection и
    /// на CDP (docs помечает его legacy: «no longer in use, only old projects»).</summary>
    public sealed class CableInfo
    {
        public string CableName;
        public int CdpCount = -1;
        public bool? IsCableConn;
        public bool? IsCableCdp;
        // Проба rev.16.3: №31019/№31020 — только чтение и лог, в логике не участвуют.
        public string CableSource;
        public string CableDest;
    }

    /// <summary>Статистика одного клеммника (rev.4.1: сверка [CROSS] по-клеммнику —
    /// отчёт показывает один клеммник, сумма по проекту не годится, summary п.21).</summary>
    public sealed class DmStripStats
    {
        public int TerminalCount;
        public int ExtCount;
        public int IntCount;
        public int BridgeCount;
        public int ConnCount { get { return ExtCount + IntCount; } }
    }

    /// <summary>Итог чтения Data Model (Задача 4, план: plan_stage2.md).</summary>
    public sealed class DmReport
    {
        public readonly List<DmRow> Rows = new List<DmRow>();
        public int StripCount;
        public int TerminalCount;
        public int ErrCount;
        // По-клеммниковая статистика: полное имя клеммника -> счётчики (rev.4.1).
        public readonly Dictionary<string, DmStripStats> PerStrip =
            new Dictionary<string, DmStripStats>();
        // rev.5.1: ВСЕ имена клемм клеммника (в порядке TerminalStrip.Terminals),
        // включая клеммы без подключений — иначе сопоставление колонок К4 уезжает
        // (урок rev.5.0: 10 клемм без подключений выпадали, 61 колонка vs 50 групп).
        public readonly Dictionary<string, List<string>> StripTerminalNames =
            new Dictionary<string, List<string>>();
        // Кабель -> число жил, встреченных в подключениях клемм (первое обнаружение
        // логируется как [DMCABLE]).
        public readonly Dictionary<string, int> CableWireCounts = new Dictionary<string, int>();
        // rev.16.3 (задача 2): полное DT кабеля -> ВСЕ непустые значения №31019/№31020,
        // собранные по строкам этого кабеля (Ext/Int/Bridge). Порядок — первого
        // появления, без дублей, сравнение Ordinal; сортировки нет — детерминизм
        // «первый по Ordinal» задаёт потребитель BreakPointResolver.Decide(список DT).
        // Ключ — oRow.CableName: у Bridge ConnectionName не заполняется никогда,
        // PeerName лежит в другом поле, так что ориентир только на имя кабеля.
        // Кабель без непустых значений 31019/31020 ключом НЕ появляется.
        public readonly Dictionary<string, List<string>> CableCoreEnds =
            new Dictionary<string, List<string>>();
        // rev.5.0: статистика пробы CDP/№31058 по УНИКАЛЬНЫМ соединениям (кэш CableInfo).
        public int ConnCdpZero;
        public int ConnCdpOne;
        public int ConnCdpMulti;
        public int ConnCdpErr;
        public int IsCable31058ConnTrue;
        public int IsCable31058CdpTrue;
    }
}
