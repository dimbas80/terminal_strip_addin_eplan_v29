using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>Вставка символов кабеля (Фаза G, spec
    /// docs/superpowers/specs/2026-09-22-fase-g-graphics-design.md; мастер-план §13 —
    /// Этап 9). KB 2.9 (проверено 22.09.2026): SymbolLibrary(Project, String) →
    /// Symbol = oLibrary[имя] → SymbolVariant = oSymbol[индекс]; Create —
    /// ИНСТАНСНЫЙ (public virtual void Create(Page, SymbolVariant)); позиция —
    /// Placement.Location (get/set). Библиотека/имя/вариант — из AddInConfiguration
    /// (в Фазе H — выбор пользователя в UI). rev.11.1: символы вставляются как
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
    /// Поворот 0° (spec §7). Центр круга
    /// = точке вставки (rev.10.4: офсет п.47 опровергнут — замер был загрязнён
    /// останцами старых прогонов). Отказ —
    /// WARN [SYMBOL]. НЕ идемпотентно (очистка — Фаза I).</summary>
    public static class CableSymbolCreator
    {
        /// <summary>Символ на каждый CableSymbolPlacement. Возвращает число созданных.
        /// Библиотека/символ/вариант недоступны — один WARN, 0 созданных.</summary>
        public static int CreateSymbols(Page oPage, CableGeometryResult oGeom,
            DiagnosticLogger log)
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
                SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project, AddInConfiguration.SymbolLibrary);
                Symbol oSymbol = oLibrary[AddInConfiguration.SymbolName];
                oVariant = oSymbol[AddInConfiguration.SymbolVariant];
            }
            catch (Exception oEx)
            {
                log.Warn("[SYMBOL] вариант '" + AddInConfiguration.SymbolLibrary + "'/" +
                    AddInConfiguration.SymbolName + "/" + AddInConfiguration.SymbolVariant +
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
                    // (круг ушёл 1:1 с точкой вставки): центр круга = точке вставки.
                    // Пишем геометрическую позицию напрямую (замер п.47 был загрязнён
                    // останцами старых прогонов — аддин неидемпотентен).
                    oFunc.Location = new PointD(oSym.Position.X, oSym.Position.Y);
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
                        WriteDeviceTagProperties(oFunc, oSym, log);
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
        /// 1600←структура) + SetNamePart×3
        /// (1800←имя, 20013←буква, 20014←счётчик; пустая
        /// часть — без записи) → присваивание oFunc.NameParts = oParts
        /// (INFO; отказ — WARN + fallback) → fallback get-modify-set на
        /// живом NameParts (все непустые части, счётчик, INFO «дописано
        /// частей: N») → [SYMDT-RD] readback ReadBackNamePart×7 + первые
        /// подчинённые ×4 (1101/1401/1201/1601; пустые не читаются) →
        /// финальный Name-readback [SYMDT-RD] — арбитр успеха.</summary>
        private static void WriteDeviceTagProperties(Function oFunc,
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
            bool bPlant = false;
            bool bLocation = false;
            bool bPlace = false;
            bool bUserStruct = false;
            bool bProduct = false;
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
                bProduct = SetNamePart(oParts, 1800, strName, log);
                bCode = SetNamePart(oParts, 20013, strCode, log);
                bCounter = SetNamePart(oParts, 20014, strCounter, log);
            }
            bool bOfflineOk = bPlant && bLocation && bPlace && bUserStruct &&
                bProduct && bCode && bCounter && oParts != null;
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
                        strPlaceOfInstallation, strUserStruct, strName, strCode,
                        strCounter, log);
                }
            }
            else
            {
                NamePartsGetModifySet(oFunc, strInstallation, strMountingSite,
                    strPlaceOfInstallation, strUserStruct, strName, strCode,
                    strCounter, log);
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
                ReadBackNamePart(oRd, log, 1800, strName);
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
            string strName, string strCode, string strCounter,
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
                    if (SetNamePart(oLive, 1800, strName, log)) nSet++;
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
