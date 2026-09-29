using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
// CS0104: HEServices.Label конфликтует с WinForms.Label — все Labels в этом файле контролы (уроки CS0108/CS0246 п.30/72).
using Label = System.Windows.Forms.Label;

namespace MyEplanActions
{
    /// <summary>Браузер символов кабеля v4 (Этап 8, H-4b v4, rev.14.2 — замечания
    /// 1–7 прогона rev.14.1; спека category_and_prewiev.md; rev.14.3 — строгий
    /// три-состояний клеток превью (bVariantsKnown + SymbolCatalog.IsCellEnabled),
    /// OK-гейты слотов H/V, пробы [SYMFDMAP-KEY]/[FD-BASE-SUM], дедуп двойного
    /// перечисления библиотеки в ctor; rev.14.4 — FD-мост [FDLIB]
    /// GetBaseSymbolFromSpecifiedSymbolLibrary (пер-либ обратный словарь
    /// _dctFdBySymbolPerLib: FD → символ В ЗАДАННУЮ библиотеку — покрытие
    /// однополюсной GOST_single_symbol, где BaseSymbol и #16018 пусты: 0 из 796,
    /// прогон rev.14.3) с первичным путём лукапа FDLIB > BaseSymbol > 16018 и
    /// дедупом сетки в RefreshPreviewGridByEntry; rev.14.5 — MDS-до-заполнение
    /// [MDFD]: гипотеза — FD-привязка «небазовых» символов (625 из 796 — не
    /// best-fitting ни одного FD, прогон rev.14.4: FDLIB дал 171) хранится на
    /// MDSymbol-УРОВНЕ (не на DataModel Symbol): MDSymbolPropertyList.
    /// SYMB_MAINFUNCTION «Main function # 16018» / SYMB_DESC «Symbol
    /// description # 16011»; пер-либ карты _dctMdFdIdByName/_dctMdDescByName
    /// до-заполняют FD/описания записей, путь расширен меткой «FDLIB+MDSymbol»;
    /// [FDLIB] отказы расщеплены (из них пустых — Invoke вернул не-Symbol/null;
    /// дублей — first-wins)); rev.14.7 — MDSymbolLibrary.Open(file, Mode.ReadOnly)
    /// по FILENAME (KB 2.9: Open(String)/Open(String, Mode) — статические,
    /// параметр «filename of the library that will be opened»; Mode: ReadOnly=1
    /// «database is read-only», Exclusive=3; исключение BaseException «readonly
    /// database opened in exclusive mode») перед [MDCREATE]-фабрикой: лог
    /// rev.14.6 показал имя («GOST_single_symbol») ≠ filename; файл-кандидаты —
    /// голое «имя.slk» + каталоги символов из настроек
    /// {USER|COMPANY|SYSTEM}.MANAGEMENT.DIRECTORIES.SYMBOLS (пробы [MDDIRS],
    /// Settings.GetStringSetting(path, idx): BaseException «setting is not
    /// defined» / «path doesn't exist» при отсутствии пути)); rev.14.9 —
    /// [MDPATH]: ПЕРВЫЙ filename-кандидат — каталог из PathInfo.Symbols
    /// (KB 2.9: «Returns default Symbols directory» — string; исключение —
    /// BaseException «directory cannot be obtained from settings»; ctor
    /// PathInfo() public, но помечен «Should be used by ProjectManager
    /// only!» — проба рантаймом, отказ ctor/Symbols — ProbeWarn [MDPATH]
    /// и продолжаем без него), затем Settings-каталоги, голое «имя.slk»
    /// последним; [MDDIRS] диагностика: прочие исключения пути — строка
    /// «'<path>' — <Type>: <msg>» (одна на путь, кап 3) и одна строка
    /// «итог: N каталогов (список: …)»); rev.14.10 — [SYSENT]: ПЕРВЫЙ
    /// filename-кандидат — полный путь системной .slk из системного пула
    /// мастер-данных Masterdata.SystemEntries (KB API 2.9: «Returns the
    /// file names of all master data in the system master data pool» —
    /// StringCollection; библиотеки символов .slk — master data, их пути
    /// есть в системном пуле; KB ExportSymbolLibrary: «Source *.slk …
    /// Destination *.esl» — .slk нативный формат, .esl экспортный
    /// (правка пользователя 29.09); боевой прецедент: 
    /// spike/TerminalStripReportSpike.cs:623 — Masterdata().SystemEntries
    /// перечислял системные формы .f11 БЕЗ исключений, прогоны Этапа 1;
    /// Masterdata — IDisposable → try/catch/finally, Dispose в finally
    /// с null-гейтом; отказ перечисления — ОДИН ProbeWarn [SYSENT] +
    /// продолжаем прежними источниками; пробы [SYSENT] кап 5 строк —
    /// итог «slk-файлов в системном пуле: N», сэмплы «пример: 'путь'»
    /// ×3 (пул без .slk — первые 3 ЛЮБЫЕ записи: диагностика формата
    /// пула), вердикт «имя → 'путь'» / WARN «'имя' в системном пуле
    /// НЕ найден»). Урок rev.14.9: системный пул ≠ PathInfo.Symbols —
    /// тот возвращает ПОЛЬЗОВАТЕЛЬСКИЙ «default Symbols directory»,
    /// а системная GOST_single_symbol.slk живёт в системном каталоге
    /// Symbols установки ([MDOPEN] rev.14.9: «Невозможно открыть
    /// библиотеку символов …»). Новый порядок кандидатов:
    /// SystemEntries → PathInfo → Settings → голое «имя.slk»:
    /// выбор библиотеки +
    /// ДЕРЕВО «Trade → Area → Категория → Группа → Определение функции → символ»
    /// (5 уровней по FunctionDefinition) + ПРЕВЬЮ-СЕТКА ФИКСИРОВАННО 8 клеток A–H
    /// (VariantNr 0..7; отсутствующие — disabled, не кликабельны; клик = визуальное
    /// выделение клетки, слоты H/V комбо — отдельно). Программный layout без
    /// designer (паттерн MainDialog: TableLayoutPanel, хелперы статическими
    /// методами — локальных функций в C#5 нет). Диалог ТОЛЬКО ЧИТАЕТ.
    /// Выход: свойства Library/SymbolName/VariantH/VariantV (контракт с MainDialog
    /// НЕ менялся: ctor(Project, library, name, variantH, variantV, logger), значения
    /// НЕ триммингуются — урок хвостового пробела п.33; Variant* — числа, как
    /// rev.14.0). «ОК» валиден только при непустых Library/SymbolName И принадлежности
    /// перечислению; «Отмена»/крестик — ничего не возвращается.
    /// === Перечисление (RefreshSymbols, rev.14.2; RC-1: замечания 1+2+4 = один
    /// корень — ранняя цепочка A пишла ТОЛЬКО имена) ===
    /// Новый порядок цепочек ПО ИНФОРМАТИВНОСТИ:
    /// - A2 (первичная): reflection-проба «Symbols» у DataModel SymbolLibrary
    ///   (рантайм-доказано rev.14.1) → элементы as MasterData.Symbol; для каждого:
    ///   варианты — ТИПИРОВАННО Symbol.Variants : SymbolVariant[] (KB страницы
    ///   Symbol~Variants; VariantNr + живые объекты для CreateDisplayList), FD —
    ///   КЛАССИФИКАЦИЯ SymbolProps [SYMFUNC-CAT] (rev.14.8 — свойства САМОГО
    ///   символа FUNC_*: cat+group непусты → FdInfo на месте, путь «SymbolProps»,
    ///   первичный), иначе обратные словари [FDLIB]/BaseSymbol (rev.14.4:
    ///   FDLIB первичен; fallback: SYMB_MAINFUNCTION #16018 через
    ///   reflection-пробу Properties на Symbol), описание — SYMB_DESC #16011
    ///   той же пробой; НИ ОДНОГО Symbol-объекта — отказ A2;
    /// - B (fallback): MDSymbolLibrary.Symbols (KB rev.13.1) — FD #16018 →
    ///   _dctFdById, число вариантов MDSymbol.Variants, SYMB_DESC;
    /// - C (последняя): names-only («Symbols» → строки / «Names») — деградация:
    ///   клетки отключены (варианты неизвестны), категории по префиксу, FunInfo
    ///   без описаний.
    /// Отказ всех — прежний статус-текст ошибки. R6: на УСПЕХЕ статус НЕ печатается
    /// (пустой); статусы ошибок/деградации сохранены.
    /// rev.14.15: шторм проб rev.14.12/13 под гейтом ProbeStormEnabled=false
    /// (CLR 0x80131506, п.109); классификация — ридер-скан
    /// TryGetMemberValueViaScan (GetProperty глотал AmbiguousMatchException
    /// на new-членах).
    /// rev.14.16: скан-семантика «первый одноимённый non-null» была не достаточна —
    /// пара base+new может отдать ПУСТУЮ обёртку от base и значение от производного
    /// (rev.14.15: 0 из 796 при живых [SYMPL]-значениях rev.14.13). Скан перебирает
    /// ВСЕХ одноимённых parameterless кандидатов (derived-вперёд) и принимает
    /// первого с НЕПУСТЫМ ToString; индекс-оверлоады не читаются вовсе.
    /// Диагностика: [SCAN-SUM] (исходы решают value/null/threw/notfound) +
    /// [SDIAG] (кап 8; победителя — DeclaringType несущего члена); Console-дампы
    /// устранены (в главном логе EPLAN глухи; WARN-бюджет не задет, INFO-канал).
    /// [SYMPL]-дампы точечных CABDCP2/CABDCP3 при выключенном шторме остаются —
    /// узкие по префиксам FUNC_*/SYMB_* (пары/броски по именам).
    /// === FD-словари (BuildFdDictionary) ===
    /// Один раз за жизнь диалога: Project.FunctionDefinitionLibrary.FunctionDefinitions,
    /// (а) _dctFdById (Id → FdInfo; первый побеждает — Id НЕ уникален глобально:
    /// 1305 FD → 40 Id, дамп [FD] rev.14.1); (б) НОВЫЙ _dctFdBySymbol
    /// (ключ = lib + '\u0001' + sym → FdInfo) из FD.BaseSymbol → SymbolLibraryName/
    /// SymbolName (KB members; рантайм НЕ подтверждён — try/catch + дамп [FD-BASE]
    /// первых 10). Поля уровней и Description локализуются SymbolCatalog.
    /// LocalizeMultiLang (формат блоба «de_DE@…;en_US@…;ru_RU@…» доказан дампом [FD];
    /// дамп [FD] первых 10 сохранён + desc).
    /// === Состояние выбора (R5, замечание 3) ===
    /// Поле «Имя выбранного символа» (_txtSymbol) УДАЛЕНО из layout: имя — private
    /// _strSelectedSymbol (SymbolName → оно же); AfterSelect листа применяет новый
    /// выбор (карточка по SymbolEntry из FD.Name/Desc, превью по записи); предвыборка
    /// — программная (FindLeaf → разворот+выделение; не найден — карточка по имени +
    /// превью по каскаду); OK-валидация — принадлежность перечислению;
    /// TxtSymbolOnTextChanged удалён совсем.
    /// === Дерево (RebuildTree, RC-3) ===
    /// Рендер SymbolCatalogNode рекурсией: Kind=Symbol — лист (Tag=имя), остальные
    /// ветки БЕЗ Tag с текстом «Name (N)» (число показанных листьев; карты чистых
    /// имён больше НЕ нужны — карточка берёт FD.Name из SymbolEntry по имени);
    /// поиск фильтрует листья (substring OrdinalIgnoreCase), пустые ветки скрыты;
    /// корень — библиотека; 5 уровней FD-пути (Trade → Area → Категория → Группа →
    /// Определение функции), unmapped — бакет «Без классификации» последним
    /// (SymbolCatalog, addin/UI/SymbolCatalog.cs).
    /// === Превью-сетка (R3+спека, RC-2: замечание 2) ===
    /// ВСЕГДА 8 клеток (4×2): внешняя Panel + ВНУТРЕННЯЯ панель рисования (Dock=Fill,
    /// добавлена ПЕРВОЙ) + Label буквы (Dock=Bottom 18, добавлена ВТОРОЙ — docking
    /// обрабатывается в обратном порядке, метка НЕ накрывает область рисования;
    /// Paint внутренней панели → DrawDisplayList(oArgs, inner.ClientRectangle);
    /// ResizeRedraw=true). Enabled — фон чёрный (R3); disabled — серый фон, серая
    /// буква, клик игнор. Клик по enabled = выделение (золотая рамка + буква золотая),
    /// слоты H/V НЕ трогаются; перерисовка выделения — Invalidate клеток. Display
    /// list клетки — каскад: живой SymbolVariant (A2) → new Symbol+new SymbolVariant
    /// (типизированная KB-связка) → by-strings (CreateDisplayList(String,String,
    /// Int32,Project), строка с именем библ и «»; рев.14.0, HE_Display), у каждой
    /// клетки СВОЙ DrawingService (один сервис = один display list); Reset перед
    /// каждым созданием + SetDefaultViewport (гипотеза центра R4 rev.14.1);
    /// деградация chain C → все клетки disabled (варианты неизвестны); Dispose всех сервисов — OnFormClosing
    /// и каждая пересборка.
    /// === Строки статуса и пробы ===
    /// Пробы [FD]/[FD-BASE]/[FD-BASE-SUM]/[SYMFDMAP]/[SYMFDMAP-KEY]/
    /// [SYMFDMAP-DESC]/[SYMFUNC-CAT]/[SYMFUNC-CAT-SUM]/[DSPROBE] — дублированный
    /// канал Console + DiagnosticLogger
    /// (урок прогона rev.14.0: только-Console был невидим в terminal_strip_addin.log!).
    /// R6: статус на успехе пустой.
    /// KB-факты API 2.9 (www.eplan.help):
    /// - Project.SymbolLibraries — «public SymbolLibrary[] SymbolLibraries { get; }»
    ///   — KB-доказан (rev.13.1);
    /// - MasterData.Symbol.Variants : SymbolVariant[]; SymbolVariant.SymbolLibraryName /
    ///   SymbolName («unique for library») / VariantNr — KB + рантайм (rev.13.8);
    /// - MDSymbolLibrary.Symbols — «public MDSymbol[] Symbols { get; }»; MD.Symbol.ctor
    ///   (path) и (Project, path) — конструктор НЕ доказан — Activator-проба (fix-1);
    /// - MDSymbolPropertyList ctor (MDSymbol) + SYMB_MAINFUNCTION (#16018, Int64),
    ///   SYMB_DESC (#16011) — KB;
    /// - DataModel.MasterData.SymbolPropertyList (SYMB_MAINFUNCTION/SYMB_DESC) —
    ///   KB-доказаны, НО как получить SymbolPropertyList от Symbol НЕ доказано →
    ///   ТОЛЬКО reflection-проба (без компиляционных рисков);
    /// - Project.FunctionDefinitionLibrary.FunctionDefinitions : FunctionDefinition[]
    ///   — KB; члены FD Id/Name/CategoryName/CategoryRegion/GroupName/MainGroup/
    ///   Description/BaseSymbol — KB members page; Id/кат/группа/имя рантайм-
    ///   подтверждены дампом [FD] rev.14.1; Description/BaseSymbol — рантайм ещё НЕ
    ///   подтвержден (try/catch + [FD-BASE] первых 10);
    /// - rev.14.4 [FDLIB]: FunctionDefinition.GetBaseSymbolFromSpecifiedSymbolLibrary(
    ///   SymbolLibrary) — KB members page FunctionDefinition: «public Symbol
    ///   GetBaseSymbolFromSpecifiedSymbolLibrary(SymbolLibrary symbolLibrary)» —
    ///   «A symbol library to get a symbol from. This may be a library from system
    ///   master data» — FD → мост символа В ЗАДАННУЮ библиотеку (гипотеза
    ///   однополюсной GOST_single_symbol); рантайм НЕ подтверждён — reflection-
    ///   Invoke (тип параметра не угадываем — урок CS0246/CS1503), try/catch
    ///   + [FDLIB]/[FDLIB-SAMPLE], отказ — честная деградация (fallback остаётся);
    /// - rev.14.5 [MDFD]: MDSymbolPropertyList.SYMB_MAINFUNCTION — «Main
    ///   function # 16018» на MDSymbol-УРОВНЕ (KB страница
    ///   MDSymbolPropertyList~SYMB_MAINFUNCTION, локальная база 28.09; rev.14.3
    ///   доказал: у DataModel Symbol тот же #16018 == null на DataModel-
    ///   обёртке) + SYMB_DESC «Symbol description # 16011» — до-заполнение
    ///   несопоставленных записей pass'ом по MDSymbolLibrary.Symbols;
    /// - rev.14.8 [SYMFUNC-CAT]: ВСЕ 4 члена — на SymbolPropertyList
    ///   (DataModel.MasterData; наш Activator-проб уже строит этот список):
    ///   FUNC_CATEGORY #20115 «Function definition: Category» —
    ///   PropertyValue(MultiLangString), read-only; FUNC_CATEGORY_REGION
    ///   #20088 «Function definition: Area» — PropertyValue(MultiLangString),
    ///   read-only; FUNC_GROUP #20116 «Function definition: Group» —
    ///   PropertyValue(MultiLangString), read-only; FUNC_CATEGORY_GROUP_ID
    ///   #20188 — PropertyValue(System.String) «Outputs the function definition
    ///   in the "Category / Group / Function definition" format» — готовая
    ///   строка классификации (KB 2.9, локальная база 28.09). Это свойства
    ///   САМОГО символа о его FD-классификации — то, чем EPLAN строит нативное
    ///   дерево (rev.14.2 читал по тому же списку только #16018 → null и
    ///   SYMB_DESC); catgroup-парсер — SymbolCatalog.ExtractFdNameFromCategoryGroup.
    /// - MasterData.Symbol(SymbolLibrary, String) + SymbolVariant(Symbol, Int32) —
    ///   конструкторы KB (вход display list клетек B/C-цепочек);
    /// - HEServices.DrawingService: ctor(); CreateDisplayList (SymbolVariant[, bool]);
    ///   CreateDisplayList (String,String,Int32,Project) — KB (пример HE_Display);
    ///   DrawDisplayList (PaintEventArgs, Rectangle) — «fit keeping aspect ratio»;
    ///   SetDefaultViewport — «Adjusts viewport to the bounding box»; Reset/Dispose.
    /// НЕ доказано KB (reflection/Activator-пробы; «одна гипотеза за прогон»):
    /// перечислитель «Symbols» у DataModel SymbolLibrary (элементы — рантайм-факт
    /// rev.14.1), Properties/Activator SymbolPropertyList(SА Symbol), поверхность
    /// PropertyValue (ExtractLongViaReflection), перегрузка CreateDisplayList
    /// (String,String,RepresentationType,Int32,Project) — сознательно НЕ пробуется.
    /// Отказы проб — Console-дамп + честные статусы старых ревизий.</summary>
    public class SymbolBrowserDialog : Form
    {
        private readonly Project _oProject;
        private readonly TextBox _txtLibrary = new TextBox();
        private readonly ListBox _lstLibraries = new ListBox();
        private readonly TextBox _txtSearch = new TextBox();
        private readonly TreeView _treeSymbols = new TreeView();

        // rev.14.1 (R6): слоты H/V — ComboBox букв A-H (всегда 8 пунктов); наружу —
        // числа (SelectedIndex = индекс варианта), пайплайн/настройки не тронуты.
        // rev.14.2: выделение клетки превью слоты сознательно НЕ трогает (спека §5).
        private readonly ComboBox _cbVariantH = new ComboBox();
        private readonly ComboBox _cbVariantV = new ComboBox();

        private readonly Label _lblCount = new Label();
        private readonly Label _lblStatus = new Label();

        // H-4b: карточка превью (справа) — имя/категория/описание символа (R7).
        private readonly Label _lblPreviewName = new Label();
        private readonly Label _lblPreviewCategory = new Label();
        private readonly Label _lblPreviewDescription = new Label();

        // Объекты библиотек под строками списка (параллельны _lstLibraries.Items).
        private readonly List<SymbolLibrary> _lstLibraryObjects = new List<SymbolLibrary>();

        // rev.14.2: записи символов (RC-1): имя + варианты (номера и ЖИВЫЕ объекты
        // SymbolVariant из A2) + резолв FD (BaseSymbol-обратный словарь → fallback
        // #16018 → MDSymbol #16018 → цепочка C без FD) + описание SYMB_DESC.
        // rev.14.3: флаг bVariantsKnown — строгий три-состояний клеток (п.93).
        private sealed class SymbolEntry
        {
            public string Name;
            public int nVariantCount;                          // -1 — неизвестно (деградация)
            /// <summary>true — Symbol.Variants прочитан успешно (даже при 0
            /// вариантов); false — сбой чтения / chain C / запись вне списка
            /// (строгий три-состояний клеток: false ⇒ все клетки disabled).</summary>
            public bool bVariantsKnown;
            public readonly List<int> lstVariantNrs = new List<int>();
            public readonly List<object> lstVariantObjects = new List<object>();
            public FdInfo Fd;                                  // null — не сопоставился
            public string strFdPath;                           // «SymbolProps»|«FDLIB»|«BaseSymbol»|«16018»|«MDSymbol»|null
            public string Description;
        }
        private List<SymbolEntry> _lstEntries = new List<SymbolEntry>();

        // rev.14.2 (R5): выбор символа — состояние БЕЗ текстового поля («Имя
        // выбранного символа» удалено из layout; имя задаётся ТОЛЬКО выбором
        // листа дерева/программной предвыборкой).
        private string _strSelectedSymbol = string.Empty;

        // rev.14.3 Task 5 (дедуп, фикс ревью C1): факт прогона rev.14.2 — двойной
        // обход библиотеки (2×796). Рантайм-тайминг подписки: SelectExact в
        // LoadLibraries стреляет событием ДО подписки (подписчик не видит), а
        // реальный вторый обход — отложенная доставка события на ShowDialog.
        // Дедуп по ИДЕНТИЧНОСТИ перечисленной библиотеки: обработчик пропускает
        // повтор, если перечислена та же SymbolLibrary; ctor после явного
        // RefreshSymbols выставляет идентичность — отложенный обработчик увидит
        // ту же библиотеку и пропустит. Смена на ДРУГУЮ библиотеку (другой
        // объект) — перечисляет; null при начале. Ровно одно перечисление.
        private SymbolLibrary _oEnumeratedLib;

        // rev.14.2: словари FD (проект; строятся ОДИН раз за жизнь диалога):
        // (а) Id → FdInfo — fallback-бакетизация (Id НЕ уникален глобально: 1305 FD
        //     → 40 Id, дамп [FD] rev.14.1; first wins);
        // (б) «lib\u0001sym» → FdInfo — ПЕРВИЧНАЯ связь символ → FD через FD.BaseSymbol.
        private Dictionary<long, FdInfo> _dctFdById;
        private Dictionary<string, FdInfo> _dctFdBySymbol;
        // rev.14.4 [FDLIB]: живые ссылки FD (пара FdInfo + объект FunctionDefinition)
        // — копятся в BuildFdDictionary; потребитель — BuildFdLibDictionary
        // (GetBaseSymbolFromSpecifiedSymbolLibrary по КАЖДОМУ FD в заданную
        // библиотеку — мост, которого нет у BaseSymbol-словаря).
        private List<KeyValuePair<FdInfo, Eplan.EplApi.DataModel.FunctionDefinition>> _lstFdRefs;
        // rev.14.4 [FDLIB]: ленивый пер-библиотечный обратный словарь (имя
        // библиотеки → «lib\u0001sym» → FdInfo); строится BuildFdLibDictionary
        // при первом появлении библиотеки в RefreshSymbols (одна запись на имя —
        // повторные перечисления той же библиотеки кэш НЕ пересобирают).
        private Dictionary<string, Dictionary<string, FdInfo>> _dctFdBySymbolPerLib;
        private int _nFdMapped;
        private int _nFdViaBase;
        private int _nFdVia16018;
        // rev.14.4 [FDLIB]: путь сопоставления рекордов A2 через пер-либ словарь.
        private int _nFdViaFdlb;
        // rev.14.8 [SYMFUNC-CAT]: путь классификации SymbolProps (свойства
        // САМОГО символа FUNC_*) + кап [SYMFUNC-CAT]-проб рядовых символов.
        // ОБА обнуляются ResetFdMatchCounters — кап на ПЕРЕЧИСЛЕНИЕ, не навсегда
        // (повторный RefreshSymbols печатает первые 10 заново).
        private int _nFdViaSymProps;
        private int _nCatProbes;
        // rev.14.5 [MDFD]: ленивые пер-библиотечные карты MDSymbol (имя
        // библиотеки → имя символа → …): (а) fdId > 0 — SYMB_MAINFUNCTION
        // (#16018) с MDSymbol-УРОВНЯ (осн. гипотеза прогона); (б) локализованный
        // SYMB_DESC — per-symbol описание точнее FD.Description. Строятся ОДИН
        // раз на библиотеку в TryFillFdViaMasterData (кэш-hit — записи
        // наполняются молча, без [MDFD]-строк).
        private Dictionary<string, Dictionary<string, long>> _dctMdFdIdByName;
        private Dictionary<string, Dictionary<string, string>> _dctMdDescByName;
        // rev.14.5 [MDFD]: счётчики до-заполнения текущего перечисления
        // (ResetFdMatchCounters обнуляет каждый RefreshSymbols; кэш-hit
        // инкрементирует заново).
        private int _nFdViaMd;
        private int _nDescViaMd;
        // rev.14.11 [SYMFUNC-MD]: пер-библиотечные карты MD-классификации
        // (имя библиотеки → имя символа → [catLoc, regionLoc, grpLoc, catGroupRaw]).
        // Кэш-контракт rev.14.5: карты вставляются в кэш ПОСЛЕ обхода (частичные —
        // тоже кэшируются, чтобы не спамить), кэш-hit — наполнение молча.
        private Dictionary<string, Dictionary<string, string[]>> _dctMdClsByName;
        // rev.14.11: счётчик наполнения FD через MD-классификацию + кап рядовых
        // [SYMFUNC-MD]-проб (10 на перечисление; оба обнуляет ResetFdMatchCounters).
        private int _nFdViaMdProps;
        private int _nMdCatProbes;
        // rev.14.12 [VARPROP]: кап MD-рядовых проб MDSymbolVariant (кап ПО
        // СИМВОЛАМ, MDSymbol.Variants); 10 на перечисление; обнуляется
        // ResetFdMatchCounters; точечные CABDCP2/CABDCP3 кап не тратят.
        // DataModel-проба капа-счётчика НЕ имеет (ревью rev.14.12: кап по
        // iOrdinal < 10 — прецедент [SYMFUNC-CAT]).
        private int _nMdVarProbes;
        // rev.14.16 [SCAN-SUM]/[SDIAG]: исходы ридер-скана за перечисление —
        // каждый вызов скана считается РОВНО в одну категорию (value / null-пусто / threw /
        // нет члена; сумма = все чтения) + капы детальных строк (win 5 / fail 8).
        // Всё — «на перечисление»: ResetFdMatchCounters обнуляет; [SCAN-SUM] печатает
        // хвост A2. INFO-канал: WARN-бюджет эталона не трогаем.
        private int _nScanRead;
        private int _nScanNull;
        private int _nScanThrew;
        private int _nScanNotFound;
        private int _nScanDiagWin;
        private int _nScanDiagFail;
        private string _strFdPath = "нет";   // путь сопоставления [SYMFDMAP] текущего перечисления

        // Превью-сетка (RC-2 fix): ВСЕГДА 8 фиксированных клеток (4×2); параллельные
        // списки: внешние панели, ВНУТРЕННИЕ панели рисования, буквы, готовность
        // display list, доступность варианта (disabled — серые, не кликаются).
        private readonly TableLayoutPanel _tlpPreviewGrid = new TableLayoutPanel();
        private readonly List<Panel> _lstPreviewCells = new List<Panel>();
        private readonly List<Panel> _lstCellDrawPanels = new List<Panel>();
        private readonly List<Label> _lstCellLetters = new List<Label>();
        private readonly List<DrawingService> _lstPreviewServices = new List<DrawingService>();
        private readonly List<bool> _lstCellReady = new List<bool>();
        // Ревью rev.14.2 (Minor-шум): успех-строка [DSPROBE] пути рендера — ОДИН раз
        // на пересборку сетки (8 клеток × 3 пути = до 24 строк на выделение).
        private bool _bPathLogged;
        private readonly List<bool> _lstCellEnabled = new List<bool>();
        private int _nSelectedCell = -1;   // выделенная клетка (клик = выбор варианта)
        // rev.14.3 Task 5 (гейт ApplySelection): имя, для которого превью-сетка
        // УЖЕ построена (ставится в RefreshPreviewGridByEntry после
        // BuildPreviewGrid — в т.ч. при полном отказе рендера: клетки уже стоят);
        // сброс — при разборке сетки пустыми строками и при смене библиотеки
        // (RefreshSymbols). Повторный AfterSelect того же имени сетку не пересобирает.
        private string _strPreviewGridKey = string.Empty;

        // Пробы [FD]/[FD-BASE]/[SYMFDMAP]/[SYMFDMAP-DESC]/[DSPROBE] — Console +
        // DiagnosticLogger (урок прогона rev.14.0: только-Console был невидим).
        private readonly DiagnosticLogger _oLogger;
        private bool _bFdDumped;           // FD-словари — один раз за жизнь диалога
        private int _nPropsFormLogged;     // [DSPROBE] «какая форма SymbolPropertyList сработала» — кап 5

        /// <summary>Вход: открытый проект (null — списки пусты), текущий выбор:
        /// строки как есть (предвыбор точным совпадением БЕЗ trim — урок п.33),
        /// индексы вариантов прижимаются к 0..7; oLogger — канал проб (nullable).</summary>
        public SymbolBrowserDialog(Project oProject, string strLibrary,
            string strName, int nVariantH, int nVariantV, DiagnosticLogger oLogger)
        {
            _oProject = oProject;
            _oLogger = oLogger;

            Text = "Выбор символа кабеля";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            // R3 (замечание 2): окно 840×620 — 8 клеток не помещались достойно.
            ClientSize = new Size(1120, 800);
            // Страховка маленьких мониторов (ревью rev.14.2): фиксированный диалог
            // не должен вылезать за рабочий стол — прижимаем к WorkingArea.
            Rectangle oWork = Screen.PrimaryScreen.WorkingArea;
            if (ClientSize.Height > oWork.Height - 40)
                ClientSize = new Size(ClientSize.Width, oWork.Height - 40);
            if (ClientSize.Width > oWork.Width - 40)
                ClientSize = new Size(oWork.Width - 40, ClientSize.Height);

            // Слоты H/V — ComboBox букв A-H (8 пунктов всегда); предвыбор из
            // настроек: число → SelectedIndex (вне 0..7 — прижать к 0 + INFO).
            FillVariantCombo(_cbVariantH);
            FillVariantCombo(_cbVariantV);
            SetVariantIndex(_cbVariantH, nVariantH);
            SetVariantIndex(_cbVariantV, nVariantV);

            // Карточка превью — прочерк до первого выбора.
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewDescription.Text = "Описание: —";

            _txtLibrary.Text = strLibrary ?? string.Empty;

            // (1) Библиотеки: Project.SymbolLibraries (KB-цитата в шапке).
            LoadLibraries(strLibrary);

            // (2) Реакции (диалог только читает; программных мутаций нет).
            // rev.14.2: поле «Имя выбранного символа» удалено (R5) — обработчик
            // TxtSymbolOnTextChanged и его подписка больше НЕ существуют.
            _lstLibraries.SelectedIndexChanged += LstLibrariesOnSelectedIndexChanged;
            _treeSymbols.AfterSelect += TreeSymbolsOnAfterSelect;
            _treeSymbols.NodeMouseDoubleClick += TreeSymbolsOnNodeMouseDoubleClick;
            _txtSearch.TextChanged += delegate { RebuildTree(); };

            // (3) Символы: цепочки перечисления для предвыбранной библиотеки,
            // затем программная предвыборка имени (FindLeaf/карточка/превью).
            // rev.14.3 Task 5 (дедуп, фикс ревью C1): явный вызов при предвыборе —
            // первичный перечислитель (событие на SelectExact теряется до подписки);
            // после него фиксируем идентичность — отложенный обработчик (Handle
            // создался к ShowDialog) увидит ту же библиотеку и пропустит.
            // Нет предвыбора (SelectedIndex<0) — не перечисляем (список пуст,
            // прежняя семантика).
            if (_lstLibraries.SelectedIndex >= 0)
            {
                RefreshSymbols();
                int iSel = _lstLibraries.SelectedIndex;
                _oEnumeratedLib = (iSel >= 0 && iSel < _lstLibraryObjects.Count)
                    ? _lstLibraryObjects[iSel] : null;
            }
            SelectSymbolPrechoice(strName);

            // (4) Кнопки: «ОК» — валидация в Click (DialogResult=OK ставится ТОЛЬКО
            // при непустых Library/Name, принадлежащих перечислению); «Отмена» —
            // DialogResult.Cancel (CancelButton).
            Button btnOk = new Button();
            btnOk.Text = "ОК";
            btnOk.Size = new Size(110, 27);
            btnOk.Click += BtnOkOnClick;
            Button btnCancel = new Button();
            btnCancel.Text = "Отмена";
            btnCancel.Size = new Size(110, 27);
            btnCancel.DialogResult = DialogResult.Cancel;
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            BuildLayout(btnOk, btnCancel);
        }

        /// <summary>Проба в главный лог (дублированный канал: Console + DiagnosticLogger,
        /// урок прогона rev.14.0 — только-Console был невидим в terminal_strip_addin.log).</summary>
        private void ProbeInfo(string strText)
        {
            Console.WriteLine(strText);
            if (_oLogger != null) _oLogger.Log(strText);
        }

        /// <summary>Проба-предупреждение (как ProbeInfo, уровень WARN в логе).</summary>
        private void ProbeWarn(string strText)
        {
            Console.WriteLine(strText);
            if (_oLogger != null) _oLogger.Warn(strText);
        }

        /// <summary>Проба [SYMFDMAP/[SYMFDMAP-DESC]/[FD…]] с капом шума: после первых
        /// 10 символов строки НЕ эмитятся вовсе (шум ~800 символов; итог — сводка
        /// [SYMFDMAP] в RefreshSymbols). Комментарий выправлен ревью rev.14.2:
        /// после капа каналов НЕТ (ни Console, ни логгер) — сводка единственный итог.</summary>
        private void Probe(string strText, int iOrdinal)
        {
            if (iOrdinal < 10) ProbeInfo(strText);
        }

        /// <summary>Имя библиотеки (как выбрано/введено, БЕЗ trim).</summary>
        public string Library
        {
            get { return _txtLibrary.Text; }
        }

        /// <summary>Имя символа (rev.14.2 R5: состояние выбора — поле «Имя выбранного
        /// символа» удалено, имя задаётся ТОЛЬКО выбором листа дерева/предвыборкой).
        /// Имя свойства SymbolName (не Name): CS0108-тень Form-члена — урок п.30.</summary>
        public string SymbolName
        {
            get { return _strSelectedSymbol; }
        }

        /// <summary>Индекс варианта слота H (0-based; UI — буква A-H, наружу число).</summary>
        public int VariantH
        {
            get { return _cbVariantH.SelectedIndex >= 0 ? _cbVariantH.SelectedIndex : 0; }
        }

        /// <summary>Индекс варианта слота V (0-based; UI — буква A-H, наружу число).</summary>
        public int VariantV
        {
            get { return _cbVariantV.SelectedIndex >= 0 ? _cbVariantV.SelectedIndex : 0; }
        }

        // --- обработчики ---

        /// <summary>Смена библиотеки (пользователь или отложенная доставка события
        /// после ctor): путь в поле → RefreshSymbols. rev.14.3 Task 5 (дедуп,
        /// фикс ревью C1): та же SymbolLibrary, что уже перечислена (ReferenceEquals),
        /// — пропуск (перечисление актуально); другая/null — перечисляет.</summary>
        private void LstLibrariesOnSelectedIndexChanged(object oSender, EventArgs oArgs)
        {
            int iIndex = _lstLibraries.SelectedIndex;
            SymbolLibrary oLib = (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                ? _lstLibraryObjects[iIndex] : null;
            if (ReferenceEquals(oLib, _oEnumeratedLib)) return;
            _oEnumeratedLib = oLib;
            if (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                _txtLibrary.Text = _lstLibraryObjects[iIndex] == null
                    ? string.Empty
                    : ResolveDisplayPath(_lstLibraryObjects[iIndex]);
            RefreshSymbols();   // stale-выбор/карточка гасятся ВНУТРИ RefreshSymbols
        }

        /// <summary>Выбор ЛИСТА дерева (символа) — состояние R5 + карточка/превью.
        /// Ветки (Trade/Area/Category/Group/Fd/бакеты) — Tag нет, игнор.</summary>
        private void TreeSymbolsOnAfterSelect(object oSender, TreeViewEventArgs oArgs)
        {
            TreeNode oNode = oArgs == null ? null : oArgs.Node;
            if (oNode == null || oNode.Tag == null) return;
            ApplySelection((string)oNode.Tag);
        }

        /// <summary>Применение выбранного имени (rev.14.2 R5): состояние + карточка
        /// (из SymbolEntry по имени) + превью-сетка по записи (каскад). rev.14.3
        /// Task 5: гейт «то же имя И сетка уже построена для него» — повторный
        /// AfterSelect (HighlightCurrentSymbol/предвыбор уже ставили SelectedNode)
        /// сетку заново не строит; при смене символа ключ обновится в
        /// RefreshPreviewGridByEntry.</summary>
        private void ApplySelection(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return;
            if (_strSelectedSymbol == strName && _strPreviewGridKey == strName)
                return;   // гейт: карточка+сетка уже применены к этому имени
            _strSelectedSymbol = strName;
            UpdateCardByEntry(strName);
            RefreshPreviewGridByEntry(strName);
        }

        /// <summary>Двойной клик по листу = ОК (наше «создать» — после валидации,
        /// как кнопка). Клик по ветке — игнор.</summary>
        private void TreeSymbolsOnNodeMouseDoubleClick(object oSender,
            TreeNodeMouseClickEventArgs oArgs)
        {
            if (oArgs == null || oArgs.Node == null || oArgs.Node.Tag == null) return;
            BtnOkOnClick(oArgs.Node, oArgs);
        }

        private void BtnOkOnClick(object oSender, EventArgs oArgs)
        {
            if (string.IsNullOrEmpty(Library) || string.IsNullOrEmpty(SymbolName))
            {
                MessageBox.Show(this, "Укажите библиотеку и выберите символ в дереве.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Перечисление недоступно — выбор невозможен (пустое перечисление при
            // старой предвыборке не должно пропустить пару «библ/имя, которой нет» —
            // Important ревью rev.14.1; проверка сохранена, только источник —
            // _lstEntries).
            if (_lstEntries.Count == 0)
            {
                MessageBox.Show(this,
                    "Перечисление символов недоступно для этой библиотеки — " +
                    "выбор невозможен (подробности в логе [DSPROBE]).",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (FindEntryIndex(SymbolName) < 0)
            {
                MessageBox.Show(this,
                    "Выбранный символ отсутствует в списке текущей библиотеки — " +
                    "выберите символ из дерева.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // rev.14.3 (п.93): варианты неизвестны (сбой чтения Symbol.Variants /
            // chain C / names-only) — OK блокирован: выбор слота по клетке не даст
            // реального варианта, падение на уровне пайплайна недопустимо.
            SymbolEntry oEntry = FindEntryObj(SymbolName);
            if (oEntry != null && !oEntry.bVariantsKnown)
            {
                MessageBox.Show(this,
                    "Варианты выбранного символа неизвестны (перечислены только " +
                    "имена) — выбор варианта невозможен.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // rev.14.3 (п.93): слоты должны указывать на РЕАЛЬНЫЙ вариант записи
            // (клетка i ↔ VariantNr == i, тот же индекс 0..7) — иначе пайплайн
            // получит слот без варианта (гейт строится на ==).
            if (oEntry != null && (!oEntry.lstVariantNrs.Contains(VariantH) ||
                !oEntry.lstVariantNrs.Contains(VariantV)))
            {
                MessageBox.Show(this,
                    "У символа нет варианта слота H/V (" +
                    VariantH.ToString(CultureInfo.InvariantCulture) +
                    "=" + ((char)('A' + VariantH)).ToString(CultureInfo.InvariantCulture) +
                    " / " +
                    VariantV.ToString(CultureInfo.InvariantCulture) +
                    "=" + ((char)('A' + VariantV)).ToString(CultureInfo.InvariantCulture) +
                    ") — выберите слот реального варианта.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>Крестик/Alt+F4 (CloseReason.UserClosing) — это «отмена»: гасим
        /// залипший DialogResult.OK (паттерн MainDialog). Валидации здесь нет.
        /// rev.14.2: здесь же — Dispose ВСЕХ DrawingService превью-сетки (владение
        /// диалогово: один сервис на клетку варианта).</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel && e.CloseReason == CloseReason.UserClosing &&
                DialogResult == DialogResult.OK)
            {
                DialogResult = DialogResult.Cancel;
            }
            DisposePreviewGrid();
        }

        // --- перечисление библиотек/символов (только чтение; цепочки проб) ---

        /// <summary>Project.SymbolLibraries — KB-доказанный SymbolLibrary[]
        /// (цитата в шапке). Отображаемое имя члена НЕ доказано KB —
        /// reflection-проба кандидатов (ResolveDisplayPath), отказ — ToString().
        /// Сбой перечисления — статусная строка, пустой список.</summary>
        private void LoadLibraries(string strPreselect)
        {
            if (_oProject == null)
            {
                SetStatus("Проект недоступен — только ручной ввод.");
                return;
            }
            try
            {
                SymbolLibrary[] arrLibs = _oProject.SymbolLibraries;
                if (arrLibs == null)
                {
                    SetStatus("Project.SymbolLibraries вернул null — только ручной ввод.");
                    return;
                }
                foreach (SymbolLibrary oLib in arrLibs)
                {
                    if (oLib == null) continue;
                    _lstLibraryObjects.Add(oLib);
                    _lstLibraries.Items.Add(ResolveDisplayPath(oLib));
                }
                SelectExact(_lstLibraries, strPreselect);
                if (_lstLibraries.Items.Count == 0)
                    SetStatus("Проектных библиотек символов не найдено — только ручной ввод.");
            }
            catch (Exception oEx)
            {
                SetStatus("Project.SymbolLibraries недоступен: " +
                    oEx.GetType().Name + ": " + oEx.Message + " — только ручной ввод.");
            }
        }

        /// <summary>Цепочки перечисления символов выбранной библиотеки — В ПОРЯДКЕ
        /// ИНФОРМАТИВНОСТИ (rev.14.2, RC-1: цепочка A rev.14.1 резко выигрывала
        /// и рано выходила, оставляя fdId=null/варианты -1/описание null):
        /// A2) DataModel SymbolLibrary «Symbols» → MasterData.Symbol (INTERVAL:
        ///     варианты + FD + SYMB_DESC — ПОЛНАЯ запись);
        /// B)  MDSymbolLibrary.Symbols (KB; FD #16018 → dctFdById, варианты числом,
        ///     SYMB_DESC);
        /// C)  names-only («Symbols» → строки / «Names») — деградация: клетки
        ///    отключены (варианты неизвестны), категории-префиксы.
        /// R6: на успехе статус НЕ печатается (пустой); деградация C и отказ всех —
        /// честные статусы. [SYMFDMAP]-сводка после каждой успешной цепочки.
        /// rev.14.4: ПЕРЕД A2 — ленивая сборка пер-либ FD-словаря [FDLIB]
        /// (BuildFdLibDictionary: мост GetBaseSymbolFromSpecifiedSymbolLibrary,
        /// кэш _dctFdBySymbolPerLib по имени библиотеки — повторный пропуск).
        /// rev.14.5: ПОСЛЕ успешной A2 — TryFillFdViaMasterData [MDFD]:
        /// MDS-до-заполнение FD/SYMB_DESC у записей без них (карты
        /// _dctMdFdIdByName/_dctMdDescByName, кэш по имени библиотеки).
        /// Никаких мутаций (диалог только читает).</summary>
        private void RefreshSymbols()
        {
            ProbeInfo("[BR] RefreshSymbols: enter");   // rev.14.14: крэш-хвост
            _lstEntries = new List<SymbolEntry>();
            ResetFdMatchCounters();
            DisposePreviewGrid();
            BuildFdDictionary();   // [FD]/[FD-BASE] — один раз за жизнь диалога
            _treeSymbols.Nodes.Clear();
            _lblCount.Text = string.Empty;
            // Fix (rev.14.1 ревью, Important) — сохранено: смена библиотеки — старое
            // имя не соответствует новому списку; stale-выбор гасим (OK не вернул бы
            // пару библ/имя, которой нет); карточка — прочерки.
            _strSelectedSymbol = string.Empty;
            _strPreviewGridKey = string.Empty;   // rev.14.3: ключ сетки — за выбором
            ClearCard();

            int iIndex = _lstLibraries.SelectedIndex;
            if (iIndex < 0 || iIndex >= _lstLibraryObjects.Count)
            {
                SetStatus("Библиотека не выбрана из списка — список символов пуст " +
                    "(выбор символа недоступен).");
                ProbeInfo("[BR] RefreshSymbols: exit (нет библиотеки)");   // rev.14.14
                return;
            }
            SymbolLibrary oLib = _lstLibraryObjects[iIndex];

            // rev.14.4 [FDLIB]: пер-библиотечный FD-словарь строится ЛЕНИВО перед
            // перечислением (кэш по имени — повторный вызов той же библиотеки
            // пропускает построение); имя — TryGetStringProperty(oLib, "Name").
            string strLibName = TryGetStringProperty(oLib, "Name");
            BuildFdLibDictionary(oLib, strLibName);

            // A2 (первичная): ПОЛНЫЕ записи Symbol (варианты/FD/описание).
            if (TryEnumerateViaDataModelTyped(oLib))
            {
                // rev.14.5 [MDFD]: MDS-до-заполнение FD/SYMB_DESC у записей без них
                // (после успешного перечисления A2, ДО расчёта пути/сводки/дерева;
                // кэш-hit — молча из карт).
                TryFillFdViaMasterData(oLib, strLibName);

                // rev.14.5: приоритет пути FDLIB > BaseSymbol > MDSymbol > 16018
                // (переписано с тернарника — читаемая if-цепочка; метки сохранены;
                // FDLIB и MDSymbol оба ненулевые → «FDLIB+MDSymbol»).
                // rev.14.8: SymbolProps ПЕРВЫЙ (классификация FUNC_* свойства
                // САМОГО символа); сработали оба пути → комбо
                // «SymbolProps+FDLIB».
                // rev.14.11: SymbolProps-MD ПЕРВЫЙ (FUNC_* на MDSymbolPropertyList —
                // MD-обёртка MDSymbol); сработали оба пути → комбо
                // «SymbolProps-MD+FDLIB».
                if (_nFdViaMdProps > 0 && _nFdViaFdlb > 0)
                    _strFdPath = "SymbolProps-MD+FDLIB";
                else if (_nFdViaMdProps > 0)
                    _strFdPath = "SymbolProps-MD";
                else if (_nFdViaSymProps > 0 && _nFdViaFdlb > 0)
                    _strFdPath = "SymbolProps+FDLIB";
                else if (_nFdViaSymProps > 0)
                    _strFdPath = "SymbolProps";
                else if (_nFdViaFdlb > 0 && _nFdViaMd > 0)
                    _strFdPath = "FDLIB+MDSymbol";
                else if (_nFdViaFdlb > 0)
                    _strFdPath = "FDLIB";
                else if (_nFdViaBase > 0)
                    _strFdPath = "BaseSymbol";
                else if (_nFdViaMd > 0)
                    _strFdPath = "MDSymbol";
                else if (_nFdVia16018 > 0)
                    _strFdPath = "16018";
                else
                    _strFdPath = "нет";
                ProbeFdSummary();
                RebuildTree();
                SetStatus(string.Empty);   // R6: успех — статус пустой
                ProbeInfo("[BR] RefreshSymbols: exit (A2)");   // rev.14.14
                return;
            }

            // Цепочка B: MDSymbolLibrary.Symbols. FIX-1 (review 09fb80d Important):
            // путь библиотеки — СВОИ кандидаты (EnumeratePathCandidates), НЕ
            // отображаемое имя ResolveDisplayPath (то остаётся возвращаемым Library).
            if (TryEnumerateViaMasterData(oLib))
            {
                _strFdPath = "MDSymbol";
                ProbeFdSummary();
                RebuildTree();
                SetStatus(string.Empty);   // R6: успех — статус пустой
                ProbeInfo("[BR] RefreshSymbols: exit (B)");   // rev.14.14
                return;
            }

            // Цепочка C: names-only — деградация (клетки отключены: варианты
            // неизвестны, префиксные бакеты).
            if (TryEnumerateNamesOnly(oLib))
            {
                _strFdPath = "нет";
                ProbeFdSummary();
                RebuildTree();
                SetStatus("Деградация: получены только имена символов (без вариантов, " +
                    "FD и описаний) — категории по префиксу. Варианты неизвестны — " +
                    "клетки превью отключены (выбор невозможен; подробности [DSPROBE]).");
                ProbeInfo("[BR] RefreshSymbols: exit (C)");   // rev.14.14
                return;
            }

            ProbeInfo("[BR] RefreshSymbols: exit (fail)");   // rev.14.14
            SetStatus("Перечисление символов недоступно (все цепочки пробили в отказ) — " +
                "выбор символа невозможен (подробности в [DSPROBE] лога).");
        }

        private void ResetFdMatchCounters()
        {
            _nFdMapped = 0;
            _nFdViaBase = 0;
            _nFdVia16018 = 0;
            _nFdViaFdlb = 0;   // rev.14.4 [FDLIB]
            _nFdViaSymProps = 0;   // rev.14.8 [SYMFUNC-CAT]
            _nFdViaMdProps = 0;    // rev.14.11 [SYMFUNC-MD]
            _nMdCatProbes = 0;     // rev.14.11: кап проб — на перечисление
            _nMdVarProbes = 0;     // rev.14.12 [VARPROP]: кап MD-рядовых символов     // rev.14.12 [VARPROP]: кап рядовых MD-вариантов
            _nCatProbes = 0;   // rev.14.8: кап проб — на перечисление, не навсегда
            _nFdViaMd = 0;     // rev.14.5 [MDFD]
            _nDescViaMd = 0;   // rev.14.5 [MDFD]
            _nScanRead = 0;       // rev.14.16 [SCAN-SUM]
            _nScanNull = 0;       // rev.14.16 [SCAN-SUM]
            _nScanThrew = 0;      // rev.14.16 [SCAN-SUM]
            _nScanNotFound = 0;   // rev.14.16 [SCAN-SUM]
            _nScanDiagWin = 0;    // rev.14.16 [SDIAG]
            _nScanDiagFail = 0;   // rev.14.16 [SDIAG]
            _strFdPath = "нет";
        }

        /// <summary>[SYMFDMAP]-сводка текущего перечисления.</summary>
        private void ProbeFdSummary()
        {
            ProbeInfo("[SYMFDMAP] сопоставлено " +
                _nFdMapped.ToString(CultureInfo.InvariantCulture) + " из " +
                _lstEntries.Count.ToString(CultureInfo.InvariantCulture) +
                " символов (путь: " + _strFdPath + ")");
        }

        // CS0618 (ruling контроллера, fix-2): PropertyInfo.GetValue(obj, args[]) —
        // устаревший, но единственный гарантированно доступный в референсе .NET 4
        // способ чтения свойства; newer overloads (GetValue(obj)/GetPropertyValue)
        // на стенде не доказаны — прямое обращение = риск CS1061. Заглушение плотной
        // парой pragma вокруг каждого метода, вызывающего GetValue; поведение не меняется.
#pragma warning disable 618

        /// <summary>Цепочка A2 (первичная, rev.14.2): reflection-доступ к «Symbols»
        /// у DataModel SymbolLibrary (рантайм-доказано rev.14.1), элементы —
        /// MasterData.Symbol; НИ ОДНОГО Symbol-объекта — false (переход к B).
        /// Для каждого символа: имя (ResolveNameViaReflection), варианты —
        /// ТИПИРОВАННО Symbol.Variants : SymbolVariant[] (KB): номера VariantNr
        /// 0..7 (вне — не в сетку A–H) + ЖИВЫЕ объекты вариантов (для
        /// CreateDisplayList), FD — обратные словари [FDLIB]/BaseSymbol, fallback
        /// #16018 (Properties-проба), описание — SYMB_DESC (та же проба). [DSPROBE]:
        /// какая форма сработала («Symbols → N объектов Symbol»). Только чтение.</summary>
        private bool TryEnumerateViaDataModelTyped(SymbolLibrary oLib)
        {
            try
            {
                PropertyInfo oProp = oLib.GetType().GetProperty("Symbols");
                if (oProp == null)
                {
                    ProbeInfo("[DSPROBE] A2: свойства «Symbols» у DataModel SymbolLibrary нет");
                    return false;
                }
                object oValue = oProp.GetValue(oLib, null);
                System.Array arrItems = oValue as System.Array;
                if (arrItems == null)
                {
                    ProbeInfo("[DSPROBE] A2: «Symbols» не массив (" +
                        (oValue == null ? "null" : oValue.GetType().Name) + ") — переход к B");
                    return false;
                }
                int nSymbols = 0;
                int iOrdinal = 0;
                foreach (object oItem in arrItems)
                {
                    if (oItem == null) continue;
                    // ГЕЙТ A2: элемент должен быть MasterData.Symbol (иначе — не наш
                    // путь: string[] уйдёт в цепочку B/C). Смешанные массивы —
                    // Symbol'ы собираем, посторонние пропускаем.
                    // CS0246-фикс: «MasterData» без квалификатора не резолвится
                    // (using импортирует ТИПЫ, не сам namespace) — «Symbol» дальше
                    // по файлу резолвится в DataModel.MasterData.Symbol тем же using.
                    Symbol oSymbol = oItem as Symbol;
                    if (oSymbol == null) continue;
                    iOrdinal++;
                    // rev.14.14: прогресс каждые 100 символов — крэш-хвост покажет,
                    // на каком символе умерло перечисление (слепая зона капа Probe).
                    if (iOrdinal % 100 == 0)
                        ProbeInfo("[BR] A2 прогресс: " +
                            iOrdinal.ToString(CultureInfo.InvariantCulture) + " символов");
                    string strName = ResolveNameViaReflection(oSymbol);
                    if (string.IsNullOrEmpty(strName)) continue;

                    SymbolEntry oEntry = new SymbolEntry();
                    oEntry.Name = strName;
                    oEntry.nVariantCount = -1;

                    // Варианты: KB Symbol~Variants — SymbolVariant[] (типизированно);
                    // вне 0..7 — в сетку A–H не попадают (16 = contact image).
                    try
                    {
                        SymbolVariant[] arrVars = oSymbol.Variants;
                        if (arrVars != null)
                        {
                            // rev.14.3: чтение Variants УСПЕШНО — варианты известны
                            // (даже при 0 вариантов); строгий три-состояний клеток.
                            oEntry.bVariantsKnown = true;
                            oEntry.nVariantCount = arrVars.Length;
                            foreach (SymbolVariant oVar in arrVars)
                            {
                                if (oVar == null) continue;
                                int nNr = -1;
                                try { nNr = oVar.VariantNr; }
                                catch (Exception oExVariant)
                                {
                                    Probe("[SYMFDMAP] VariantNr «" + strName + "» — " +
                                        oExVariant.GetType().Name + ": " +
                                        oExVariant.Message, iOrdinal - 1);
                                    continue;
                                }
                                if (nNr < 0 || nNr > 7) continue;
                                oEntry.lstVariantNrs.Add(nNr);
                                oEntry.lstVariantObjects.Add(oVar);
                            }
                        }
                    }
                    catch (Exception oEx)
                    {
                        // rev.14.3: сбой чтения Symbol.Variants — варианты
                        // НЕИЗВЕСТНЫ → все клетки disabled (не «только A»).
                        oEntry.bVariantsKnown = false;
                        Probe("[SYMFDMAP] Symbol.Variants «" + strName + "» — " +
                            oEx.GetType().Name + ": " + oEx.Message, iOrdinal - 1);
                    }

                    // rev.14.12 [VARPROP]: проба классификации на УРОВНЕ ВАРИАНТА —
                    // SymbolVariant.Properties (DataModel-обёртка, reflection);
                    // капы внутри (точечные CABDCP2/CABDCP3 всегда, рядовые 10).
                    TryProbeVariantProps(oEntry, strName, iOrdinal - 1);

                    // FD записи: (1) ПЕРВИЧНО — классификация SymbolProps
                    // [SYMFUNC-CAT] (rev.14.8): свойства САМОГО символа FUNC_*
                    // (KB 2.9, локальная база) — cat+group непусты → FdInfo на
                    // месте, путь «SymbolProps». Не дала — прежние пути:
                    // (2) пер-либ словарь [FDLIB] (rev.14.4) по lib/sym первого
                    // варианта; (3) fallback — BaseSymbol-обратный словарь;
                    // (4) SYMB_MAINFUNCTION #16018 через Properties-пробу.
                    if (oEntry.Fd == null)
                    {
                        TryClassifyViaSymbolProps(oEntry, oSymbol, strName,
                            iOrdinal - 1);
                    }
                    if (oEntry.Fd == null &&
                        oEntry.lstVariantNrs.Count > 0 && oEntry.lstVariantObjects.Count > 0)
                    {
                        object oFirstVariant = oEntry.lstVariantObjects[0];
                        string strVarLib = TryGetStringProperty(oFirstVariant, "SymbolLibraryName");
                        string strVarSym = TryGetStringProperty(oFirstVariant, "SymbolName");
                        // rev.14.3: гипотеза прогона — различить «пустые lib/sym»
                        // и «ключ есть / промах словаря» (0 из 796 при 279 пар).
                        // Проба ДО IsNullOrEmpty-гейта: пустой случай обязан быть
                        // ВИДЕН в логе (иначе ячейка «молчит» четырёхзначно, п.94).
                        if (string.IsNullOrEmpty(strVarLib) ||
                            string.IsNullOrEmpty(strVarSym))
                        {
                            Probe("[SYMFDMAP-KEY] «" + strName + "» вар[0] тип=" +
                                oFirstVariant.GetType().Name + " lib='" +
                                (strVarLib ?? "<null>") + "' sym='" +
                                (strVarSym ?? "<null>") +
                                "' → lookup НЕ выполнялся (пустые lib/sym)",
                                iOrdinal - 1);
                        }
                        if (!string.IsNullOrEmpty(strVarLib) && !string.IsNullOrEmpty(strVarSym))
                        {
                            string strKey = MakeFdSymbolKey(strVarLib, strVarSym);
                            FdInfo oInfoFd = null;
                            // rev.14.4: ПЕРВИЧНЫЙ лукап — пер-либ словарь [FDLIB]
                            // (FD → GetBaseSymbolFromSpecifiedSymbolLibrary(oLib)):
                            // покрывает однополюсные библиотеки, где BaseSymbol
                            // «best fitting» указывает в полнолинейные (прогон
                            // rev.14.3: GOST_single_symbol — 0 пар BaseSymbol).
                            bool bHitFdlb = _dctFdBySymbolPerLib != null &&
                                _dctFdBySymbolPerLib.ContainsKey(strVarLib) &&
                                _dctFdBySymbolPerLib[strVarLib] != null &&
                                _dctFdBySymbolPerLib[strVarLib].TryGetValue(strKey, out oInfoFd);
                            bool bHitBase = !bHitFdlb && _dctFdBySymbol != null &&
                                _dctFdBySymbol.TryGetValue(strKey, out oInfoFd);
                            bool bHit = bHitFdlb || bHitBase;
                            Probe("[SYMFDMAP-KEY] «" + strName + "» вар[0] тип=" +
                                oFirstVariant.GetType().Name + " lib='" +
                                (strVarLib ?? "<null>") + "' sym='" +
                                (strVarSym ?? "<null>") + "' → ключ " +
                                (bHit
                                    ? "НАЙДЕН " +
                                      (bHitFdlb
                                          ? "(FDLIB, FD «" + (oInfoFd == null ? "<null>" : oInfoFd.Name) + "»)"
                                          : "(FD «" + (oInfoFd == null ? "<null>" : oInfoFd.Name) + "»)")
                                    : "нет в словаре"),
                                iOrdinal - 1);
                            if (bHit)
                            {
                                if (bHitFdlb)
                                {
                                    oEntry.Fd = oInfoFd;
                                    oEntry.strFdPath = "FDLIB";
                                    _nFdViaFdlb++;
                                }
                                else
                                {
                                    oEntry.Fd = oInfoFd;
                                    oEntry.strFdPath = "BaseSymbol";
                                    _nFdViaBase++;
                                }
                            }
                        }
                    }
                    if (oEntry.Fd == null)
                    {
                        long? nFdId = TryGetFdIdFromSymbolProperties(
                            oSymbol, iOrdinal - 1, strName);
                        if (nFdId.HasValue && _dctFdById != null)
                        {
                            FdInfo oInfoById;
                            if (_dctFdById.TryGetValue(nFdId.Value, out oInfoById))
                            {
                                oEntry.Fd = oInfoById;
                                oEntry.strFdPath = "16018";
                                _nFdVia16018++;
                            }
                        }
                    }

                    // Описание: SYMB_DESC #16011 через Properties-пробу (rev.14.2).
                    oEntry.Description = TryGetSymbolSymbDesc(oSymbol, iOrdinal - 1, strName);

                    _lstEntries.Add(oEntry);
                    if (oEntry.Fd != null) _nFdMapped++;
                    nSymbols++;
                }
                if (nSymbols == 0)
                {
                    // Спека: «если ни одного Symbol-объекта → false» — B перечислит,
                    // C доберёт names-only.
                    ProbeInfo("[DSPROBE] A2: MasterData.Symbol-объектов нет — переход к B");
                    return false;
                }
                ProbeInfo("[DSPROBE] A2: «Symbols» → " +
                    nSymbols.ToString(CultureInfo.InvariantCulture) +
                    " объектов MasterData.Symbol");
                // rev.14.8 [SYMFUNC-CAT-SUM]: итог классификации SymbolProps за
                // перечисление — одна строка, капа нет. N — записей
                // strFdPath=="SymbolProps", M — всего записей A2, X — FDLIB,
                // Z — остальные (N+X+Z = M; попадают и BaseSymbol/16018-пути,
                // и действительно неклассифицированные). Подсчёт локальный по
                // _lstEntries (в A2 список содержит только записи перечисления).
                int nSumProps = 0;
                foreach (SymbolEntry oSumEntry in _lstEntries)
                {
                    if (oSumEntry != null && oSumEntry.strFdPath == "SymbolProps")
                        nSumProps++;
                }
                ProbeInfo("[SYMFUNC-CAT-SUM] классификация SymbolProps: " +
                    nSumProps.ToString(CultureInfo.InvariantCulture) + " из " +
                    _lstEntries.Count.ToString(CultureInfo.InvariantCulture) +
                    " символов (FDLIB " +
                    _nFdViaFdlb.ToString(CultureInfo.InvariantCulture) +
                    ", без классификации " +
                    (_lstEntries.Count - nSumProps - _nFdViaFdlb)
                        .ToString(CultureInfo.InvariantCulture) + ")");
                // rev.14.16 [SCAN-SUM]: исходы ридер-скана за перечисление —
                // вердикт пары «данные пусты (null/пусто)» vs «пара/оверлоад
                // глушит чтение (threw)» vs «имени нет (notfound)». Вердикт
                // следующего шага встаёт на эти три числа.
                ProbeInfo("[SCAN-SUM] ридер-скан за перечисление: value " +
                    _nScanRead.ToString(CultureInfo.InvariantCulture) +
                    ", null/пусто " + _nScanNull.ToString(CultureInfo.InvariantCulture) +
                    ", threw " + _nScanThrew.ToString(CultureInfo.InvariantCulture) +
                    ", нет члена " + _nScanNotFound.ToString(CultureInfo.InvariantCulture) +
                    " (всего " +
                    (_nScanRead + _nScanNull + _nScanThrew + _nScanNotFound)
                        .ToString(CultureInfo.InvariantCulture) + ")");
                return true;
            }
            catch (Exception oEx)
            {
                ProbeInfo("[DSPROBE] A2 отказ: " + oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }
#pragma warning restore 618

        /// <summary>Цепочка B (fallback): MDSymbolLibrary по кандидатам ПУТИ
        /// библиотеки (конструктор НЕ доказан KB — Activator-проба форм (string) и
        /// (Project, string) для каждого кандидата, fix-1), затем Symbols —
        /// KB-доказанное свойство (прямое типизированное обращение). Имена —
        /// ResolveNameViaReflection; число вариантов — MDSymbol.Variants (KB);
        /// FD-ID (#16018) → _dctFdById (TryGetFdId + [SYMFDMAP]); SYMB_DESC.
        /// Записи: номера вариантов 0..min(count,8)-1 (клетки включены по списку).</summary>
        private bool TryEnumerateViaMasterData(SymbolLibrary oLib)
        {
            object oMdLib = TryCreateMdLibrary(oLib);
            if (oMdLib == null) return false;
            try
            {
                Eplan.EplApi.MasterData.MDSymbol[] arrSymbols =
                    ((Eplan.EplApi.MasterData.MDSymbolLibrary)oMdLib).Symbols;
                if (arrSymbols == null || arrSymbols.Length == 0) return false;
                int iOrdinal = 0;
                foreach (Eplan.EplApi.MasterData.MDSymbol oMdSym in arrSymbols)
                {
                    if (oMdSym == null) continue;
                    string strName = ResolveNameViaReflection(oMdSym);
                    if (string.IsNullOrEmpty(strName)) continue;
                    int nVariantCount = TryGetVariantCount(oMdSym);
                    long? nFdId = TryGetFdId(oMdSym, iOrdinal, strName);
                    string strDesc = TryGetSymbDesc(oMdSym, iOrdinal, strName);

                    SymbolEntry oEntry = new SymbolEntry();
                    oEntry.Name = strName;
                    oEntry.nVariantCount = nVariantCount;
                    // rev.14.3: (п.93) Variants прочитан (=число) → известен;
                    // сбой/отказ чтения (-1) → неизвестен (все клетки disabled).
                    oEntry.bVariantsKnown = (nVariantCount >= 0);
                    int nCells = nVariantCount < 0 ? 0 : Math.Min(nVariantCount, 8);
                    for (int i = 0; i < nCells; i++) oEntry.lstVariantNrs.Add(i);
                    // FD: словарь по Id (16018 — FD-Id; первый FD с этим Id выигрывает:
                    // принцип dctFdById first-wins, рантайм-правило rev.14.1).
                    if (nFdId.HasValue && _dctFdById != null)
                    {
                        FdInfo oInfoFd;
                        if (_dctFdById.TryGetValue(nFdId.Value, out oInfoFd))
                        {
                            oEntry.Fd = oInfoFd;
                            oEntry.strFdPath = "MDSymbol";
                            _nFdMapped++;
                        }
                    }
                    oEntry.Description = strDesc;
                    _lstEntries.Add(oEntry);
                    iOrdinal++;
                }
                return _lstEntries.Count > 0;
            }
            catch (Exception oEx)
            {
                ProbeInfo("SymbolBrowserDialog: MDSymbolLibrary.Symbols — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }

        // CS0618 — см. блок выше.
#pragma warning disable 618
        /// <summary>Цепочка C (последняя, names-only): reflection-перебор кандидатов
        /// перечисления у DataModel SymbolLibrary (имена членов НЕ доказаны KB —
        /// только проба). Значение — string[] или Array элементов (имена —
        /// ResolveDisplayPath); записи БЕЗ вариантов/FD/описания (деградация:
        /// клетки отключены (варианты неизвестны), префиксные бакеты SymbolCatalog).
        /// Пустой/отказной результат кандидата — следующий; всё не удалось — false.</summary>
        private bool TryEnumerateNamesOnly(SymbolLibrary oLib)
        {
            string[] arrCandidateProps = new string[] { "Symbols", "Names" };
            foreach (string strProp in arrCandidateProps)
            {
                try
                {
                    PropertyInfo oProp = oLib.GetType().GetProperty(strProp);
                    if (oProp == null) continue;
                    object oValue = oProp.GetValue(oLib, null);
                    string[] arrStrings = oValue as string[];
                    if (arrStrings != null)
                    {
                        foreach (string strName in arrStrings)
                        {
                            if (string.IsNullOrEmpty(strName)) continue;
                            AddNamesOnlyEntry(strName);
                        }
                        if (_lstEntries.Count > 0)
                        {
                            ProbeInfo("[DSPROBE] C: «" + strProp + "» → " +
                                _lstEntries.Count.ToString(CultureInfo.InvariantCulture) +
                                " имён (строковый массив) — деградация");
                            return true;
                        }
                        continue;
                    }
                    System.Array arrItems = oValue as System.Array;
                    if (arrItems != null)
                    {
                        foreach (object oItem in arrItems)
                        {
                            if (oItem == null) continue;
                            string strName = ResolveDisplayPath(oItem);
                            if (string.IsNullOrEmpty(strName)) continue;
                            AddNamesOnlyEntry(strName);
                        }
                        if (_lstEntries.Count > 0)
                        {
                            ProbeInfo("[DSPROBE] C: «" + strProp + "» → " +
                                _lstEntries.Count.ToString(CultureInfo.InvariantCulture) +
                                " имён — деградация");
                            return true;
                        }
                    }
                }
                catch (Exception oEx)
                {
                    // Проба — не мутация: отказ кандидата — в дамп, далее (урок rev.7).
                    ProbeInfo("SymbolBrowserDialog: проба «" + strProp + "» — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            return false;
        }
#pragma warning restore 618

        /// <summary>Деградационная запись (цепочка C): имя, варианты НЕИЗВЕСТНЫ
        /// (nVariantCount=-1, bVariantsKnown=false — все клетки disabled, rev.14.3),
        /// FD=null (префиксные бакеты), описание=null.</summary>
        private void AddNamesOnlyEntry(string strName)
        {
            SymbolEntry oEntry = new SymbolEntry();
            oEntry.Name = strName;
            oEntry.nVariantCount = -1;
            oEntry.bVariantsKnown = false;
            _lstEntries.Add(oEntry);
        }

        /// <summary>rev.14.8 [SYMFUNC-CAT]: ПЕРВИЧНАЯ классификация FD через
        /// свойства САМОГО Symbol — FUNC_CATEGORY #20115 / FUNC_CATEGORY_REGION
        /// #20088 / FUNC_GROUP #20116 (PropertyValue(MultiLangString), read-only)
        /// и FUNC_CATEGORY_GROUP_ID #20188 (PropertyValue(System.String), формат
        /// «Category / Group / Function definition») на SymbolPropertyList
        /// (DataModel.MasterData — наш Activator-проб GetSymbolPropertyListViaProbes
        /// его уже строит). cat+group непусты → FdInfo на месте: MainGroup=null,
        /// Area=region, Category=cat, Group=group, Name — последний сегмент
        /// catgroup по " / " (SymbolCatalog.ExtractFdNameFromCategoryGroup; null →
        /// group), Description=null; strFdPath="SymbolProps". Печать [SYMFUNC-CAT]
        /// «имя»: cat=… region=… group=… catgroup=… — ВСЕГДА для CABDCP2/CABDCP3
        /// (точечные, кап не тратят), иначе первые 10 рядовых (поле _nCatProbes —
        /// обнуляется ResetFdMatchCounters, т.е. на перечисление). Первые три —
        /// LocalizeMultiLang от ToString (блоб MultiLangString, формат доказан);
        /// catgroup — сырая строка. Пустые свойства — НОРМА: false БЕЗ WARN
        /// (видно в [SYMFUNC-CAT]/[SYMFUNC-CAT-SUM]). Инстансный (Probe-канал,
        /// поля класса).</summary>
        private bool TryClassifyViaSymbolProps(SymbolEntry oEntry, object oSymbol,
            string strName, int iOrdinal)
        {
            if (oSymbol == null) return false;
            object oProps = GetSymbolPropertyListViaProbes(oSymbol, strName);
            if (oProps == null) return false;
            // rev.14.13 [SYMPL]: дамп непустых членов DM-списка ДО чтения
            // именованных FUNC_* — поиск привязки не угадыванием имён.
            // rev.14.16: точечные CABDCP2/CABDCP3 (bFull) живут и при
            // ВЫКЛЮЧЕННОМ шторме — узкий дамп (префиксы FUNC_*/SYMB_*, брошки/
            // пары по именам); рядовые (bFull=false) глушатся гейтом
            // DumpNonEmptyValues целиком. Сам дамп в try/catch — наружных
            // WARN не даёт.
            bool bPointCls =
                string.Compare(strName, "CABDCP2", StringComparison.Ordinal) == 0 ||
                string.Compare(strName, "CABDCP3", StringComparison.Ordinal) == 0;
            DumpNonEmptyValues(oProps, "«" + strName + "» DM-список", bPointCls,
                iOrdinal);
            try
            {
                object oValCat = TryGetMemberValueViaScan(oProps, "FUNC_CATEGORY");
                object oValRegion = TryGetMemberValueViaScan(oProps, "FUNC_CATEGORY_REGION");
                object oValGroup = TryGetMemberValueViaScan(oProps, "FUNC_GROUP");
                object oValCatGroup = TryGetMemberValueViaScan(oProps, "FUNC_CATEGORY_GROUP_ID");
                // PropertyValue.ToString() → строка (MultiLangString-блоб / String);
                // null/пусто → null. Первые три — локализация, catgroup — сырая.
                string strCat = ValueToStringOrNull(oValCat);
                string strRegion = ValueToStringOrNull(oValRegion);
                string strGroup = ValueToStringOrNull(oValGroup);
                string strCatGroup = ValueToStringOrNull(oValCatGroup);
                string strCatLoc = string.IsNullOrEmpty(strCat)
                    ? null : SymbolCatalog.LocalizeMultiLang(strCat);
                string strRegionLoc = string.IsNullOrEmpty(strRegion)
                    ? null : SymbolCatalog.LocalizeMultiLang(strRegion);
                string strGroupLoc = string.IsNullOrEmpty(strGroup)
                    ? null : SymbolCatalog.LocalizeMultiLang(strGroup);

                // Спайк: точечные CABDCP2/CABDCP3 — ВСЕГДА; рядовые — первые 10
                // (iOrdinal 0-based; кап — поле класса, точечные не тратят).
                // Точечные печатаются ProbeInfo НАПРЯМУЮ: Probe(text, iOrdinal)
                // внутри глушит iOrdinal >= 10 (кап helper'а), а точечные обязаны
                // печататься при ЛЮБОМ ординале.
                bool bPointName =
                    string.Compare(strName, "CABDCP2", StringComparison.Ordinal) == 0 ||
                    string.Compare(strName, "CABDCP3", StringComparison.Ordinal) == 0;
                if (bPointName || (iOrdinal < 10 && _nCatProbes < 10))
                {
                    if (!bPointName) _nCatProbes++;
                    string strText = "[SYMFUNC-CAT] «" + strName + "»: cat='" +
                        (strCatLoc ?? "<null>") + "' region='" +
                        (strRegionLoc ?? "<null>") + "' group='" +
                        (strGroupLoc ?? "<null>") + "' catgroup='" +
                        (strCatGroup ?? "<null>") + "'";
                    if (bPointName) ProbeInfo(strText);
                    else Probe(strText, iOrdinal);
                }

                // Классификация: cat И group непусты → FdInfo на месте.
                if (string.IsNullOrEmpty(strCatLoc) || string.IsNullOrEmpty(strGroupLoc))
                    return false;
                FdInfo oFd = new FdInfo();
                oFd.MainGroup = null;
                oFd.Area = strRegionLoc;
                oFd.Category = strCatLoc;
                oFd.Group = strGroupLoc;
                string strFdName = SymbolCatalog.ExtractFdNameFromCategoryGroup(strCatGroup);
                oFd.Name = strFdName ?? strGroupLoc;
                oFd.Description = null;
                oEntry.Fd = oFd;
                oEntry.strFdPath = "SymbolProps";
                _nFdViaSymProps++;
                // _nFdMapped++ ЗДЕСЬ НЕ НУЖЕН: единственный вызов метода — внутри
                // цикла A2 ДО хвоста «_lstEntries.Add(oEntry); if (oEntry.Fd !=
                // null) _nFdMapped++;» — хвост сосчитает и эту запись (паттерн
                // rev.14.5: in-loop пути считает хвост цикла; пост-loop
                // до-заполнения (TryFillFdViaMasterData) инкрементируют сами).
                // Инкремент в методе дал бы двойной счёт [SYMFDMAP].
                return true;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFUNC-CAT] «" + strName + "» FUNC_* через Symbol.Properties — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }

        /// <summary>PropertyValue → строка (ToString) без локализации: null-объект
        /// или пустой результат → null (rev.14.8 [SYMFUNC-CAT]).</summary>
        private static string ValueToStringOrNull(object oVal)
        {
            if (oVal == null) return null;
            string strRaw = oVal.ToString();
            return string.IsNullOrEmpty(strRaw) ? null : strRaw;
        }

        /// <summary>REV.14.13 [SYMPL]: ToString произвольного объекта-значения в
        /// try/catch: null-объект → null; пустой результат или брошенное
        /// исключение (MDPropertyValue на пустом свойстве бросает
        /// MDEmptyPropertyException — факт rev.14.10) → null БЕЗ шума. Для
        /// дамп-проб с произвольными типами (ValueToStringOrNull не ловит
        /// исключения — точечно-ловящий вариант). Static: зеркалит
        /// ValueToStringOrNull.</summary>
        private static string SafeToString(object oVal)
        {
            if (oVal == null) return null;
            try
            {
                string s = oVal.ToString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>FD-ID символа в цепочке A2 — FALLBACK #16018 c DataModel Symbol:
        /// как получить SymbolPropertyList от Symbol НЕ доказано (страниц
        /// Symbol~Properties / SymbolPropertyList~_ctor в базе нет) — ТОЛЬКО
        /// reflection-проба (свойство «Properties» / Activator ctor(Symbol)); далее
        /// SYMB_MAINFUNCTION чтение той же пробой → ExtractLongViaReflection →
        /// _dctFdById. Метка [SYMFDMAP] первых 10. Отказ — null (→ «Без
        /// классификации»). Инстансный (Probe-канал).</summary>
        private long? TryGetFdIdFromSymbolProperties(object oSymbol, int iOrdinal,
            string strName)
        {
            if (oSymbol == null) return null;
            object oProps = GetSymbolPropertyListViaProbes(oSymbol, strName);
            if (oProps == null) return null;
            try
            {
                object oVal = TryGetMemberValueViaScan(oProps, "SYMB_MAINFUNCTION");
                if (oVal == null)
                {
                    Probe("[SYMFDMAP] «" + strName + "» Symbol.props #16018 = null",
                        iOrdinal);
                    return null;
                }
                string strVia;
                long? nId = ExtractLongViaReflection(oVal, "SYMFDMAP:" + strName,
                    out strVia);
                Probe("[SYMFDMAP] «" + strName + "» Symbol.props #16018 → " +
                    (nId.HasValue
                        ? nId.Value.ToString(CultureInfo.InvariantCulture) + " via " + strVia
                        : "НЕТ"), iOrdinal);
                return nId;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP] «" + strName + "» #16018 через Symbol.Properties — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>Описание SYMB_DESC (#16011) в цепочке A2 — через Properties-пробу
        /// (reflection — как получить SymbolPropertyList от Symbol НЕ доказано).
        /// Блоб может вернуться MultiLangString-форматом «de_DE@…;ru_RU@…» —
        /// локализуется SymbolCatalog.LocalizeMultiLang. Метка [SYMFDMAP-DESC]
        /// первых 10. Пусто/отказ — null (карточка «—», иначе FD.Description).</summary>
        private string TryGetSymbolSymbDesc(object oSymbol, int iOrdinal, string strName)
        {
            if (oSymbol == null) return null;
            object oProps = GetSymbolPropertyListViaProbes(oSymbol, strName);
            if (oProps == null) return null;
            try
            {
                object oVal = TryGetMemberValueViaScan(oProps, "SYMB_DESC");
                if (oVal == null) return null;
                string strRaw = oVal.ToString();
                string strLocalized = SymbolCatalog.LocalizeMultiLang(strRaw);
                Probe("[SYMFDMAP-DESC] «" + strName + "» raw=«" +
                    (string.IsNullOrEmpty(strRaw) ? "<пусто>" : strRaw) + "» → «" +
                    (string.IsNullOrEmpty(strLocalized) ? "<пусто>" : strLocalized) + "»",
                    iOrdinal);
                return string.IsNullOrEmpty(strLocalized) ? null : strLocalized;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP-DESC] «" + strName + "» SYMB_DESC — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>SymbolPropertyList для DataModel MasterData Symbol — НИКАК не
        /// доказано (Rev.14.2: страниц Symbol~Properties / SymbolPropertyList~
        /// _ctor(Symbol) в базе нет): проба (1) свойство «Properties» объекта;
        /// (2) Activator ctor MasterData.SymbolPropertyList(Symbol). Формы сработок
        /// — в [DSPROBE] (кап 5; [DSPROBE] обязан показать КАКАЯ форма).
        /// Null/отказ — null; каждый отказ — Console-дамп.</summary>
        private object GetSymbolPropertyListViaProbes(object oSymbol, string strName)
        {
            if (oSymbol == null) return null;
            object oProps = TryGetMemberValue(oSymbol, "Properties");
            if (oProps != null)
            {
                if (_nPropsFormLogged < 5)
                {
                    ProbeInfo("[DSPROBE] SymbolPropertyList: форма «Symbol.Properties» → " +
                        oProps.GetType().FullName);
                    _nPropsFormLogged++;
                }
                return oProps;
            }
            try
            {
                Type oPlType = oSymbol.GetType().Assembly.GetType(
                    "Eplan.EplApi.DataModel.MasterData.SymbolPropertyList");
                if (oPlType == null) return null;
                oProps = Activator.CreateInstance(oPlType, new object[] { oSymbol });
                if (oProps != null && _nPropsFormLogged < 5)
                {
                    ProbeInfo("[DSPROBE] SymbolPropertyList: форма «Activator ctor(Symbol)» → ок");
                    _nPropsFormLogged++;
                }
                return oProps;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] SymbolPropertyList (Activator) «" + strName +
                    "» — " + oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>FD-ID символа в цепочке B — ПЕРВИЧНО из MDSymbol (rev.14.0
        /// KB MDSymbolPropertyList ctor(MDSymbol) + SYMB_MAINFUNCTION «Returns
        /// System.Int64»): извлечение числа — ExtractLongViaReflection (НЕ угадываем
        /// поверхность MDPropertyValue). Метка [SYMFDMAP] первых 10. Отказ — null:
        /// символ → «Без классификации».</summary>
        private long? TryGetFdId(Eplan.EplApi.MasterData.MDSymbol oMdSym,
            int iOrdinal, string strName)
        {
            if (oMdSym == null) return null;
            long? nId = null;
            string strRaw = null;
            try
            {
                Eplan.EplApi.MasterData.MDSymbolPropertyList oProps =
                    new Eplan.EplApi.MasterData.MDSymbolPropertyList(oMdSym);
                Eplan.EplApi.MasterData.MDPropertyValue oVal = oProps.SYMB_MAINFUNCTION;
                try { strRaw = oVal == null ? null : oVal.ToString(); }
                catch (Exception oEx)
                {
                    strRaw = null;
                    Probe("[SYMFDMAP] raw ToString «" + strName + "» — " +
                        oEx.GetType().Name + ": " + oEx.Message, iOrdinal);
                }
                // rev.14.11: ожидаемо-пустое #16018 (rev.14.10: пуст у всех 796,
                // oVal.ToString() бросает MDEmptyPropertyException или null) — тихий
                // выход ДО ExtractLongViaReflection, чтобы не сыпать серию WARN
                // (SYMFD:<имя> ToInt() TargetInvocationException + ToString) на
                // заведомо пустом свойстве. Raw-проба выше остаётся Probe-капнутой.
                if (string.IsNullOrEmpty(strRaw)) return null;
                string strVia;
                nId = ExtractLongViaReflection(oVal, "SYMFD:" + strName, out strVia);
                Probe("[SYMFDMAP] «" + strName + "» raw=«" + (strRaw ?? "<null>") +
                    "» id=" + (nId.HasValue
                        ? nId.Value.ToString(CultureInfo.InvariantCulture) + " via " + strVia
                        : "НЕТ"), iOrdinal);
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP] «" + strName + "» #16018 — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            return nId;
        }

        /// <summary>Описание символа в цепочке B: KB MDSymbolPropertyList.SYMB_DESC —
        /// «Symbol description # 16011»; значение → ToString(). Метка [SYMFDMAP-DESC]
        /// первых 10. Пусто/отказ — null (карточка «—» или FD.Description).</summary>
        private string TryGetSymbDesc(Eplan.EplApi.MasterData.MDSymbol oMdSym,
            int iOrdinal, string strName)
        {
            if (oMdSym == null) return null;
            try
            {
                Eplan.EplApi.MasterData.MDSymbolPropertyList oProps =
                    new Eplan.EplApi.MasterData.MDSymbolPropertyList(oMdSym);
                Eplan.EplApi.MasterData.MDPropertyValue oVal = oProps.SYMB_DESC;
                string strText = oVal == null ? null : oVal.ToString();
                if (iOrdinal < 10)
                    ProbeInfo("[SYMFDMAP-DESC] «" + strName + "» desc=«" +
                        (string.IsNullOrEmpty(strText) ? "<пусто>" : strText) + "»");
                return string.IsNullOrEmpty(strText) ? null : strText;
            }
            catch (Exception oEx)
            {
                if (iOrdinal < 10)
                    ProbeWarn("[SYMFDMAP-DESC] «" + strName + "» #16011 — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                return null;
            }
        }

        /// <summary>REV.14.11: классификация FUNC_* на MDSymbolPropertyList
        /// (MD-обёртка, ctor(MDSymbol) — в отличие от rev.14.8-спайка через
        /// DataModel-обёртку: KB-проверка 29.09 показала, что все 4 члена
        /// FUNC_CATEGORY #20115 / FUNC_CATEGORY_REGION #20088 / FUNC_GROUP #20116 /
        /// FUNC_CATEGORY_GROUP_ID #20188 есть именно на MDSymbolPropertyList).
        /// Typed-обращения НЕ используем (конвенция проекта: reflection-проба
        /// TryGetMemberValue — не ловить CS на стенде); первые три — локализация
        /// SymbolCatalog.LocalizeMultiLang, catgroup — сырая строка. Пустые
        /// свойства — НОРМА: в карту не попадают БЕЗ WARN. Запись в карту —
        /// только при непустых cat И group (region/group — опциональные).
        /// Пробы: точечные CABDCP2/CABDCP3 — ProbeInfo ВСЕГДА; рядовые — первые 10
        /// (кап _nMdCatProbes). Инстансный (Probe-канал, поля класса).</summary>
        private void TryClassifyViaMdProperties(
            Eplan.EplApi.MasterData.MDSymbol oMdSym,
            int iOrdinal, string strName,
            Dictionary<string, string[]> dctClsNew)
        {
            if (oMdSym == null || dctClsNew == null) return;
            try
            {
                Eplan.EplApi.MasterData.MDSymbolPropertyList oProps =
                    new Eplan.EplApi.MasterData.MDSymbolPropertyList(oMdSym);
                // rev.14.13 [SYMPL]: дамп непустых членов MD-списка ДО
                // отражённых чтений 4 FUNC_* — поиск привязки не угадыванием
                // имён. rev.14.16: точечные CABDCP2/CABDCP3 (bFull) живут и
                // при ВЫКЛЮЧЕННОМ шторме — узкий дамп (префиксы FUNC_*/SYMB_*,
                // брошки/пары по именам); рядовые глушатся гейтом целиком.
                bool bPointName =
                    string.Compare(strName, "CABDCP2", StringComparison.Ordinal) == 0 ||
                    string.Compare(strName, "CABDCP3", StringComparison.Ordinal) == 0;
                DumpNonEmptyValues(oProps, "«" + strName + "» MD-список",
                    bPointName, iOrdinal);
                object oValCat = TryGetMemberValueViaScan(oProps, "FUNC_CATEGORY");
                object oValRegion = TryGetMemberValueViaScan(oProps, "FUNC_CATEGORY_REGION");
                object oValGroup = TryGetMemberValueViaScan(oProps, "FUNC_GROUP");
                object oValCatGroup = TryGetMemberValueViaScan(oProps, "FUNC_CATEGORY_GROUP_ID");
                // rev.14.10-факт: MDPropertyValue.ToString() на ОЖИДАЕМО-пустом
                // свойстве бросает MDEmptyPropertyException — НЕ ValueToStringOrNull:
                // тот не ловит исключения, проба-метод поймал бы по WARN-у на символ
                // (тот же шум, что глушим в TryGetFdId). Хелпер ниже — тихо null.
                // null/пусто → null. Первые три — локализация, catgroup — сырая.
                string strCat = MdValueToStringOrNull(oValCat);
                string strRegion = MdValueToStringOrNull(oValRegion);
                string strGroup = MdValueToStringOrNull(oValGroup);
                string strCatGroup = MdValueToStringOrNull(oValCatGroup);
                string strCatLoc = string.IsNullOrEmpty(strCat)
                    ? null : SymbolCatalog.LocalizeMultiLang(strCat);
                string strRegionLoc = string.IsNullOrEmpty(strRegion)
                    ? null : SymbolCatalog.LocalizeMultiLang(strRegion);
                string strGroupLoc = string.IsNullOrEmpty(strGroup)
                    ? null : SymbolCatalog.LocalizeMultiLang(strGroup);

                // Пробы: точечные CABDCP2/CABDCP3 — ВСЕГДА ProbeInfo; рядовые —
                // первые 10 per перечисление (кап-счётчик _nMdCatProbes; Probe
                // внутри глушит iOrdinal >= 10, поэтому кап — двойной).
                if (bPointName || (iOrdinal < 10 && _nMdCatProbes < 10))
                {
                    if (!bPointName) _nMdCatProbes++;
                    string strText = "[SYMFUNC-MD] «" + strName + "»: cat='" +
                        (strCatLoc ?? "<null>") + "' region='" +
                        (strRegionLoc ?? "<null>") + "' group='" +
                        (strGroupLoc ?? "<null>") + "' catgroup='" +
                        (strCatGroup ?? "<null>") + "'";
                    if (bPointName) ProbeInfo(strText);
                    else Probe(strText, iOrdinal);
                }

                // Классификация: cat И group непусты → запись в карту
                // [catLoc, regionLoc, grpLoc, catGroupRaw]; неполные не кладём.
                if (string.IsNullOrEmpty(strCatLoc) || string.IsNullOrEmpty(strGroupLoc))
                    return;
                if (dctClsNew.ContainsKey(strName)) return;   // first-wins
                dctClsNew[strName] = new string[]
                {
                    strCatLoc, strRegionLoc, strGroupLoc, strCatGroup
                };
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFUNC-MD] «" + strName + "» FUNC_* на MD-обёртке — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>REV.14.12 [VARPROP-MEMS]: surface-дамп public-свойств объекта
        /// (GetProperties — Public|Instance по умолчанию; нижний регистр имён не
        /// ожидается, члены EPLAN — PascalCase). bFull=true (точечные
        /// CABDCP2/CABDCP3) — до 24 имён построчно (кап 24, хвост — «ещё K»);
        /// bFull=false — ОДНА строка, имена через «; », общий кап 240 символов
        /// (substring, без трепания). Исключение — ProbeWarn (тип исключения +
        /// сообщение). Инстансный (Probe-канал, поля класса).</summary>
        private void DumpMemberSurface(object oProps, string strTag, bool bFull,
            int iOrdinal)
        {
            if (oProps == null) return;
            // rev.14.15: шторм проб выключен (ProbeStormEnabled=false — CLR 0x80131506, п.109).
            if (!AddInConfiguration.ProbeStormEnabled) return;
            try
            {
                PropertyInfo[] arrProps = oProps.GetType().GetProperties();
                if (arrProps == null || arrProps.Length == 0)
                {
                    string strEmpty = "[VARPROP-MEMS] " + strTag +
                        ": <нет public-свойств>";
                    if (bFull) ProbeInfo(strEmpty);
                    else Probe(strEmpty, iOrdinal);
                    return;
                }
                if (bFull)
                {
                    int nNames = 0;
                    foreach (PropertyInfo oPropItem in arrProps)
                    {
                        if (nNames >= 24)
                        {
                            int nRest = arrProps.Length - nNames;
                            ProbeInfo("[VARPROP-MEMS] " + strTag + " … ещё " +
                                nRest.ToString(CultureInfo.InvariantCulture) +
                                " (кап 24)");
                            break;
                        }
                        ProbeInfo("[VARPROP-MEMS] " + strTag + " «" +
                            oPropItem.Name + "»");
                        nNames++;
                    }
                }
                else
                {
                    string strJoined = null;
                    foreach (PropertyInfo oPropItem in arrProps)
                    {
                        if (!string.IsNullOrEmpty(strJoined))
                            strJoined += "; ";
                        strJoined += oPropItem.Name;
                        if (strJoined.Length > 240)
                        {
                            strJoined = strJoined.Substring(0, 240);
                            break;
                        }
                    }
                    Probe("[VARPROP-MEMS] " + strTag + ": " + strJoined, iOrdinal);
                }
            }
            catch (Exception oEx)
            {
                ProbeWarn("[VARPROP-MEMS] " + strTag + " — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>REV.14.13 [SYMPL]: дамп ВСЕХ непустых значений public-свойств
        /// объекта (GetProperties — Public|Instance по умолчанию) с именами
        /// членов: поиск привязки символ→определение функции НЕ угадыванием имён,
        /// а полным списком — значение-кандидат покажет сам себя. Чтение каждого
        /// члена — через TryGetMemberValue (исключения чтения глотаются тихо:
        /// «члена нет» и «член бросает» для ДАМПА неразличимы и оба = пропуск;
        /// различение — отдельной пробой [VARPROP-EXC]). bFull=true (точечные
        /// CABDCP2/CABDCP3) — ПОСТРОЧНО через ProbeInfo без капа; bFull=false —
        /// ОДНА строка join " ", общий кап 400 символов (substring по char —
        /// UTF-байты не рвутся). Ноль непустых — честная строка «все члены
        /// пусты (N public-членов)». Инстансный (Probe-канал).
        /// rev.14.16: при ВЫКЛЮЧЕННОМ шторме дамп остаётся ТОЛЬКО для точечных
        /// (bFull) вызовов — и УЗКИЙ: члены по префиксам FUNC_*/SYMB_* (пары
        /// base+new и броски по именам — карта данных классификации). Полный
        /// дамп всех ~115 членов живёт за ProbeStormEnabled=true.</summary>
        private void DumpNonEmptyValues(object oProps, string strTag, bool bFull,
            int iOrdinal)
        {
            if (oProps == null) return;
            // rev.14.16: шторм ВЫКЛЮЧЕН (ProbeStormEnabled=false — CLR
            // 0x80131506, п.109), но точечные (bFull — только CABDCP2/CABDCP3)
            // [SYMPL]-дампы ОСТАЮТСЯ — это диагностика классификации, не шторм.
            // Рядовые (bFull=false) по-прежнему глушатся целиком.
            bool bPointOnly = !AddInConfiguration.ProbeStormEnabled;
            if (bPointOnly && !bFull) return;
            try
            {
                PropertyInfo[] arrProps = oProps.GetType().GetProperties();
                List<string> lstValues = new List<string>();
                List<string> lstThrew = new List<string>();
                int nScanned = 0;
                foreach (PropertyInfo oProp in arrProps)
                {
                    // rev.14.16: точечный дамп — УЗКИЙ: только члены классификации
                    // FUNC_*/SYMB_* (пара/броски по именам). Объём чтений на список
                    // — десятки против ~115×4×796 шторма rev.14.12/13 (п.5, п.109).
                    if (bPointOnly)
                    {
                        string strNameHere = oProp.Name;
                        if (string.IsNullOrEmpty(strNameHere)) continue;
                        if (!strNameHere.StartsWith("FUNC_", StringComparison.Ordinal) &&
                            !strNameHere.StartsWith("SYMB_", StringComparison.Ordinal))
                            continue;
                    }
                    nScanned++;
                    // rev.14.13 ревью Major-2: TryGetMemberValue глотает
                    // исключение → «пусто» и «бросок» неразличимы. Здесь
                    // чтение с явным диагнозом (НО в INFO-канал — не
                    // возвращать 1746-WARN-шум).
                    object oV;
                    try
                    {
#pragma warning disable 618
                        oV = oProp.GetValue(oProps, null);
#pragma warning restore 618
                    }
                    catch (Exception oExV)
                    {
                        Exception oE = oExV.InnerException != null
                            ? oExV.InnerException : oExV;
                        lstThrew.Add(oProp.Name + "(" + oE.GetType().Name + ")");
                        continue;
                    }
                    string strVal = SafeToString(oV);
                    if (!string.IsNullOrEmpty(strVal))
                        lstValues.Add(oProp.Name + "='" + strVal + "'");
                }
                string strRear = lstThrew.Count > 0
                    ? " | бросков " +
                        lstThrew.Count.ToString(CultureInfo.InvariantCulture) +
                        ": " + string.Join(", ",
                            lstThrew.Count > 3
                                ? lstThrew.GetRange(0, 3).ToArray()
                                : lstThrew.ToArray())
                    : "";
                if (lstValues.Count == 0 && lstThrew.Count == 0)
                {
                    string strNone = "[SYMPL] " + strTag + ": все члены пусты (" +
                        nScanned.ToString(CultureInfo.InvariantCulture) + " public-членов"
                        + (bPointOnly ? ", узкий дамп FUNC_*/SYMB_*" : "") + ")";
                    if (bFull) ProbeInfo(strNone);
                    else Probe(strNone, iOrdinal);
                    return;
                }
                if (bFull)
                {
                    foreach (string strItem in lstValues)
                        ProbeInfo("[SYMPL] " + strTag + " " + strItem);
                    foreach (string strThrew in lstThrew)
                        ProbeInfo("[SYMPL] " + strTag + " " + strThrew +
                            " <бросил>");
                }
                else
                {
                    string strJoined = string.Join(" ", lstValues.ToArray()) +
                        strRear;
                    if (strJoined.Length > 400)
                        strJoined = strJoined.Substring(0, 400);
                    Probe("[SYMPL] " + strTag + ": " + strJoined, iOrdinal);
                }
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMPL] " + strTag + " — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>REV.14.13 [VARPROP-EXC]: диагноз Properties=<null> — TryGetMemberValue
        /// глотает исключение молча (Console-канал невидим в главном логе), поэтому
        /// «члена нет» vs «член есть, GetValue бросает» не различено (рев.14.12: ×434
        /// all-null на обеих поверхностях). Проба: GetMethod("Properties")
        /// (public-only достаточно; KB-член public — NonPublic-вариант НЕ нужен).
        /// oM==null → «метода Properties нет (только property/field?)»; иначе
        /// Invoke(oVariant, new object[]{}) в try/catch: бросок → тип + сообщение
        /// (+InnerException) — ROOT-диагноз; успех + не-null → тип возвращённого +
        /// DumpNonEmptyValues/DumpMemberSurface поверх значения (тег
        /// «…-список(invoke)»). Печати: bFull (точечные) — ProbeInfo, остальные
        /// Probe(iOrdinal). Общий try/catch → ProbeWarn. Инстансный.</summary>
        private void ProbePropertiesViaInvoke(object oVariant, string strTag,
            bool bFull, int iOrdinal)
        {
            if (oVariant == null) return;
            // rev.14.15: шторм проб выключен (ProbeStormEnabled=false — CLR 0x80131506, п.109).
            if (!AddInConfiguration.ProbeStormEnabled) return;
            try
            {
                // rev.14.13 контроллер: Properties — PROPERTY (KB «{get;}»,
                // сигнатура getter = get_Properties) — GetMethod("Properties")
                // вернул бы null всегда (кодер-сомнение 1). Диагноз:
                // property есть → Invoke getter → исключение В ПРОБУ (root
                // rev.14.12-null: «члена нет» vs «член бросает»).
                System.Reflection.MethodInfo oM = null;
                try
                {
                    System.Reflection.PropertyInfo oP =
                        oVariant.GetType().GetProperty("Properties");
                    if (oP != null) oM = oP.GetGetMethod();
                }
                catch (System.Reflection.AmbiguousMatchException)
                {
                    try
                    {
                        System.Reflection.PropertyInfo[] arrPs =
                            oVariant.GetType().GetProperties();
                        foreach (System.Reflection.PropertyInfo oPs in arrPs)
                        {
                            if (string.Compare(oPs.Name, "Properties",
                                StringComparison.Ordinal) != 0)
                                continue;
                            // rev.14.13 ревью Major-1: наиболее производное
                            // объявление (DeclaringType == рантайм-тип) —
                            // иначе «new»-член можно схватить от базы.
                            if (oPs.DeclaringType == oVariant.GetType())
                            {
                                oM = oPs.GetGetMethod();
                                break;
                            }
                            if (oM == null) oM = oPs.GetGetMethod();   // первый как fallback
                        }
                    }
                    catch (Exception oExP)
                    {
                        string strScan = "[VARPROP-EXC] " + strTag +
                            ": скан GetProperties бросил — " +
                            oExP.GetType().Name + ": " + oExP.Message;
                        if (bFull) ProbeInfo(strScan);
                        else Probe(strScan, iOrdinal);
                        return;
                    }
                }
                if (oM == null)
                {
                    string strNoMethod = "[VARPROP-EXC] " + strTag +
                        ": свойства Properties нет (surface в [VARPROP-MEMS])";
                    if (bFull) ProbeInfo(strNoMethod);
                    else Probe(strNoMethod, iOrdinal);
                    return;
                }
                object oInv;
                try
                {
                    oInv = oM.Invoke(oVariant, new object[] { });
                }
                catch (Exception oExI)
                {
                    string strThrown = "[VARPROP-EXC] " + strTag +
                        ": Properties бросил " + oExI.GetType().Name + ": " +
                        oExI.Message +
                        (oExI.InnerException != null
                            ? " внутри " + oExI.InnerException.GetType().Name +
                                ": " + oExI.InnerException.Message
                            : "");
                    if (bFull) ProbeInfo(strThrown);
                    else Probe(strThrown, iOrdinal);
                    return;
                }
                if (oInv != null)
                {
                    // ревью Major-1: DeclaringType выбранного getter — факт
                    // «new-член варианта или унаследованный»
                    string strInvoked = "[VARPROP-EXC] " + strTag +
                        ": Properties вызван — " + oInv.GetType().FullName +
                        " (объявлен в " + oM.DeclaringType.Name + ")";
                    if (bFull) ProbeInfo(strInvoked);
                    else Probe(strInvoked, iOrdinal);
                    DumpNonEmptyValues(oInv, strTag + "-список(invoke)", bFull,
                        iOrdinal);
                    DumpMemberSurface(oInv, strTag + "-список(invoke)", bFull,
                        iOrdinal);
                }
                else
                {
                    string strNullInv = "[VARPROP-EXC] " + strTag +
                        ": Properties вызван → null (getter вернул null — " +
                        "читается без бросков, значения нет)";
                    if (bFull) ProbeInfo(strNullInv);
                    else Probe(strNullInv, iOrdinal);
                }
            }
            catch (Exception oEx)
            {
                ProbeWarn("[VARPROP-EXC] " + strTag + " — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>REV.14.12 [VARPROP]: проба классификации на УРОВНЕ ВАРИАНТА —
        /// SymbolVariant.Properties (DataModel-обёртка; KB: список =
        /// SymbolVariantPropertyList ctor(SymbolVariant)). Чтение 5 членов
        /// (FUNC_CATEGORY / FUNC_CATEGORY_REGION / FUNC_GROUP /
        /// FUNC_CATEGORY_GROUP_ID / SYMB_MAINFUNCTION) — ТОЛЬКО через
        /// TryGetMemberValue (тихий null при отсутствии; typed-члены на
        /// варианте-списках в KB НЕ задокументированы). SYMB_MAINFUNCTION —
        /// ExtractLongViaReflection ТОЛЬКО у ТОЧЕЧНЫХ (CABDCP2/CABDCP3) и ТОЛЬКО
        /// при непустом значении (Extract WARN-и на отказах — шум не масштабируем);
        /// рядовым — только строка значения. Поверх — DumpMemberSurface.
        /// Пробы: точечные всегда (ProbeInfo); рядовые — кап ПО СИМВОЛАМ
        /// (iOrdinal < 10 — прецедент [SYMFUNC-CAT]; внутренний кап
        /// Probe(iOrdinal>=10) дублирует). Инстансный.</summary>
        private void TryProbeVariantProps(SymbolEntry oEntry, string strName,
            int iOrdinal)
        {
            if (oEntry == null || oEntry.lstVariantObjects == null) return;
            // rev.14.15: шторм проб выключен (ProbeStormEnabled=false — CLR 0x80131506, п.109).
            if (!AddInConfiguration.ProbeStormEnabled) return;
            bool bPointVar =
                string.Compare(strName, "CABDCP2", StringComparison.Ordinal) == 0 ||
                string.Compare(strName, "CABDCP3", StringComparison.Ordinal) == 0;
            // rev.14.12 ревью Major/Minor: кап ПО СИМВОЛАМ (iOrdinal < 10 —
            // прецедент [SYMFUNC-CAT]); _nVarProbes удалён — протечка
            // surface-дампа после исчерпания вариант-капа и покрытие «10
            // вариантных проб ≈ 1-2 символа» вместе с ней. Рядовым печатям
            // внутренний кап Probe(iOrdinal>=10) достаточен.
            bool bRowAllowed = iOrdinal < 10;
            try
            {
                foreach (object oVarObj in oEntry.lstVariantObjects)
                {
                    if (oVarObj == null) continue;
                    object oVarProps = TryGetMemberValue(oVarObj, "Properties");
                    string strKind;
                    if (oVarProps == null)
                    {
                        strKind = "[VARPROP] «" + strName + "» Properties=<null>";
                        if (bPointVar) ProbeInfo(strKind);
                        else if (bRowAllowed) Probe(strKind, iOrdinal);
                        // rev.14.13 [VARPROP-EXC]: симметрично MD-варианту —
                        // GetMethod+Invoke-проба; рядовые гейтнуты bRowAllowed
                        // (внутри Probe(iOrdinal>=10) и так замолчали бы).
                        if (bPointVar || bRowAllowed)
                        {
                            ProbePropertiesViaInvoke(oVarObj,
                                "«" + strName + "» вар", bPointVar, iOrdinal);
                            // rev.14.13 [VARPROP-MEMS]: при null — surface
                            // САМОГО объекта-варианта (не списка).
                            DumpMemberSurface(oVarObj,
                                "«" + strName + "» объект", bPointVar, iOrdinal);
                        }
                        continue;
                    }
                    strKind = "[VARPROP] «" + strName + "» Properties=" +
                        oVarProps.GetType().FullName;
                    if (bPointVar) ProbeInfo(strKind);
                    else if (bRowAllowed) Probe(strKind, iOrdinal);
                    string[] arrMembers = new string[]
                    {
                        "FUNC_CATEGORY", "FUNC_CATEGORY_REGION", "FUNC_GROUP",
                        "FUNC_CATEGORY_GROUP_ID", "SYMB_MAINFUNCTION"
                    };
                    foreach (string strNm in arrMembers)
                    {
                        object oV = TryGetMemberValue(oVarProps, strNm);
                        string strVal = ValueToStringOrNull(oV);
                        string strLine = strNm + "=«" +
                            (string.IsNullOrEmpty(strVal) ? "<null>" : strVal) + "»";
                        if (string.CompareOrdinal(strNm, "SYMB_MAINFUNCTION") == 0 &&
                            bPointVar && !string.IsNullOrEmpty(strVal))
                        {
                            string strVia;
                            long? nId = ExtractLongViaReflection(oV,
                                "VARPROP:" + strName, out strVia);
                            if (nId.HasValue)
                            {
                                strLine += " id=" +
                                    nId.Value.ToString(CultureInfo.InvariantCulture) +
                                    " via " + strVia;
                            }
                        }
                        if (bPointVar) ProbeInfo("[VARPROP]   " + strLine);
                        else if (bRowAllowed) Probe("[VARPROP]   " + strLine, iOrdinal);
                    }
                    string strTag = bPointVar
                        ? "«" + strName + "» точечн"
                        : strName;
                    DumpMemberSurface(oVarProps, strTag, bPointVar, iOrdinal);
                }
            }
            catch (Exception oEx)
            {
                // rev.14.12: ValueToStringOrNull НЕ ловит исключения (пустое
                // свойство на DataModel-обёртке может бросить при ToString) —
                // отказ пробы НЕ должен обрывать цикл A2 (весь TryEnumerate
                // сидит на одном try/catch). Каналы: точечные ProbeInfo,
                // рядовые Probe (кап iOrdinal).
                string strErr = "[VARPROP] «" + strName + "» проба-отказ — " +
                    oEx.GetType().Name + ": " + oEx.Message;
                if (bPointVar) ProbeInfo(strErr);
                else Probe(strErr, iOrdinal);
            }
        }

        /// <summary>REV.14.12 [VARPROP]: проба классификации на УРОВНЕ MD-ВАРИАНТА —
        /// MDSymbol.Variants → MDSymbolVariant.Properties (KB: свойство без
        /// параметров, «public new MDSymbolVariantPropertyList Properties»;
        /// список = MDSymbolVariantPropertyList). Протокол как в
        /// TryProbeVariantProps (5 членов + DumpMemberSurface), strTag —
        /// ««имя» md-вар-список». Пробы: точечные всегда; рядовые — кап 10
        /// (_nMdVarProbes). Инстансный.</summary>
        private void TryProbeMdVariantProps(
            Eplan.EplApi.MasterData.MDSymbol oMdSym, string strName, int iOrdinal)
        {
            if (oMdSym == null) return;
            // rev.14.15: шторм проб выключен (ProbeStormEnabled=false — CLR 0x80131506, п.109).
            if (!AddInConfiguration.ProbeStormEnabled) return;
            bool bPointVar =
                string.Compare(strName, "CABDCP2", StringComparison.Ordinal) == 0 ||
                string.Compare(strName, "CABDCP3", StringComparison.Ordinal) == 0;
            if (!bPointVar && !(iOrdinal < 10 && _nMdVarProbes < 10)) return;
            object oArr = TryGetMemberValue(oMdSym, "Variants");
            if (oArr == null)
            {
                string strNull = "[VARPROP] «" + strName + "» md-Variants=<null>";
                if (bPointVar) ProbeInfo(strNull);
                else Probe(strNull, iOrdinal);
                if (!bPointVar) _nMdVarProbes++;
                return;
            }
            System.Array arrVars = oArr as System.Array;
            if (arrVars == null || arrVars.Length == 0)
            {
                string strEmpty = "[VARPROP] «" + strName + "» md-Variants=<пусто>";
                if (bPointVar) ProbeInfo(strEmpty);
                else Probe(strEmpty, iOrdinal);
                if (!bPointVar) _nMdVarProbes++;
                return;
            }
            foreach (object oVariant in arrVars)
            {
                if (oVariant == null) continue;
                object oVarProps = TryGetMemberValue(oVariant, "Properties");
                string strKind;
                if (oVarProps == null)
                {
                    strKind = "[VARPROP] md-вар «" + strName +
                        "» Properties=<null>";
                    if (bPointVar) ProbeInfo(strKind);
                    else Probe(strKind, iOrdinal);
                    // rev.14.13 [VARPROP-EXC]: «члена нет» vs «член бросает»
                    // — GetMethod+Invoke-проба (TryGetMemberValue глотал тихо).
                    ProbePropertiesViaInvoke(oVariant,
                        "«" + strName + "» md-вар", bPointVar, iOrdinal);
                    // rev.14.13 [VARPROP-MEMS]: при null — surface САМОГО
                    // объекта-варианта (не списка): где окончательно живёт
                    // Properties (свойство/метод/база).
                    DumpMemberSurface(oVariant,
                        "«" + strName + "» md-объект", bPointVar, iOrdinal);
                    continue;
                }
                strKind = "[VARPROP] md-вар «" + strName + "» Properties=" +
                    oVarProps.GetType().FullName;
                if (bPointVar) ProbeInfo(strKind);
                else Probe(strKind, iOrdinal);
                // rev.14.13 [VARPL]: дамп ВСЕХ непустых членов md-вар-списка —
                // ДО блока 5 именованных (блок ниже остаётся как есть).
                DumpNonEmptyValues(oVarProps, "«" + strName + "» md-вар-список",
                    bPointVar, iOrdinal);
                string[] arrMembers = new string[]
                {
                    "FUNC_CATEGORY", "FUNC_CATEGORY_REGION", "FUNC_GROUP",
                    "FUNC_CATEGORY_GROUP_ID", "SYMB_MAINFUNCTION"
                };
                foreach (string strNm in arrMembers)
                {
                    object oV = TryGetMemberValue(oVarProps, strNm);
                    string strVal = MdValueToStringOrNull(oV);
                    string strLine = strNm + "=«" +
                        (string.IsNullOrEmpty(strVal) ? "<null>" : strVal) + "»";
                    if (string.CompareOrdinal(strNm, "SYMB_MAINFUNCTION") == 0 &&
                        bPointVar && !string.IsNullOrEmpty(strVal))
                    {
                        string strVia;
                        long? nId = ExtractLongViaReflection(oV,
                            "VARPROP:" + strName, out strVia);
                        if (nId.HasValue)
                        {
                            strLine += " id=" +
                                nId.Value.ToString(CultureInfo.InvariantCulture) +
                                " via " + strVia;
                        }
                    }
                    if (bPointVar) ProbeInfo("[VARPROP]   " + strLine);
                    else Probe("[VARPROP]   " + strLine, iOrdinal);
                }
                DumpMemberSurface(oVarProps, "«" + strName + "» md-вар-список",
                    bPointVar, iOrdinal);
            }
            if (!bPointVar) _nMdVarProbes++;
        }

        /// <summary>MDPropertyValue → строка (ToString) без локализации: null-объект,
        /// пустой результат ИЛИ исключение ToString() (MDEmptyPropertyException на
        /// пустом MD-свойстве — rev.14.10-факт) → null БЕЗ шума.</summary>
        private static string MdValueToStringOrNull(object oVal)
        {
            if (oVal == null) return null;
            try
            {
                string strRaw = oVal.ToString();
                return string.IsNullOrEmpty(strRaw) ? null : strRaw;
            }
            catch
            {
                // rev.14.11 ревью M-1: молчание ЛЮБОГО отказа ToString —
                // намеренное (пустое MDPropertyValue бросает MDEmptyPropertyException
                // — ожидаемый случай, прогон rev.14.10; ссылку на тип исключения в
                // legacy-csc не доказывали — не рисковать CS). Прочие исключения
                // маловероятны и не являются сигналом классификации — карта пропустит
                // символ, а [SYMFDMAP]/[SYMFUNC-MD] покажут факт записи null.
                return null;
            }
        }

        // CS0618 — см. блоки выше: ExtractLongViaReflection и TryGetMemberValue
        // используют устаревший GetValue(args[]).
#pragma warning disable 618
        /// <summary>Извлечение числа из объекта-провайдера (PropertyValue и прочих):
        /// reflection-проба кандидатов — НИЧЕГО не угадываем (урок rev.7):
        /// (1) уже готовые long/int; (2) методы ToInt64()/ToInt(); (3) свойство Value;
        /// (4) fallback — ToString()+long.TryParse. strVia — каким путём получилось
        /// (печатается в [SYMFDMAP]). Отказ — null.</summary>
        private long? ExtractLongViaReflection(object oValue, string strContext,
            out string strVia)
        {
            strVia = "—";
            if (oValue == null) return null;
            if (oValue is long) { strVia = "direct long"; return (long)oValue; }
            if (oValue is int) { strVia = "direct int"; return (long)(int)oValue; }
            Type oType = oValue.GetType();
            // (2) методы ToInt64()/ToInt() — кандидаты; отказ/отсутствие — далее.
            string[] arrMethods = new string[] { "ToInt64", "ToInt" };
            foreach (string strMethod in arrMethods)
            {
                try
                {
                    MethodInfo oMethodInfo = oType.GetMethod(strMethod, Type.EmptyTypes);
                    if (oMethodInfo == null) continue;
                    object oRes = oMethodInfo.Invoke(oValue, null);
                    if (oRes is long)
                    {
                        strVia = strMethod + "()";
                        return (long)oRes;
                    }
                    if (oRes is int)
                    {
                        strVia = strMethod + "()";
                        return (long)(int)oRes;
                    }
                }
                catch (Exception oEx)
                {
                    ProbeWarn("[SYMFDMAP] " + strContext + " " + strMethod +
                        "() — " + oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            // (3) свойство Value — может вернуть готовое число.
            try
            {
                PropertyInfo oProp = oType.GetProperty("Value");
                if (oProp != null)
                {
                    object oVal = oProp.GetValue(oValue, null);
                    if (oVal is long) { strVia = "Value(long)"; return (long)oVal; }
                    if (oVal is int)
                    {
                        strVia = "Value(int)";
                        return (long)(int)oVal;
                    }
                }
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP] " + strContext + " Value — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            // (4) fallback — ToString + TryParse (урок: НЕ угадывать API).
            try
            {
                string strText = oValue.ToString();
                if (!string.IsNullOrEmpty(strText))
                {
                    long nParsed;
                    if (long.TryParse(strText.Trim(), out nParsed))
                    {
                        strVia = "ToString+TryParse";
                        return nParsed;
                    }
                }
            }
            catch (Exception oEx)
            {
                ProbeWarn("[SYMFDMAP] " + strContext + " ToString — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            return null;
        }
#pragma warning restore 618

        // CS0618 — GetValue(args[]) внутри TryGetMemberValue.
#pragma warning disable 618
        /// <summary>Чтение члена (свойство или поле) — reflection-проба БЕЗ
        /// компиляционных рисков; отказ/отсутствие — null + Console-дамп ошибки.
        /// Static: используется из статических хелперов (урок CS0120 rev.14.1).</summary>
        private static object TryGetMemberValue(object oTarget, string strMember)
        {
            if (oTarget == null) return null;
            try
            {
                PropertyInfo oProp = oTarget.GetType().GetProperty(strMember);
                if (oProp != null) return oProp.GetValue(oTarget, null);
                FieldInfo oField = oTarget.GetType().GetField(strMember);
                if (oField != null) return oField.GetValue(oTarget);
            }
            catch (Exception oEx)
            {
                Console.WriteLine("SymbolBrowserDialog: чтение «" + strMember +
                    "» бросило — " + oEx.GetType().Name + ": " + oEx.Message);
            }
            return null;
        }
#pragma warning restore 618

        // CS0618 — GetValue(args[]) внутри ридер-скана rev.14.16.
#pragma warning disable 618
        /// <summary>REV.14.16: скан-чтение члена ПО GetProperties()-перечислению
        /// (GetProperty(name) на property-списках ловит AmbiguousMatchException —
        /// имена дублируются парами base + new-производная, факт [VARPROP-MEMS]
        /// rev.14.13). rev.14.15-семантика «первый одноимённый, non-null» была
        /// НЕдостаточна: пара base+new складывается в ДВА чтения, и одно может
        /// отдать ПУСТУЮ обёртку (PropertyValue/MDPropertyValue non-null с
        /// пустым ToString — MD-пусто бросает MDEmptyPropertyException, факт
        /// rev.14.10), а другое — значение (дампы [SYMPL] rev.14.13: имена
        /// парами, значения у одного из двойников). Теперь: кандидаты = ВСЕ
        /// одноимённые parameterless public геттеры (индексаторные оверлоады НЕ
        /// читаются — TargetParameterCount закрывается на входе, считается
        /// diag-строкой); порядок — derived-объявления вперёд
        /// (DeclaringType == рантайм-тип), затем прочие; кандидат принимается
        /// только при НЕПУСТОМ ToString (пустая обёртка проскакивает к
        /// следующему). Инстансный — диагностика Probe-каналами: [SCAN-SUM]
        /// (исходы за перечисление, печатает хвост A2), [SDIAG] победителя
        /// (кап 5, DeclaringType несущего члена) и отказов (кап 8: «нет
        /// члена» / «все бросили: типы» / «пустых K, бросков T»). Console-дамп
        /// rev.14.15 (невидим в главном логе EPLAN) убран. Отказ — null; исход
        /// на вызов считается РОВНО один раз (value/null/threw/notfound).</summary>
        private object TryGetMemberValueViaScan(object oTarget, string strMember)
        {
            if (oTarget == null) return null;
            Type oType = oTarget.GetType();
            PropertyInfo[] arrProps;
            try
            {
                arrProps = oType.GetProperties();
            }
            catch (Exception oExG)
            {
                _nScanThrew++;
                LogScanFail(strMember, "GetProperties бросил — " +
                    oExG.GetType().Name + ": " + oExG.Message);
                return null;
            }
            if (arrProps == null)
            {
                _nScanThrew++;
                LogScanFail(strMember, "GetProperties вернул null");
                return null;
            }
            // Сбор кандидатов: одноимённые; parameterless — в карту, индексные — счёт.
            List<PropertyInfo> lstParamless = new List<PropertyInfo>();
            int nIndexed = 0;
            bool bNamed = false;
            foreach (PropertyInfo oProp in arrProps)
            {
                if (oProp.Name == null ||
                    string.Compare(oProp.Name, strMember, StringComparison.Ordinal) != 0)
                    continue;
                bNamed = true;
                if (oProp.GetIndexParameters().Length > 0)
                {
                    nIndexed++;
                    continue;
                }
                lstParamless.Add(oProp);
            }
                if (!bNamed)
                {
                    // rev.14.16: члена-свойства нет — столб по полям
                    // (ревью rev.14.16 M2: формулировка отказа честная —
                    // «свойства нет» И «поле есть, непустого значения не дало»
                    // раздельно не нужны; общий не-чтение считается notfound).
                    FieldInfo oField = oType.GetField(strMember);
                    if (oField != null)
                    {
                        object oFieldVal = oField.GetValue(oTarget);
                        string strFV;
                        string strFEx;
                        if (oFieldVal != null &&
                            TryToStringSafe(oFieldVal, out strFV, out strFEx) &&
                            !string.IsNullOrEmpty(strFV))
                        {
                            _nScanRead++;
                            LogScanWin(strMember,
                                "поле " + (oField.FieldType == null ? "<null>" : oField.FieldType.Name),
                                0, 0);
                            return oFieldVal;
                        }
                    }
                    _nScanNotFound++;
                    LogScanFail(strMember,
                        "свойства нет в GetProperties; поле есть, но непустого значения не выдало");
                    return null;
                }
            // Порядок чтения: derived (DeclaringType == рантайм-тип) вперёд,
            // затем прочие объявления в порядке GetProperties().
            List<PropertyInfo> lstOrdered = new List<PropertyInfo>();
            foreach (PropertyInfo oProp in lstParamless)
            {
                if (oProp.DeclaringType == oType) lstOrdered.Add(oProp);
            }
            foreach (PropertyInfo oProp in lstParamless)
            {
                if (oProp.DeclaringType != oType) lstOrdered.Add(oProp);
            }
            int nThrew = 0;
            List<string> lstThrowTypes = new List<string>();
            foreach (PropertyInfo oProp in lstOrdered)
            {
                object oVal;
                try
                {
                    oVal = oProp.GetValue(oTarget, null);
                }
                catch (Exception oExV)
                {
                    nThrew++;
                    lstThrowTypes.Add(TryGetExceptionName(oExV));
                    continue;
                }
                if (oVal == null) continue;
                string strVal;
                string strExName;
                if (!TryToStringSafe(oVal, out strVal, out strExName))
                {
                    // rev.14.16: MD-пусто бросает на ToString (rev.14.10) —
                    // НЕ проваливаться с этим кандидатом: читать следующий
                    // (второй член пары base+new может нести значение).
                    // rev.14.16 ревью M1: тип броска — В список (это главный
                    // путь пустых MD-обёрток; без него «все THROW ветка»
                    // печатала «—» при непустом nThrew).
                    nThrew++;
                    lstThrowTypes.Add(strExName == null ? "ToString" : strExName);
                    continue;
                }
                if (string.IsNullOrEmpty(strVal)) continue;
                // ПОБЕДИТЕЛЬ: непустой ToString — значение есть.
                _nScanRead++;
                LogScanWin(strMember,
                    oProp.DeclaringType == null ? "<null>" : oProp.DeclaringType.Name,
                    lstOrdered.Count, nIndexed);
                return oVal;
            }
            if (lstOrdered.Count == 0)
            {
                // Все одноимённые — индексные оверлоады; для классификационных
                // чтений это «члена нет» (parameterless-геттера не существует).
                _nScanNotFound++;
                LogScanFail(strMember, "только индекс-оверлоады (" +
                    nIndexed.ToString(CultureInfo.InvariantCulture) +
                    "), parameterless нет");
                return null;
            }
            if (nThrew == lstOrdered.Count)
            {
                _nScanThrew++;
                LogScanFail(strMember, "все кандидаты (" +
                    lstOrdered.Count.ToString(CultureInfo.InvariantCulture) +
                    ") бросили: " + JoinTypes(lstThrowTypes));
                return null;
            }
            _nScanNull++;
            LogScanFail(strMember, "кандидатов " +
                lstOrdered.Count.ToString(CultureInfo.InvariantCulture) +
                ": пусто/null " +
                (lstOrdered.Count - nThrew).ToString(CultureInfo.InvariantCulture) +
                ", бросков " + nThrew.ToString(CultureInfo.InvariantCulture));
            return null;
        }
#pragma warning restore 618

        /// <summary>REV.14.16: безопасный ToString: null-объект или бросок → false
        /// (бросок = обёртка MD-пуста, rev.14.10; имя исключения — strExName);
        /// строка → true. Статичный.</summary>
        private static bool TryToStringSafe(object oVal, out string strVal,
            out string strExName)
        {
            strVal = null;
            strExName = null;
            if (oVal == null) return false;
            try
            {
                strVal = oVal.ToString();
                return true;
            }
            catch (Exception oExS)
            {
                Exception oInner = oExS.InnerException != null
                    ? oExS.InnerException : oExS;
                strExName = oInner.GetType().Name;
                return false;
            }
        }

        /// <summary>REV.14.16: тип исключения с распаковкой InnerException
        /// (TargetInvocationException → внутреннее, факт рев.14.13 dump-проб).</summary>
        private static string TryGetExceptionName(Exception oEx)
        {
            Exception oInner = (oEx != null && oEx.InnerException != null)
                ? oEx.InnerException : oEx;
            return oInner == null ? "—" : oInner.GetType().Name;
        }

        /// <summary>REV.14.16: типы бросков для [SDIAG] — до 3 штук + хвост.</summary>
        private static string JoinTypes(List<string> lst)
        {
            if (lst == null || lst.Count == 0) return "—";
            List<string> lstShown = lst.Count > 3 ? lst.GetRange(0, 3) : lst;
            string strJoin = string.Join(", ", lstShown.ToArray());
            return lst.Count > 3
                ? strJoin + " +" + (lst.Count - 3).ToString(CultureInfo.InvariantCulture)
                : strJoin;
        }

        /// <summary>REV.14.16: капнутая диагностика скана — победитель (какой
        /// DeclaringType несёт значение). INFO-канал: WARN-бюджет эталона
        /// (17/13) не трогаем. Кап 5 на перечисление (ResetFdMatchCounters).</summary>
        private void LogScanWin(string strMember, string strFrom,
            int nCands, int nIndexed)
        {
            if (_nScanDiagWin >= 5) return;
            _nScanDiagWin++;
            string strSuffix = "";
            if (nCands > 0 || nIndexed > 0)
            {
                strSuffix = " (кандидатов " + nCands.ToString(CultureInfo.InvariantCulture);
                if (nIndexed > 0)
                    strSuffix += ", индекс-оверлоадов " +
                        nIndexed.ToString(CultureInfo.InvariantCulture);
                strSuffix += ")";
            }
            ProbeInfo("[SDIAG] «" + strMember + "» значение от " + strFrom + strSuffix);
        }

        /// <summary>REV.14.16: капнутая диагностика скана — отказ (категория +
        /// детали). INFO-канал. Кап 8 на перечисление (ResetFdMatchCounters);
        /// полная статистика — [SCAN-SUM] без капов.</summary>
        private void LogScanFail(string strMember, string strWhat)
        {
            if (_nScanDiagFail >= 8) return;
            _nScanDiagFail++;
            ProbeInfo("[SDIAG] «" + strMember + "» " + strWhat);
        }

        /// <summary>Линейное reflection-чтение строкового свойства (TryGetMemberValue
        /// + ToString()); отказ — null (Console-дамп внутри). Static.</summary>
        private static string TryGetStringProperty(object oTarget, string strProp)
        {
            object oValue = TryGetMemberValue(oTarget, strProp);
            return oValue == null ? null : oValue.ToString();
        }

        /// <summary>Reflection-проба имени (MDSymbol.Name / Symbol.Name KB НЕ
        /// доказан): Name → IdentifyingName → ToString(). Только чтение.</summary>
        private static string ResolveNameViaReflection(object oTarget)
        {
            string strName = TryGetStringProperty(oTarget, "Name");
            if (!string.IsNullOrEmpty(strName)) return strName;
            strName = TryGetStringProperty(oTarget, "IdentifyingName");
            if (!string.IsNullOrEmpty(strName)) return strName;
            try { return oTarget.ToString(); }
            catch { return null; }
        }

        /// <summary>Отображаемое ИМЯ (он же возвращаемый Library): кандидаты Name →
        /// IdentifyingName → LocationInfo (свойства НЕ доказаны KB — reflection-проба;
        /// fix-ревью 1: путь для цепочки B здесь НЕ берётся — EnumeratePathCandidates).
        /// Отказ всех — ToString().</summary>
        private static string ResolveDisplayPath(object oTarget)
        {
            string[] arrCandidates = new string[] { "Name", "IdentifyingName", "LocationInfo" };
            foreach (string strProp in arrCandidates)
            {
                string strValue = TryGetStringProperty(oTarget, strProp);
                if (!string.IsNullOrEmpty(strValue)) return strValue;
            }
            try { return oTarget.ToString(); }
            catch { return null; }
        }

        /// <summary>rev.14.7: каталоги символов из настроек (кандидаты USER/
        /// COMPANY/SYSTEM.MANAGEMENT.DIRECTORIES.SYMBOLS — пробы покажут, какой
        /// определён). Для каждого setting-пути — 0-based индексы 0..9 через
        /// Settings.GetStringSetting(strPath, idx) (KB 2.9: инстансный —
        /// new Settings(); BaseException «setting is not defined» при отсутствии
        /// пути / «path doesn't exist» — Settings-объект ЛОКАЛЬНЫЙ (Dispose у
        /// ISettings не доказан — без using-новинок); строка «не определён» —
        /// ОДНА на ПУТЬ (флаг), не на индекс; прочие отказы индекса
        /// (за пределами списка значений setting может бросить другое) —
        /// rev.14.9: НЕ тихо — строка "[MDDIRS] '<path>' — <Type>: <msg>"
        /// (флаг bPathGenFail, одна на путь, отдельный кап nErrProbes 3;
        /// тихий break остаётся) + после цикла строка
        /// "[MDDIRS] итог: N каталогов (список: 'a'; 'b' …)"). Непустые значения — в список (dedup через
        /// IndexOf, хвостовые слэши не трогаем: тримится в потребителе). Проба
        /// [MDDIRS] '&lt;path&gt;'[&lt;idx&gt;] = '&lt;dir&gt;' — кап 8 строк суммарно
        /// (nDirProbes). Probe-канал: метод СТАТИЧЕСКИЙ (точка расширения) —
        /// логгер передаётся параметром (урок CS0120 rev.14.1: у статических
        /// методов нет _oLogger).</summary>
        private static List<string> EnumerateSymbolDirectories(DiagnosticLogger oLogger)
        {
            List<string> lstDirs = new List<string>();
            string[] arrSettingPaths = new string[]
            {
                "USER.MANAGEMENT.DIRECTORIES.SYMBOLS",
                "COMPANY.MANAGEMENT.DIRECTORIES.SYMBOLS",
                "SYSTEM.MANAGEMENT.DIRECTORIES.SYMBOLS"
            };
            int nDirProbes = 0; // кап 8 строк [MDDIRS] суммарно
            // rev.14.9: ошибки путей — отдельный кап 3 (nErrProbes), строка
            // на ПУТЬ; рядовые значения продолжают печатать под nDirProbes.
            int nErrProbes = 0;
            // (фикс ревью M1) один инстанс Settings на весь вызов — чтения
            // stateless, три инстанса на путь были лишними.
            Eplan.EplApi.Base.Settings oSettingsShared = null;
            try
            {
                oSettingsShared = new Eplan.EplApi.Base.Settings();
            }
            catch (Exception oEx)
            {
                Console.WriteLine("[MDDIRS] Settings() отказ — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                if (oLogger != null) oLogger.Log("[MDDIRS] Settings() отказ — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            foreach (string strPath in arrSettingPaths)
            {
                bool bPathUndefined = false; // строка «не определён» — одна на путь
                bool bPathGenFail = false; // rev.14.9: строка прочих ошибок — одна на путь (сброс per-путь)
                Eplan.EplApi.Base.Settings oSettings = oSettingsShared;
                if (oSettings == null) continue;
                for (int nIdx = 0; nIdx <= 9; nIdx++)
                {
                    string strValue = null;
                    try
                    {
                        strValue = oSettings.GetStringSetting(strPath, nIdx);
                    }
                    catch (Eplan.EplApi.Base.BaseException)
                    {
                        // Setting-путь не определён («setting is not defined» /
                        // «path doesn't exist») — ОДНА строка на ПУТЬ (флаг),
                        // не на индекс; индексы того же смысла — выход из цикла.
                        if (!bPathUndefined)
                        {
                            bPathUndefined = true;
                            string strMsg = "[MDDIRS] '" + strPath + "' — не определён";
                            Console.WriteLine(strMsg);
                            if (oLogger != null) oLogger.Log(strMsg);
                        }
                        break;
                    }
                    catch (Exception oEx)
                    {
                        // rev.14.9: прочие отказы индекса — НЕ тихо: ОДНА
                        // строка на ПУТЬ (флаг bPathGenFail, сбрасывается
                        // per-путь) под отдельным капом 3 (nErrProbes —
                        // общий nDirProbes не трогаем): путь/тип/сообщение
                        // (урок rev.14.8: тихий break оставил [MDDIRS] без
                        // единой строки — источник каталога не опознан).
                        if (!bPathGenFail && nErrProbes < 3)
                        {
                            bPathGenFail = true;
                            nErrProbes++;
                            string strMsg = "[MDDIRS] '" + strPath + "' — " +
                                oEx.GetType().Name + ": " + oEx.Message;
                            Console.WriteLine(strMsg);
                            if (oLogger != null) oLogger.Log(strMsg);
                        }
                        break;
                    }
                    if (string.IsNullOrEmpty(strValue)) continue;
                    if (lstDirs.IndexOf(strValue) < 0) lstDirs.Add(strValue);
                    if (nDirProbes < 8)
                    {
                        nDirProbes++;
                        string strMsg = "[MDDIRS] '" + strPath + "'[" +
                            nIdx.ToString(CultureInfo.InvariantCulture) +
                            "] = '" + strValue + "'";
                        Console.WriteLine(strMsg);
                        if (oLogger != null) oLogger.Log(strMsg);
                    }
                }
            }
            // rev.14.9: итог — ОДНА строка (N + список через «; »), капа нет.
            string strDirsSummary = "[MDDIRS] итог: " +
                lstDirs.Count.ToString(CultureInfo.InvariantCulture) +
                " каталогов (список: " +
                (lstDirs.Count > 0 ? "'" + string.Join("'; '", lstDirs) + "'" : "—") +
                ")";
            Console.WriteLine(strDirsSummary);
            if (oLogger != null) oLogger.Log(strDirsSummary);
            return lstDirs;
        }

        /// <summary>Проба получения MDSymbolLibrary. rev.14.10: порядок
        /// filename-кандидатов — (0) ПОЛНЫЙ ПУТЬ системной .slk из
        /// Masterdata.SystemEntries (KB 2.9: «Returns the file names of all
        /// master data in the system master data pool» — StringCollection;
        /// new Eplan.EplApi.HEServices.Masterdata() + перечисление — в try,
        /// Dispose — в finally с null-гейтом; совпадение — EndsWith('\\'+имя.slk)
        /// ИЛИ EndsWith(имя.slk) (запись без пути) — В НАЧАЛО
        /// lstFileCandidates (dedup IndexOf, как в Settings-цикле); отказ
        /// перечисления — ОДИН ProbeWarn [SYSENT]; пробы [SYSENT] кап 5
        /// строк: итог N, сэмплы ×3, вердикт; N=0 — сэмплы ЛЮБЫХ записей —
        /// формат пула, вход следующей гипотезы), (1) каталог из
        /// PathInfo.Symbols (KB 2.9:
        /// «Returns default Symbols directory» — string; отказ —
        /// BaseException «directory cannot be obtained from settings»;
        /// ctor PathInfo() public, но помечен «Should be used by
        /// ProjectManager only!» — проба рантаймом: отказ ctor ИЛИ Symbols —
        /// ProbeWarn [MDPATH], кап 3 — nPathProbes, продолжаем без него),
        /// (2) Settings-каталоги
        /// {USER|COMPANY|SYSTEM}.MANAGEMENT.DIRECTORIES.SYMBOLS
        /// (EnumerateSymbolDirectories), (3) голое «имя.slk» — ПОСЛЕДНИМ
        /// (вдруг MD сам резолвит имя); хвостовой \ каталога тримится; имя,
        /// уже оканчивающееся на .slk — расширение НЕ дублируется.
        /// rev.14.10: пункты нумеруются теперь (0)/(1)/(2)/(3) — SystemEntries
        /// ВПЕРЕДИ; порядок попыток на файл прежний: (a) для каждого файла —
        /// MDSymbolLibrary.Open(file, Mode.ReadOnly) → Open(file) — ПРЯМЫЕ
        /// типизированные вызовы (KB 2.9:-static, «Opens an existing symbol
        /// library», параметр — filename), дальше на отказ. (b) существующий
        /// [MDCREATE]-путь: статическая фабрика MDSymbolLibrary.Create →
        /// Activator-ветки ctor(string)/(Project,string) по кандидатам имени
        /// EnumeratePathCandidates (LocationInfo→Name→IdentifyingName). Причина
        /// нового пункта (1)/(2) — лог rev.14.6: «[MDCREATE] путь=
        /// 'GOST_single_symbol' → MDInvalidHandleException: S030006Недействительный
        /// ид. номер» — фабрика отработала, но имя ≠ filename. Проба [MDOPEN]
        /// файлов: успех «файл='…' → ок», отказ «файл='…' → &lt;Exception&gt;: &lt;msg&gt;»
        /// (кап 6 — nOpenProbes; попытки за капом продолжаются, только их строки
        /// НЕ печатаются); закрытие открытой библиотеки НЕ зовём — чтение
        /// одноразовое (Mode.ReadOnly: Mode=1 «database is read-only»; объект
        /// живёт до конца жизни диалога). Старый путь: resolving фабрики ОДИН
        /// раз через Type.GetMethod по имени «Create»; null → ОДИН ProbeWarn —
        /// не должен случиться: метод KB-доказан. Activator-формы ctor(string)
        /// and ctor(Project, string) ОСТАВЛЕНЫ ПОСЛЕ фабрики как fallback БЕЗ
        /// изменений — рантайм rev.14.5 (лог строки 59–60: «Конструктор для типа
        /// … не найден» обе формы) опроверг их существование в 2.9; флаги
        /// MissingMethodException гасят после первой пробы. Пробы [MDCREATE]
        /// (ТОЛЬКО статическая ветка, кап 4 строки — nCreateProbes): успех
        /// «путь='…' → ок», отказ «путь='…' → &lt;ExceptionType&gt;: &lt;msg&gt;» — одна
        /// строка на попытку. Ни одна комбинация — null.</summary>
        private object TryCreateMdLibrary(SymbolLibrary oLib)
        {
            Type oType = typeof(Eplan.EplApi.MasterData.MDSymbolLibrary);
            int nCreateProbes = 0;
            MethodInfo oCreate = null;
            // ОДИН ProbeWarn на отвал resolving'а — независимо от причины
            // (исключение GetMethod ИЛИ null-результат: метод не найден).
            string strCreateFail = null;
            try
            {
                oCreate = oType.GetMethod("Create");
            }
            catch (Exception oEx)
            {
                strCreateFail = oEx.GetType().Name + ": " + oEx.Message;
                oCreate = null;
            }
            if (oCreate == null)
            {
                if (strCreateFail == null) strCreateFail = "метод не найден";
                ProbeWarn("[MDCREATE] статическая фабрика MDSymbolLibrary.Create недоступна — " +
                    strCreateFail);
            }

            // rev.14.7: ПЕРЕД кандидами ПУТИ — кандидаты ФАЙЛА из настроек
            // каталогов символов: Open(file, Mode.ReadOnly)/Open(file) — прямые
            // типизированные вызовы. Открытую библиотеку НЕ закрываем — чтение
            // одноразовое (Mode.ReadOnly: база уже читается EPLAN; у
            // MDSymbolLibrary нет документированного Close в 2.9; объект живёт
            // в кэше rev.14.5 до конца жизни диалога, EPLAN сам приберёт).
            string strLibName = TryGetStringProperty(oLib, "Name");
            int nOpenProbes = 0; // кап 6 строк [MDOPEN] суммарно
            if (!string.IsNullOrEmpty(strLibName))
            {
                string strFileName = strLibName.EndsWith(".slk")
                    ? strLibName
                    : strLibName + ".slk";
                List<string> lstFileCandidates = new List<string>();
                // rev.14.10: САМЫЙ канонический источник — системный пул
                // мастер-данных Masterdata.SystemEntries (KB 2.9: «Returns the
                // file names of all master data in the system master data
                // pool» — StringCollection; библиотеки символов .slk — master
                // data → полные пути системного каталога установки. KB-факт
                // ExportSymbolLibrary: «Source *.slk … Destination *.esl» —
                // .slk нативный формат, .esl экспортный). Боевой
                // прецедент: spike/TerminalStripReportSpike.cs:623 — то же
                // перечисление (формы .f11) без исключений. Совпадение —
                // запись кончается на «\имя.slk» (полный путь) ИЛИ «имя.slk»
                // (имя без пути) → ПЕРВЫЙ filename-кандидат (вставка ДО
                // PathInfo ниже). Dedup — IndexOf, как в Settings-цикле.
                // Отказ перечисления — ОДИН ProbeWarn [SYSENT], работаем
                // дальше прежними источниками. Masterdata — IDisposable по
                // KB → Dispose в finally (null-гейт).
                Eplan.EplApi.HEServices.Masterdata oMasterData = null;
                try
                {
                    oMasterData = new Eplan.EplApi.HEServices.Masterdata();
                    System.Collections.Specialized.StringCollection lstSysEntries =
                        oMasterData.SystemEntries;
                    int nSlkTotal = 0;
                    string strSysMatch = null;
                    // имя без «.slk» — для вердикта [SYSENT]
                    string strNameBare = strFileName.EndsWith(".slk", StringComparison.OrdinalIgnoreCase)
                        ? strFileName.Substring(0, strFileName.Length - 4)
                        : strFileName;
                    List<string> lstSlkSamples = new List<string>();
                    // первые ЛЮБЫЕ записи — если .slk в пуле нет (диагностика
                    // формата записей пула — вход следующей гипотезы)
                    List<string> lstAnySamples = new List<string>();
                    foreach (object oEntry in lstSysEntries)
                    {
                        string strEntry = oEntry as string;
                        if (string.IsNullOrEmpty(strEntry)) continue;
                        if (lstAnySamples.Count < 3) lstAnySamples.Add(strEntry);
                        if (!strEntry.EndsWith(".slk", StringComparison.OrdinalIgnoreCase)) continue;
                        nSlkTotal++;
                        if (lstSlkSamples.Count < 3) lstSlkSamples.Add(strEntry);
                        if (strSysMatch != null) continue;
                        if (strEntry.EndsWith("\\" + strFileName, StringComparison.OrdinalIgnoreCase) ||
                            strEntry.EndsWith(strFileName, StringComparison.OrdinalIgnoreCase))
                            strSysMatch = strEntry;
                    }
                    ProbeInfo("[SYSENT] slk-файлов в системном пуле: " + nSlkTotal);
                    if (strSysMatch != null && lstFileCandidates.IndexOf(strSysMatch) < 0)
                        lstFileCandidates.Add(strSysMatch); // ПЕРВЫЙ кандидат
                    List<string> lstShownSamples = (nSlkTotal > 0)
                        ? lstSlkSamples
                        : lstAnySamples;
                    foreach (string strSample in lstShownSamples)
                        ProbeInfo("[SYSENT] пример: '" + strSample + "'");
                    if (strSysMatch != null)
                        ProbeInfo("[SYSENT] " + strNameBare + " → '" + strSysMatch + "'");
                    else
                        ProbeWarn("[SYSENT] '" + strNameBare +
                            "' в системном пуле НЕ найден (" + nSlkTotal +
                            " slk-файлов, в т.ч. сэмплы)");
                }
                catch (Exception oEx)
                {
                    ProbeWarn("[SYSENT] перечисление — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
                finally
                {
                    if (oMasterData != null)
                    {
                        try { oMasterData.Dispose(); }
                        catch { }   // конвенция проекта — безымянный catch (CS0168)
                    }
                }
                // rev.14.10: следующий кандидат (после SystemEntries) — каталог
                // символов из PathInfo (KB 2.9: Symbols «Returns default
                // Symbols directory»; ctor PathInfo() public, но «Should be
                // used by ProjectManager only!» — рантайм может отказаться:
                // проба в try/catch). Отказ ctor/Symbols — ProbeWarn [MDPATH]
                // (кап 3 — nPathProbes), работаем дальше без него.
                int nPathProbes = 0; // rev.14.9: кап 3 строк [MDPATH]
                string strPathInfoDir = null;
                Eplan.EplApi.DataModel.PathInfo oPathInfo = null;
                try
                {
                    oPathInfo = new Eplan.EplApi.DataModel.PathInfo();
                }
                catch (Exception oEx)
                {
                    if (nPathProbes < 3)
                    {
                        nPathProbes++;
                        ProbeWarn("[MDPATH] PathInfo() — " +
                            oEx.GetType().Name + ": " + oEx.Message);
                    }
                }
                if (oPathInfo != null)
                {
                    try
                    {
                        strPathInfoDir = oPathInfo.Symbols;
                    }
                    catch (Exception oEx)
                    {
                        if (nPathProbes < 3)
                        {
                            nPathProbes++;
                            ProbeWarn("[MDPATH] Symbols — " +
                                oEx.GetType().Name + ": " + oEx.Message);
                        }
                        strPathInfoDir = null;
                    }
                    if (!string.IsNullOrEmpty(strPathInfoDir))
                    {
                        ProbeInfo("[MDPATH] каталог символов='" + strPathInfoDir + "'");
                        lstFileCandidates.Add(strPathInfoDir.TrimEnd('\\') +
                            "\\" + strFileName); // ВТОРОЙ кандидат (после [SYSENT])
                    }
                }
                List<string> lstDirs = EnumerateSymbolDirectories(_oLogger);
                foreach (string strDir in lstDirs)
                {
                    // (фикс ревью M-dedup) PathInfo.Symbols почти наверняка
                    // совпадёт с одним из Settings-каталогов — без общего dedup
                    // дубликат давал бы 2 идентичные [MDOPEN]-попытки.
                    string strDirTrim = strDir.TrimEnd('\\');
                    string strFile = strDirTrim + "\\" + strFileName;
                    if (lstFileCandidates.IndexOf(strFile) < 0)
                        lstFileCandidates.Add(strFile);
                }
                // rev.14.9: голое «имя.slk» — ПОСЛЕДНИМ кандидатом (вдруг MD
                // сам резолвит имя; урок rev.14.8 — оба файла без каталога);
                // rev.14.10: перед ним SystemEntries/PathInfo/Settings;
                // (фикс ревью) гейт dedup — если пул вернул запись без пути,
                // голое имя уже в списке (иначе лишняя дубль-попытка [MDOPEN]).
                if (lstFileCandidates.IndexOf(strFileName) < 0)
                    lstFileCandidates.Add(strFileName);
                foreach (string strFile in lstFileCandidates)
                {
                    // Порядок попыток на файл: Open(ReadOnly) → Open(). Один
                    // [MDOPEN]-печат на попытку (ок / null / отказ), кап 6 —
                    // попытки за капом ПРОДОЛЖАЮТСЯ (ищем файл), строки НЕ печатаются.
                    Eplan.EplApi.MasterData.MDSymbolLibrary oOpenLib = null;
                    try
                    {
                        oOpenLib = Eplan.EplApi.MasterData.MDSymbolLibrary.Open(strFile,
                            Eplan.EplApi.MasterData.MDSymbolLibrary.Mode.ReadOnly);
                    }
                    catch (Exception oEx)
                    {
                        if (nOpenProbes < 6)
                        {
                            nOpenProbes++;
                            ProbeInfo("[MDOPEN] файл='" + strFile + "' → " +
                                oEx.GetType().Name + ": " + oEx.Message);
                        }
                        oOpenLib = null;
                    }
                    if (oOpenLib != null)
                    {
                        if (nOpenProbes < 6)
                        {
                            nOpenProbes++;
                            ProbeInfo("[MDOPEN] файл='" + strFile + "' → ок");
                        }
                        return oOpenLib;
                    }
                    try
                    {
                        oOpenLib = Eplan.EplApi.MasterData.MDSymbolLibrary.Open(strFile);
                    }
                    catch (Exception oEx)
                    {
                        if (nOpenProbes < 6)
                        {
                            nOpenProbes++;
                            ProbeInfo("[MDOPEN] файл='" + strFile + "' → " +
                                oEx.GetType().Name + ": " + oEx.Message);
                        }
                        oOpenLib = null;
                    }
                    if (oOpenLib != null)
                    {
                        if (nOpenProbes < 6)
                        {
                            nOpenProbes++;
                            ProbeInfo("[MDOPEN] файл='" + strFile + "' → ок");
                        }
                        return oOpenLib;
                    }
                }
            }

            bool bStringFormMissing = false;
            bool bProjectFormMissing = false;
            foreach (string strPath in EnumeratePathCandidates(oLib))
            {
                // rev.14.6: статическая фабрика — первый кандидат на каждый путь
                // (после rev.14.7 [MDOPEN]-проб файлов — fallback по имени).
                // Один [MDCREATE]-печат на попытку (ок / null / исключение), кап 4.
                // TargetInvocationException (Invoke оборачивает отказ самого
                // Create) — разворот InnerException: без него дамп «target of an
                // invocation» бесполезен для следующей гипотезы (цель пробы).
                if (oCreate != null && nCreateProbes < 4)
                {
                    nCreateProbes++;
                    try
                    {
                        object oMdCandidate = oCreate.Invoke(null,
                            new object[] { strPath });
                        if (oMdCandidate != null)
                        {
                            ProbeInfo("[MDCREATE] путь='" + strPath + "' → ок");
                            return oMdCandidate;
                        }
                        ProbeInfo("[MDCREATE] путь='" + strPath + "' → null (Create вернул null)");
                    }
                    catch (TargetInvocationException oEx)
                    {
                        Exception oInner = oEx.InnerException;
                        if (oInner != null)
                            ProbeInfo("[MDCREATE] путь='" + strPath + "' → " +
                                oInner.GetType().Name + ": " + oInner.Message);
                        else
                            ProbeInfo("[MDCREATE] путь='" + strPath + "' → " +
                                oEx.GetType().Name + ": " + oEx.Message);
                    }
                    catch (Exception oEx)
                    {
                        ProbeInfo("[MDCREATE] путь='" + strPath + "' → " +
                            oEx.GetType().Name + ": " + oEx.Message);
                    }
                }
                if (!bStringFormMissing)
                {
                    try
                    {
                        object oCandidate = Activator.CreateInstance(oType,
                            new object[] { strPath });
                        if (oCandidate != null) return oCandidate;
                    }
                    catch (MissingMethodException oEx)
                    {
                        bStringFormMissing = true;
                        ProbeInfo("SymbolBrowserDialog: ctor(string) не найден — " + oEx.Message);
                    }
                    catch (Exception oEx)
                    {
                        ProbeInfo("SymbolBrowserDialog: ctor(string) «" + strPath +
                            "» бросил — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                }
                if (!bProjectFormMissing)
                {
                    try
                    {
                        object oCandidate = Activator.CreateInstance(oType,
                            new object[] { _oProject, strPath });
                        if (oCandidate != null) return oCandidate;
                    }
                    catch (MissingMethodException oEx)
                    {
                        bProjectFormMissing = true;
                        ProbeInfo("SymbolBrowserDialog: ctor(Project,string) не найден — " + oEx.Message);
                    }
                    catch (Exception oEx)
                    {
                        ProbeInfo("SymbolBrowserDialog: ctor(Project,string) «" + strPath +
                            "» бросил — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                }
            }
            return null;
        }

        /// <summary>Кандидаты ПУТИ к библиотеке (fix-1: ОТДЕЛЬНО от отображаемого
        /// имени ResolveDisplayPath). Порядок path-first: LocationInfo (полный путь —
        /// основной кандидат для MDSymbolLibrary(path)), затем Name, IdentifyingName.
        /// Свойства НЕ доказаны KB — reflection-проба, пустые пропуск, дубли —
        /// не зондируем.</summary>
        private static List<string> EnumeratePathCandidates(SymbolLibrary oLib)
        {
            string[] arrPathProps = new string[] { "LocationInfo", "Name", "IdentifyingName" };
            List<string> lstPaths = new List<string>();
            foreach (string strProp in arrPathProps)
            {
                string strValue = TryGetStringProperty(oLib, strProp);
                if (string.IsNullOrEmpty(strValue)) continue;
                if (lstPaths.IndexOf(strValue) >= 0) continue;
                lstPaths.Add(strValue);
            }
            return lstPaths;
        }

        /// <summary>Число вариантов MDSymbol: KB (цитата в шапке) «public
        /// MDSymbolVariant[] Variants { get; }» — прямое обращение; бросил/null →
        /// -1 (деградация: варианты неизвестны, клетки disabled — rev.14.3).
        /// Инстансный (Probe-канал; фикс ревью rev.14.1: static + ProbeWarn = CS0120).</summary>
        private int TryGetVariantCount(Eplan.EplApi.MasterData.MDSymbol oMdSym)
        {
            try
            {
                Eplan.EplApi.MasterData.MDSymbolVariant[] arrVariants = oMdSym.Variants;
                if (arrVariants != null) return arrVariants.Length;
            }
            catch (Exception oEx)
            {
                ProbeWarn("SymbolBrowserDialog: MDSymbol.Variants — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            return -1;
        }

        // --- FD-словари (Project.FunctionDefinitionLibrary) ---

        /// <summary>Словари FD (rev.14.2, ОДИН раз за жизнь диалога; смотрят
        /// _bFdDumped): (а) _dctFdById (Id → FdInfo, first wins — Id НЕ уникален
        /// глобально: 1305 FD → 40 Id, дамп [FD] rev.14.1) — fallback-бакетизация
        /// для #16018-путей; (б) НОВЫЙ _dctFdBySymbol («lib\u0001sym» → FdInfo)
        /// из FD.BaseSymbol → SymbolLibraryName/SymbolName — ПЕРВИЧНАЯ связь
        /// символ → FD. rev.14.4: в том же цикле каждый FD даёт пару
        /// (FdInfo, живой FunctionDefinition) в _lstFdRefs — источник моста
        /// [FDLIB] (BuildFdLibDictionary). Поля уровней: MainGroup (Trade)/CategoryRegion (Area)/
        /// CategoryName/GroupName/Name/Description — каждый getter в try/catch
        /// (Description/BaseSymbol рантайм НЕ подтверждён); MultiLangString-блобы
        /// локализуются SymbolCatalog.LocalizeMultiLang (формат «de_DE@…;ru_RU@…»
        /// доказан дампом [FD] rev.14.1). Дамп [FD] первых 10 (формат сохранён)
        /// + [FD-BASE] первых 10 пар BaseSymbol. Сбой/пусто — словари пусты,
        /// A2 хмурится в fallback-пути.</summary>
        private void BuildFdDictionary()
        {
            if (_bFdDumped) return;   // дамп/словари строим ОДИН раз за жизнь диалога
            _bFdDumped = true;
            _dctFdById = new Dictionary<long, FdInfo>();
            _dctFdBySymbol = new Dictionary<string, FdInfo>();
            _lstFdRefs = new List<KeyValuePair<FdInfo,
                Eplan.EplApi.DataModel.FunctionDefinition>>();   // rev.14.4 [FDLIB]
            // rev.14.3: счётчик попыток записи пары по strBaseLib ([FD-BASE-SUM];
            // ключ null/пустой lib — тоже считаем, как попытку — категория «<пусто>»).
            Dictionary<string, int> dctBaseLibs = new Dictionary<string, int>();
            if (_oProject == null)
            {
                ProbeInfo("[FD] недоступен: проект null");
                return;
            }
            try
            {
                // KB: Project.FunctionDefinitionLibrary (в .MasterData),
                // FunctionDefinitions : FunctionDefinition[] — сам FunctionDefinition
                // в Eplan.EplApi.DataModel (урок CS0246 п.30/63 — пространство
                // имён сверять с компилирующимся кодом).
                Eplan.EplApi.DataModel.FunctionDefinition[] arrFdDefs =
                    _oProject.FunctionDefinitionLibrary.FunctionDefinitions;
                int nCount = arrFdDefs == null ? 0 : arrFdDefs.Length;
                ProbeInfo("[FD] FunctionDefinitions: " +
                    nCount.ToString(CultureInfo.InvariantCulture) + " шт.");
                if (nCount == 0) return;

                int iOrdinal = 0;
                foreach (Eplan.EplApi.DataModel.FunctionDefinition oFd in arrFdDefs)
                {
                    if (oFd == null) continue;
                    long nId = -1;
                    string strCat = null;
                    string strGroup = null;
                    string strMain = null;
                    string strRegion = null;
                    string strName = null;
                    string strDesc = null;
                    try { nId = oFd.Id; }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Id — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strCat = oFd.CategoryName == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.CategoryName.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] CategoryName — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strGroup = oFd.GroupName == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.GroupName.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] GroupName — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strMain = oFd.MainGroup == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.MainGroup.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] MainGroup — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strRegion = oFd.CategoryRegion == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.CategoryRegion.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] CategoryRegion — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    try
                    {
                        strName = oFd.Name == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.Name.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Name — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    // Description: KB members page есть, рантайм НЕ подтверждён —
                    // проба try/catch (отказ — только диагностика, поле описания
                    // карточки остаётся SYMB_DESC-первичным).
                    try
                    {
                        strDesc = oFd.Description == null
                            ? null : SymbolCatalog.LocalizeMultiLang(oFd.Description.ToString());
                    }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD] Description — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    if (iOrdinal < 10)
                    {
                        ProbeInfo("[FD] id=" + nId.ToString(CultureInfo.InvariantCulture) +
                            " cat='" + (strCat ?? "<null>") + "' main='" + (strMain ?? "<null>") +
                            "' region='" + (strRegion ?? "<null>") + "' group='" +
                            (strGroup ?? "<null>") + "' name='" + (strName ?? "<null>") +
                            "' desc='" + (strDesc ?? "<null>") + "'");
                    }

                    // (а) dctFdById — first wins (Id НЕ уникален глобально; равный Id
                    // у разных FD — аспект fallback-бакетизации, рантайм-факт rev.14.1).
                    // rev.14.4 [FDLIB]: FdInfo строится ДО first-wins-гейта — пара
                    // (FdInfo, живой FD) кладётся в _lstFdRefs ДЛЯ КАЖДОГО FD цикла
                    // (1305 FD; first-wins-ветка сохранила бы лишь 40 — Id-дубли
                    // проигнорировались бы мостом).
                    FdInfo oInfo = new FdInfo();
                    oInfo.MainGroup = strMain;
                    oInfo.Area = strRegion;
                    oInfo.Category = strCat;
                    oInfo.Group = strGroup;
                    oInfo.Name = strName;
                    oInfo.Description = strDesc;
                    if (nId >= 0 && !_dctFdById.ContainsKey(nId))
                    {
                        _dctFdById[nId] = oInfo;
                    }
                    _lstFdRefs.Add(new KeyValuePair<FdInfo,
                        Eplan.EplApi.DataModel.FunctionDefinition>(oInfo, oFd));

                    // (б) BaseSymbol → (lib\sym) → dctFdBySymbol. BaseSymbol: KB
                    // members («Gets the best fitting SymbolVariant ...»), рантайм
                    // НЕ подтверждён — типизированная проба в try/catch + [FD-BASE].
                    object oBaseSymbol = null;
                    try { oBaseSymbol = oFd.BaseSymbol; }
                    catch (Exception oEx)
                    {
                        if (iOrdinal < 10)
                            ProbeWarn("[FD-BASE] BaseSymbol FD «" + (strName ?? "<null>") +
                                "» — " + oEx.GetType().Name + ": " + oEx.Message);
                    }
                    string strBaseLib = null;
                    string strBaseSym = null;
                    if (oBaseSymbol != null)
                    {
                        // SymbolLibraryName/SymbolName у MasterData.Symbol — рантайм
                        // НЕ подтверждены; отражаем reflection-пробой (TryGetStringProperty)
                        // БЕЗ компиляционных рисков (урок некомпилируемого угадывания).
                        strBaseLib = TryGetStringProperty(oBaseSymbol, "SymbolLibraryName");
                        strBaseSym = TryGetStringProperty(oBaseSymbol, "SymbolName");
                    }
                    if (iOrdinal < 10)
                    {
                        ProbeInfo("[FD-BASE] FD id=" + nId.ToString(CultureInfo.InvariantCulture) +
                            " «" + (strName ?? "<null>") + "» → lib «" +
                            (strBaseLib ?? "<null>") + "» sym «" + (strBaseSym ?? "<null>") + "»");
                    }
                    if (!string.IsNullOrEmpty(strBaseSym))
                    {
                        // rev.14.3: инкремент при КАЖДОЙ попытке записи пары (до
                        // ContainsKey-гейта) — расклад ключей по библиотекам для
                        // сверки с [SYMFDMAP-KEY].
                        string strKey = MakeFdSymbolKey(strBaseLib, strBaseSym);
                        // нормализация null/пустого lib в категорию «<пусто>»
                        // (Dictionary<string,int> null-ключ не принимает)
                        string strLibBucket = string.IsNullOrEmpty(strBaseLib)
                            ? "<пусто>" : strBaseLib;
                        if (dctBaseLibs.ContainsKey(strLibBucket))
                            dctBaseLibs[strLibBucket]++;
                        else
                            dctBaseLibs[strLibBucket] = 1;
                        if (!_dctFdBySymbol.ContainsKey(strKey))
                        {
                            // rev.14.4: FdInfo уже построен выше (oInfo — одна
                            // конструкция на FD; ориг. oInfoBySymbol дублировал
                            // сборку полей — поведение идентично).
                            _dctFdBySymbol[strKey] = oInfo;
                        }
                    }
                    iOrdinal++;
                }
                // rev.14.3: расклад попыток записи пар по базовым библиотекам
                // (каждая попытка, вкл. дубли BaseSymbol) — сортировка по убыванию,
                // кап 8, хвост «+X ещё».
                string strJoined = string.Empty;
                List<KeyValuePair<string, int>> lstLibs =
                    new List<KeyValuePair<string, int>>(dctBaseLibs);
                lstLibs.Sort(delegate(KeyValuePair<string, int> oA,
                    KeyValuePair<string, int> oB)
                {
                    return oB.Value - oA.Value;   // по убыванию count
                });
                for (int iLib = 0; iLib < lstLibs.Count; iLib++)
                {
                    if (iLib == 8)
                    {
                        strJoined += " (+" +
                            (lstLibs.Count - 8).ToString(CultureInfo.InvariantCulture) +
                            " ещё)";
                        break;
                    }
                    strJoined += (iLib == 0 ? string.Empty : ", ") + lstLibs[iLib].Key +
                        "=" + lstLibs[iLib].Value.ToString(CultureInfo.InvariantCulture);
                }
                ProbeInfo("[FD] словари FD: по Id " +
                    _dctFdById.Count.ToString(CultureInfo.InvariantCulture) +
                    ", по символу " + _dctFdBySymbol.Count.ToString(CultureInfo.InvariantCulture) +
                    " записей");
                ProbeInfo("[FD-BASE-SUM] BaseSymbol-словарь: " +
                    _dctFdBySymbol.Count.ToString(CultureInfo.InvariantCulture) +
                    " пар; библиотеки (кап 8 по убыванию; попытки записи вкл. дубли): " +
                    (strJoined.Length == 0 ? "<пусто>" : strJoined));
            }
            catch (Exception oEx)
            {
                ProbeWarn("[FD] недоступен: " + oEx.GetType().Name + ": " +
                    oEx.Message + " — FD-пути дерева отключены");
            }
        }

        /// <summary>Ключ обратного словаря FD: «libName\u0001symName» (разделитель
        /// — невидимый \u0001; коллизий «libA|symB» нет).</summary>
        private static string MakeFdSymbolKey(string strLib, string strSym)
        {
            return (strLib ?? string.Empty) + "\u0001" + (strSym ?? string.Empty);
        }

        /// <summary>[FDLIB] rev.14.4 — пер-библиотечный обратный словарь FD: для
        /// КАЖДОГО FD из _lstFdRefs — reflection-проба
        /// FunctionDefinition.GetBaseSymbolFromSpecifiedSymbolLibrary(oLib) (KB
        /// members FunctionDefinition: мост FD → Symbol В ЗАДАННУЮ библиотеку;
        /// рантайм НЕ подтверждён — Invoke в try/catch, НЕ угадываем тип параметра —
        /// урок CS0246/CS1503). Результат — Symbol: имя TryGetStringProperty(oSym,
        /// "Name") (null/пусто — пропуск с подсчётом), пара «lib\u0001sym» → FdInfo
        /// (тот же ключ MakeFdSymbolKey, что у BaseSymbol-словаря). Ленивый кэш
        /// _dctFdBySymbolPerLib: запись на ИМЯ библиотеки — повторные перечисления
        /// той же библиотеки построение пропускают (вызов-гейт в RefreshSymbols).
        /// Пробы: [FDLIB] ОДНА строка на построение (FD N → пар M, отказов K
        /// (из них пустых N — Invoke вернул не-Symbol/null), без имени J,
        /// дублей Z — first-wins) + [FDLIB-SAMPLE] первых 5 пар; НЕ печатаем на
        /// каждый FD. Инвариант (rev.14.5, ветки цикла не пересекаются и
        /// исчерпывают пары цикла): M + K + J + Z == nFd — проверка арифметики
        /// отказов (метод недоступен / «пустой ответ» / Invoke бросил),
        /// без-имени и дублей.
        /// Отказ метода (NoSuchMethod) — один ProbeWarn на цикл, честная
        /// деградация (fallback BaseSymbol/16018 остаётся). strLibName пуст —
        /// словарь не строится + один ProbeWarn.</summary>
        private void BuildFdLibDictionary(SymbolLibrary oLib, string strLibName)
        {
            if (string.IsNullOrEmpty(strLibName))
            {
                ProbeWarn("[FDLIB] имя библиотеки пусто — пер-либ словарь не строится");
                return;
            }
            if (_dctFdBySymbolPerLib == null)
                _dctFdBySymbolPerLib = new Dictionary<string, Dictionary<string, FdInfo>>();
            if (_dctFdBySymbolPerLib.ContainsKey(strLibName)) return;   // кэш: уже построен
            // (фикс ревью M3): пустой _lstFdRefs — до вставки кэша, иначе пустой
            // словарь кэшировался бы навсегда при вырожденном порядке инициализации.
            if (_lstFdRefs == null || _lstFdRefs.Count == 0)
            {
                ProbeInfo("[FDLIB] lib='" + strLibName + "': FD 0 → пар 0, " +
                    "отказов 0 (из них пустых 0), без имени 0, дублей 0");
                return;
            }
            Dictionary<string, FdInfo> dctLib = new Dictionary<string, FdInfo>();
            _dctFdBySymbolPerLib[strLibName] = dctLib;
            int nFd = 0;
            int nPairs = 0;
            int nFail = 0;
            int nNoName = 0;
            // rev.14.5 [FDLIB]: счётчики расщеплены, ветки цикла не пересекаются:
            // (а) nFail — «метод недоступен» (NoSuchMethod) ИЛИ Invoke бросил;
            // (б) nNull — «пустой ответ»: Invoke вернул null / не-Symbol (FD не
            //     представлен в заданной библиотеке);
            // (в) nNoName — Symbol без имени;
            // (г) nDup — дубликат ключа (first-wins continue, раньше был молча:
            //     прогона rev.14.4: 171+983+0 = 1154 ≠ FD 1305 — недостающие 151).
            // Инвариант: nPairs + nFail + nNull + nNoName + nDup == nFd.
            int nNull = 0;
            int nDup = 0;
            int nSample = 0;
            bool bWarned = false;   // ProbeWarn «метод недоступен» — один на цикл
            bool bWarnedInvoke = false;   // (фикс ревью M1) первый Invoke-отказ — строка диагностики
            foreach (KeyValuePair<FdInfo, Eplan.EplApi.DataModel.FunctionDefinition>
                oRef in _lstFdRefs)
            {
                if (oRef.Key == null || oRef.Value == null) continue;
                nFd++;
                try
                {
                    MethodInfo oMethod = oRef.Value.GetType().GetMethod(
                        "GetBaseSymbolFromSpecifiedSymbolLibrary");
                    if (oMethod == null)
                    {
                        if (!bWarned)
                        {
                            bWarned = true;
                            ProbeWarn("[FDLIB] метод GetBaseSymbolFromSpecifiedSymbolLibrary " +
                                "недоступен (NoSuchMethod) — мост отключён, fallback " +
                                "BaseSymbol/16018 остаётся");
                        }
                        nFail++;
                        continue;
                    }
                    object oSymRaw = oMethod.Invoke(oRef.Value,
                        new object[] { oLib });
                    Symbol oSym = oSymRaw as Symbol;
                    if (oSym == null)
                    {
                        // rev.14.5 [FDLIB]: НЕ «отказ» (Invoke сработал) — FD не
                        // представлен в заданной библиотеке (null/посторонний
                        // тип) — «пустой ответ» в свою графу.
                        nNull++;
                        continue;
                    }
                    string strSymName = TryGetStringProperty(oSym, "Name");
                    if (string.IsNullOrEmpty(strSymName))
                    {
                        nNoName++;
                        continue;
                    }
                    string strKey = MakeFdSymbolKey(strLibName, strSymName);
                    if (dctLib.ContainsKey(strKey))
                    {
                        // rev.14.5 [FDLIB]: дубли теперь ВИДЕНЫ (first-wins — как в
                        // dctFdById/dctFdBySymbol; раньше ветка не считалась).
                        nDup++;
                        continue;   // first wins
                    }
                    dctLib[strKey] = oRef.Key;
                    nPairs++;
                    if (nSample < 5)
                    {
                        nSample++;
                        ProbeInfo("[FDLIB-SAMPLE] FD «" +
                            (oRef.Key.Name ?? "<null>") + "» → sym «" + strSymName + "»");
                    }
                }
                catch (Exception oEx)
                {
                    nFail++;
                    // (фикс ревью M1/M2): флаг-гейт вместо nFail==1 — отказ
                    // GetMethod (nFail уже ≥1) не гасил диагностику Invoke-отказа;
                    // текст «мост (GetMethod/Invoke)» — catch покрывает оба вызова.
                    if (!bWarnedInvoke)
                    {
                        bWarnedInvoke = true;
                        ProbeWarn("[FDLIB] FD «" + (oRef.Key.Name ?? "<null>") +
                            "» мост (GetMethod/Invoke) — " + oEx.GetType().Name + ": " +
                            oEx.Message);
                    }
                }
            }
            // rev.14.5 [FDLIB]: инвариант после всех веток — M+K+N+J+Z == nFd
            // (пары+отказы+пустые+без-имени+дубли). Отклонение (теоретический
            // случай пропущенной ветки) — честный ProbeWarn с числами.
            int nSum = nPairs + nFail + nNull + nNoName + nDup;
            if (nSum != nFd)
            {
                ProbeWarn("[FDLIB] ИНВАРИАНТ нарушен: пар " +
                    nPairs.ToString(CultureInfo.InvariantCulture) + " + отказов " +
                    nFail.ToString(CultureInfo.InvariantCulture) + " + пустых " +
                    nNull.ToString(CultureInfo.InvariantCulture) + " + без имени " +
                    nNoName.ToString(CultureInfo.InvariantCulture) + " + дублей " +
                    nDup.ToString(CultureInfo.InvariantCulture) + " = " +
                    nSum.ToString(CultureInfo.InvariantCulture) + " ≠ FD " +
                    nFd.ToString(CultureInfo.InvariantCulture));
            }
            ProbeInfo("[FDLIB] lib='" + strLibName + "': FD " +
                nFd.ToString(CultureInfo.InvariantCulture) + " → пар " +
                nPairs.ToString(CultureInfo.InvariantCulture) + ", отказов " +
                nFail.ToString(CultureInfo.InvariantCulture) + ", пустых " +
                nNull.ToString(CultureInfo.InvariantCulture) + ", без имени " +
                nNoName.ToString(CultureInfo.InvariantCulture) + ", дублей " +
                nDup.ToString(CultureInfo.InvariantCulture) +
                " (инвариант: пары+отказы+пустые+без-имени+дубли == FD)");
        }

        /// <summary>[MDFD] rev.14.5 — MDS-до-заполнение записей FD/описаниями из
        /// пер-библиотечных MDSymbol-карт: (а) SYMB_MAINFUNCTION #16018 «Main
        /// function # 16018» с MDSymbol-УРОВНЯ (KB страница
        /// MDSymbolPropertyList~SYMB_MAINFUNCTION — локальная база 28.09; rev.14.3
        /// доказал: у DataModel Symbol тот же #16018 == null на обёртке) — карта
        /// имя символа → fdId > 0; (б) SYMB_DESC «Symbol description # 16011» —
        /// карта имя → локализованный блоб. Кэш-гейт по ИМЕНИ библиотеки: ОБЕ карты
        /// строятся ОДИН раз (TryCreateMdLibrary — статическая фабрика
        /// MDSymbolLibrary.Create(string), Activator-ctor-пробы — fallback;
        /// Symbols — KB-доказанное свойство, обход в try/catch; у TryGetFdId/
        /// TryGetSymbDesc свои Probe-капы). Повторное перечисление той же
        /// библиотеки — кэш-hit: записи наполняются молча, БЕЗ [MDFD]-печатей.
        /// Наполнение _lstEntries: (а) запись БЕЗ FD → fdId → _dctFdById
        /// (first-wins; Id НЕ уникален глобально: 1305 FD → 40 Id — риск неверного
        /// FD фиксируется пробами [MDFD-KEY], критерий — сверка пользователя с
        /// нативным браузером) → Fd + strFdPath="MDSymbol" + _nFdViaMd;
        /// (б) ЛЮБАЯ запись с пустым Description (вкл. Fd!=null) — SYMB_DESC из
        /// карты (+ _nDescViaMd; per-symbol описание точнее FD.Description —
        /// UpdateCardByEntry приоритетен entry.Description).
        /// Пробы (единожды на ПОСТРОЕНИЕ карт): [MDFD] ОДНА строка после
        /// наполнения; [MDFD-KEY] ×10 — первые БЕЗ-FD имена (собраны ДО наполнения)
        /// → id=… → dctFdById НАЙДЕН/ПРОМАХ / id нет; [MDFD-DESC] ×5 первых
        /// непустых. Отказ строителя — ProbeWarn «MDSymbolLibrary недоступен» +
        /// пустые карты в кэш (честная деградация; rev.14.4-состояние дерева
        /// сохраняется) + [MDFD]-строка с нулями. strLibName пуст — один
        /// ProbeWarn, ничего не строится. Только чтение исходников.</summary>
        private void TryFillFdViaMasterData(SymbolLibrary oLib, string strLibName)
        {
            if (string.IsNullOrEmpty(strLibName))
            {
                ProbeWarn("[MDFD] имя библиотеки пусто — MDS-до-заполнение отключено");
                return;
            }
            if (_dctMdFdIdByName == null)
                _dctMdFdIdByName = new Dictionary<string, Dictionary<string, long>>();
            if (_dctMdDescByName == null)
                _dctMdDescByName = new Dictionary<string, Dictionary<string, string>>();
            if (_dctMdClsByName == null)
                _dctMdClsByName = new Dictionary<string, Dictionary<string, string[]>>();
            int nMdCount = 0;
            int nDupId = 0;
            Dictionary<string, long> dctId;
            Dictionary<string, string> dctDesc;
            Dictionary<string, string[]> dctCls;
            bool bJustBuilt = false;
            if (!_dctMdFdIdByName.ContainsKey(strLibName))
            {
                // Строим ОБЕ карты разом; вставка в кэш ПОСЛЕ обхода — частичные
                // карты (при исключении в середине) тоже кэшируются: повторные
                // RefreshSymbols не спамят и не пересобирают.
                Dictionary<string, long> dctIdNew = new Dictionary<string, long>();
                Dictionary<string, string> dctDescNew = new Dictionary<string, string>();
                Dictionary<string, string[]> dctClsNew = new Dictionary<string, string[]>();
                object oMdLib = TryCreateMdLibrary(oLib);
                if (oMdLib == null)
                {
                    ProbeWarn("[MDFD] MDSymbolLibrary недоступен — MDS-до-заполнение отключено");
                    ProbeInfo("[MDFD] lib='" + strLibName + "': MDSymbol 0 → id 0, " +
                        "id-промахов 0, desc 0; заполнено FD 0, desc 0");
                    _dctMdFdIdByName[strLibName] = dctIdNew;
                    _dctMdDescByName[strLibName] = dctDescNew;
                    _dctMdClsByName[strLibName] = dctClsNew;
                    return;
                }
                try
                {
                    Eplan.EplApi.MasterData.MDSymbol[] arrSymbols =
                        ((Eplan.EplApi.MasterData.MDSymbolLibrary)oMdLib).Symbols;
                    int iOrdinalMd = 0;   // капы TryGetFdId/TryGetSymbDesc по symbol-порядку
                    if (arrSymbols != null)
                    {
                        foreach (Eplan.EplApi.MasterData.MDSymbol oMdSym in arrSymbols)
                        {
                            if (oMdSym == null) continue;
                            string strSymName = ResolveNameViaReflection(oMdSym);
                            if (string.IsNullOrEmpty(strSymName)) continue;
                            nMdCount++;
                            // rev.14.14: прогресс MDS-цикла (крэш-хвост). iOrdinalMd
                            // инкрементируется в конце тела — маркер использует текущее
                            // значение до инкремента (номер обрабатываемого символа).
                            if (iOrdinalMd > 0 && iOrdinalMd % 100 == 0)
                                ProbeInfo("[BR] MDS прогресс: " +
                                    iOrdinalMd.ToString(CultureInfo.InvariantCulture) + " символов");
                            long? nFdId = TryGetFdId(oMdSym, iOrdinalMd, strSymName);
                            if (nFdId.HasValue && nFdId.Value > 0)
                            {
                                if (dctIdNew.ContainsKey(strSymName))
                                    nDupId++;   // first wins; дубли ВИДЕНЫ одной WARN
                                else
                                    dctIdNew[strSymName] = nFdId.Value;
                            }
                            // rev.14.11 [SYMFUNC-MD]: классификация FUNC_* на той же
                            // MD-обёртке — проба + запись в карту dctClsNew (карта
                            // кэшируется — повторные перечисления не спамят
                            // [SYMFUNC-MD] заново).
                            TryClassifyViaMdProperties(oMdSym, iOrdinalMd, strSymName,
                                dctClsNew);
                            // rev.14.12 [VARPROP]: проба на MD-вариантах
                            // (MDSymbol.Variants → MDSymbolVariant.Properties,
                            // reflection); капы внутри (рядовые 10 на перечисление).
                            TryProbeMdVariantProps(oMdSym, strSymName, iOrdinalMd);
                            // TryGetSymbDesc возвращает RAW блоб (ToString()) —
                            // локализуем SymbolCatalog.LocalizeMultiLang (формат
                            // «de_DE@…;ru_RU@…»); пусто после локализации — не в карту.
                            string strRawDesc = TryGetSymbDesc(oMdSym, iOrdinalMd, strSymName);
                            if (!string.IsNullOrEmpty(strRawDesc))
                            {
                                string strLocalized = SymbolCatalog.LocalizeMultiLang(strRawDesc);
                                if (!string.IsNullOrEmpty(strLocalized))
                                {
                                    // first-wins — конвенция дубликатов имён совпадает
                                    // с id-картой (у дублей desc идентичен).
                                    if (!dctDescNew.ContainsKey(strSymName))
                                        dctDescNew[strSymName] = strLocalized;
                                }
                            }
                            iOrdinalMd++;
                        }
                    }
                }
                catch (Exception oEx)
                {
                    ProbeWarn("[MDFD] обход MDSymbolLibrary «" + strLibName + "» — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
                if (nDupId > 0)
                {
                    ProbeWarn("[MDFD] lib='" + strLibName + "': id-дублей " +
                        nDupId.ToString(CultureInfo.InvariantCulture) +
                        " first-wins (разные MDSymbol с одним #16018?)");
                }
                _dctMdFdIdByName[strLibName] = dctIdNew;
                _dctMdDescByName[strLibName] = dctDescNew;
                _dctMdClsByName[strLibName] = dctClsNew;
                dctId = dctIdNew;
                dctDesc = dctDescNew;
                dctCls = dctClsNew;
                bJustBuilt = true;
                // rev.14.11 [SYMFUNC-MD-SUM]: ОДНА строка после построения —
                // M=записей класс-карты, N=ОБРАБОТАННЫХ MDSymbol (при исключении
                // посреди обхода N<полного размера библиотеки — ревью M-2);
                // сопоставление — в [SYMFDMAP]. Кэш-hit НЕ печатает (конвенция).
                ProbeInfo("[SYMFUNC-MD-SUM] классификация MD: " +
                    dctClsNew.Count.ToString(CultureInfo.InvariantCulture) + " из " +
                    nMdCount.ToString(CultureInfo.InvariantCulture) +
                    " обработанных (карта; сопоставление в [SYMFDMAP])");
            }
            else
            {
                // Кэш-hit: карты уже построены — наполняем молча.
                dctId = _dctMdFdIdByName[strLibName];
                dctDesc = _dctMdDescByName[strLibName];
                dctCls = _dctMdClsByName[strLibName];
            }
            // Сэмплы [MDFD-KEY] — первые 10 имён БЕЗ FD на входе, ДО наполнения
            // (сняты в отдельный список; исходы эквивалентны веткам цикла ниже).
            List<string> lstKeySamples = new List<string>();
            if (bJustBuilt)
            {
                foreach (SymbolEntry oEntry in _lstEntries)
                {
                    if (lstKeySamples.Count >= 10) break;
                    if (oEntry == null || oEntry.Fd != null) continue;
                    if (string.IsNullOrEmpty(oEntry.Name)) continue;
                    lstKeySamples.Add(oEntry.Name);
                }
            }
            // Наполнение записей (после построения и на кэш-hit):
            // (а-0) rev.14.11: Fd == null → класс-карта MD ([catLoc, regionLoc,
            // grpLoc, catGroupRaw]) → FdInfo на месте (путь «SymbolProps-MD»);
            // (а) Fd == null → id-карта → _dctFdById (first-wins);
            // (б) пустое Description (ЛЮБАЯ запись, вкл. Fd!=null) → desc-карта.
            int nIdMiss = 0;
            int nNoId = 0;
            foreach (SymbolEntry oEntry in _lstEntries)
            {
                if (oEntry == null) continue;
                if (oEntry.Fd == null && !string.IsNullOrEmpty(oEntry.Name))
                {
                    // rev.14.11 [SYMFUNC-MD]: ПЕРВЫЙ путь — класс-карта MD (rev.14.10
                    // показал: #16018 пуст на всех 796, id-путь dead; класс-карта
                    // собрана в момент построения — cat+group непусты).
                    string[] arrCls;
                    if (dctCls.TryGetValue(oEntry.Name, out arrCls) && arrCls != null &&
                        arrCls.Length >= 4 &&
                        !string.IsNullOrEmpty(arrCls[0]) && !string.IsNullOrEmpty(arrCls[2]))
                    {
                        FdInfo oFd = new FdInfo();
                        oFd.MainGroup = null;
                        // rev.14.11 ревью I-1: ?? не ловит Empty — контракт карты
                        // допускает String.Empty; пустая Region → Area = Group.
                        oFd.Area = string.IsNullOrEmpty(arrCls[1]) ? arrCls[2] : arrCls[1];
                        oFd.Category = arrCls[0];
                        oFd.Group = arrCls[2];
                        oFd.Name = SymbolCatalog.ExtractFdNameFromCategoryGroup(arrCls[3])
                            ?? arrCls[2];
                        oFd.Description = null;
                        oEntry.Fd = oFd;
                        oEntry.strFdPath = "SymbolProps-MD";
                        _nFdViaMdProps++;
                        // пост-loop до-заполнение — инкремент ЗДЕСЬ ОБЯЗАТЕЛЕН
                        // (конвенция rev.14.5: см. комменты 2630-2636/2646-2651).
                        _nFdMapped++;
                    }
                    else
                    {
                        long nMdId;
                        if (dctId.TryGetValue(oEntry.Name, out nMdId))
                        {
                            FdInfo oInfoFd;
                            if (_dctFdById != null &&
                                _dctFdById.TryGetValue(nMdId, out oInfoFd))
                            {
                                oEntry.Fd = oInfoFd;
                                oEntry.strFdPath = "MDSymbol";
                                _nFdViaMd++;
                                // (fix ревью Important) счётчик общей сводки [SYMFDMAP]:
                                // без него «сопоставлено» печатает только A2-хиты,
                                // а MDS-наполнение осталось бы невидимым.
                                _nFdMapped++;
                            }
                            else
                            {
                                nIdMiss++;   // id в карте — dctFdById промах
                            }
                        }
                        else
                        {
                            nNoId++;   // имя отсутствует в MDS-карте id
                        }
                    }
                }
                if (string.IsNullOrEmpty(oEntry.Description) &&
                    !string.IsNullOrEmpty(oEntry.Name))
                {
                    string strMdDesc;
                    if (dctDesc.TryGetValue(oEntry.Name, out strMdDesc) &&
                        !string.IsNullOrEmpty(strMdDesc))
                    {
                        oEntry.Description = strMdDesc;
                        _nDescViaMd++;
                    }
                }
            }
            if (bJustBuilt)
            {
                // [MDFD-KEY] ×10: имя → id → dctFdById НАЙДЕН/ПРОМАХ / id нет.
                foreach (string strKeyName in lstKeySamples)
                {
                    long nKeyId;
                    if (!dctId.TryGetValue(strKeyName, out nKeyId))
                    {
                        ProbeInfo("[MDFD-KEY] «" + strKeyName + "» → id нет");
                    }
                    else
                    {
                        FdInfo oKeyFd;
                        if (_dctFdById != null && _dctFdById.TryGetValue(nKeyId, out oKeyFd))
                        {
                            ProbeInfo("[MDFD-KEY] «" + strKeyName + "» → id=" +
                                nKeyId.ToString(CultureInfo.InvariantCulture) +
                                " → dctFdById НАЙДЕН (FD «" +
                                (oKeyFd == null ? "<null>" : oKeyFd.Name) + "»)");
                        }
                        else
                        {
                            ProbeInfo("[MDFD-KEY] «" + strKeyName + "» → id=" +
                                nKeyId.ToString(CultureInfo.InvariantCulture) +
                                " → dctFdById ПРОМАХ (id=" +
                                nKeyId.ToString(CultureInfo.InvariantCulture) + ")");
                        }
                    }
                }
                // [MDFD-DESC] ×5: первые НЕПУСТЫЕ описания MDS-карты (карта хранит
                // только непустые — условие избыточно-безопасное).
                int nDescSample = 0;
                foreach (KeyValuePair<string, string> oDescPair in dctDesc)
                {
                    if (nDescSample >= 5) break;
                    if (string.IsNullOrEmpty(oDescPair.Value)) continue;
                    ProbeInfo("[MDFD-DESC] «" + oDescPair.Key + "» → «" +
                        oDescPair.Value + "»");
                    nDescSample++;
                }
                // [MDFD] — ОДНА строка после наполнения: N=записей MDS-обхода,
                // M=размер id-карты, K=id-промахов (_dctFdById), D=размер
                // desc-карты; счётчики fill — текущего перечисления.
                ProbeInfo("[MDFD] lib='" + strLibName + "': MDSymbol " +
                    nMdCount.ToString(CultureInfo.InvariantCulture) + " → id " +
                    dctId.Count.ToString(CultureInfo.InvariantCulture) +
                    ", id-промахов " + nIdMiss.ToString(CultureInfo.InvariantCulture) +
                    ", desc " + dctDesc.Count.ToString(CultureInfo.InvariantCulture) +
                    "; заполнено FD " +
                    _nFdViaMd.ToString(CultureInfo.InvariantCulture) + ", desc " +
                    _nDescViaMd.ToString(CultureInfo.InvariantCulture));
            }
        }

        // --- дерево категорий (SymbolCatalog 5 уровней) + поиск + предвыбор ---

        /// <summary>Пересборка дерева из чистого SymbolCatalog (rev.14.2, R2/RC-3;
        /// rev.14.3: SYMB_DESC записи — в SearchIndex листа): 5 уровней Trade →
        /// Area → Категория → Группа → Определение функции, листья — символы
        /// (Tag = имя); unmapped — «Без классификации» (или префиксные бакеты при
        /// полном отсутствии FD-пути). Поиск — индексно-рекурсивный: лист
        /// совпадает по SearchIndex (имя + FD-поля + SYMB_DESC), ветка — по
        /// своему SearchIndex; совпавшая ветка показывает ВСЕХ детей (рестарт
        /// фильтра), непустые без self-match ветки фильтруют листья рекурсивно;
        /// пустые ветки скрыты; корень — библиотека. В конце — подсветка
        /// текущего символа (если он в дереве).</summary>
        private void RebuildTree()
        {
            string strFilter = _txtSearch.Text ?? string.Empty;
            // Каталог — rev.14.2: SymbolCatalogEntry {Name, Fd} →
            // дерево SymbolCatalogNode (5 уровней).
            List<SymbolCatalogEntry> lstCatalogEntries = new List<SymbolCatalogEntry>();
            foreach (SymbolEntry oEntry in _lstEntries)
            {
                if (oEntry == null) continue;
                SymbolCatalogEntry oCatEntry = new SymbolCatalogEntry();
                oCatEntry.Name = oEntry.Name;
                oCatEntry.Fd = oEntry.Fd;
                oCatEntry.Description = oEntry.Description;   // SYMB_DESC — в SearchIndex листа (rev.14.3)
                lstCatalogEntries.Add(oCatEntry);
            }
            List<SymbolCatalogNode> lstTop =
                SymbolCatalog.Build(lstCatalogEntries, _dctFdById);
            string strRootText = string.IsNullOrEmpty(_txtLibrary.Text)
                ? "Библиотека"
                : _txtLibrary.Text;

            _treeSymbols.BeginUpdate();
            _treeSymbols.Nodes.Clear();
            TreeNode oRoot = new TreeNode(strRootText);
            int nShownTotal = 0;
            foreach (SymbolCatalogNode oNode in lstTop)
            {
                nShownTotal += RenderCatalogNode(oNode, strFilter, oRoot.Nodes);
            }
            if (oRoot.Nodes.Count > 0) _treeSymbols.Nodes.Add(oRoot);
            oRoot.Expand();   // верхние уровни видны сразу (символы — по желанию клика)
            _treeSymbols.EndUpdate();

            _lblCount.Text = "показано " + nShownTotal.ToString(CultureInfo.InvariantCulture) +
                " из " + _lstEntries.Count.ToString(CultureInfo.InvariantCulture);
            HighlightCurrentSymbol();
        }

        /// <summary>Рекурсивный рендер узла каталога (rev.14.2; rev.14.3 —
        /// индексно-рекурсивный фильтр): Kind=Symbol — лист (Tag = имя; FindLeaf
        /// ищет по Tag), остальные ветки БЕЗ Tag; текст узла = Name + « (N)»
        /// (N — число ПОКАЗАННЫХ листьев subtree). Фильтр: лист совпадает по
        /// SearchIndex (имя + FD-поля + SYMB_DESC, substring, OrdinalIgnoreCase);
        /// ветка, чей SearchIndex сам совпал (например, FD-имя/описание),
        /// показывает ВСЕХ детей — фильтр для детей рестартует на пустой;
        /// иначе пустые ветки скрыты; возврат — число листьев.</summary>
        private int RenderCatalogNode(SymbolCatalogNode oCatNode, string strFilter,
            TreeNodeCollection oDest)
        {
            if (oCatNode == null) return 0;
            bool bLeaf = string.Compare(oCatNode.Kind, SymbolCatalog.KIND_SYMBOL,
                StringComparison.Ordinal) == 0;
            string strHaystack = oCatNode.SearchIndex ?? oCatNode.Name;
            bool bSelfMatch = strFilter.Length > 0 &&
                strHaystack.IndexOf(strFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            if (bLeaf)
            {
                if (strFilter.Length > 0 && !bSelfMatch) return 0;   // пустой фильтр — все листья
                TreeNode oLeaf = new TreeNode(oCatNode.Name);
                oLeaf.Tag = oCatNode.Name;
                oDest.Add(oLeaf);
                return 1;
            }
            TreeNode oBranch = new TreeNode(oCatNode.Name);
            string strChildFilter = bSelfMatch ? string.Empty : strFilter;
            int nShown = 0;
            foreach (SymbolCatalogNode oChild in oCatNode.Children)
            {
                nShown += RenderCatalogNode(oChild, strChildFilter, oBranch.Nodes);
            }
            if (nShown == 0) return 0;   // ветка без листьев — скрыта; self-match с 0
            // детей не различим от пустой (поддерева нет — скрывать нечего)
            oBranch.Text = oCatNode.Name + " (" +
                nShown.ToString(CultureInfo.InvariantCulture) + ")";
            oDest.Add(oBranch);
            return nShown;
        }

        /// <summary>Подсветка текущего символа (_strSelectedSymbol) в дереве:
        /// точное совпадение листа (БЕЗ trim — урок п.33) — разворот пути +
        /// выделение (AfterSelect применит карточку/превью — идемпотентно). Нет
        /// листа — дерево остаётся без подсветки (имя вне дерева — OK-валидация
        /// скажет). Символ может быть корректно скрыт фильтром поиска — это не
        /// ошибка: состояние сохранилось, OK-валидация идёт по _lstEntries.</summary>
        private void HighlightCurrentSymbol()
        {
            if (string.IsNullOrEmpty(_strSelectedSymbol)) return;
            TreeNode oLeaf = FindLeaf(_treeSymbols.Nodes, _strSelectedSymbol);
            if (oLeaf == null || _treeSymbols.SelectedNode == oLeaf) return;
            TreeNode oWalk = oLeaf;
            while (oWalk != null)
            {
                oWalk.Expand();
                oWalk = oWalk.Parent;
            }
            _treeSymbols.SelectedNode = oLeaf;
        }

        /// <summary>Поиск листа по Tag (точное string-совпадение, Ordinal).
        /// Tag ставится ТОЛЬКО листьям (RenderCatalogNode) — ветки/корню Tag нет,
        /// «(N)»-суффиксы дерева карточке не мешают.</summary>
        private static TreeNode FindLeaf(TreeNodeCollection oNodes, string strName)
        {
            foreach (TreeNode oNode in oNodes)
            {
                if (oNode.Tag != null &&
                    string.Equals((string)oNode.Tag, strName, StringComparison.Ordinal))
                {
                    return oNode;
                }
                TreeNode oFound = FindLeaf(oNode.Nodes, strName);
                if (oFound != null) return oFound;
            }
            return null;
        }

        /// <summary>Предвыбор текущего имени символа точным совпадением (без trim —
        /// урок п.33; rev.14.2 R5: имя — в _strSelectedSymbol, БЕЗ текстового поля).
        /// rev.14.3 Task 5 (дедуп): лист найден — ТОЛЬКО разворот+выделение,
        /// карточку/превью применит AfterSelect → ApplySelection (единожды);
        /// явный apply — только когда листа в дереве НЕТ (имя вне перечисления /
        /// скрыто фильтром): карточка по имени + превью по каскаду (OK-валидация
        /// проверит принадлежность).</summary>
        private void SelectSymbolPrechoice(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return;
            _strSelectedSymbol = strName;
            TreeNode oLeaf = FindLeaf(_treeSymbols.Nodes, strName);
            if (oLeaf != null)
            {
                if (_treeSymbols.SelectedNode != oLeaf)
                {
                    TreeNode oWalk = oLeaf;
                    while (oWalk != null)
                    {
                        oWalk.Expand();
                        oWalk = oWalk.Parent;
                    }
                    _treeSymbols.SelectedNode = oLeaf;   // AfterSelect → ApplySelection единожды
                }
                return;   // applied через AfterSelect (или уже был выделен)
            }
            UpdateCardByEntry(strName);
            RefreshPreviewGridByEntry(strName);
        }

        // --- превью-сетка (DrawingService; R3/R4/R5 + фиксированные 8 клеток) ---

        /// <summary>Настройки вида ОДНОГО сервиса превью (связи и мета-логика
        /// макроса не в тему диалога символа; фон — клетка сама, чёрная по R3).
        /// Каждое свойство — отдельная проба try/catch (отказ — ProbeWarn,
        /// без остановки).</summary>
        private void ApplyPreviewSettings(DrawingService oDs)
        {
            if (oDs == null) return;
            try { oDs.DrawConnections = false; }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] DrawConnections=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try { oDs.MacroPreview = false; }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] MacroPreview=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try { oDs.DrawBackGround = false; }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] DrawBackGround=false — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Пересборка превью-сетки по ИМЕНИ символа (rev.14.2): запись
        /// ищется в _lstEntries; пустые Library/Name — сброс + ClearCard.
        /// rev.14.3 Task 5: после сборки (в т.ч. при полном отказе рендера —
        /// клетки уже стоят, повторный apply не нужен) ключ _strPreviewGridKey
        /// запоминается для гейта ApplySelection. rev.14.4: дедуп перенесён сюда —
        /// единая точка: ОДНА строка [DSPROBE] «сетка» на предвыбор (факт прогона
        /// rev.14.3: сетка строилась 2× при предвыборе через UpdateCardByEntry-
        /// каскад; гейт здесь покрывает ВСЕ повторные вызовы этого метода с тем же
        /// именем; ApplySelection-гейт остаётся).</summary>
        private void RefreshPreviewGridByEntry(string strName)
        {
            string strLib = _txtLibrary.Text;
            if (string.IsNullOrEmpty(strLib) || string.IsNullOrEmpty(strName))
            {
                // Fix-минор r14.1: при сбросе превью карточка тоже очищается.
                DisposePreviewGrid();
                ClearCard();
                _strPreviewGridKey = string.Empty;   // сетки нет — ключ гасится
                return;
            }
            // rev.14.4: дедуп — сетка для этого имени УЖЕ построена (ключ ставится
            // после BuildPreviewGrid ниже): повторный вызов тем же именем ничего
            // не перестраивает (одна [DSPROBE] «сетка» на предвыбор).
            if (_strPreviewGridKey == strName) return;
            SymbolEntry oEntry = FindEntryObj(strName);
            BuildPreviewGrid(strLib, strName, oEntry);
            _strPreviewGridKey = strName;   // сетка построена для этого имени
        }

        /// <summary>Построение ВСЕХ 8 клеток A–H (rev.14.2, R3+спека): внешний
        /// Panel чёрный (enabled) / серый (disabled, Color.FromArgb(56,56,56)); ВНУТРЕННЯЯ
        /// панель рисования Dock=Fill добавляется ПЕРВОЙ, Label буквы Dock=Bottom
        /// (18px) добавляется ВТОРОЙ — docking обрабатывается в обратном порядке,
        /// метка НЕ накрывает область рисования (fix RC-2: раньше Paint получал всю
        /// клетку, а метка снизу её занимала). У каждой клетки СВОЙ DrawingService;
        /// Reset() перед созданием списка + SetDefaultViewport (гипотеза центра
        /// R4). Отсутствующие варианты — disabled (без display list): фон серый,
        /// буква серая, клик игнор. Клик по enabled — выделение (спека §16):
        /// рамка на Paint + цвет буквы; слоты H/V НЕ трогаются. Отказ рендера
        /// клетки не ломает остальные и выбор символа.</summary>
        private void BuildPreviewGrid(string strLib, string strName, SymbolEntry oEntry)
        {
            DisposePreviewGrid();
            _nSelectedCell = -1;
            _bPathLogged = false;   // путь успеха — один раз на пересборку (шум [DSPROBE])
            _tlpPreviewGrid.SuspendLayout();
            int nFailed = 0;   // отказ РЕНДЕРА enabled-клеток
            int nEnabledTotal = 0;
            for (int i = 0; i < 8; i++)
            {
                string strLetter = ((char)('A' + i)).ToString(CultureInfo.InvariantCulture);
                bool bEnabled = IsVariantEnabled(oEntry, i);
                if (bEnabled) nEnabledTotal++;

                Panel oCell = new Panel();
                oCell.BackColor = bEnabled ? Color.Black : Color.FromArgb(56, 56, 56);
                oCell.BorderStyle = BorderStyle.FixedSingle;
                oCell.Dock = DockStyle.Fill;
                oCell.Margin = new Padding(1);

                // ВНУТРЕННЯЯ панель рисования: Dock=Fill, добавляется ПЕРВОЙ —
                // docking обрабатывается в обратном порядке (последний добавленный
                // размещается первым) → метка снизу отъездит свой нижний пояс,
                // рисование НЕ накроется (fix RC-2). Класс DrawPanel — CS1540-фикс:
                // ResizeRedraw protected, чужому экземпляру недоступен.
                Panel oDraw = new DrawPanel();
                oDraw.Dock = DockStyle.Fill;
                oDraw.BackColor = oCell.BackColor;
                oCell.Controls.Add(oDraw);

                Label oLetter = new Label();
                oLetter.Text = strLetter;
                oLetter.BackColor = oCell.BackColor;
                oLetter.ForeColor = bEnabled ? Color.White : Color.FromArgb(160, 160, 160);
                oLetter.TextAlign = ContentAlignment.MiddleCenter;
                oLetter.Height = 18;
                // Fix ревью rev.14.2 (Important): Dock БЫЛ ПОТЕРЯН — метка без
                // закрепления остаётся в (0,0) за непрозрачной панелью рисования
                // (буквы невидимы). Dock=Bottom: docking обрабатывается в обратном
                // порядке добавления — метка (добавлена ВТОРОЙ) резервирует нижние
                // 18px, панель рисования (Dock=Fill, добавлена ПЕРВОЙ) занимает
                // остаток; области не пересекаются (fix RC-2 работает).
                oLetter.Dock = DockStyle.Bottom;
                oCell.Controls.Add(oLetter);

                DrawingService oDs = null;
                bool bReady = false;
                if (bEnabled)
                {
                    try
                    {
                        oDs = new DrawingService();
                        ApplyPreviewSettings(oDs);
                        bReady = BuildCellList(oDs, oEntry, strLib, strName, i, strLetter);
                    }
                    catch (Exception oEx)
                    {
                        ProbeWarn("[DSPROBE] DrawingService клетки " + strLetter + " — " +
                            oEx.GetType().Name + ": " + oEx.Message);
                    }
                    if (!bReady)
                    {
                        nFailed++;
                        if (oDs != null)
                        {
                            try { oDs.Dispose(); }
                            catch (Exception oEx)
                            {
                                ProbeWarn("[DSPROBE] Dispose клетки " + strLetter + " — " +
                                    oEx.GetType().Name + ": " + oEx.Message);
                            }
                            oDs = null;
                        }
                    }
                }
                _lstPreviewCells.Add(oCell);
                _lstCellDrawPanels.Add(oDraw);
                _lstCellLetters.Add(oLetter);
                _lstPreviewServices.Add(oDs);
                _lstCellReady.Add(bReady);
                _lstCellEnabled.Add(bEnabled);

                // Paint ВНУТРЕННЕЙ панели: DrawDisplayList(oArgs, ClientRectangle
                // ВНУТРЕННЕЙ — fix RC-2; не вся клетка, метка больше не накрывает
                // графику) + рамка выделения поверх. Захват индекса — C#5 делегат.
                int iCell = i;
                oDraw.Paint += delegate(object oSender, PaintEventArgs oArgs)
                {
                    if (iCell >= _lstPreviewServices.Count) return;
                    DrawingService oCellDs = _lstPreviewServices[iCell];
                    if (oCellDs != null && _lstCellReady[iCell])
                    {
                        try
                        {
                            oCellDs.DrawDisplayList(oArgs, oDraw.ClientRectangle);
                        }
                        catch (Exception oEx)
                        {
                            // Отказ рендера — ProbeWarn, выбор символа не ломаем.
                            ProbeWarn("[DSPROBE] DrawDisplayList клетка " +
                                ((char)('A' + iCell)).ToString(CultureInfo.InvariantCulture) +
                                " — " + oEx.GetType().Name + ": " + oEx.Message);
                        }
                    }
                    if (_nSelectedCell == iCell && _lstCellEnabled[iCell])
                    {
                        try
                        {
                            Rectangle rcSel = oDraw.ClientRectangle;
                            rcSel.Inflate(-2, -2);
                            using (Pen oPenSel = new Pen(Color.Gold, 2f))
                            {
                                oArgs.Graphics.DrawRectangle(oPenSel, rcSel);
                            }
                        }
                        catch { /* рамка — украшение; отказ не мешает */ }
                    }
                };
                // Клик по рисуемой области / букве enabled клетки — выделение
                // варианта (визуально); слоты H/V НЕ меняются (спека).
                oDraw.MouseClick += delegate { SelectPreviewCell(iCell); };
                oLetter.Click += delegate { SelectPreviewCell(iCell); };
                _tlpPreviewGrid.Controls.Add(oCell, i % 4, i / 4);
            }
            _tlpPreviewGrid.ResumeLayout();
            // Сводка пересборки вместо по-клеточных успехов (ревью rev.14.2): один
            // [DSPROBE]-итог на выделение символа; отказы клеток — WARN выше.
            ProbeInfo("[DSPROBE] сетка: готово " +
                (nEnabledTotal - nFailed).ToString(CultureInfo.InvariantCulture) + " из " +
                nEnabledTotal.ToString(CultureInfo.InvariantCulture) + " enabled / 8 клеток");
            if (nEnabledTotal > 0 && nFailed == nEnabledTotal)
                SetStatus("Превью недоступно: все клетки не отрисованы (см. [DSPROBE] в логе).");
        }

        /// <summary>Строгий три-состояний гейт клетки i (0..7), решение пользователя
        /// (summary п.93): запись есть → делегация SymbolCatalog.IsCellEnabled —
        /// bVariantsKnown=false (сбой чтения Variants) или известный ПУСТОЙ список
        /// вариантов (0 шт.) → ВСЕ клетки disabled; известный список варианта →
        /// только реальные VariantNr. «Неизвестно → есть только A» ЗАПРЕЩЕНО.
        /// Записи нет (oEntry null, предвыборка вне библиотеки) — false.</summary>
        private static bool IsVariantEnabled(SymbolEntry oEntry, int i)
        {
            if (oEntry == null) return false;
            return SymbolCatalog.IsCellEnabled(oEntry.bVariantsKnown, oEntry.lstVariantNrs, i);
        }

        /// <summary>Панель рисования клетки с перерисовкой при ресайзе
        /// (CS1540-фикс, ревью/прогон rev.14.2: ResizeRedraw — protected член
        /// Control, через экземпляр ЧУЖОГО типа недоступен; внутри подкласса —
        /// можно. DrawDisplayList «fit keeping aspect ratio» на каждый Paint,
        /// при ресайзе без ResizeRedraw картинка не перемасштабируется).</summary>
        private class DrawPanel : Panel
        {
            public DrawPanel()
            {
                SetStyle(ControlStyles.ResizeRedraw, true);
            }
        }

        /// <summary>Display list ОДНОЙ клетки (готовность + каскад): Reset() →
        /// (1) живой SymbolVariant из A2 (KB CreateDisplayList(SymbolVariant));
        /// (2) типизированно new Symbol(SymbolLibrary)+new SymbolVariant (rev.14.1);
        /// (3) каскад by-strings (CreateDisplayList(String,String,Int32,Project)).
        /// true — клетка готова к отрисовке. Вызывается ТОЛЬКО для enabled-клеток
        /// (caller-гейт bEnabled — display list disabled-клетке не нужен); успех
        /// пути печатается ОДИН раз на пересборку сетки (_bPathLogged) — иначе
        /// до 32 [DSPROBE]-строк на каждое выделение (шум, ревью rev.14.2).</summary>
        private bool BuildCellList(DrawingService oDs, SymbolEntry oEntry, string strLib,
            string strName, int iVariant, string strLetter)
        {
            try
            {
                oDs.Reset();   // KB: «Resets all settings to standard values» (R4)
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] Reset клетки " + strLetter + " — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            bool bCreated = false;
            Exception oErr = null;
            // (1) Живой объект варианта из A2 — KB-перегрузка CreateDisplayList(SymbolVariant).
            object oVariantObject = FindVariantObject(oEntry, iVariant);
            if (oVariantObject != null)
            {
                oErr = TryDisplayListVariantObject(oDs, oVariantObject, strLetter);
                bCreated = oErr == null;
            }
            // (2) Типизированная сборка заново (как rev.14.1; также для B).
            if (!bCreated)
            {
                oErr = TryDisplayListTyped(oDs, strName, iVariant, strLetter);
                bCreated = oErr == null;
            }
            // (3) By-strings (rev.14.0, HE_Display).
            if (!bCreated)
            {
                oErr = TryDisplayListByStrings(oDs, strName, strLib, iVariant, strLetter);
                bCreated = oErr == null;
            }
            if (!bCreated)
            {
                ProbeWarn("[DSPROBE] вариант " + strLetter + " не отрисован: " +
                    oErr.GetType().Name + ": " + oErr.Message);
                return false;
            }
            TrySetDefaultViewport(oDs);
            return true;
        }

        /// <summary>Отыскать ЖИВОЙ объект SymbolVariant записи по VariantNr
        /// (A2-перечисление сохраняет пары VariantNr/объект).</summary>
        private static object FindVariantObject(SymbolEntry oEntry, int iVariant)
        {
            if (oEntry == null) return null;
            for (int i = 0; i < oEntry.lstVariantNrs.Count; i++)
            {
                if (oEntry.lstVariantNrs[i] == iVariant &&
                    i < oEntry.lstVariantObjects.Count)
                {
                    object oVariant = oEntry.lstVariantObjects[i];
                    if (oVariant != null) return oVariant;
                }
            }
            return null;
        }

        /// <summary>Путь 0 (rev.14.2, A2-первичный): CreateDisplayList(SymbolVariant)
        /// на ЖИВОМ объекте из Symbol.Variants — KB-доказанная перегрузка; сборка
        /// новых объектов не нужна. Отказ — исключение (каскад продолжится).</summary>
        private Exception TryDisplayListVariantObject(DrawingService oDs,
            object oVariantObject, string strLetter)
        {
            SymbolVariant oSymbolVariant = oVariantObject as SymbolVariant;
            if (oSymbolVariant == null)
            {
                Exception oNot = new InvalidOperationException(
                    "сохранённый объект варианта не SymbolVariant");
                ProbeWarn("[DSPROBE] клетка " + strLetter + " — " + oNot.Message);
                return oNot;
            }
            try
            {
                oDs.CreateDisplayList(oSymbolVariant);
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(SymbolVariant) — ок " +
                        "(живой объект из Symbol.Variants, клетка " + strLetter +
                        "; дальнейшие успехи клеток не печатаются)");
                }
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(SymbolVariant-объект) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Путь 1 (первичный при отсутствии живого объекта): DataModel
        /// MasterData Symbol(oSymbolLibrary, strName) → SymbolVariant(oSymbol,
        /// nVariant) → CreateDisplayList(SymbolVariant) — все шаги KB-доказаны.
        /// Объект библиотеки — из _lstLibraryObjects (тот же, что использует цепочки
        /// A2/B); не выбран — путь недоступен (исключение-заглушка, [DSPROBE]).</summary>
        private Exception TryDisplayListTyped(DrawingService oDs, string strName,
            int nVariant, string strLetter)
        {
            int iIndex = _lstLibraries.SelectedIndex;
            SymbolLibrary oLib = (iIndex >= 0 && iIndex < _lstLibraryObjects.Count)
                ? _lstLibraryObjects[iIndex]
                : null;
            if (oLib == null)
            {
                Exception oStub = new InvalidOperationException(
                    "объект библиотеки не выбран из списка — типизированный путь недоступен");
                ProbeInfo("[DSPROBE] CreateDisplayList(SymbolVariant) — " + oStub.Message);
                return oStub;
            }
            try
            {
                Symbol oSymbol = new Symbol(oLib, strName);
                SymbolVariant oSymbolVariant = new SymbolVariant(oSymbol, nVariant);
                oDs.CreateDisplayList(oSymbolVariant);
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(SymbolVariant) — ок " +
                        "(типизированный путь: Symbol(SymbolLibrary) + " +
                        "SymbolVariant(Symbol,Int32), клетка " + strLetter +
                        "; дальнейшие успехи клеток не печатаются)");
                }
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(SymbolVariant) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Путь 2 (запасной): CreateDisplayList(String,String,Int32,Project)
        /// — KB-доказана (пример HE_Display: CreateDisplayList(strObj,"",0,gProject));
        /// первый пробой — с именем библиотеки, второй — с "" вместо библиотеки.
        /// Перегрузка с RepresentationType сознательно НЕ пробуется (не доказана —
        /// одна гипотеза за прогон). Оба отказа — последнее исключение.</summary>
        private Exception TryDisplayListByStrings(DrawingService oDs, string strName,
            string strLib, int nVariant, string strLetter)
        {
            try
            {
                oDs.CreateDisplayList(strName, strLib, nVariant, _oProject);
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(String,String,Int32,Project)" +
                        " — ок (имя библ: «" + strLib + "», клетка " + strLetter +
                        "; дальнейшие успехи клеток не печатаются)");
                }
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(...," + strLib + ",...) — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            try
            {
                oDs.CreateDisplayList(strName, "", nVariant, _oProject);
                if (!_bPathLogged)
                {
                    _bPathLogged = true;
                    ProbeInfo("[DSPROBE] CreateDisplayList(String,String,Int32,Project)" +
                        " — ок (пустое имя библ, паттерн HE_Display, клетка " +
                        strLetter + "; дальнейшие успехи клеток не печатаются)");
                }
                return null;
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] CreateDisplayList(...,\"\") — " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return oEx;
            }
        }

        /// <summary>Проба SetDefaultViewport ПОСЛЕ создания списка (KB: «Adjusts
        /// viewport to the bounding box of the objects from drawing list») —
        /// безвредна. Успех НЕ печатается (шум: 8 клеток на выделение, ревью
        /// rev.14.2); отказ — WARN (не поле отказа рендера: список уже создан).</summary>
        private void TrySetDefaultViewport(DrawingService oDs)
        {
            try
            {
                oDs.SetDefaultViewport();
            }
            catch (Exception oEx)
            {
                ProbeWarn("[DSPROBE] SetDefaultViewport — " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Выделение клетки превью (спека §16): только enabled-клетка;
        /// хранит индекс (_nSelectedCell); перерисовка — Invalidate панелей рисования
        /// (Paint рисует золотую рамку) + цвет букв; слоты H/V НЕ меняются.</summary>
        private void SelectPreviewCell(int iCellIndex)
        {
            if (iCellIndex < 0 || iCellIndex >= _lstCellEnabled.Count) return;
            if (!_lstCellEnabled[iCellIndex]) return;   // disabled / отсутствующий вариант
            if (_nSelectedCell == iCellIndex) return;   // уже выделена
            _nSelectedCell = iCellIndex;
            for (int i = 0; i < _lstCellLetters.Count; i++)
            {
                Label oLetter = _lstCellLetters[i];
                if (oLetter != null)
                {
                    oLetter.ForeColor = (i == _nSelectedCell)
                        ? Color.Gold
                        : (_lstCellEnabled[i] ? Color.White : Color.FromArgb(160, 160, 160));
                }
                Panel oDraw = _lstCellDrawPanels[i];
                if (oDraw != null)
                {
                    try { oDraw.Invalidate(); }
                    catch { /* контрол уже отсоединён — не мешает */ }
                }
            }
        }

        /// <summary>Сброс сетки: Dispose ВСЕХ сервисов + очистка всех параллельных
        /// списков клеток (OnFormClosing и каждая пересборка). Внутренние панели и
        /// буквы — дети клеток, dispose'ятся рекурсивно вместе с ними.</summary>
        private void DisposePreviewGrid()
        {
            foreach (DrawingService oDs in _lstPreviewServices)
            {
                if (oDs == null) continue;
                try { oDs.Dispose(); }
                catch (Exception oEx)
                {
                    ProbeWarn("[DSPROBE] Dispose DrawingService — " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }
            _lstPreviewServices.Clear();
            foreach (Panel oCell in _lstPreviewCells)
            {
                if (oCell == null) continue;
                try { oCell.Dispose(); }
                catch { /* контрол уже отсоединён — не мешает */ }
            }
            _lstPreviewCells.Clear();
            _lstCellDrawPanels.Clear();
            _lstCellLetters.Clear();
            _lstCellReady.Clear();
            _lstCellEnabled.Clear();
            _nSelectedCell = -1;
            _tlpPreviewGrid.Controls.Clear();
            _tlpPreviewGrid.Invalidate();
        }

        // --- карточка ---

        /// <summary>Карточка превью (rev.14.2 R4): имя (как есть); категория =
        /// FD.Name выбранной записи (или «—» — Fd не сопоставился/пустое имя);
        /// описание = SYMB_DESC записи, иначе FD.Description, иначе «—».</summary>
        private void UpdateCardByEntry(string strName)
        {
            SymbolEntry oEntry = FindEntryObj(strName);
            string strCategoryText = null;
            string strDescriptionText = null;
            if (oEntry != null)
            {
                if (oEntry.Fd != null && !string.IsNullOrEmpty(oEntry.Fd.Name))
                    strCategoryText = oEntry.Fd.Name;
                if (!string.IsNullOrEmpty(oEntry.Description))
                    strDescriptionText = oEntry.Description;
                else if (oEntry.Fd != null && !string.IsNullOrEmpty(oEntry.Fd.Description))
                    strDescriptionText = oEntry.Fd.Description;
            }
            _lblPreviewName.Text = "Имя: " +
                (string.IsNullOrEmpty(strName) ? "—" : strName);
            _lblPreviewCategory.Text = "Категория: " +
                (string.IsNullOrEmpty(strCategoryText) ? "—" : strCategoryText);
            _lblPreviewDescription.Text = "Описание: " +
                (string.IsNullOrEmpty(strDescriptionText) ? "—" : strDescriptionText);
        }

        /// <summary>Сброс карточки в прочерки (фикс r14.1: вместе со сбросом
        /// превью). Вызывается из RefreshPreviewGridByEntry при пустых строках и
        /// RefreshSymbols при смене библиотеки.</summary>
        private void ClearCard()
        {
            _lblPreviewName.Text = "Имя: —";
            _lblPreviewCategory.Text = "Категория: —";
            _lblPreviewDescription.Text = "Описание: —";
        }

        /// <summary>Поиск записи (точное Ordinal) по имени — для карточки/OK-
        /// валидации (rev.14.2: дубли имён сохраняются — первый побеждает).</summary>
        private int FindEntryIndex(string strName)
        {
            if (string.IsNullOrEmpty(strName)) return -1;
            for (int i = 0; i < _lstEntries.Count; i++)
            {
                SymbolEntry oEntry = _lstEntries[i];
                if (oEntry != null &&
                    string.Compare(oEntry.Name, strName, StringComparison.Ordinal) == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Объект записи по имени (null — нет такой; карточка прочерками
        /// «категория/описание», превью — каскад по строкам).</summary>
        private SymbolEntry FindEntryObj(string strName)
        {
            int iIndex = FindEntryIndex(strName);
            if (iIndex >= 0) return _lstEntries[iIndex];
            return null;
        }

        private void SetStatus(string strText)
        {
            _lblStatus.Text = strText;
        }

        // --- слоты вариантов ---

        /// <summary>Слоты — ComboBox букв A-H (всегда 8; rev.14.1 R6). Буква =
        /// (char)('A'+индекс); наружу Variant*/настройки — числа (индексы).</summary>
        private static void FillVariantCombo(ComboBox oComboBox)
        {
            oComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            oComboBox.Items.Clear();
            for (int i = 0; i < 8; i++)
                oComboBox.Items.Add(((char)('A' + i)).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Предвыбор слота из числа: 0..7 → SelectedIndex; вне диапазона —
        /// прижать к 0 + INFO (сохранённое имя/вариант другой библиотеки).</summary>
        private void SetVariantIndex(ComboBox oComboBox, int nValue)
        {
            if (nValue < 0 || nValue > 7)
            {
                if (nValue > 7)
                    ProbeInfo("[DSPROBE] вариант " +
                        nValue.ToString(CultureInfo.InvariantCulture) +
                        " вне A-H — слот прижат к A");
                nValue = 0;
            }
            oComboBox.SelectedIndex = nValue;
        }

        // --- Layout (rev.14.2: 2 колонки ~50%/50%; паттерн MainDialog) ---

        private static Label LabelOf(string strText)
        {
            Label oLabel = new Label();
            oLabel.Text = strText;
            oLabel.AutoSize = true;
            oLabel.Margin = new Padding(3, 0, 3, 0);
            return oLabel;
        }

        /// <summary>Добавить контрол строкой в одноколоночный TableLayoutPanel
        /// (bStretch — растянуть по ширине панели).</summary>
        private static void AddRow(TableLayoutPanel oPanel, Control oControl, bool bStretch)
        {
            oPanel.RowCount++;
            oPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            oControl.Anchor = bStretch
                ? AnchorStyles.Left | AnchorStyles.Right
                : AnchorStyles.Left;
            oPanel.Controls.Add(oControl, 0, oPanel.RowCount - 1);
        }

        /// <summary>Строка с КОНКРЕТНОЙ высотой (превью-сетка фиксированной
        /// высоты: 2 ряда по ~170 — как решено для 8 клеток).</summary>
        private static void AddRowFixed(TableLayoutPanel oPanel, Control oControl, int nHeight)
        {
            oPanel.RowCount++;
            oPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, nHeight));
            oControl.Dock = DockStyle.Fill;
            oPanel.Controls.Add(oControl, 0, oPanel.RowCount - 1);
        }

        /// <summary>Layout (rev.14.2, R3): контент — TableLayoutPanel 2 колонки
        /// (50/50), слева — библиотека/список/поиск/дерево 5 уровней/счётчик (поле
        /// «Имя выбранного символа» УДАЛЕНО — R5); справа — превью-сетка 8 клеток
        /// (2×170) и карточка; внизу — слоты H/V (комбо букв), статус, кнопки.
        /// ClientSize 1120×800.</summary>
        private void BuildLayout(Button btnOk, Button btnCancel)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12, 8, 12, 8);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // Контент: 2 колонки 50/50 (rev.14.2: 1120 ширины — колонки поровну).
            TableLayoutPanel oContent = new TableLayoutPanel();
            oContent.ColumnCount = 2;
            oContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            oContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            oContent.Dock = DockStyle.Fill;
            oContent.AutoSize = true;

            // ЛЕВО: библиотека/список/поиск/дерево/счётчик.
            TableLayoutPanel oLeft = new TableLayoutPanel();
            oLeft.Dock = DockStyle.Fill;
            oLeft.ColumnCount = 1;
            oLeft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(oLeft, LabelOf("Библиотека (проекта; имя можно ввести вручную):"), false);
            AddRow(oLeft, _txtLibrary, true);
            _lstLibraries.Height = 110;
            _lstLibraries.HorizontalScrollbar = true;
            AddRow(oLeft, _lstLibraries, true);
            AddRow(oLeft, LabelOf("Поиск символа (подстрока):"), false);
            AddRow(oLeft, _txtSearch, true);
            AddRow(oLeft, LabelOf("Дерево: Trade → Area → Категория → Группа → " +
                "Определение функции → символ; двойной клик — выбрать:"), false);
            _treeSymbols.Height = 340;
            // HSCROLL у TreeView автоматический (свойства HorizontalScrollbar нет — CS1061).
            AddRow(oLeft, _treeSymbols, true);
            AddRow(oLeft, _lblCount, false);

            // ПРАВО: превью-сетка (8 клеток A-H, дискретные 2×170) + карточка.
            TableLayoutPanel oRight = new TableLayoutPanel();
            oRight.Dock = DockStyle.Fill;
            oRight.ColumnCount = 1;
            oRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            AddRow(oRight, LabelOf("Превью (A–H; выделение — клик по клетке):"), false);
            _tlpPreviewGrid.ColumnCount = 4;
            for (int iCol = 0; iCol < 4; iCol++)
                _tlpPreviewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            _tlpPreviewGrid.RowCount = 2;
            for (int iRow = 0; iRow < 2; iRow++)
                _tlpPreviewGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 170f));
            _tlpPreviewGrid.Dock = DockStyle.Fill;
            _tlpPreviewGrid.BackColor = Color.Black;
            AddRowFixed(oRight, _tlpPreviewGrid, 352);
            _lblPreviewName.AutoSize = true;
            _lblPreviewCategory.AutoSize = true;
            _lblPreviewDescription.AutoSize = true;
            AddRow(oRight, _lblPreviewName, false);
            AddRow(oRight, _lblPreviewCategory, false);
            AddRow(oRight, _lblPreviewDescription, false);

            oContent.Controls.Add(oLeft, 0, 0);
            oContent.Controls.Add(oRight, 1, 0);

            // Низ (как rev.13.1): контент → слоты H/V → статус (шире) → кнопки.
            root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Controls.Add(oContent, 0, 0);

            FlowLayoutPanel pnlVariants = new FlowLayoutPanel();
            pnlVariants.FlowDirection = FlowDirection.LeftToRight;
            pnlVariants.AutoSize = true;
            pnlVariants.Margin = new Padding(3, 0, 3, 0);
            pnlVariants.Controls.Add(LabelOf("Вариант для горизонтальной формы:"));
            _cbVariantH.Width = 90;
            pnlVariants.Controls.Add(_cbVariantH);
            pnlVariants.Controls.Add(LabelOf("  Вариант для вертикальной формы:"));
            _cbVariantV.Width = 90;
            pnlVariants.Controls.Add(_cbVariantV);
            AddRow(root, pnlVariants, false);

            _lblStatus.AutoSize = true;
            _lblStatus.MaximumSize = new Size(1050, 0);
            AddRow(root, _lblStatus, false);

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel();
            pnlButtons.FlowDirection = FlowDirection.RightToLeft;
            pnlButtons.Dock = DockStyle.Fill;
            pnlButtons.AutoSize = true;
            pnlButtons.Controls.Add(btnOk);       // первый — у правого края
            pnlButtons.Controls.Add(btnCancel);
            AddRow(root, pnlButtons, true);

            Controls.Add(root);
        }

        /// <summary>Предвыбор точным совпадением (БЕЗ trim — урок п.33); значения
        /// нет в списке — список остаётся без предвыбора.</summary>
        private static void SelectExact(ListBox oList, string strValue)
        {
            if (string.IsNullOrEmpty(strValue)) return;
            int iIndex = oList.Items.IndexOf(strValue);
            if (iIndex >= 0) oList.SelectedIndex = iIndex;
        }
    }
}
