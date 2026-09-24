using System;
using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;

namespace MyEplanActions
{
    /// <summary>Диагностическая проба [SYMBOX] (rev.10.6, Фаза G — подготовка Фазы H):
    /// размеры символов кабеля через API 2.9 (KB www.eplan.help, проверено 22.09.2026).
    /// Для двух вариантов — рабочего SPECIAL/CABDCP2/0 (аддин, прогоны rev.10.0–10.5)
    /// и кандидата GOST_single_symbol/K/0 (Фаза H: выбор символа пользователем в UI) —
    /// проба отвечает: (а) отдаёт ли GetBoundingBox() размер на библиотечном
    /// SymbolVariant вне страницы (KB: «In order to use function GetBoundingBox
    /// property Placement.Page can't be null» — на библиотечном варианте ожидаем
    /// отказ, логируем как INFO); (б) какие W×H у размещённого символа — KB:
    /// GetBoundingBox возвращает PointD[] ровно из 2 точек, [0] — левый-нижний,
    /// [1] — правый-верхний; (в) совпадает ли центр bbox с точкой вставки
    /// (п.48 summary: для CABDCP2 «центр = точке вставки» — подтверждение на
    /// чистой странице; замер п.47 был опровергнут из-за останцев);
    /// (г) есть ли GetLogicalArea() (KB: NotImplementedException, если логической
    /// области нет — macro box, cable definition line и т.п.; для символов кабеля
    /// ожидаемо нет). Резолв варианта и вставка — паттерн CableSymbolCreator.
    /// Каждый замер — в отдельном try/catch: отказ одного не роняет остальные.
    /// Символы вставляются у пробной точки и СРАЗУ удаляются (урок п.48: аддин
    /// неидемпотентен — останцы искажают будущие замеры). Проба — только
    /// диагностика: геометрию, пайплайн и счётчики этапов 1–5 не трогает.</summary>
    public static class SymbolBoxProbe
    {
        /// <summary>Прогон обеих проб. Страница/проект отсутствуют — WARN и выход.
        /// По каждой пробе: резолв варианта, бокс на библиотечном SymbolVariant,
        /// вставка у пробной точки, замеры Location/GetBoundingBox/GetLogicalArea,
        /// обязательный Remove в finally, итоговая строка [SYMBOX]. В конце —
        /// свод [SYMBOX-SUM].</summary>
        public static void Probe(Page oPage, Project oProject, DiagnosticLogger log)
        {
            if (oPage == null || oProject == null)
            {
                log.Warn("[SYMBOX] Probe: page или project == null — проба не выполняется");
                return;
            }

            // Список проб: [0] — рабочий символ аддина, [1] — кандидат Фазы H
            // (индексы трёх массивов согласованы).
            string[] arrLibs = new string[]
            {
                AddInConfiguration.SymbolLibrary,
                AddInConfiguration.SymbolProbeLibrary
            };
            string[] arrNames = new string[]
            {
                AddInConfiguration.SymbolName,
                AddInConfiguration.SymbolProbeName
            };
            int[] arrVariants = new int[]
            {
                AddInConfiguration.SymbolVariant,
                AddInConfiguration.SymbolProbeVariant
            };

            log.Log("[INFO] --- Проба [SYMBOX]: размеры символов (GetBoundingBox/GetLogicalArea) ---");

            int nInserted = 0;
            int nRemoved = 0;
            int nFailed = 0;
            for (int i = 0; i < arrLibs.Length; i++)
            {
                string strId = "'" + arrLibs[i] + "/" + arrNames[i] + "/" + arrVariants[i] + "'";

                // (а) Резолв варианта — паттерн CableSymbolCreator:
                // SymbolLibrary(проект, имя библиотеки) → oLibrary[имя] → oSymbol[индекс].
                SymbolVariant oVariant;
                try
                {
                    SymbolLibrary oLibrary = new SymbolLibrary(oPage.Project, arrLibs[i]);
                    Symbol oSymbol = oLibrary[arrNames[i]];
                    oVariant = oSymbol[arrVariants[i]];
                }
                catch (Exception oEx)
                {
                    log.Warn("[SYMBOX] вариант " + strId + " недоступен: " +
                        oEx.GetType().Name + ": " + oEx.Message);
                    nFailed++;
                    continue;
                }

                // (б) Библиотечный уровень: SymbolVariant вне страницы
                // (Placement.Page == null) — по KB GetBoundingBox ожидаемо бросает
                // (BaseException/InvalidOperationException). Ожидаемый исход — INFO,
                // не WARN (не засоряем WARN-бюджет).
                try
                {
                    PointD[] arrLibBox = oVariant.GetBoundingBox();
                    if (arrLibBox != null && arrLibBox.Length >= 2)
                    {
                        log.Log("[INFO] [SYMBOX] LIB " + strId + ": вне страницы бокс ДОСТУПЕН: LL=(" +
                            Fmt(arrLibBox[0].X) + ";" + Fmt(arrLibBox[0].Y) + "), UR=(" +
                            Fmt(arrLibBox[1].X) + ";" + Fmt(arrLibBox[1].Y) + ") — KB ожидал отказ (Page == null)");
                    }
                    else
                    {
                        log.Log("[INFO] [SYMBOX] LIB " + strId + ": GetBoundingBox вернул " +
                            (arrLibBox == null ? "null" : arrLibBox.Length.ToString(CultureInfo.InvariantCulture) + " точек"));
                    }
                }
                catch (Exception oEx)
                {
                    log.Log("[INFO] [SYMBOX] LIB " + strId + ": вне страницы бокс недоступен: " +
                        oEx.GetType().Name + ": " + oEx.Message + " (Page == null — ожидаемо по KB)");
                }

                // (в) Размещённый уровень: вставка у пробной точки; шаг 20 мм по
                // вертикали между пробами — страховка на случай провала Remove
                // (останец не должен мешать замеру следующей пробы).
                double dX = AddInConfiguration.SymbolProbeX;
                double dY = AddInConfiguration.SymbolProbeY - i * 20.0;

                SymbolReference oRef = null;
                // Накопленные значения для итоговой строки пробы (г).
                string strBbox = "n/a";
                string strDelta = "n/a";
                string strLogical = "n/a";
                try
                {
                    oRef = new SymbolReference();
                    oRef.Create(oPage, oVariant);
                    nInserted++;
                    oRef.Location = new PointD(dX, dY);
                    log.Log("[INFO] [SYMBOX] " + strId + " вставлен @ (" + Fmt(dX) + ";" + Fmt(dY) + ")");

                    // Замер 1: Location считывается обратно (если чтение не удастся,
                    // Δ центра считается от номинальной точки вставки).
                    double dLocX = dX;
                    double dLocY = dY;
                    try
                    {
                        PointD oLoc = oRef.Location;
                        dLocX = oLoc.X;
                        dLocY = oLoc.Y;
                        log.Log("[INFO] [SYMBOX] Location считан: (" + Fmt(dLocX) + ";" + Fmt(dLocY) + ")");
                    }
                    catch (Exception oEx)
                    {
                        log.Warn("[SYMBOX] Location бросил " + oEx.GetType().Name + ": " + oEx.Message +
                            " — Δ центра считается от номинальной точки вставки");
                    }

                    // Замер 2: GetBoundingBox размещённого символа (KB: ровно 2 точки —
                    // [0] левый-нижний, [1] правый-верхний; базовая точка может кинуть
                    // BaseException/InvalidOperationException).
                    try
                    {
                        PointD[] arrBox = oRef.GetBoundingBox();
                        if (arrBox != null && arrBox.Length >= 2)
                        {
                            PointD oLL = arrBox[0];
                            PointD oUR = arrBox[1];
                            double dW = oUR.X - oLL.X;
                            double dH = oUR.Y - oLL.Y;
                            double dCenterX = (oLL.X + oUR.X) / 2.0;
                            double dCenterY = (oLL.Y + oUR.Y) / 2.0;
                            strBbox = "W=" + Fmt(dW) + "×H=" + Fmt(dH) + " (LL=(" + Fmt(oLL.X) + ";" +
                                Fmt(oLL.Y) + ")..UR=(" + Fmt(oUR.X) + ";" + Fmt(oUR.Y) + "))";
                            strDelta = "(" + Fmt(dCenterX - dLocX) + ";" + Fmt(dCenterY - dLocY) + ")";
                            log.Log("[INFO] [SYMBOX] bbox: " + strBbox + ", центр (" + Fmt(dCenterX) + ";" +
                                Fmt(dCenterY) + "), Δ центра от Location " + strDelta +
                                " (п.48: для CABDCP2 центр = точке вставки)");
                        }
                        else
                        {
                            log.Warn("[SYMBOX] GetBoundingBox вернул " +
                                (arrBox == null ? "null" : arrBox.Length.ToString(CultureInfo.InvariantCulture) + " точек") +
                                " — ожидалось 2 (KB)");
                        }
                    }
                    catch (Exception oEx)
                    {
                        log.Warn("[SYMBOX] GetBoundingBox бросил " + oEx.GetType().Name + ": " + oEx.Message);
                    }

                    // Замер 3: GetLogicalArea (KB: NotImplementedException, если
                    // логической области нет — для символов кабеля ожидаемо).
                    try
                    {
                        RectangleD oArea = oRef.GetLogicalArea();
                        strLogical = "W=" + Fmt(oArea.Width) + "×H=" + Fmt(oArea.Height);
                        log.Log("[INFO] [SYMBOX] логическая область: " + strLogical);
                    }
                    catch (NotImplementedException)
                    {
                        strLogical = "нет";
                        log.Log("[INFO] [SYMBOX] GetLogicalArea: логической области нет (ожидаемо по KB)");
                    }
                    catch (Exception oEx)
                    {
                        // EPLAN API иногда оборачивает исходную ошибку в своё
                        // исключение: штатный «области нет» должен дойти до INFO,
                        // а не уехать в WARN (WARN-бюджет пробы).
                        NotImplementedException oNoArea = oEx.InnerException as NotImplementedException;
                        if (oEx is NotImplementedException) oNoArea = (NotImplementedException)oEx;
                        if (oNoArea != null)
                        {
                            strLogical = "нет";
                            log.Log("[INFO] [SYMBOX] GetLogicalArea: логической области нет (ожидаемо по KB" +
                                (oEx is NotImplementedException
                                    ? ""
                                    : ", обёрнуто в " + oEx.GetType().Name) + ")");
                        }
                        else
                        {
                            log.Warn("[SYMBOX] GetLogicalArea бросил " + oEx.GetType().Name + ": " + oEx.Message);
                        }
                    }
                }
                catch (Exception oEx)
                {
                    log.Warn("[SYMBOX] вставка " + strId + " бросил " + oEx.GetType().Name + ": " +
                        oEx.Message + " — замеры размещения пропущены");
                    nFailed++;
                }
                finally
                {
                    // Урок п.48: страница не должна засоряться — аддин неидемпотентен,
                    // останцы искажают будущие замеры. Удаляем по ВАЛИДНОСТИ объекта,
                    // а не по флагу «Create прошёл»: если движок добавил размещение
                    // и бросил уже после — флаг бы пропустил Remove, остался бы останец.
                    try
                    {
                        if (oRef != null && oRef.IsValid)
                        {
                            oRef.Remove();
                            nRemoved++;
                            log.Log("[INFO] [SYMBOX] " + strId + " пробный символ удалён");
                        }
                    }
                    catch (Exception oEx)
                    {
                        log.Warn("[SYMBOX] Remove бросил " + oEx.GetType().Name + ": " + oEx.Message +
                            " — возможен останец на странице (урок п.48)");
                        nFailed++;
                    }
                }

                // (г) Итоговая строка пробы — из накопленных значений;
                // отсутствующие части — «n/a».
                log.Log("[INFO] [SYMBOX] " + strId + ": bbox " + strBbox + ", Δ центра=" + strDelta +
                    ", logical " + strLogical);
            }

            log.Log("[INFO] [SYMBOX-SUM] проб " + arrLibs.Length.ToString(CultureInfo.InvariantCulture) +
                ", вставлено " + nInserted.ToString(CultureInfo.InvariantCulture) +
                ", удалено " + nRemoved.ToString(CultureInfo.InvariantCulture) +
                ", отказов " + nFailed.ToString(CultureInfo.InvariantCulture) + ".");
        }

        /// <summary>Формат чисел пробы — «F3», инвариантная культура (как в
        /// CableSymbolCreator).</summary>
        private static string Fmt(double dValue)
        {
            return dValue.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
