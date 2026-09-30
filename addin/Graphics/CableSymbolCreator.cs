using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;

namespace MyEplanActions
{
    /// <summary>Вставка символов кабеля (Фаза G, spec
    /// docs/superpowers/specs/2026-09-22-fase-g-graphics-design.md; мастер-план §13 —
    /// Этап 9). KB 2.9 (проверено 22.09.2026): SymbolLibrary(Project, String) →
    /// Symbol = oLibrary[имя] → SymbolVariant = oSymbol[индекс]; Create —
    /// ИНСТАНСНЫЙ (public virtual void Create(Page, SymbolVariant)); позиция —
    /// Placement.Location (get/set). Библиотека/имя/вариант — полная перегрузка
    /// параметрами (UI, Фаза H: из настроек); старая сигнатура — обёртка с
    /// константами AddInConfiguration + компенсация (0;0) (headless без изменений).
    /// rev.11.1: символы вставляются как
    /// Function (наследник SymbolReference), НЕ как голый SymbolReference —
    /// root cause S063113: DT-свойства функционального домена
    /// (1120/1220/1620/20000, класс «Base class for functions»/FunctionBase)
    /// SymbolReference не принимаются — все пути записи давали S063113.
    /// Дополнительно на символе: главная функция off (свойство 20122) и вид
    /// представления «однополюсный» (ManualPlacementType = CircuitSingleLine).
    /// rev.11.2: ОУ символа по частям в свойства 1120/1220/1420/1620/1820
    /// property-списка — root cause прогона: эти свойства read-only в API
    /// (1420 в таблице нет вовсе) → 16×S063113, readback пустой, Name собрался
    /// '=+++#'. rev.11.3: ОУ через FunctionBase.NameParts — структурная запись
    /// блоков, мимо DT-парсера и мимо read-only property-списка (и мимо
    /// пере-разбора Name из rev.11.1, ломавшего 'HII-1.1'); Function.Name НЕ
    /// пишем (см. WriteDeviceTagProperties). rev.11.4: root cause — в
    /// rev.11.3 писали через typed-аксессоры DESIGNATION_FULL* — это номера
    /// 1120/1220/1420/1620/1820, вычисляемые агрегаты «Full …» (KB Remarks:
    /// «This property is read-only»): offline-список молча не сохранял
    /// значения (bOfflineOk=true), NameParts присваивался без частей →
    /// Name '=+++#'; read-back FULL*-аксессорами — EmptyPropertyException.
    /// Фактические блоки DT — базовые номера 1100 (PLANT)/1200 (LOCATION)/
    /// 1400 (PLACEOFINSTALLATION)/1600 (USERDEFINED)/1800 (PRODUCT); запись —
    /// индексатором Property(AnyPropertyId) {set} (создаёт записи; паттерн
    /// 20122). rev.11.5: слоты блоков уточнены по факту прогона — «М»
    /// (место сборки, «++»), записанное в 1200, отрисовалось в слоте «+»:
    /// «++» — это 1400, «+» — это 1200 (1200/1400 поменяны местами). Имя
    /// 1800 через NameParts платформа выбросила (readback
    /// EmptyPropertyException) — добавлена прямая online-запись
    /// oFunc.Properties[1800] (паттерн 20122; отказ — WARN [SYMDT-N2]).
    /// rev.11.6: имя устройства НЕ в DESIGNATION_* — факт эталона
    /// [SRC-DT] (Properties 1800='—'), прямая запись 1800 → S063113
    /// (read-only) — блок [SYMDT-N2] удалён. Остался сеттер
    /// FunctionBase.Name: пишем ГОЛОЕ имя (без '='/'++'/'+'/'#'/'-' —
    /// парсер DT неразрывен; полный DT rev.11.1 ломался на '-' внутри
    /// 'HII-1.1'); выживание структур после записи — замер [SYMDT-RD2].
    /// rev.11.7: факт прогона rev.11.6 — Name-сеттер СТЁР структуры
    /// ([SYMDT-RD2] 1100/1400/1600='—': сеттер = полная замена DT),
    /// порядок изменён: имя первым, структуры вторым; проверка
    /// выживания имени — финальный Name.
    /// rev.11.9: ревизия 11.8 (видимое ОУ) отклонена ДО прогона: KB —
    /// DESIGNATION_*_VISIBLE read-only, 20051 при пустом видимом DT
    /// опасен; имя устройства через API 2.9 недостижимо (сеттеры
    /// Name/NameParts взаимно исключающи — факты rev.11.6/11.7; 1800
    /// read-only S063113; имя ∉ NameParts — эталон [SRC-DT]).
    /// Структуры — стабильны.
    /// rev.11.10: чистый эксперимент «только Name» — парсер DT рвёт
    /// значения структур на '-' ('HII-1.1' → установка='HII', факт
    /// rev.11.1), т.к. '-' не входит в разрешённые символы
    /// идентификатора. Гипотеза (KB 2.9, Project.DeviceTagConfig →
    /// Project.DeviceTagSettings: EnableSyntaxCheck/AllowUserCharacters/
    /// UserCharacters; GUI: Настройки → Проекты → Устройства → Проверка
    /// синтаксиса ОУ → Идентификатор структуры → Спец. текстовые
    /// символы; UserCharacters действует только при обоих bool=true):
    /// временно разрешив '-' (EnableSyntaxCheck=true,
    /// AllowUserCharacters=true, UserCharacters += '-'), запишем ПОЛНЫЙ
    /// DT одной строкой: oFunc.Name = '=HII-1.1++М+#3-K140' — парсер
    /// примет 'HII-1.1' целиком как установку, '-K140' — как имя.
    /// Запись NameParts удалена целиком: подтвердится — путь не нужен,
    /// нет — вернём в rev.11.11. Настройки — временные: оригиналы
    /// читаются до эксперимента и восстанавливаются в finally (даже
    /// при отказе записи Name).
    /// rev.11.11: эксперимент rev.11.10 (UserCharacters+='-') опровергнут
    /// прогоном: разбиение по '-' в парсере DT не зависит от настроек
    /// синтаксиса (EnableSyntaxCheck был False и с разрешённым '-' парсер
    /// рвёт так же); откат к NameParts-структурам; имя устройства —
    /// ограничение API 2.9 (см. rev.11.9).
    /// rev.11.12: попытка внешнего сервиса полного ОУ («sets the given
    /// full name as the new full name ... and adjusts the visible
    /// name»; false = объект НЕ изменён) — в rev.11.13 удалена как
    /// лишний слой.
    /// rev.11.13: ПРОРЫВ (KB API 2.9, авторитетные Remarks): имя
    /// устройства ('K140') — НЕ структурный блок, а ДВЕ части имени
    /// в NameParts: FUNC_CODE #20013 (буква 'K') и FUNC_COUNTER
    /// #20014 (счётчик '140'); Remarks обоих: «This property is used
    /// as part of a name. In order to set it, member NameParts must
    /// be used on object which name will be changed.» — запись тем же
    /// индексатором Property(AnyPropertyId) {set}, что и структуры.
    /// Это объясняет все прошлые неудачи: в NameParts писались только
    /// DESIGNATION_* (структуры), а имя — не структурный блок. Разбор
    /// 'K140' → 'K'+'140': буква — ведущие нецифровые, счётчик —
    /// хвост-цифры; без цифр — всё в букву, счётчик пропускается.
    /// rev.11.14: установка — не одна строка в 1100, а ГЛАВНЫЙ
    /// идентификатор 'HII-1' (PLANT #1100) + цепочка подчинённых
    /// '1' (SUBPLANT1..9 = 1101..1109), разбор по точкам; строку
    /// '=HII-1.1' собирает платформа. Запись 'HII-1.1' одной строкой
    /// в 1100 давала отдельные узлы-двойники в дереве структуры (не
    /// прикреплялись к существующим кабелям); разбор по точкам —
    /// слияние с деревом идентификаторов (пишет WritePlantSegments
    /// в обоих путях: offline + fallback).
    /// rev.11.15: разбивка по точкам — обобщение rev.11.14 на ВСЕ
    /// структурные блоки (в других проектах точки могут быть в любом
    /// блоке): слот '++' (место сборки, KB-имя PLACEOFINSTALLATION)
    /// 1400 → 1401..1409; слот '+' (место установки, KB-имя LOCATION)
    /// 1200 → 1201..1209; опред.польз. 1600 → 1601..1609; номера
    /// подчинённых = базовый+1..9. WritePlantSegments →
    /// WriteStructureSegments (базовый номер — параметр), вызовы ×4 в
    /// обоих путях; readback первых подчинённых ×4 (1101/1401/1201/1601).
    /// rev.12.8: пустое «Видимое ОУ» после NameParts-присваивания → символ
    /// терял ОУ при обновлении листа/перемещении графики (visible не
    /// вычисляется; FUNC_VISIBLENAME #20002 read-only). Пробованный путь —
    /// HEServices.NameService.SetFullNameAndAdjustVisibleName(Page,
    /// FunctionBase, UniversalPropertyList) (KB topic2727: «sets the full
    /// name ... and adjusts the visible name»; false — объект НЕ изменён,
    /// атомарно); offline-список FunctionBasePropertyList наследует
    /// UniversalPropertyList. Ветка УДАЛЕНА в rev.12.9 (см. ниже).
    /// Readback [SYMDT-RD] 20002 — критерий успеха в логе (INFO): видимое
    /// заполнено и равно источнику.
    /// rev.12.8 ПРОГОН (25.09): SetFullNameAndAdjustVisibleName вернул false на
    /// ВСЕХ 4 символах (без исключения; docs: false = «visible name cannot be
    /// evaluated», объект не тронут — откат отработал, полное ОУ как в rev.11.15,
    /// 20002=''). rev.12.9: первичная запись возвращена к rev.11.15 (проверенный
    /// offline-путь), ветка SetFullName... удалена; видимое ОУ — отдельным шагом
    /// ПОСЛЕ записанного полного имени: NameService.AdjustVisibleName(Page,
    /// FunctionBase) (KB: «evaluates the visible name and visible name format
    /// FROM THE FULLNAME of the functionbase and sets these evaluated values at
    /// the functionbase-object»; false — «instance name could not be evaluated
    /// and set due to nesting»). Отличная от 12.8 временна́я точка: полное имя
    /// уже на объекте (не внутри атомарной установки). Отказ — WARN [SYMDT-AVN],
    /// не прерывает; критерий успеха — readback 20002.
    /// rev.13.0: по примеру пользователя — первичный путь 2-арг.
    /// SetFullNameAndAdjustVisibleName(oFunc, oParts) с ПРЕДВАРИТЕЛЬНО
    /// выставленным NameService.Page (docs: ApplicationException «when page is
    /// not set»), offline-список БЕЗ 1800 (платформа его отбрасывает; в примере
    /// его нет — подозреваемый в false rev.12.8). true — сервис поставил
    /// полное+видимое (присваивание/AVN пропускаются); false/исключение — WARN
    /// [SYMDT-NSS] → rev.11.15-присваивание + AVN. Цепочка различима по токенам;
    /// арбитр — [SYMDT-RD] 20002 + Name.
    /// rev.13.0 ПРОГОН (25.09, п.70 summary) — ЗАКРЫТА: сервис 2-arg ok на всех
    /// 4 символах (fallback/AVN не понадобились), 20002 заполнено, Name==источнику,
    /// ОУ не теряется при обновлении листа/перемещении (визуально). Visible
    /// вычислен по схеме EPLAN (маркер пустого блока «место установки» опущен:
    /// '=HII-1.1++М#3-K140' — норма).
    /// Поворот 0° (spec §7). Центр круга
    /// = точке вставки (rev.10.4: офсет п.47 опровергнут — замер был загрязнён
    /// останцами старых прогонов). rev.13.1 (Этап 8, H-4): для символа с ненулевой
    /// компенсацией центра (dx,dy) из замера [SYMSIZE-OFF] позиция вставки
    /// скорректирована (SymbolPlacementMath.Compensate) — визуальный центр символа
    /// на конце линии; offset (0;0) (CABDCP2, headless) — поведение прежнее. Отказ —
    /// WARN [SYMBOL]. НЕ идемпотентно (очистка — Фаза I).</summary>
    public static class CableSymbolCreator
    {
        /// <summary>Старая (headless) сигнатура — обёртка полной перегрузки с
        /// константами AddInConfiguration и компенсацией центра (0;0) (ruling R8:
        /// headless побайтно без изменений; CABDCP2 Δ=(0;0) доказан пробой п.49 —
        /// поведение == rev.11.15+).</summary>
        public static int CreateSymbols(Page oPage, CableGeometryResult oGeom,
            DiagnosticLogger log)
        {
            return CreateSymbols(oPage, oGeom, log,
                AddInConfiguration.SymbolLibrary, AddInConfiguration.SymbolName,
                AddInConfiguration.SymbolVariant, 0.0, 0.0,
                AddInConfiguration.BlockFormatIndexDefault);
        }

        /// <summary>Символ на каждый CableSymbolPlacement. Возвращает число созданных.
        /// Библиотека/символ/вариант недоступны — один WARN, 0 созданных.
        /// rev.13.1 (Этап 8, H-4): тройка символа — параметрами (UI: из настроек,
        /// индекс варианта ФАКТИЧЕСКОЙ ориентации); (dOffsetX, dOffsetY) — компенсация
        /// визуального центра варианта (ruling R9): Location = desired − (dx,dy),
        /// чистая арифметика — SymbolPlacementMath.Compensate (non-finite offset →
        /// без компенсации). (0;0) — прежнее поведение (центр = точке вставки).
        /// rev.16.0: nBlockFormatIndex — индекс слота «Свойство блока: Формат [x]»
        /// (#20202, 1..100); при непустом oSym.BlockFormat строка пишется в 20202[x]
        /// (WriteBlockFormat, отказ — WARN, символ создаётся).</summary>
        public static int CreateSymbols(Page oPage, CableGeometryResult oGeom,
            DiagnosticLogger log,
            string strLibrary, string strSymbolName, int nVariant,
            double dOffsetX, double dOffsetY, int nBlockFormatIndex)
        {
            if (oPage == null || oGeom == null)
            {
                log.Warn("[SYMBOL] CreateSymbols: page или geometry == null — символов не создаём");
                return 0;
            }
            int nTotal = oGeom.Symbols.Count;
            if (nTotal == 0) return 0;

            SymbolVariant oVariant;
            try
            {
                SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project, strLibrary);
                Symbol oSymbol = oLibrary[strSymbolName];
                oVariant = oSymbol[nVariant];
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMBOL] вариант '" + strLibrary + "'/" +
                    strSymbolName + "/" + nVariant +
                    " недоступен: " + oEx.GetType().Name + ": " + oEx.Message);
                log.Log("[INFO] [SYMBOL-SUM] символов 0 из " + nTotal + ".");
                return 0;
            }

            int nCreated = 0;
            foreach (CableSymbolPlacement oSym in oGeom.Symbols)
            {
                try
                {
                    Function oFunc = new Function();
                    oFunc.Create(oPage, oVariant);
                    // rev.11.1: главная функция off — свойство 20122 FUNC_MAINFUNCTION
                    // (Boolean, объекты «Functions», перезаписываемое). Каждый отказ —
                    // WARN [SYMFUNC] и не мешает второй записи.
                    bool bMainFuncOffOk = false;
                    try
                    {
                        AnyPropertyId oIdMainFunc = CreateAnyPropertyIdFromNumber(20122);
                        if (oIdMainFunc == null)
                            throw new InvalidOperationException("CreateAnyPropertyIdFromNumber(20122) вернул null");
                        oFunc.Properties[oIdMainFunc] = (PropertyValue)false;
                        bMainFuncOffOk = true;
                    }
                    catch (Exception oEx)
                    {
                        log.Warn("[SYMFUNC] 20122 (главная функция=false): " +
                            oEx.GetType().Name + ": " + oEx.Message);
                    }
                    // rev.11.1: вид представления «однополюсный» — типизированное
                    // свойство ManualPlacementType (FUNC_TYPE=20121 значение
                    // 2=Single-line).
                    bool bPlacementTypeOk = false;
                    try
                    {
                        oFunc.ManualPlacementType = DocumentTypeManager.DocumentType.CircuitSingleLine;
                        bPlacementTypeOk = true;
                    }
                    catch (Exception oEx)
                    {
                        log.Warn("[SYMFUNC] ManualPlacementType (однополюсный): " +
                            oEx.GetType().Name + ": " + oEx.Message);
                    }
                    if (bMainFuncOffOk && bPlacementTypeOk)
                        log.Log("[INFO] [SYMFUNC] главная функция=false, вид представления=однополюсный (CircuitSingleLine)");
                    // rev.10.4: офсет центра CABDCP2 из п.47 ОПРОВЕРГНУТ прогоном rev.10.3
                    // (круг ушёл 1:1 с точкой вставки): центр круга = точке вставки —
                    // для offset (0;0) compensation ниже — идентичность (headless-путь).
                    // rev.13.1 (H-4, ruling R9): Location = desired − (dx,dy) — визуальный
                    // центр символа встаёт на конец линии; (dx,dy)=0 → прежнее поведение.
                    double dLocX, dLocY;
                    SymbolPlacementMath.Compensate(oSym.Position.X, oSym.Position.Y,
                        dOffsetX, dOffsetY, out dLocX, out dLocY);
                    oFunc.Location = new PointD(dLocX, dLocY);
                    nCreated++;
                    log.Log("[INFO] [SYMBOL] '" + (oSym.CableName ?? "<без имени>") + "' #" +
                        oSym.CableIndex.ToString(CultureInfo.InvariantCulture) + " @ (" +
                        oSym.Position.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                        oSym.Position.Y.ToString("F3", CultureInfo.InvariantCulture) + ")");

                    // rev.11.13: ОУ символа — структуры через
                    // FunctionBase.NameParts (offline-список →
                    // присваивание, fallback get-modify-set) плюс имя
                    // устройства — теперь ТОЖЕ через NameParts как
                    // FUNC_CODE #20013 + FUNC_COUNTER #20014 (шапка
                    // rev.11.13). Отказы — WARN [SYMDT] внутри,
                    // счётчик и остальные символы не страдают.
                    if (!string.IsNullOrEmpty(oSym.CableName))
                        WriteDeviceTagProperties(oPage, oFunc, oSym, log);

                    // rev.16.0 (спека §4.4): строка «Свойство блока: Формат [x]» —
                    // пишется ПОСЛЕ ОУ-санитарии (не мешать WriteDeviceTagProperties).
                    // Поле null/пустое — фича off, тишина.
                    if (!string.IsNullOrEmpty(oSym.BlockFormat))
                        WriteBlockFormat(oFunc, oSym.BlockFormat, nBlockFormatIndex, log);
                }
                catch (Exception oEx)
                {
                    log.Warn("[SYMBOL] вставка/размещение бросили " + oEx.GetType().Name + ": " + oEx.Message +
                        " (символ '" + (oSym.CableName ?? "<без имени>") + "' #" +
                        oSym.CableIndex.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
            log.Log("[INFO] [SYMBOL-SUM] символов " + nCreated + " из " + nTotal + ".");
            return nCreated;
        }

        /// <summary>Запись строки формата в «Свойство блока: Формат [x]» —
        /// rev.16.0 (спека §4.4): свойство #20202, индексы 1..100
        /// (KB FunctionPropertyList~FUNC_BLOCK_FORMAT). Путь (a) — типизированный
        /// индексатор Property(id,index); путь (b) — fallback через
        /// PropertyValue-индексатор (KB PropertyValue~Item: oProperty[1]).
        /// Отказ — WARN, символ создаётся (не мешает остальным). Пустая строка
        /// или индекс вне 1..100 — тихий skip (фича off / некорректный индекс).</summary>
        private static void WriteBlockFormat(Function oFunc, string strFormat, int nIdx,
            DiagnosticLogger log)
        {
            if (string.IsNullOrEmpty(strFormat) || nIdx < 1 || nIdx > 100)
            {
                if (nIdx < 1 || nIdx > 100)
                    log.Warn("[BLOCKFMT] индекс слота " + nIdx +
                        " вне 1..100 — запись пропущена");
                return;
            }
            // Путь (a): типизированный индексатор Properties[PropertyId, index].
            string strErrA = null;
            try
            {
                // rev.16.0 fix: ctor PropertyValue(string) в 2.9 нет —
                // implicit op_Implicit(String→PropertyValue) (KB).
                oFunc.Properties[Properties.Function.FUNC_BLOCK_FORMAT, nIdx] = strFormat;
            }
            catch (Exception oEx)
            {
                strErrA = oEx.GetType().Name + ": " + oEx.Message;
            }
            if (strErrA != null)
            {
                // Путь (b): fallback через PropertyValue-индексатор
                // (get → oPV[nIdx] = … → set). Отказ — WARN без rethrow.
                try
                {
                    AnyPropertyId oId = CreateAnyPropertyIdFromNumber(20202);
                    PropertyValue oPV = oFunc.Properties[oId];
                    oPV[nIdx] = strFormat;   // implicit (KB op_Implicit)
                    oFunc.Properties[oId] = oPV;
                    log.Log("[INFO] [BLOCKFMT] путь (a) не удался (" + strErrA +
                        "), записано через PropertyValue-индексатор");
                }
                catch (Exception oEx)
                {
                    log.Warn("[BLOCKFMT] запись 20202[" + nIdx + "] не удалась (" +
                        oEx.GetType().Name + ": " + oEx.Message + ")");
                }
            }
            // Readback: 20202[nIdx] → INFO (до 60 симв; «—» при отказе/пустом).
            try
            {
                AnyPropertyId oIdR = CreateAnyPropertyIdFromNumber(20202);
                PropertyValue oVal = oFunc.Properties[oIdR];
                // rev.16.0 fix: MaxIndex в 2.9 НЕТ (пример KB — другой API) —
                // недопустимый индекс даст исключение → catch → «—».
                string strRb = (oVal != null && !oVal.IsEmpty)
                    ? oVal[nIdx].ToString() : "—";
                log.Log("[INFO] [BLOCKFMT] 20202[" + nIdx + "]='" + Trim60(strRb) + "'");
            }
            catch
            {
                log.Log("[INFO] [BLOCKFMT] 20202[" + nIdx + "]='—'");
            }
        }

        /// <summary>Обрезка строки до 60 символов (readback-лог); null → «—».</summary>
        private static string Trim60(string s)
        {
            if (s == null) return "—";
            return s.Length <= 60 ? s : s.Substring(0, 60);
        }

        /// <summary>Запись ОУ символа — rev.11.15: NameParts-структуры
        /// (стабильный путь rev.11.4–11.11) плюс имя устройства как ДВЕ
        /// части имени; структурные блоки — через WriteStructureSegments
        /// ×4 (главный + подчинённые base+1..base+9 по точкам:
        /// 1100/1400/1200/1600, обобщение rev.11.14). KB API 2.9, авторитетные Remarks: FUNC_CODE
        /// #20013 (буква) и FUNC_COUNTER #20014 (счётчик) — «This
        /// property is used as part of a name. In order to set it, member
        /// NameParts must be used» — запись тем же индексатором
        /// Property(AnyPropertyId) {set}, что и структуры. Это объясняет
        /// прошлые неудачи: имя — не структурный блок, в NameParts
        /// писались только DESIGNATION_*; эксперимент rev.11.10 «полный
        /// DT одной строкой Name» рвал структуры на '-' (откат
        /// rev.11.11), 1800 read-only S063113 (rev.11.6). Поток: [SYMDT]
        /// дамп источника (ParseDeviceTag; отсутствующая часть — «—») →
        /// разбор имени SplitDeviceTagLetterCounter ('K140' → 'K'+'140';
        /// без цифр — всё в букву) → offline-список
        /// FunctionBasePropertyList (отказ — WARN [SYMDT]) +
        /// WriteStructureSegments×4 (главный + подчинённые по точкам:
        /// 1100←установка, 1400←место сборки, 1200←место установки,
        /// 1600←структура) + SetNamePart×2
        /// (20013←буква, 20014←счётчик; пустая
        /// часть — без записи; 1800 исключён из списка rev.13.0 — платформа
        /// его отбрасывает, имя несут 20013/20014) →
        /// [SYMDT-RD] readback ReadBackNamePart×6 + первые
        /// подчинённые ×4 (1101/1401/1201/1601; пустые не читаются) →
        /// финальный Name-readback [SYMDT-RD] — арбитр успеха → rev.12.8 (проба
        /// SetFullNameAndAdjustVisibleName 3-арг=false на всех символах)
        /// → rev.12.9: шаг NameService.AdjustVisibleName(oPage, oFunc) ПОСЛЕ
        /// записи полного (ok — INFO [SYMDT-AVN], false/исключение —
        /// WARN [SYMDT-AVN]) → rev.13.0 (пример пользователя): ПЕРВИЧНЫМ путём
        /// — 2-арг SetFullNameAndAdjustVisibleName(oFunc, oParts) с
        /// предварительным oNames.Page = oPage, список БЕЗ 1800; true —
        /// полное+видимое поставлены сервисом (присваивание/AVN пропускаются);
        /// false/исключение — WARN [SYMDT-NSS] → rev.11.15-присваивание + AVN →
        /// readback + [SYMDT-RD] 20002 (видимое). Цепочка механизмов различима
        /// по токенам: [SYMDT] запись ОУ: NameService 2-arg / запись NameParts:
        /// offline-список / [SYMDT-AVN].</summary>
        private static void WriteDeviceTagProperties(Page oPage, Function oFunc,
            CableSymbolPlacement oSym, DiagnosticLogger log)
        {
            string[] arrParts = ParseDeviceTag(oSym.CableName);
            string strInstallation = arrParts[0];
            string strMountingSite = arrParts[1];
            string strPlaceOfInstallation = arrParts[2];
            string strUserStruct = arrParts[3];
            string strName = arrParts[4];

            // Дамп ИСТОЧНИКА (полный DT кабеля из DataModel), не запись.
            log.Log("[INFO] [SYMDT] '" + oSym.CableName + "': =" +
                (strInstallation ?? "—") + " ++" + (strMountingSite ?? "—") + " +" +
                (strPlaceOfInstallation ?? "—") + " #" + (strUserStruct ?? "—") +
                " имя=" + (strName ?? "—"));

            // rev.11.13: имя устройства ('K140') — НЕ структурный блок, а ДВЕ части
            // имени в NameParts: FUNC_CODE #20013 (буква 'K') и FUNC_COUNTER #20014
            // (счётчик '140'). KB Remarks: «In order to set it, member NameParts
            // must be used». Разбор: буква — ведущие нецифровые, счётчик — хвост
            // (цифры). Без цифр — всё в букву, счётчик пропускается.
            string strCode, strCounter;
            SplitDeviceTagLetterCounter(strName, out strCode, out strCounter);

            // rev.11.15: сегменты всех структурных блоков (главный +
            // подчинённые по точкам) — для записи WriteStructureSegments
            // и readback первых подчинённых (1101/1401/1201/1601).
            string[] arrInstallSegs = SplitSegments(strInstallation);
            string[] arrMountSegs = SplitSegments(strMountingSite);
            string[] arrPlaceSegs = SplitSegments(strPlaceOfInstallation);
            string[] arrUserSegs = SplitSegments(strUserStruct);

            // rev.11.11: структуры ОУ — offline-список NameParts +
            // присваивание (стабильный путь rev.11.4–11.9). Пустая/null
            // часть — без записи (SetNamePart → true), отказ части —
            // WARN [SYMDT], остальные продолжают писаться.
            FunctionBasePropertyList oParts = null;
            try
            {
                oParts = new FunctionBasePropertyList();
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMDT] offline-список NameParts: " +
                    oEx.GetType().Name + ": " + oEx.Message);
            }
            // rev.13.0: 1800 (PRODUCT) из offline-списка ИСКЛЮЧЁН — платформа
            // его отбрасывает (p.58), имя несут 20013/20014; в списке для
            // NameService это подозреваемый «problem» (пример-референс:
            // DESIGNATION_PLANT/LOCATION + FUNC_CODE/COUNTER, без 1800).
            // Заодно и для fallback-присваивания: rev.11.15 Name собирается
            // без 1800 (readback 1800 и так был недоступен).
            bool bPlant = false;
            bool bLocation = false;
            bool bPlace = false;
            bool bUserStruct = false;
            bool bCode = false;
            bool bCounter = false;
            if (oParts != null)
            {
                bPlant = WriteStructureSegments(oParts, 1100,
                    arrInstallSegs, strInstallation, log);
                bLocation = WriteStructureSegments(oParts, 1400,
                    arrMountSegs, strMountingSite, log);
                bPlace = WriteStructureSegments(oParts, 1200,
                    arrPlaceSegs, strPlaceOfInstallation, log);
                bUserStruct = WriteStructureSegments(oParts, 1600,
                    arrUserSegs, strUserStruct, log);
                bCode = SetNamePart(oParts, 20013, strCode, log);
                bCounter = SetNamePart(oParts, 20014, strCounter, log);
            }
            bool bOfflineOk = bPlant && bLocation && bPlace && bUserStruct &&
                bCode && bCounter && oParts != null;
            // rev.13.0 (пример пользователя, KB topic2726): первичный путь —
            // 2-арг. перегрузка SetFullNameAndAdjustVisibleName(FunctionBase,
            // UniversalPropertyList) с ПРЕДВАРИТЕЛЬНО выставленным oNames.Page
            // (docs: ApplicationException «when page is not set»). Отличия от
            // провалившейся rev.12.8 (3-арг, false×4): список без 1800, вызов
            // 2-арг + Page сервиса. true — сервис сам поставил полное+видимое
            // (присваивание и AVN НЕ выполняются); false/исключение — WARN
            // [SYMDT-NSS] → rev.11.15-присваивание (fallback) → AVN-шаг.
            bool bServiceOk = false;
            bool bServiceThrew = false;
            if (bOfflineOk)
            {
                try
                {
                    NameService oNamesSvc = new NameService();
                    oNamesSvc.Page = oPage;
                    bServiceOk = oNamesSvc.SetFullNameAndAdjustVisibleName(oFunc, oParts);
                }
                catch (Exception oEx)
                {
                    bServiceThrew = true;
                    // throw: «no changes» документирован только для false; fail-safe —
                    // следующий шаг oFunc.NameParts = oParts заменяет список целиком.
                    log.Warn("[SYMDT-NSS] SetFullNameAndAdjustVisibleName(2-arg) бросил " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
                if (bServiceOk)
                    log.Log("[INFO] [SYMDT] запись ОУ: NameService 2-arg (полное + видимое)");
                else if (!bServiceThrew)
                    log.Warn("[SYMDT-NSS] SetFullNameAndAdjustVisibleName(2-arg) вернул " +
                        "false — откат: присваивание NameParts (rev.11.15)");
            }
            if (!bServiceOk)
            {
                if (bOfflineOk)
                {
                    try
                    {
                        oFunc.NameParts = oParts;
                        log.Log("[INFO] [SYMDT] запись NameParts: offline-список");
                    }
                    catch (Exception oEx)
                    {
                        log.Warn("[SYMDT] NameParts (offline-список): " +
                            oEx.GetType().Name + ": " + oEx.Message);
                        NamePartsGetModifySet(oFunc, strInstallation, strMountingSite,
                            strPlaceOfInstallation, strUserStruct, strCode,
                            strCounter, log);
                    }
                }
                else
                {
                    NamePartsGetModifySet(oFunc, strInstallation, strMountingSite,
                        strPlaceOfInstallation, strUserStruct, strCode,
                        strCounter, log);
                }

                // rev.12.9: видимое ОУ — NameService.AdjustVisibleName из уже
                // сохранённого полного имени (KB: «evaluates the visible name ...
                // from the fullname ... and sets these evaluated values»; false —
                // «could not be evaluated and set due to nesting»). Отказ — WARN
                // [SYMDT-AVN], не прерывает (полное ОУ уже записано); критерий —
                // readback [SYMDT-RD] 20002.
                try
                {
                    NameService oNames = new NameService();
                    if (oNames.AdjustVisibleName(oPage, oFunc))
                        log.Log("[INFO] [SYMDT-AVN] AdjustVisibleName ok (видимое вычислено из полного)");
                    else
                        log.Warn("[SYMDT-AVN] AdjustVisibleName вернул false (docs: «could not be evaluated" +
                            " and set due to nesting»; символ не-главный 20122=false — кандидат на nesting)");
                }
                catch (Exception oEx)
                {
                    log.Warn("[SYMDT-AVN] AdjustVisibleName бросил " +
                        oEx.GetType().Name + ": " + oEx.Message);
                }
            }

            // Readback [SYMDT-RD]: свежий get NameParts, по одной части
            // (rev.11.4–11.9): пустая/null часть не писалась — не читается;
            // отказ чтения — «недоступен» с типом исключения.
            try
            {
                FunctionBasePropertyList oRd = oFunc.NameParts;
                ReadBackNamePart(oRd, log, 1100, strInstallation);
                ReadBackNamePart(oRd, log, 1400, strMountingSite);
                ReadBackNamePart(oRd, log, 1200, strPlaceOfInstallation);
                ReadBackNamePart(oRd, log, 1600, strUserStruct);
                ReadBackNamePart(oRd, log, 20013, strCode);
                ReadBackNamePart(oRd, log, 20014, strCounter);
                if (arrInstallSegs.Length > 1)
                    ReadBackNamePart(oRd, log, 1101, arrInstallSegs[1]);
                if (arrMountSegs.Length > 1)
                    ReadBackNamePart(oRd, log, 1401, arrMountSegs[1]);
                if (arrPlaceSegs.Length > 1)
                    ReadBackNamePart(oRd, log, 1201, arrPlaceSegs[1]);
                if (arrUserSegs.Length > 1)
                    ReadBackNamePart(oRd, log, 1601, arrUserSegs[1]);
            }
            catch (Exception oEx)
            {
                log.Log("[INFO] [SYMDT-RD] NameParts недоступен (" + oEx.GetType().Name + ")");
            }

            // Финальный readback Name — собранное EPLAN полное ОУ
            // (главный критерий успеха: платформа пере-собирает части,
            // включая имя устройства rev.11.13).
            try
            {
                log.Log("[INFO] [SYMDT-RD] Name='" + oFunc.Name + "'");
            }
            catch (Exception oEx)
            {
                log.Log("[INFO] [SYMDT-RD] Name недоступен (" + oEx.GetType().Name + ")");
            }

            // rev.12.8: видимое ОУ #20002 (FUNC_VISIBLENAME, read-only) — online
            // чтение; пустое видимое → EmptyPropertyException («недоступен») =
            // критерий провала adjust.
            try
            {
                AnyPropertyId oIdVis = CreateAnyPropertyIdFromNumber(20002);
                if (oIdVis == null)
                    throw new InvalidOperationException("CreateAnyPropertyIdFromNumber(20002) вернул null");
                string strVis = oFunc.Properties[oIdVis];
                log.Log("[INFO] [SYMDT-RD] 20002='" + (strVis ?? "—") + "'");
            }
            catch (Exception oEx)
            {
                log.Log("[INFO] [SYMDT-RD] 20002 недоступен (" + oEx.GetType().Name + ")");
            }
        }

        /// <summary>Разбор имени устройства на букву и счётчик (rev.11.13):
        /// FUNC_CODE #20013 — ведущие нецифровые символы ('K' из 'K140'),
        /// FUNC_COUNTER #20014 — хвост из цифр ('140'). Без цифр — всё в
        /// букву, счётчик пропускается (null). Пустое/null имя — обе
        /// части null.</summary>
        private static void SplitDeviceTagLetterCounter(string strName, out string strCode, out string strCounter)
        {
            strCode = null; strCounter = null;
            if (string.IsNullOrEmpty(strName)) return;
            int i = 0;
            while (i < strName.Length && !char.IsDigit(strName[i])) i++;
            strCode = i > 0 ? strName.Substring(0, i) : null;
            strCounter = i < strName.Length ? strName.Substring(i) : null;
            if (string.IsNullOrEmpty(strCode)) strCode = null;
            if (string.IsNullOrEmpty(strCounter)) strCounter = null;
        }

        /// <summary>Fallback get-modify-set на живом oFunc.NameParts
        /// (rev.11.4–11.9; возвращён rev.11.11; rev.11.13 — плюс части
        /// имени 20013/20014; rev.11.15 — четыре структурных блока через
        /// WriteStructureSegments: главный + подчинённые по точкам;
        /// сигнатура — на сырых строках, сплит внутри): get, допись всех
        /// НЕпустых частей поверх прочитанного (безусловно — независимо
        /// от результата offline-списка), счётчик nSet, INFO
        /// «get-modify-set (дописано частей: N)». Каждая часть —
        /// SetNamePart (отказ — WARN [SYMDT], остальные продолжают
        /// писаться).</summary>
        private static void NamePartsGetModifySet(Function oFunc,
            string strInstallation, string strMountingSite,
            string strPlaceOfInstallation, string strUserStruct,
            string strCode, string strCounter,
            DiagnosticLogger log)
        {
            try
            {
                FunctionBasePropertyList oLive = oFunc.NameParts;
                int nSet = 0;
                if (oLive != null)
                {
                    if (WriteStructureSegments(oLive, 1100,
                        SplitSegments(strInstallation), strInstallation, log)) nSet++;
                    if (WriteStructureSegments(oLive, 1400,
                        SplitSegments(strMountingSite), strMountingSite, log)) nSet++;
                    if (WriteStructureSegments(oLive, 1200,
                        SplitSegments(strPlaceOfInstallation), strPlaceOfInstallation, log)) nSet++;
                    if (WriteStructureSegments(oLive, 1600,
                        SplitSegments(strUserStruct), strUserStruct, log)) nSet++;
                    // rev.13.0: 1800 (имя) не пишем — платформа отбрасывает,
                    // имя несут 20013/20014.
                    if (SetNamePart(oLive, 20013, strCode, log)) nSet++;
                    if (SetNamePart(oLive, 20014, strCounter, log)) nSet++;
                }
                log.Log("[INFO] [SYMDT] get-modify-set (дописано частей: " +
                    nSet.ToString(CultureInfo.InvariantCulture) + ")");
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMDT] get-modify-set: " + oEx.GetType().Name + ": " + oEx.Message);
            }
        }

        /// <summary>Запись одной части DT индексатором NameParts по базовому
        /// номеру свойства (rev.11.4; rev.11.10 не вызывалась — NameParts-путь
        /// был удалён на время эксперимента «полный DT одной строкой»; в
        /// rev.11.11 возвращена в строй — offline-список и fallback
        /// get-modify-set; superseded typed-аксессоры rev.11.3:
        /// DESIGNATION_FULL* — вычисляемые FULL*-свойства 1120/1220/1420/1620/
        /// 1820, read-only — значения молча не сохранялись). Индексатор
        /// Property(AnyPropertyId) {set} создаёт записи (паттерн 20122).
        /// Пустая/null часть — без записи (true). Отказ — WARN [SYMDT]
        /// «свойство <номер> ('<часть>'): <тип>: <сообщение>», false; остальные
        /// части продолжают писаться.</summary>
        private static bool SetNamePart(FunctionBasePropertyList oParts, int nLabel,
            string strPart, DiagnosticLogger log)
        {
            if (string.IsNullOrEmpty(strPart)) return true;
            try
            {
                AnyPropertyId oIdPart = CreateAnyPropertyIdFromNumber(nLabel);
                if (oIdPart == null)
                    throw new InvalidOperationException(
                        "CreateAnyPropertyIdFromNumber(" +
                        nLabel.ToString(CultureInfo.InvariantCulture) + ") вернул null");
                oParts[oIdPart] = (PropertyValue)strPart;
                return true;
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMDT] свойство " +
                    nLabel.ToString(CultureInfo.InvariantCulture) + " ('" + strPart + "'): " +
                    oEx.GetType().Name + ": " + oEx.Message);
                return false;
            }
        }

        /// <summary>Разбор структурного значения на сегменты по точкам
        /// (rev.11.15): 'HII-1.1' → 'HII-1' + '1'. Пустое/null —
        /// пустой массив.</summary>
        private static string[] SplitSegments(string strValue)
        {
            if (string.IsNullOrEmpty(strValue)) return new string[0];
            return strValue.Split('.');
        }

        /// <summary>Запись структурного блока (rev.11.15, обобщение rev.11.14):
        /// 'HII-1.1' / 'М1.2' / '3.1' — это ГЛАВНЫЙ идентификатор (nBaseId:
        /// 1100/1400/1200/1600) + цепочка подчинённых (nBaseId+1..nBaseId+9),
        /// разбор по точкам. Эталон [SRC-DT]: 1100='HII-1'. Запись одной строкой
        /// в главный давала другой узел дерева структуры. Пустое значение — true
        /// без записи. >10 сегментов — WARN, лишние пропускаются
        /// (strValue — исходная строка для WARN-текста).</summary>
        private static bool WriteStructureSegments(FunctionBasePropertyList oParts,
            int nBaseId, string[] arrSegments, string strValue, DiagnosticLogger log)
        {
            bool bOk = true;
            if (arrSegments == null || arrSegments.Length == 0) return true;
            for (int i = 0; i < arrSegments.Length && i < 10; i++)
            {
                bool bSeg = SetNamePart(oParts, nBaseId + i, arrSegments[i], log);
                bOk = bOk && bSeg;
            }
            if (arrSegments.Length > 10)
                log.Warn("[SYMDT] '" + strValue + "' (базовый " +
                    nBaseId.ToString(CultureInfo.InvariantCulture) + "): сегментов " +
                    arrSegments.Length.ToString(CultureInfo.InvariantCulture) +
                    " (>10: главный + 9 подчинённых) — лишние пропущены");
            return bOk;
        }

        /// <summary>Readback записанной части DT через NameParts (rev.11.4,
        /// superseded typed-аксессоры rev.11.3 — read-only FULL*): чтение
        /// индексатором по базовому номеру, явное присваивание
        /// PropertyValue→string (тот же неявный оператор, что использовали
        /// typed-аксессоры). Пустая/null часть не писалась — не
        /// читается. Null-значение или отказ чтения — [SYMDT-RD] «недоступен»
        /// с типом исключения, по образцу существующего кода.</summary>
        private static void ReadBackNamePart(FunctionBasePropertyList oParts,
            DiagnosticLogger log, int nLabel, string strPart)
        {
            if (string.IsNullOrEmpty(strPart)) return;
            try
            {
                AnyPropertyId oIdPart = CreateAnyPropertyIdFromNumber(nLabel);
                if (oIdPart == null)
                    throw new InvalidOperationException(
                        "CreateAnyPropertyIdFromNumber(" +
                        nLabel.ToString(CultureInfo.InvariantCulture) + ") вернул null");
                string strBack = oParts[oIdPart];
                if (strBack == null)
                    log.Log("[INFO] [SYMDT-RD] " +
                        nLabel.ToString(CultureInfo.InvariantCulture) + " недоступен (null)");
                else
                    log.Log("[INFO] [SYMDT-RD] " +
                        nLabel.ToString(CultureInfo.InvariantCulture) + "='" + strBack + "'");
            }
            catch (Exception oEx)
            {
                log.Log("[INFO] [SYMDT-RD] " +
                    nLabel.ToString(CultureInfo.InvariantCulture) + " недоступен (" +
                    oEx.GetType().Name + ")");
            }
        }

        /// <summary>Разбор полного DT кабеля на структурные части (spec §9.5).
        /// Формат: =<установка>++<место сборки>+<место установки>#<опред. структура>-<имя>.
        /// Возвращает 5 строк: installation / mountingSite / placeOfInstallation /
        /// userStruct / name; null = блок отсутствует (пустой блок приравнен к
        /// отсутствующему). Место установки (между '+' и '#'/'-') разбирается и
        /// пишется в свойство 1200, если непусто (rev.11.5: слоты по факту
        /// прогона — «+» это 1200, rev.11.4 ошибочно писало в 1400; rev.11.2
        /// ошибочно именовался 1420 — это read-only FULL-агрегат). Любая аномалия (нет
        /// маркеров) — возвращается что разобрано, остальное null.</summary>
        private static string[] ParseDeviceTag(string strFullName)
        {
            string strInstallation = null;
            string strMountingSite = null;
            string strPlaceOfInstallation = null;
            string strUserStruct = null;
            string strName = null;

            if (string.IsNullOrEmpty(strFullName))
                return new string[] { null, null, null, null, null };

            string s = strFullName.StartsWith("=", StringComparison.Ordinal)
                ? strFullName.Substring(1) : strFullName;

            // Установка — до "++". Без "++" установка/места/структура отсутствуют:
            // имя — хвост после последнего '-'.
            int nInstallEnd = s.IndexOf("++", StringComparison.Ordinal);
            if (nInstallEnd < 0)
            {
                int nDash = s.LastIndexOf('-');
                if (nDash >= 0 && nDash < s.Length - 1)
                    strName = NullIfEmpty(s.Substring(nDash + 1));
                return new string[] { null, null, null, null, strName };
            }
            strInstallation = NullIfEmpty(s.Substring(0, nInstallEnd));

            // Место сборки — после "++" до следующего '+'. Без '+' весь хвост — место сборки.
            string strTail = s.Substring(nInstallEnd + 2);
            int nMountEnd = strTail.IndexOf('+');
            if (nMountEnd < 0)
                return new string[] { strInstallation, NullIfEmpty(strTail), null, null, null };
            strMountingSite = NullIfEmpty(strTail.Substring(0, nMountEnd));

            // Место установки — после '+' до '#' или '-'; пишется в 1200, если непусто (rev.11.5).
            string strTail2 = strTail.Substring(nMountEnd + 1);
            int nHash = strTail2.IndexOf('#');
            if (nHash < 0)
            {
                // Без '#': место установки — до первого '-'; имя — хвост после '-'.
                // Нет и '-': формат аномален (нет разделителя имени) — хвост
                // трактуем как ИМЯ (именной блок обязателен), место установки — null.
                int nDash = strTail2.IndexOf('-');
                if (nDash < 0)
                {
                    strName = NullIfEmpty(strTail2);
                    return new string[] { strInstallation, strMountingSite, null, null, strName };
                }
                strPlaceOfInstallation = NullIfEmpty(strTail2.Substring(0, nDash));
                if (nDash < strTail2.Length - 1)
                    strName = NullIfEmpty(strTail2.Substring(nDash + 1));
                return new string[] { strInstallation, strMountingSite, strPlaceOfInstallation, null, strName };
            }
            strPlaceOfInstallation = NullIfEmpty(strTail2.Substring(0, nHash));
            string strTail3 = strTail2.Substring(nHash + 1);

            // Определяющая структура — после '#' до '-'; имя — после этого '-'.
            int nStructEnd = strTail3.IndexOf('-');
            if (nStructEnd < 0)
                return new string[] { strInstallation, strMountingSite, strPlaceOfInstallation, NullIfEmpty(strTail3), null };
            strUserStruct = NullIfEmpty(strTail3.Substring(0, nStructEnd));
            if (nStructEnd < strTail3.Length - 1)
                strName = NullIfEmpty(strTail3.Substring(nStructEnd + 1));

            return new string[] { strInstallation, strMountingSite, strPlaceOfInstallation, strUserStruct, strName };
        }

        /// <summary>Пустой блок == отсутствующий: пустая строка нормализуется в null.</summary>
        private static string NullIfEmpty(string strValue)
        {
            if (string.IsNullOrEmpty(strValue)) return null;
            return strValue;
        }

        /// <summary>Создаёт AnyPropertyId из номера свойства. По документации API 2.9
        /// конвертация есть ИЗ номера (op_Implicit Int32 -> AnyPropertyId). Оператор
        /// вызывается через reflection (порт из spike rev.7), чтобы сборка не зависела
        /// от точной сигнатуры оператора. Не удалось — null (логирует вызывающий).
        /// internal с rev.11.5: используется также дампом [SRC-DT] в
        /// EplanTerminalStripReader.</summary>
        internal static AnyPropertyId CreateAnyPropertyIdFromNumber(int nNumber)
        {
            try
            {
                MethodInfo[] arrMethods =
                    typeof(AnyPropertyId).GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (MethodInfo oMethod in arrMethods)
                {
                    if ((oMethod.Name != "op_Implicit" && oMethod.Name != "op_Explicit") ||
                        oMethod.ReturnType != typeof(AnyPropertyId))
                        continue;
                    ParameterInfo[] arrParams = oMethod.GetParameters();
                    if (arrParams.Length != 1 || arrParams[0].ParameterType != typeof(int)) continue;

                    object oResult = oMethod.Invoke(null, new object[] { nNumber });
                    if (oResult is AnyPropertyId) return (AnyPropertyId)oResult;
                }
            }
            catch { }
            return null;
        }
    }
}
