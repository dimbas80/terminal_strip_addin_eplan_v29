using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Topology;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;

namespace MyEplanActions
{
    /// <summary>
    /// Action-скрипт для EPLAN Electric P8 2.9.
    ///
    /// Использование:
    /// 1. Откройте страницу типа "Топология".
    /// 2. Кликните на нужный сегмент маршрутизации.
    /// 3. Запустите действие (SHOW_CABLES_IN_SEGMENT). Можно создать кнопку и прописать эту команду
    /// 4. В точке (-20, -20) создаётся группа с таблицей кабелей.
    ///    Переместите её командой M (Move).
    ///
    /// Размеры ячейки: 10x4 мм при масштабе 1:1.
    /// Текст: высота 2.0 мм, выравнивание по центру ячейки.
    /// Формат имени кабеля: =Установка-ОУ (если есть установка),
    /// иначе только ОУ.
    /// </summary>
    public class ShowCablesInSegmentAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            try
            {
                SelectionSet oSelectionSet = new SelectionSet();
                Project oProject = oSelectionSet.GetCurrentProject(false);

                if (oProject == null)
                {
                    MessageBox.Show("Нет открытого проекта.");
                    return false;
                }

                // Ищем сегмент маршрутизации среди выделенных объектов
                Segment oSelectedSegment = null;
                StorableObject[] oSelection = oSelectionSet.Selection;

                for (int i = 0; i < oSelection.Length; i++)
                {
                    Segment oCandidate = oSelection[i] as Segment;
                    if (oCandidate != null)
                    {
                        oSelectedSegment = oCandidate;
                        break;
                    }
                }

                if (oSelectedSegment == null)
                {
                    MessageBox.Show(
                        "Сегмент маршрутизации не выделен.\n\n" +
                        "Откройте страницу типа \"Топология\", кликните на нужный " +
                        "сегмент и запустите действие снова.",
                        "Сегмент не выбран");
                    return false;
                }

                Page oCurrentPage = oSelectedSegment.Page;
                if (oCurrentPage == null)
                {
                    MessageBox.Show("Не удалось получить страницу сегмента.");
                    return false;
                }

                // Собираем все кабели проекта
                DMObjectsFinder oFinder = new DMObjectsFinder(oProject);
                FunctionsFilter oCableFilter = new FunctionsFilter();
                oCableFilter.Category = Function.Enums.Category.Cable;
                Function[] oCableFunctions = oFinder.GetFunctions(oCableFilter);

                // Отбираем кабели, проходящие через выбранный сегмент
                // Ключ — уникальный DT кабеля (oCable.Name), значение — отображаемое имя
                SortedDictionary<string, string> oResultCables =
                    new SortedDictionary<string, string>();

                for (int c = 0; c < oCableFunctions.Length; c++)
                {
                    Cable oCable = oCableFunctions[c] as Cable;
                    if (oCable == null)
                    {
                        continue;
                    }

                    PropertyValue oCablingPathValue = oCable.Properties.CABLING_PATH;
                    if (oCablingPathValue.IsEmpty)
                    {
                        continue;
                    }

                    string sCablingPath = oCablingPathValue.ToString();
                    if (string.IsNullOrEmpty(sCablingPath))
                    {
                        continue;
                    }

                    string[] oSegmentNamesInPath = sCablingPath.Split(';');
                    for (int s = 0; s < oSegmentNamesInPath.Length; s++)
                    {
                        string sSegName = oSegmentNamesInPath[s].Trim();
                        if (sSegName.Length == 0)
                        {
                            continue;
                        }

                        if (sSegName == oSelectedSegment.Name)
                        {
                            // Ключ — полный DT кабеля (уникален)
                            // Значение — отображаемое имя
                            string sKey = oCable.Name;
                            if (!oResultCables.ContainsKey(sKey))
                            {
                                oResultCables.Add(sKey, FormatCableName(oCable));
                            }
                            break;
                        }
                    }
                }

                if (oResultCables.Count == 0)
                {
                    MessageBox.Show(
                        "Через сегмент \"" + oSelectedSegment.Name +
                        "\" не проходит ни один кабель.",
                        "Результат");
                    return true;
                }

                // Если кабелей больше 10 — предупреждаем и выходим
                if (oResultCables.Count > 10)
                {
                    MessageBox.Show(
                        "Количество кабелей более 10. Используйте отчет.",
                        "Предупреждение",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return true;
                }

                // Создаём таблицу и собираем в группу
                DrawTableGroupOnPage(oCurrentPage, oSelectedSegment, oResultCables);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при выполнении: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Форматирует имя кабеля: =Установка-ОУ (если есть установка),
        /// иначе только ОУ (без впереди стоящих префиксов).
        /// Растение парсится напрямую из полного DT, чтобы
        /// не терять точки в составных обозначениях (1.1 и т.п.).
        /// </summary>
        private string FormatCableName(Cable oCable)
        {
            string sName = oCable.Name;
            if (string.IsNullOrEmpty(sName))
            {
                return "";
            }

            // Извлекаем установку: часть от '=' до первого '+' или '-'
            string sPlant = "";
            string sRest = sName;

            if (sName[0] == '=')
            {
                int nEnd = -1;
                for (int i = 1; i < sName.Length; i++)
                {
                    if (sName[i] == '+' || sName[i] == '-')
                    {
                        nEnd = i;
                        break;
                    }
                }

                if (nEnd > 1)
                {
                    // Есть растение: "=XXX..."
                    sPlant = sName.Substring(0, nEnd);
                    sRest = sName.Substring(nEnd);
                }
                else if (nEnd == 1)
                {
                    // Пустое растение: "=+..." или "=-..."
                    // Пропускаем '=', начинаем с разделителя
                    sRest = sName.Substring(1);
                }
                // else nEnd == -1: вся строка — это только установка
                // оставляем sPlant = "" и sRest = sName
            }

            // ОУ — последняя часть после последнего '-'
            string sOU = sRest;
            if (sRest.Length > 0)
            {
                int nLastDash = sRest.LastIndexOf('-');
                if (nLastDash >= 0 && nLastDash < sRest.Length - 1)
                {
                    sOU = sRest.Substring(nLastDash + 1);
                }
                else if (nLastDash < 0)
                {
                    sOU = sRest;
                }
                // else: последний символ — '-', ОУ пустое
                // оставляем sOU как есть (будет пустой строкой)

                sOU = sOU.TrimStart('=', '+');
            }

            if (!string.IsNullOrEmpty(sPlant) && !string.IsNullOrEmpty(sOU))
            {
                return sPlant + "-" + sOU;
            }
            else if (!string.IsNullOrEmpty(sOU))
            {
                return sOU;
            }
            else
            {
                // Если не удалось выделить ОУ — возвращаем оригинал
                return sName;
            }
        }

        /// <summary>
        /// Коэффициент пересчёта размеров под масштаб страницы.
        /// </summary>
        private double GetScaleFactor(Page oPage)
        {
            try
            {
                string sScale = oPage.Properties.PAGE_SCALE_RELATION.ToString();

                if (!string.IsNullOrEmpty(sScale))
                {
                    string[] oParts = sScale.Trim().Split(':');
                    if (oParts.Length == 2)
                    {
                        double dFirst = double.Parse(oParts[0].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture);
                        double dSecond = double.Parse(oParts[1].Trim(),
                            System.Globalization.CultureInfo.InvariantCulture);

                        if (dSecond != 0.0)
                        {
                            return dFirst / dSecond;
                        }
                    }
                }
            }
            catch
            {
            }

            return 1.0;
        }

        /// <summary>
        /// Создаёт таблицу ОУ кабелей и собирает её в группу.
        /// </summary>
        private void DrawTableGroupOnPage(Page oPage, Segment oSelectedSegment,
            SortedDictionary<string, string> oCableNames)
        {
            double dScaleFactor = GetScaleFactor(oPage);

            // Размеры с учётом масштаба
            double dCellWidth = 10.0 / dScaleFactor;    // 10 мм при 1:1
            double dCellHeight = 4.0 / dScaleFactor;     // 4 мм при 1:1
            double dTextHeight = 2.0 / dScaleFactor;     // 2.0 мм при 1:1
            // Толщина линий — всегда 0.35, без масштаба

            int nRowCount = oCableNames.Count;

            // Позиция — центр сегмента маршрутизации
            PointD oSegStart = oSelectedSegment.StartPoint;
            PointD oSegEnd = oSelectedSegment.EndPoint;
            double dMidX = (oSegStart.X + oSegEnd.X) / 2.0;
            double dMidY = (oSegStart.Y + oSegEnd.Y) / 2.0;

            // Таблица встаёт левым верхним углом на середину сегмента
            double dTableWidth = dCellWidth;
            double dTableHeight = nRowCount * dCellHeight;
            double dStartX = dMidX;
            double dStartY = dMidY - dTableHeight;

            // Перо для линий сетки
            Pen oGridPen = new Pen();
            oGridPen.ColorId = 0;      // чёрный
            oGridPen.Width = 0.35;     // всегда 0.35 мм
            oGridPen.StyleId = 0;      // сплошная

            double dLeftX = dStartX;
            double dRightX = dStartX + dCellWidth;

            List<Placement> oPlacements = new List<Placement>();

            // --- Горизонтальные линии ---
            for (int r = 0; r <= nRowCount; r++)
            {
                double y = dStartY + r * dCellHeight;

                Line oLine = new Line();
                oLine.Create(oPage,
                    new PointD(dLeftX, y),
                    new PointD(dRightX, y));
                oLine.Pen = oGridPen;
                oPlacements.Add(oLine);
            }

            // --- Вертикальные линии ---
            Line oLeftLine = new Line();
            oLeftLine.Create(oPage,
                new PointD(dLeftX, dStartY),
                new PointD(dLeftX, dStartY + nRowCount * dCellHeight));
            oLeftLine.Pen = oGridPen;
            oPlacements.Add(oLeftLine);

            Line oRightLine = new Line();
            oRightLine.Create(oPage,
                new PointD(dRightX, dStartY),
                new PointD(dRightX, dStartY + nRowCount * dCellHeight));
            oRightLine.Pen = oGridPen;
            oPlacements.Add(oRightLine);

            // --- Текст — имена кабелей (центрирование по ячейке) ---
            // Justification: 0=Left, 1=Center, 2=Right
            double dCellCenterX = dLeftX + dCellWidth / 2.0;
            int nRow = 0;

            foreach (KeyValuePair<string, string> oEntry in oCableNames)
            {
                // Базовая линия текста — центр ячейки по вертикали
                double dTextY = dStartY + nRow * dCellHeight
                    + (dCellHeight - dTextHeight) / 2.0;

                Text oText = new Text();
                oText.Create(oPage, oEntry.Value, dTextHeight);
                oText.Justification = TextBase.JustificationType.MiddleCenter;
                // Y +1 мм для корректировки точки вставки
                oText.Location = new PointD(dCellCenterX,
                    dTextY + 1.0 / dScaleFactor);
                oPlacements.Add(oText);

                nRow++;
            }

            // Группируем — вся таблица одним объектом
            Group oGroup = new Group();
            oGroup.Create(oPlacements.ToArray());
        }

        public bool OnRegister(ref string ActionName, ref int Ordinal)
        {
            ActionName = "SHOW_CABLES_IN_SEGMENT";
            Ordinal = 0;
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }
    }

    public class ShowCablesInSegmentAddIn : IEplAddIn
    {
        public bool OnRegister(ref bool bLoadOnStart)
        {
            bLoadOnStart = true;
            return true;
        }

        public bool OnUnregister() { return true; }
        public bool OnInit() { return true; }
        public bool OnInitGui() { return true; }
        public bool OnExit() { return true; }
    }
}
