using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;

namespace MyEplanActions
{
    /// <summary>
    /// Чтение встроенного отчёта клеммника — порт EPLAN-части rev.13 из
    /// spike/TerminalStripReportSpike.cs (план: plan_stage2.md, Задача 3).
    /// Добавление формы в проект (Masterdata.AddToProjectEx), создание отчёта
    /// перебором (FormName × Type × FilterSchemaName), рекурсивный обход
    /// SubPlacements с логом [TREE], перепись слоёв [ALLLAYERS], дамп линий
    /// [LINE]/[LAYER]. Диагностика spike, закрывшие свои вопросы (§5.2 п.4,
    /// §5.3 якоря, [PROP]/[GLAYER] дампы), НЕ переносится.
    /// </summary>
    public sealed class EmbeddedReportReader
    {
        private readonly DiagnosticLogger _log;

        public EmbeddedReportReader(DiagnosticLogger oLogger)
        {
            _log = oLogger;
        }

        // Кандидаты имени формы (без расширения .f11): основное имя из конфига
        // + вариант с концевым пробелом (файл исторически existed с пробелом).
        private static string[] FormNameCandidates()
        {
            return new string[]
            {
                AddInConfiguration.ReportFormName,
                AddInConfiguration.ReportFormName + " "
            };
        }

        // Кандидаты имени файла формы для AddToProjectEx (мастер-данные -> проект).
        private static string[] FormFileCandidates()
        {
            return new string[]
            {
                AddInConfiguration.ReportFormName + ".f11",
                AddInConfiguration.ReportFormName + " .f11"
            };
        }

        // Тип отчёта НЕ фиксируем: «Схема подключения клемм» (f11) — НЕ TerminalDiagram
        // (rev.6). Перебираем приоритетные типы + всё Terminal*/Interconnect* из enum.
        private static readonly string[] REPORT_TYPE_NAME_PRIORITY = new string[]
        {
            "TerminalDiagram",
            "TerminalConnectiondiagram",
            "InterconnectDiagram",
            "TerminalLineupDiagram",
            "TerminalStripOverview"
        };

        private static DocumentTypeManager.DocumentType[] GetReportTypeCandidates()
        {
            Type oEnumType = typeof(DocumentTypeManager.DocumentType);
            List<string> lstAllNames = new List<string>(Enum.GetNames(oEnumType));
            List<DocumentTypeManager.DocumentType> lstResult = new List<DocumentTypeManager.DocumentType>();

            foreach (string strPriority in REPORT_TYPE_NAME_PRIORITY)
            {
                if (lstAllNames.Contains(strPriority))
                    lstResult.Add((DocumentTypeManager.DocumentType)Enum.Parse(oEnumType, strPriority));
            }
            foreach (string strName in lstAllNames)
            {
                if ((strName.Contains("Terminal") || strName.Contains("Interconnect")) &&
                    strName != "Invalid" && strName != "Undefined")
                {
                    DocumentTypeManager.DocumentType eType =
                        (DocumentTypeManager.DocumentType)Enum.Parse(oEnumType, strName);
                    if (!lstResult.Contains(eType)) lstResult.Add(eType);
                }
            }
            return lstResult.ToArray();
        }

        // Кандидаты FilterSchemaName ("" = не задавать: в примере документации
        // CreateEmbeddedReport работает без него).
        private static readonly string[] FILTER_SCHEMA_CANDIDATES = new string[]
        {
            "",
            "Structured terminals",
            "Структурированные клеммы"
        };

        /// <summary>Пытается добавить форму из мастер-данных в проект (AddToProjectEx —
        /// как в примере документации CreateEmbeddedReport). Затем перечисляет формы
        /// проекта (*.f11) через Masterdata.get_ProjectEntries и возвращает их имена
        /// (без расширения) как дополнительные кандидаты для FormName.</summary>
        public List<string> TryAddFormToProject(Project oProject)
        {
            List<string> lstResult = new List<string>();

            foreach (string strFormFile in FormFileCandidates())
            {
                try
                {
                    StringCollection oEntries = new StringCollection();
                    oEntries.Add(strFormFile);
                    Hashtable oAdded = new Masterdata().AddToProjectEx(oProject, oEntries);
                    // ВАЖНО: AddToProjectEx НЕ бросает исключение при ошибке добавления
                    // отдельного файла — ошибка возвращается значением в Hashtable
                    // (в rev.4 это маскировалось как «выполнен»).
                    bool bError = false;
                    if (oAdded == null)
                    {
                        _log.Warn("AddToProjectEx вернул null для '" + strFormFile +
                            "' — результат неизвестен, пробуем следующий вариант имени.");
                        bError = true;
                    }
                    else
                    {
                        foreach (DictionaryEntry oEntry in oAdded)
                        {
                            Exception oAddError = oEntry.Value as Exception;
                            if (oAddError != null)
                            {
                                _log.Log("[ERROR] AddToProjectEx: '" + oEntry.Key + "' -> " + oAddError.Message);
                                bError = true;
                            }
                            else
                            {
                                _log.Log("[INFO] AddToProjectEx: '" + oEntry.Key + "' -> " + oEntry.Value);
                            }
                        }
                    }
                    if (!bError)
                    {
                        _log.Log("[INFO] Форма '" + strFormFile + "' добавлена в проект.");
                        break;
                    }
                }
                catch (Exception oException)
                {
                    _log.Warn("AddToProjectEx не удался для '" + strFormFile + "': " +
                        oException.GetType().Name + ": " + oException.Message);
                }
            }

            try
            {
                _log.Log("[INFO] --- Формы (*.f11) в ПРОЕКТЕ (Masterdata.get_ProjectEntries) ---");
                IEnumerable oProjectEntries = new Masterdata().get_ProjectEntries(oProject);
                foreach (object oEntry in oProjectEntries)
                {
                    string strEntry = oEntry as string;
                    if (strEntry == null) continue;
                    if (strEntry.EndsWith(".f11", StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Log("[FORMP] " + strEntry);
                        string strName = System.IO.Path.GetFileNameWithoutExtension(strEntry);
                        // Берём только «свои» формы — rev.4 схватывал чужие и создавал
                        // отчёт не на той форме.
                        if (strName.IndexOf(AddInConfiguration.FormNameFilter, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            !lstResult.Contains(strName))
                        {
                            lstResult.Add(strName);
                        }
                    }
                }
                _log.Log("[INFO] Найдено форм .f11 в проекте: " + lstResult.Count);
            }
            catch (Exception oException)
            {
                _log.Warn("Перечисление форм проекта не удалось: " +
                    oException.GetType().Name + ": " + oException.Message);
            }

            return lstResult;
        }

        /// <summary>Все имена форм (*.f11) проекта и системы БЕЗ фильтра «addin»
        /// (Фаза H, H-2; спека 2026-09-25-fase-h-ui-design.md §2 п.3) — список для
        /// диалога UI-режима. Источники — Masterdata.get_ProjectEntries и свойство
        /// Masterdata.SystemEntries (KB API 2.9: StringCollection — свойство, не
        /// метод). Элемент — имя файла: путь отбрасывается Path.GetFileName
        /// (безопасно и для полных путей — паттерн [FORMP], и для голых имён),
        /// расширение .f11 отбрасывается, БЕЗ trim (хвостовой пробел имени формы
        /// значим — урок п.33). Дубликаты имени: первый побеждает; сортировка
        /// OrdinalIgnoreCase — стабильный список для диалога. Исключения каждого
        /// источника наружу не выходят: WARN по источнику + частично собранный
        /// список (отказ SystemEntries не теряет формы проекта); пустой результат —
        /// WARN. Маркер [FORMLIST] с числом найденных; вызывается ОДИН раз перед
        /// циклом диалога — в цикле прогона логов не дублирует.</summary>
        public static List<string> CollectAvailableFormNames(Project oProject, DiagnosticLogger log)
        {
            List<string> lstResult = new List<string>();
            try
            {
                CollectFormNamesFrom(new Masterdata().get_ProjectEntries(oProject), lstResult);
            }
            catch (Exception oException)
            {
                log.Warn("[FORMLIST] проектные формы: перечисление не удалось: " +
                    oException.GetType().Name + ": " + oException.Message);
            }
            try
            {
                CollectFormNamesFrom(new Masterdata().SystemEntries, lstResult);
            }
            catch (Exception oException)
            {
                log.Warn("[FORMLIST] системные формы: перечисление не удалось: " +
                    oException.GetType().Name + ": " + oException.Message);
            }
            if (lstResult.Count == 0)
                log.Warn("[FORMLIST] формы (*.f11) не найдены ни в проекте, ни в системе");
            lstResult.Sort(StringComparer.OrdinalIgnoreCase);
            log.Log("[FORMLIST] форм (*.f11, проект+система, без фильтра): " + lstResult.Count);
            return lstResult;
        }

        /// <summary>Складывает в lstResult имена *.f11 из одной выдачи мастер-данных:
        /// путь отбрасывается Path.GetFileName, расширение .f11 отбрасывается,
        /// БЕЗ trim (урок п.33), дубликаты — первый побеждает.</summary>
        private static void CollectFormNamesFrom(IEnumerable oEntries, List<string> lstResult)
        {
            if (oEntries == null) return;
            foreach (object oEntry in oEntries)
            {
                string strEntry = oEntry as string;
                if (strEntry == null) continue;
                if (!strEntry.EndsWith(".f11", StringComparison.OrdinalIgnoreCase)) continue;
                string strFile = System.IO.Path.GetFileName(strEntry);
                if (strFile.Length <= 4) continue;   // вырожденная запись '.f11' без имени
                string strName = strFile.Substring(0, strFile.Length - 4);   // минус ".f11"
                if (!lstResult.Contains(strName)) lstResult.Add(strName);
            }
        }

        /// <summary>UI-ветка (H-2): AddToProjectEx для имени формы, выбранного в
        /// диалоге (план plan_stage8.md H-2). Отказ не фатален: форма могла уже
        /// быть в проекте (список CollectAvailableFormNames включает и проектные,
        /// и системные) — каждая ошибка в лог, попытка продолжается. Кандидаты
        /// имени файла — без и с хвостовым пробелом (паттерн FormFileCandidates).
        /// rev.15.5: публичный продакшн-метод EnsureFormInProject (переименование
        /// TryAddFormFileToProject; байт-в-байт прежнее поведение) — вызывается
        /// из TryCreateEmbeddedReport, когда форму нужно добавить в проект из
        /// мастер-данных (мутация проекта — ТОЛЬКО в этом случае).</summary>
        public void EnsureFormInProject(Project oProject, string strFormName)
        {
            string[] arrFileCandidates = new string[]
            {
                strFormName + ".f11",
                strFormName + " .f11"
            };
            foreach (string strFormFile in arrFileCandidates)
            {
                if (TryAddOneFormFile(oProject, strFormFile)) break;
            }
        }

        /// <summary>Одна попытка AddToProjectEx (тот же шаг цикла, что в
        /// TryAddFormToProject): true — добавлено без ошибок. AddToProjectEx НЕ
        /// бросает исключение при ошибке отдельного файла — ошибка возвращается
        /// в Hashtable (урок rev.4).</summary>
        private bool TryAddOneFormFile(Project oProject, string strFormFile)
        {
            try
            {
                StringCollection oEntries = new StringCollection();
                oEntries.Add(strFormFile);
                Hashtable oAdded = new Masterdata().AddToProjectEx(oProject, oEntries);
                if (oAdded == null)
                {
                    _log.Warn("AddToProjectEx вернул null для '" + strFormFile + "'.");
                    return false;
                }
                bool bError = false;
                foreach (DictionaryEntry oEntry in oAdded)
                {
                    Exception oAddError = oEntry.Value as Exception;
                    if (oAddError != null)
                    {
                        _log.Log("[ERROR] AddToProjectEx: '" + oEntry.Key + "' -> " + oAddError.Message);
                        bError = true;
                    }
                    else
                    {
                        _log.Log("[INFO] AddToProjectEx: '" + oEntry.Key + "' -> " + oEntry.Value);
                    }
                }
                return !bError;
            }
            catch (Exception oException)
            {
                _log.Warn("AddToProjectEx не удался для '" + strFormFile + "': " +
                    oException.GetType().Name + ": " + oException.Message);
                return false;
            }
        }

        /// <summary>Метрики шаблона формы .f11 (rev.15.5, решение пользователя
        /// 30.09.2026): размер призрака больше НЕ из PlotFrame.Size — форма
        /// мастер-данных, страницы формы в проекте может не быть; источник —
        /// ПАРСИНГ ФАЙЛА ФОРМЫ .f11 (PXF-XML, UTF-8; эталон
        /// example/Клемник_ОУ(горизонтально)_addin.f11 проверен пользователем).
        /// Формат: области формы = элементы &lt;O128 .../&gt; с атрибутами
        /// A1651="x1/y1", A1652="x2/y2" (углы, мм, разделитель '/') и
        /// A2096="индекс" (0..3). Семантика (подтверждена пользователем):
        /// A2096=1 — ОБЛАСТЬ ДАННЫХ (шаблонно колонка 7 мм); A2096=0/2 (левее
        /// данных) — ШАПКА (14+15=29 мм); A2096=3 — ФУТЕР (1.5 мм); высота
        /// областей = 180 (Y-размах блоков). X3-факт отчёта: шапка 29,
        /// данные 20 строк×7=140, высота 180. Координаты бывают дробными
        /// с хвостом ("71.9999999999992") — double.TryParse(InvariantCulture).
        /// Поиск файла: Masterdata.get_ProjectEntries ЗАТЕМ Masterdata.SystemEntries
        /// (идиома CollectFormNamesFrom: foreach по IEnumerable, отказ источника —
        /// WARN в лог, обход не прерывается); кандидат — запись, чья последняя
        /// секция пути равна strFormName+".f11" ИЛИ strFormName+" .f11"
        /// (OrdinalIgnoreCase, БЕЗ trim — урок п.33). Парсинг — ЧИСТЫЙ
        /// string-парсинг (IndexOf/Substring): System.Xml.Linq НЕ подключается
        /// (сборка csc /recurse без новых ссылок на сборки). Неразбираемый блок —
        /// скипается (счётчик в лог). out: dHeaderMm — сумма ALONG-ширин блоков,
        /// ALONG-начало которых ЛЕВЕЕ блока данных (нет — 0, INFO);
        /// dDataColMm — ALONG-ширина блока данных (A2096=1; нет — WARN + false);
        /// dFooterMm — ALONG-ширина блока A2096=3 (нет — 0, INFO);
        /// dTotalAcrossMm — размах ВСЕХ блоков поперёк ALONG; bDataAlongX —
        /// направление ПОВТОРЕНИЯ данных шаблона: вдоль УЗКОЙ оси блока данных
        /// (эталон: полоска-столбец 7 (X) × 180 (Y) — каждая «строка» данных =
        /// вертикальная полоса 7 мм во всю высоту, полосы идут вдоль X → true).
        /// Проверено на эталоне песочницей: брифовое правило «шире по X → вдоль
        /// X» даёт мусор (header=0, dataCol=180) — ИНВЕРТИРОВАНО; сверяется
        /// вызывателем с ориентацией призрака. ВАЖНО: геометрия шаблона по ALONG
        /// (в эталоне 37.5 мм = 29+7+1.5) — НЕ размер призрака: шаблон показывает
        /// ОДНУ строку данных, призрак экстраполируется N строками
        /// (GhostFrameMath.ComputeFromTemplateMetrics). Ориентированность
        /// пары углов (x1/y1-x2/y2 в любом порядке) — Math.Abs/Min/Max.
        /// Любой отказ (файл не найден в пулах, чтение, нет области данных) —
        /// false + [GHOST-SIZE] с причиной; исключения наружу НЕ выходят
        /// (вызов ДО создания призрака — отказ лишь включает фоллбэк-эвристику
        /// размера).</summary>
        public bool TryGetFormTemplateMetrics(Project oProject, string strFormName,
            out double dHeaderMm, out double dDataColMm, out double dFooterMm,
            out double dTotalAcrossMm, out bool bDataAlongX)
        {
            dHeaderMm = 0.0;
            dDataColMm = 0.0;
            dFooterMm = 0.0;
            dTotalAcrossMm = 0.0;
            bDataAlongX = true;
            if (oProject == null || string.IsNullOrEmpty(strFormName))
            {
                _log.Warn("[GHOST-SIZE] вход не задан (проект/имя формы пусты) — метрики шаблона не получены");
                return false;
            }
            try
            {
                // --- 1. Полный путь файла формы: проект → система.
                string strPath = FindFormFilePath(oProject, strFormName);
                if (strPath == null)
                {
                    _log.Warn("[GHOST-SIZE] файл формы '" + strFormName + ".f11' не найден в пулах мастер-данных (проект+система) — метрики не получены");
                    return false;
                }
                _log.Log("[INFO] [GHOST-SIZE] файл формы найден: " + strPath);

                // --- 2. Чтение (ReadAllText — PXF-XML в UTF-8).
                string strXml = System.IO.File.ReadAllText(strPath);

                // --- 3. Разбор областей <O128 .../> (чистый string-парсинг).
                int nSkipped;
                List<O128Block> lstBlocks = ParseO128Blocks(strXml, out nSkipped);
                if (nSkipped > 0)
                    _log.Log("[INFO] [GHOST-SIZE] областей <O128> не разобрано (скипнуто): " +
                        nSkipped.ToString(CultureInfo.InvariantCulture));
                if (lstBlocks.Count == 0)
                {
                    _log.Warn("[GHOST-SIZE] областей <O128> не найдено в '" + strPath + "' — метрики не получены");
                    return false;
                }
                string strBlockList = lstBlocks.Count.ToString(CultureInfo.InvariantCulture) + " шт:";
                for (int iB = 0; iB < lstBlocks.Count; iB++)
                {
                    if (iB == 12) { strBlockList += " ..."; break; }
                    O128Block oB = lstBlocks[iB];
                    strBlockList += " " + oB.Idx.ToString(CultureInfo.InvariantCulture) + ":" +
                        Math.Abs(oB.X2 - oB.X1).ToString("F1", CultureInfo.InvariantCulture) + "×" +
                        Math.Abs(oB.Y2 - oB.Y1).ToString("F1", CultureInfo.InvariantCulture);
                }
                _log.Log("[INFO] [GHOST-SIZE] области <O128>" + strBlockList);

                // --- 4. Область данных (A2096=1) — без неё метрики бессмысленны.
                O128Block oData = null;
                foreach (O128Block oBlock in lstBlocks)
                {
                    if (oBlock.Idx == 1) { oData = oBlock; break; }
                }
                if (oData == null)
                {
                    _log.Warn("[GHOST-SIZE] нет области данных (A2096=1) в '" + strPath + "' — метрики не получены");
                    return false;
                }

                // --- 5. Ось ALONG по блоку данных: данные ПОВТОРЯЮТСЯ вдоль
                //        УЗКОЙ оси блока данных. Эталон (проверен песочницей на
                //        example/*.f11): область данных = полоска-столбец
                //        7 (X) × 180 (Y) — «строка» данных = вертикальная полоса
                //        7 мм во всю высоту, полосы идут вдоль X (шапка 14+15
                //        левее, футер 1.5 правее). Правило брифа «шире по X →
                //        данные вдоль X» на эталоне даёт мусор (header=0,
                //        dataCol=180) — ИНВЕРТИРОВАНО: уже по X → вдоль X.
                bDataAlongX = Math.Abs(oData.X2 - oData.X1) <= Math.Abs(oData.Y2 - oData.Y1);
                dDataColMm = bDataAlongX
                    ? Math.Abs(oData.X2 - oData.X1)
                    : Math.Abs(oData.Y2 - oData.Y1);
                double dDataAlongStart = bDataAlongX
                    ? Math.Min(oData.X1, oData.X2)
                    : Math.Min(oData.Y1, oData.Y2);

                // --- 6. Шапка / футер / размах поперёк: один проход по блокам.
                //        Шапка — сумма ALONG-ширин блоков, ALONG-начало которых
                //        левее данных (в эталоне idx 0 и 2); футер — ALONG-ширина
                //        блока idx==3; поперёк — min/max по ВСЕМ блокам.
                double dHeader = 0.0;
                int nHeaderBlocks = 0;
                double dFooter = 0.0;
                bool bFooterFound = false;
                double dAcrossMin = double.MaxValue;
                double dAcrossMax = double.MinValue;
                foreach (O128Block oBlock in lstBlocks)
                {
                    double dAlongStart = bDataAlongX ? Math.Min(oBlock.X1, oBlock.X2) : Math.Min(oBlock.Y1, oBlock.Y2);
                    double dAlongEnd = bDataAlongX ? Math.Max(oBlock.X1, oBlock.X2) : Math.Max(oBlock.Y1, oBlock.Y2);
                    double dAcrossLow = bDataAlongX ? Math.Min(oBlock.Y1, oBlock.Y2) : Math.Min(oBlock.X1, oBlock.X2);
                    double dAcrossHigh = bDataAlongX ? Math.Max(oBlock.Y1, oBlock.Y2) : Math.Max(oBlock.X1, oBlock.X2);
                    if (dAcrossLow < dAcrossMin) dAcrossMin = dAcrossLow;
                    if (dAcrossHigh > dAcrossMax) dAcrossMax = dAcrossHigh;
                    if (oBlock.Idx == 3 && !bFooterFound)
                    {
                        dFooter = dAlongEnd - dAlongStart;
                        bFooterFound = true;
                    }
                    if (oBlock.Idx != 1 && dAlongStart < dDataAlongStart)
                    {
                        dHeader += dAlongEnd - dAlongStart;
                        nHeaderBlocks++;
                    }
                }
                dHeaderMm = dHeader;
                dFooterMm = dFooter;
                dTotalAcrossMm = dAcrossMax - dAcrossMin;

                if (nHeaderBlocks == 0)
                    _log.Log("[INFO] [GHOST-SIZE] блоков шапки левее данных нет — шапка 0");
                if (!bFooterFound)
                    _log.Log("[INFO] [GHOST-SIZE] блока футера (A2096=3) нет — футер 0");
                _log.Log(string.Format(CultureInfo.InvariantCulture,
                    "[INFO] [GHOST-SIZE] метрики шаблона '{0}': шапка={1:F1} область_данных={2:F1} футер={3:F1} поперёк={4:F1} (данные шаблона вдоль {5})",
                    strFormName, dHeaderMm, dDataColMm, dFooterMm, dTotalAcrossMm,
                    (bDataAlongX ? "X" : "Y")));
                return true;
            }
            catch (Exception oException)
            {
                _log.Warn("[GHOST-SIZE] разбор шаблона формы '" + strFormName + "': " +
                    oException.GetType().Name + ": " + oException.Message + " — метрики не получены");
                return false;
            }
        }

        /// <summary>Полный путь файла формы: проектные записи
        /// (Masterdata.get_ProjectEntries) ЗАТЕМ системные (Masterdata.SystemEntries
        /// — KB 2.9: StringCollection, свойство) — идиома CollectFormNamesFrom
        /// (foreach по IEnumerable; отказ источника — WARN в лог, обход не
        /// прерывается). Кандидат — запись, чья последняя секция пути (после
        /// последнего '\\' или '/') равна strFormName+".f11" ИЛИ
        /// strFormName+" .f11" (OrdinalIgnoreCase, БЕЗ trim — урок п.33).
        /// null — не найден ни в одном пуле.</summary>
        private string FindFormFilePath(Project oProject, string strFormName)
        {
            string[] arrCandidates = new string[] { strFormName + ".f11", strFormName + " .f11" };
            string strFound = null;
            try
            {
                strFound = FindFormFileIn(new Masterdata().get_ProjectEntries(oProject), arrCandidates);
            }
            catch (Exception oException)
            {
                _log.Warn("[GHOST-SIZE] проектные записи форм: перечисление не удалось: " +
                    oException.GetType().Name + ": " + oException.Message);
            }
            if (strFound != null) return strFound;
            try
            {
                strFound = FindFormFileIn(new Masterdata().SystemEntries, arrCandidates);
            }
            catch (Exception oException)
            {
                _log.Warn("[GHOST-SIZE] системные записи форм: перечисление не удалось: " +
                    oException.GetType().Name + ": " + oException.Message);
            }
            return strFound;
        }

        /// <summary>Первый entry, чья последняя секция пути совпала с кандидатом;
        /// возвращает запись ЦЕЛИКОМ (полный путь, как лежит в мастер-данных).</summary>
        private static string FindFormFileIn(IEnumerable oEntries, string[] arrCandidates)
        {
            if (oEntries == null) return null;
            foreach (object oEntry in oEntries)
            {
                string strEntry = oEntry as string;
                if (strEntry == null) continue;
                string strFile = LastPathSection(strEntry);
                foreach (string strCandidate in arrCandidates)
                {
                    if (string.Equals(strFile, strCandidate, StringComparison.OrdinalIgnoreCase))
                        return strEntry;
                }
            }
            return null;
        }

        /// <summary>Последняя секция пути (после последнего '\\' или '/');
        /// разделителей нет — вся строка как есть.</summary>
        private static string LastPathSection(string strPath)
        {
            if (string.IsNullOrEmpty(strPath)) return strPath;
            int nCut = Math.Max(strPath.LastIndexOf('/'), strPath.LastIndexOf('\\'));
            return nCut < 0 ? strPath : strPath.Substring(nCut + 1);
        }

        /// <summary>Разбор всех областей &lt;O128 .../&gt; текста .f11: фрагмент —
        /// от вхождения "&lt;O128" до ближайшего "/&gt;"; из фрагмента вырезаются
        /// A1651="x/y", A1652="x/y", A2096="n" (IndexOf + Substring; значения
        /// атрибутов без пробелов внутри — проверенный формат). Неразбираемый
        /// блок — скипается (nSkipped). Чистый string-парсинг: НИКАКИХ
        /// System.Xml.Linq — сборка csc /recurse без новых ссылок на сборки.</summary>
        private static List<O128Block> ParseO128Blocks(string strXml, out int nSkipped)
        {
            nSkipped = 0;
            List<O128Block> lstResult = new List<O128Block>();
            if (string.IsNullOrEmpty(strXml)) return lstResult;
            int iSearch = 0;
            while (true)
            {
                int nTagStart = strXml.IndexOf("<O128", iSearch, StringComparison.Ordinal);
                if (nTagStart < 0) break;
                int nAfterName = nTagStart + 5;
                // Защита от более длинного имени тега с тем же префиксом (<O128x).
                if (nAfterName < strXml.Length && char.IsLetterOrDigit(strXml[nAfterName]))
                {
                    iSearch = nTagStart + 1;
                    continue;
                }
                int nTagEnd = strXml.IndexOf("/>", nAfterName, StringComparison.Ordinal);
                if (nTagEnd < 0) break;
                string strFragment = strXml.Substring(nTagStart, nTagEnd + 2 - nTagStart);
                O128Block oBlock;
                if (TryParseO128Fragment(strFragment, out oBlock) && oBlock != null)
                    lstResult.Add(oBlock);
                else
                    nSkipped++;
                iSearch = nTagEnd + 2;
            }
            return lstResult;
        }

        /// <summary>Фрагмент тега &lt;O128 .../&gt; → блок (idx, x1/y1, x2/y2):
        /// A2096 — целое, A1651/A1652 — пары "x/y" (InvariantCulture). Нет
        /// атрибута / не распарсилось — false.</summary>
        private static bool TryParseO128Fragment(string strFragment, out O128Block oBlock)
        {
            oBlock = null;
            if (string.IsNullOrEmpty(strFragment)) return false;
            string strIdx = GetO128AttributeValue(strFragment, "A2096");
            string strP1 = GetO128AttributeValue(strFragment, "A1651");
            string strP2 = GetO128AttributeValue(strFragment, "A1652");
            if (strIdx == null || strP1 == null || strP2 == null) return false;
            int nIdx;
            if (!int.TryParse(strIdx, NumberStyles.Integer, CultureInfo.InvariantCulture, out nIdx))
                return false;
            double dX1;
            double dY1;
            double dX2;
            double dY2;
            if (!TryParseMmPair(strP1, out dX1, out dY1)) return false;
            if (!TryParseMmPair(strP2, out dX2, out dY2)) return false;
            oBlock = new O128Block();
            oBlock.Idx = nIdx;
            oBlock.X1 = dX1;
            oBlock.Y1 = dY1;
            oBlock.X2 = dX2;
            oBlock.Y2 = dY2;
            return true;
        }

        /// <summary>Значение атрибута name="value" во фрагменте тега: ищем
        /// name+"=\"" с границей имени (перед ним пробел/таб/перевод строки/'&lt;'
        /// — исключает ложное попадание внутрь другого имени или значения);
        /// значение — до следующего '"'. Нет — null.</summary>
        private static string GetO128AttributeValue(string strFragment, string strName)
        {
            string strNeedle = strName + "=\"";
            int iSearch = 0;
            while (true)
            {
                int nHit = strFragment.IndexOf(strNeedle, iSearch, StringComparison.Ordinal);
                if (nHit < 0) return null;
                char chBefore = nHit > 0 ? strFragment[nHit - 1] : '<';
                if (chBefore == ' ' || chBefore == '\t' || chBefore == '\r' ||
                    chBefore == '\n' || chBefore == '<')
                {
                    int nValueStart = nHit + strNeedle.Length;
                    int nQuoteEnd = strFragment.IndexOf('"', nValueStart);
                    if (nQuoteEnd < 0) return null;
                    return strFragment.Substring(nValueStart, nQuoteEnd - nValueStart);
                }
                iSearch = nHit + 1;
            }
        }

        /// <summary>"x/y" → два double (InvariantCulture; координаты бывают
        /// дробными с хвостом "71.9999999999992"). NaN/∞ — false.</summary>
        private static bool TryParseMmPair(string strPair, out double dX, out double dY)
        {
            dX = 0.0;
            dY = 0.0;
            if (string.IsNullOrEmpty(strPair)) return false;
            int nSlash = strPair.IndexOf('/');
            if (nSlash < 0) return false;
            if (!double.TryParse(strPair.Substring(0, nSlash), NumberStyles.Float,
                CultureInfo.InvariantCulture, out dX)) return false;
            if (!double.TryParse(strPair.Substring(nSlash + 1), NumberStyles.Float,
                CultureInfo.InvariantCulture, out dY)) return false;
            if (double.IsNaN(dX) || double.IsInfinity(dX)) return false;
            if (double.IsNaN(dY) || double.IsInfinity(dY)) return false;
            return true;
        }

        /// <summary>Одна область формы из &lt;O128&gt; (rev.15.5): индекс A2096
        /// (0..3) и углы A1651/A1652 в мм. Локальный класс парсинга .f11.</summary>
        private sealed class O128Block
        {
            internal int Idx;
            internal double X1;
            internal double Y1;
            internal double X2;
            internal double Y2;
        }

        /// <summary>Создаёт встроенный отчёт, перебирая комбинации имён форм и схем фильтра.
        /// Имена: статические кандидаты + имена форм, фактически найденные в проекте.
        /// На каждую комбинацию — свежий ReportBlock, чтобы свойства не «залипали» между
        /// попытками. Неиспользованные описатели уходят при сохранении/сжатии проекта.
        /// rev.12.1 (Фаза H, H-2): strFormNameOverride — имя формы, выбранное в диалоге
        /// UI-режима: список кандидатов FormName = ровно оно + вариант с хвостовым
        /// пробелом (существующий паттерн FormNameCandidates), плюс AddToProjectEx
        /// выбранного имени (EnsureFormInProject, бывш. TryAddFormFileToProject).
        /// null — headless-поведение
        /// байт-в-байт прежнее (кандидаты из конфига + lstExtraFormNames).
        /// bCheckForeignForm — WARN «чужой формы» (FormNameFilter): в UI-режиме НЕ
        /// проверяется (пользователь осознанно выбирает любую форму, спека §2 п.3);
        /// headless — true (как сейчас).
        /// rev.12.5 (H-3b): arrTargets — цели отчёта (StorableObject[], например
        /// выбранный TerminalStrip). Передача непустого массива включает режим
        /// «отчёт по целям»: сначала пробывается документированная 4-аргументная
        /// перегрузка API 2.9 CreateEmbeddedReport(ReportBlock, Page, PointD,
        /// StorableObject[]) — без неё EPLAN строит встроенный отчёт по умолчанию
        /// (по контексту активной страницы), и геометрия соответствует не выбранному
        /// клеммнику. При исключении 4-арг. — фоллбэк на 3-арг. в той же попытке
        /// ([REPORT-TARGET]-лог). null/пустой массив (headless-путь) — поведение и
        /// вызовы без изменений.
        /// rev.15.0 (H-5/H-6): oInsertPoint — точка вставки, захваченная интеракцией
        /// TSA_INSERT_POINT (клик пользователя, [IPING]). Примечание: PointD —
        /// STRUCT (KB 2.9, страница ...PointD~_ctor(PointD3D).html — «PointD
        /// Structure»), литерал «PointD oInsertPoint = null» не компилируется
        /// (CS0453) — параметр PointD? (Nullable; C#5 допустим). Headless (null) —
        /// константная точка InsertX/InsertY в конфигу, поведение байт-в-байт
        /// прежнее (эталоны X2/X3).</summary>
        public ReportBlockReference TryCreateEmbeddedReport(Project oProject, Page oPage,
            List<string> lstExtraFormNames, string strFormNameOverride = null,
            bool bCheckForeignForm = true, StorableObject[] arrTargets = null,
            System.Nullable<PointD> oInsertPoint = null)
        {
            List<string> lstFormNames = new List<string>();
            if (!string.IsNullOrEmpty(strFormNameOverride))
            {
                lstFormNames.Add(strFormNameOverride);
                if (!lstFormNames.Contains(strFormNameOverride + " "))
                    lstFormNames.Add(strFormNameOverride + " ");
                EnsureFormInProject(oProject, strFormNameOverride);
            }
            else
            {
                foreach (string strName in FormNameCandidates())
                    if (!lstFormNames.Contains(strName)) lstFormNames.Add(strName);
                if (lstExtraFormNames != null)
                    foreach (string strName in lstExtraFormNames)
                        if (!lstFormNames.Contains(strName)) lstFormNames.Add(strName);
            }

            DocumentTypeManager.DocumentType[] arrTypes = GetReportTypeCandidates();
            List<string> lstTypeNames = new List<string>();
            foreach (DocumentTypeManager.DocumentType eType in arrTypes)
                lstTypeNames.Add(eType.ToString());
            _log.Log("[INFO] Кандидаты FormName: [" + string.Join("; ", lstFormNames.ToArray()) + "]");
            _log.Log("[INFO] Кандидаты Type:    [" + string.Join("; ", lstTypeNames.ToArray()) + "]");

            foreach (DocumentTypeManager.DocumentType eReportType in arrTypes)
            {
                foreach (string strFormName in lstFormNames)
                {
                    foreach (string strSchema in FILTER_SCHEMA_CANDIDATES)
                    {
                        try
                        {
                            ReportBlock oReportBlock = new ReportBlock();
                            oReportBlock.Create(oProject);
                            oReportBlock.FormName = strFormName;
                            oReportBlock.Type = eReportType;
                            if (strSchema.Length > 0) oReportBlock.FilterSchemaName = strSchema;

                            _log.Log("[INFO] Попытка: FormName='" + strFormName + "', Type=" + eReportType +
                                ", FilterSchemaName='" + strSchema + "' ...");
                            // rev.15.0 (H-5/H-6): oInsertPoint != null — точка клика
                            // пользователя ([IPING]); else — константная точка вставки
                            // из конфигу (headless-поведение прежнее, эталоны X2/X3).
                            PointD oLocation = oInsertPoint.HasValue
                                ? oInsertPoint.Value
                                : new PointD(AddInConfiguration.InsertX, AddInConfiguration.InsertY);
                            ReportBlockReference oReportRef;
                            if (arrTargets != null && arrTargets.Length > 0)
                            {
                                // rev.12.5 (H-3b): отчёт по целям — 4-арг. перегрузка API 2.9
                                // (Targets = выбранный TerminalStrip). При её исключении —
                                // фоллбэк на 3-арг. в той же попытке (то, что делали всегда).
                                try
                                {
                                    oReportRef = new Reports().CreateEmbeddedReport(oReportBlock,
                                        oPage, oLocation, arrTargets);
                                    _log.Log("[REPORT-TARGET] отчёт создан по целям: " +
                                        arrTargets.Length + " шт");
                                }
                                catch (Exception oTargetsException)
                                {
                                    _log.Warn("[REPORT-TARGET] ВНИМАНИЕ: 4-арг. перегрузка (цели) не прошла (" +
                                        oTargetsException.GetType().Name + ": " + oTargetsException.Message +
                                        "; например MissingMethodException — сборка без 4-арг. перегрузки)" +
                                        " — фоллбэк на 3-арг. вызов: отчёт строится БЕЗ целей, по контексту" +
                                        " активной страницы; геометрия может НЕ соответствовать выбранному" +
                                        " клеммнику — гейт [CROSSGATE] прервёт прогон");
                                    // Инвариант файла (см. XML-коммент метода): на каждую комбинацию —
                                    // свежий ReportBlock. 4-арг. вызов мог частично мутировать/вставить
                                    // oReportBlock — фоллбэк только на новом блоке с той же комбинацией.
                                    // Исключения 3-арг. фоллбэка ловит внешний catch попытки
                                    // (переход к следующей комбинации).
                                    ReportBlock oFreshBlock = new ReportBlock();
                                    oFreshBlock.Create(oProject);
                                    oFreshBlock.FormName = strFormName;
                                    oFreshBlock.Type = eReportType;
                                    if (strSchema.Length > 0) oFreshBlock.FilterSchemaName = strSchema;
                                    oReportRef = new Reports().CreateEmbeddedReport(oFreshBlock,
                                        oPage, oLocation);
                                }
                            }
                            else
                            {
                                oReportRef = new Reports().CreateEmbeddedReport(oReportBlock,
                                    oPage, oLocation);
                            }
                            _log.Log("[SUCCESS-TYPE] " + eReportType + " (FormName='" + strFormName +
                                "', FilterSchemaName='" + strSchema + "').");
                            DumpReportProperties(oReportRef);
                            // bCheckForeignForm=false (UI): пользователь осознанно
                            // выбирает любую форму — WARN «чужая форма» не выполняется.
                            if (bCheckForeignForm &&
                                strFormName.IndexOf(AddInConfiguration.FormNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                _log.Warn("ОТЧЁТ СОЗДАН НА ЧУЖОЙ ФОРМЕ '" + strFormName +
                                    "' (нет подстроки '" + AddInConfiguration.FormNameFilter + "') — результат невалиден!");
                                _log.Summarize("ВНИМАНИЕ: отчёт создан на ЧУЖОЙ форме '" + strFormName + "'.");
                            }
                            _log.Summarize("Тип отчёта, принявший форму: " + eReportType + ".");
                            return oReportRef;
                        }
                        catch (Exception oException)
                        {
                            _log.Warn("Не удалось: FormName='" + strFormName + "', Type=" + eReportType +
                                ", FilterSchemaName='" + strSchema + "': " + oException.GetType().Name + ": " +
                                oException.Message);
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>Дамп специфичных свойств созданного отчёта — фактический тип и форма,
        /// которые EPLAN записал в ReportBlockReference (могут отличаться от запрошенных).</summary>
        private void DumpReportProperties(ReportBlockReference oReportRef)
        {
            _log.Log("[INFO] --- Свойства созданного ReportBlockReference ---");
            try
            {
                AnyPropertyId[] arrIds = Properties.AllReportBlockReferencePropIDs;
                foreach (AnyPropertyId oId in arrIds)
                {
                    try
                    {
                        PropertyValue oValue = oReportRef.Properties[oId];
                        if (oValue == null || oValue.IsEmpty) continue;
                        _log.Log("[REPORT] " + DescribePropertyId(oId) + " = " + oValue);
                    }
                    catch { }
                }
            }
            catch (Exception oException)
            {
                _log.Warn("Дамп свойств отчёта не удался: " + oException.GetType().Name + ": " + oException.Message);
            }
        }

        /// <summary>Рекурсивный обход вложенной графики с логированием ДЕРЕВА (rev.11):
        /// для каждого объекта — тип, слой, для Line — геометрия; у групп — число дочерних.
        /// Группы (ReportBlockReference : Group) читаются штатно — элементы внутри групп
        /// доступны, включая их слои (GraphicalPlacement.Layer/LayerId).</summary>
        public void CollectSubPlacementsTree(Placement[] arrPlacements, List<Placement> lstResult,
            HashSet<Placement> hsVisited, int nDepth)
        {
            foreach (Placement oPlacement in arrPlacements)
            {
                if (hsVisited.Contains(oPlacement)) continue;
                hsVisited.Add(oPlacement);
                lstResult.Add(oPlacement);

                _log.Log("[TREE] " + new string(' ', nDepth * 2) + "d" + nDepth + " " +
                    DescribePlacementBrief(oPlacement));

                Group oGroup = oPlacement as Group;
                if (oGroup != null)
                {
                    Placement[] arrChildren = SafeSubPlacements(oGroup);
                    _log.Log("[TREE] " + new string(' ', nDepth * 2) + "  children=" + arrChildren.Length);
                    CollectSubPlacementsTree(arrChildren, lstResult, hsVisited, nDepth + 1);
                }
            }
        }

        private static Placement[] SafeSubPlacements(Group oGroup)
        {
            try
            {
                Placement[] arrChildren = oGroup.SubPlacements;
                return arrChildren ?? new Placement[0];
            }
            catch { return new Placement[0]; }
        }

        /// <summary>Краткое описание объекта для дерева: тип, слой; для Line — координаты и длина.</summary>
        private string DescribePlacementBrief(Placement oPlacement)
        {
            string strText = oPlacement.GetType().Name;
            GraphicalPlacement oGraphical = oPlacement as GraphicalPlacement;
            if (oGraphical == null) return strText;

            string strName = GetGraphicalLayerMember(TryGetLayer(oGraphical), "Name");
            string strLayer = strName == null ? ("<LayerId " + SafeLayerId(oGraphical) + ">") : strName.Trim();
            strText += " слой='" + strLayer + "'";

            Line oLine = oPlacement as Line;
            if (oLine != null)
            {
                PointD oStart = oLine.StartPoint;
                PointD oEnd = oLine.EndPoint;
                double dDx = oEnd.X - oStart.X;
                double dDy = oEnd.Y - oStart.Y;
                double dLen = Math.Sqrt(dDx * dDx + dDy * dDy);
                strText += " (" + oStart.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oStart.Y.ToString("F3", CultureInfo.InvariantCulture) + ")->(" +
                    oEnd.X.ToString("F3", CultureInfo.InvariantCulture) + ";" +
                    oEnd.Y.ToString("F3", CultureInfo.InvariantCulture) + ") len=" +
                    dLen.ToString("F3", CultureInfo.InvariantCulture);
            }
            return strText;
        }

        /// <summary>LayerId графического объекта (GraphicalPlacement.LayerId, short) или -1 при ошибке.</summary>
        private static short SafeLayerId(GraphicalPlacement oPlacement)
        {
            try { return oPlacement.LayerId; }
            catch { return (short)-1; }
        }

        /// <summary>GraphicalLayer графического объекта или null (в т.ч. когда объект не привязан к проекту).</summary>
        private static GraphicalLayer TryGetLayer(GraphicalPlacement oPlacement)
        {
            try { return oPlacement.Layer; }
            catch { return null; }
        }

        /// <summary>Строковый член GraphicalLayer по имени (например 'Name') через reflection —
        /// компиляция не должна зависеть от точного состава класса GraphicalLayer в 2.9.</summary>
        private static string GetGraphicalLayerMember(GraphicalLayer oLayer, string strMemberName)
        {
            if (oLayer == null) return null;
            try
            {
                PropertyInfo oProp = oLayer.GetType().GetProperty(strMemberName);
                if (oProp == null || oProp.PropertyType != typeof(string)) return null;
                return oProp.GetValue(oLayer, null) as string;
            }
            catch { return null; }
        }

        /// <summary>Имя слоя графического объекта (trim) или null.</summary>
        public static string LayerNameOf(GraphicalPlacement oPlacement)
        {
            string strName = GetGraphicalLayerMember(TryGetLayer(oPlacement), "Name");
            return strName == null ? null : strName.Trim();
        }

        // Кэш описаний id свойств (описание получается один раз через reflection).
        private static readonly Dictionary<AnyPropertyId, string> s_dicIdDescriptions =
            new Dictionary<AnyPropertyId, string>();

        /// <summary>Человекочитаемое описание id свойства. AnyPropertyId.ToString() возвращает
        /// только имя класса (урок rev.4), поэтому: 1) оператор преобразования в int через
        /// reflection; 2) свойство Name.</summary>
        private static string DescribePropertyId(AnyPropertyId oId)
        {
            if (oId == null) return "<null>";
            string strCached;
            if (s_dicIdDescriptions.TryGetValue(oId, out strCached)) return strCached;

            string strDescription = null;
            try
            {
                MethodInfo[] arrMethods = typeof(AnyPropertyId).GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (MethodInfo oMethod in arrMethods)
                {
                    if ((oMethod.Name == "op_Explicit" || oMethod.Name == "op_Implicit") &&
                        oMethod.ReturnType == typeof(int))
                    {
                        object oNumber = oMethod.Invoke(null, new object[] { oId });
                        if (oNumber is int)
                        {
                            strDescription = "№" + (int)oNumber;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (strDescription == null)
            {
                try
                {
                    PropertyInfo oNameProp = typeof(AnyPropertyId).GetProperty("Name");
                    if (oNameProp != null)
                    {
                        object oName = oNameProp.GetValue(oId, null);
                        if (oName is string && ((string)oName).Length > 0)
                            strDescription = (string)oName;
                    }
                }
                catch { }
            }

            if (strDescription == null) strDescription = oId.ToString();
            s_dicIdDescriptions[oId] = strDescription;
            return strDescription;
        }
    }
}
