using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MyEplanActions
{
    /// <summary>Режим ориентации отчёта в настройках UI (Фаза H, спека
    /// 2026-09-25-fase-h-ui-design.md §2 п.4). Отдельный enum: ReportOrientation
    /// (Этап 3) значения Auto не имеет — тот про ФАКТИЧЕСКУЮ ориентацию отчёта,
    /// этот — про режим выбора.</summary>
    public enum SettingsOrientation
    {
        /// <summary>Геометрическое определение ориентации из дерева отчёта
        /// (спека §5) — значение по умолчанию.</summary>
        Auto,
        /// <summary>Ручной override из диалога: горизонтальная ориентация.</summary>
        Horizontal,
        /// <summary>Ручной override из диалога: вертикальная ориентация.</summary>
        Vertical
    }

    /// <summary>
    /// Персистентные настройки Add-in (Фаза H, задача H-1; спека
    /// 2026-09-25-fase-h-ui-design.md §2 п.9, §4). ЧИСТЫЙ модуль — без EPLAN-типов
    /// (компилируется и в консольный тест-раннер tests/). Файл
    /// terminal_strip_addin.settings ищется/пишется в первом СУЩЕСТВУЮЩЕМ
    /// каталоге-кандидате — те же кандидаты, что у DiagnosticLogger (ruling R1;
    /// НЕ Assembly.Location — ShadowCopy, урок п.10).
    /// Формат: строки key=value; разбор по ПЕРВОМУ '=' (значения содержат '=':
    /// TargetStrip='=HII-1.1++ЯЧ67+#2-X2'); ключ БЕЗ trim (строки файла пишем сами
    /// без отступов, ReadAllLines снимает \r\n; хвостовой пробел ключа
    /// GridPitch.&lt;форма&gt; значим — форма «Клемник_ОУ(вертикально)_addin »
    /// существует, урок п.33), значение БЕЗ trim НИ слева НИ справа (тот же
    /// урок); '#' в начале строки — комментарий, пустые строки — пропуск,
    /// строки без '=' и неизвестные ключи — пропуск; известный ключ с пустым
    /// значением — пропуск (поле остаётся дефолтным); int/double —
    /// InvariantCulture.
    /// rev.16.0: ИСКЛЮЧЕНИЕ — BlockFormat1/BlockFormat2 пустое значение
    /// ЗАПИСЫВАЕТСЯ как "" (фича «формат свойства блока» off; спека
    /// 2026-09-30-blockprop-format-design.md §3).
    /// Исключения наружу не выходят: Load при любых IO-ошибках возвращает
    /// дефолты (strUsedPath=null), Save — false.
    /// </summary>
    public class AddInSettings
    {
        /// <summary>Имя файла настроек (в каталоге лога — ruling R1).</summary>
        public const string SETTINGS_FILE_NAME = "terminal_strip_addin.settings";

        // Префикс ключей кэша шага сетки per-form (спека §2 п.6, §6).
        private const string GRID_PITCH_PREFIX = "GridPitch.";

        /// <summary>Целевой клеммник — ПОЛНОЕ имя с ведущим '=' (дефолт —
        /// AddInConfiguration.TargetStripName).</summary>
        public string TargetStrip;

        /// <summary>Имя формы отчёта; хвостовой пробел значим (урок п.33) —
        /// дефолт — AddInConfiguration.ReportFormName.</summary>
        public string Form;

        /// <summary>Библиотека кабельного символа (дефолт —
        /// AddInConfiguration.SymbolLibrary).</summary>
        public string SymbolLibrary;

        /// <summary>Имя кабельного символа (дефолт —
        /// AddInConfiguration.SymbolName).</summary>
        public string SymbolName;

        /// <summary>Индекс варианта символа в слоте H (дефолт —
        /// AddInConfiguration.SymbolVariant, спека §2 п.7).</summary>
        public int VariantH;

        /// <summary>Индекс варианта символа в слоте V (дефолт — тот же
        /// AddInConfiguration.SymbolVariant, спека §2 п.7).</summary>
        public int VariantV;

        /// <summary>Номер свойства сверки «Место сборки (видимое)» — УДАЛЕН
        /// rev.16.2 (01.10): вся фича 20202 снята, сверка 1429 не нужна
        /// (ResolveCableEnds для BP читает 20376/20377 — ТОЛЬКО как FALLBACK; основной путь — свойства соединений жил 31019/31020; фильтра 1429 по-прежнему нет).</summary>

        /// <summary>Режим выбора ориентации отчёта (дефолт — Auto:
        /// детекция из дерева отчёта).</summary>
        public SettingsOrientation OrientationMode;

        /// <summary>Выбор профиля .emc для СТУБА, слот H (дефолт — null: выбора
        /// нет). Значение — EmcProfileCatalog.EncodeSelection(file, name)
        /// ("file|name", задача 2 rev.17); consumer — задача 5/7.</summary>
        public string EmcStripH;

        /// <summary>Выбор профиля .emc для СТУБА, слот V (дефолт — null).</summary>
        public string EmcStripV;

        /// <summary>Выбор профиля .emc для кабельного УСТРОЙСТВА, слот H
        /// (дефолт — null).</summary>
        public string EmcDeviceH;

        /// <summary>Выбор профиля .emc для кабельного УСТРОЙСТВА, слот V
        /// (дефолт — null).</summary>
        public string EmcDeviceV;

        /// <summary>Выбор профиля .emc для СВЯЗИ (кабельного соединения),
        /// слот H (дефолт — null).</summary>
        public string EmcLinkH;

        /// <summary>Выбор профиля .emc для СВЯЗИ (кабельного соединения),
        /// слот V (дефолт — null).</summary>
        public string EmcLinkV;

        // Кэш шага сетки по имени формы (нижняя медиана шага стубов K4 прошлого
        // успешного прогона — спека §6). Пустой = кэша нет → fallback
        // AddInConfiguration.GhostPitchFallbackMm (потребитель — H-6).
        private readonly Dictionary<string, double> _dicGridPitch = new Dictionary<string, double>();

        /// <summary>Дефолты из AddInConfiguration (headless-значения — ruling R3):
        /// TargetStrip/Form — целевой клеммник и форма отчёта; символ и вариант —
        /// константы кабельного символа; оба слота варианта H/V — один и тот же
        /// индекс (спека §2 п.7); ориентация — Auto (детекция из формы).</summary>
        public AddInSettings()
        {
            TargetStrip = AddInConfiguration.TargetStripName;
            Form = AddInConfiguration.ReportFormName;
            SymbolLibrary = AddInConfiguration.SymbolLibrary;
            SymbolName = AddInConfiguration.SymbolName;
            VariantH = AddInConfiguration.SymbolVariant;
            VariantV = AddInConfiguration.SymbolVariant;
            OrientationMode = SettingsOrientation.Auto;
            // rev.17 (спека 2026-10-01-ui-emc-profiles): профили .emc не выбраны —
            // null (пустое значение Save не пишет; после Load поле остаётся null).
        }

        /// <summary>Шаг сетки кэша для формы: записи нет — false, dPitch=0.
        /// Имя формы сравнивается КАК ЕСТЬ (без trim, включая хвостовой пробел —
        /// урок п.33).</summary>
        public bool TryGetGridPitch(string strFormName, out double dPitch)
        {
            return _dicGridPitch.TryGetValue(strFormName ?? string.Empty, out dPitch);
        }

        /// <summary>Записать шаг сетки формы (перезапись: второй Set побеждает).
        /// Имя формы сохраняется КАК ЕСТЬ (без trim, включая хвостовой пробел —
        /// урок п.33).</summary>
        public void SetGridPitch(string strFormName, double dPitch)
        {
            _dicGridPitch[strFormName ?? string.Empty] = dPitch;
        }

        /// <summary>Загрузка настроек: первый существующий каталог-кандидат; файла
        /// нет (или каталога нет ни одного, или IO-ошибка) — дефолты,
        /// strUsedPath=null. Повреждённые строки/непарсable значения пропускаются
        /// (соответствующее поле остаётся дефолтным). Ключ НЕ триммится
        /// (пишется нами без отступов; хвостовой пробел ключа
        /// GridPitch.&lt;форма&gt; значим — урок п.33); известный ключ с пустым
        /// значением пропускается — поле остаётся дефолтным (ИСКЛЮЧЕНИЕ
        /// rev.16.0: BlockFormat1/BlockFormat2 пустое записывается как "").</summary>
        public static AddInSettings Load(string[] arrDirCandidates, out string strUsedPath)
        {
            strUsedPath = null;
            try
            {
                string strDir = FirstExistingDir(arrDirCandidates);
                if (strDir == null)
                    return new AddInSettings();   // каталога нет ни одного — дефолты
                string strPath = Path.Combine(strDir, SETTINGS_FILE_NAME);
                if (!File.Exists(strPath))
                    return new AddInSettings();   // файла нет — дефолты

                AddInSettings oSettings = new AddInSettings();
                foreach (string strLine in File.ReadAllLines(strPath))
                {
                    string strTrimmed = strLine.Trim();
                    if (strTrimmed.Length == 0 || strTrimmed.StartsWith("#", StringComparison.Ordinal))
                        continue;   // пустая строка или комментарий
                    int iEq = strLine.IndexOf('=');
                    if (iEq < 0)
                        continue;   // повреждённая строка (нет '=') — пропуск
                    string strKey = strLine.Substring(0, iEq);   // БЕЗ trim (урок п.33)
                    string strValue = strLine.Substring(iEq + 1);   // БЕЗ trim (урок п.33)
                    ApplyValue(oSettings, strKey, strValue);
                }
                strUsedPath = strPath;
                return oSettings;
            }
            catch
            {
                return new AddInSettings();   // IO-ошибка чтения — дефолты
            }
        }

        /// <summary>Сохранение настроек: в первый СУЩЕСТВУЮЩИЙ каталог-кандидат
        /// (каталоги НЕ создаются; ни один не существует — false, strUsedPath=null).
        /// null/пустые строковые значения НЕ пишутся — после Load соответствующее
        /// поле остаётся дефолтным (ИСКЛЮЧЕНИЕ rev.16.0: BlockFormat1/BlockFormat2
        /// пишутся ВСЕГДА, пустое значение = явное отключение механизма, спека §3;
        /// иначе Load вернул бы дефолт и фича самопере-включилась бы).
        /// Атомарная запись: сперва во временный файл
        /// SETTINGS_FILE_NAME + ".tmp" в том же каталоге, затем File.Replace
        /// (замена невозможна — в т.ч. файла ещё нет — fallback delete+move);
        /// сбой на любом шаге — false, временный файл удаляется по возможности.
        /// UTF-8 без BOM (File.WriteAllText по умолчанию), переводы строк CRLF.</summary>
        public static bool Save(AddInSettings oSettings, string[] arrDirCandidates, out string strUsedPath)
        {
            strUsedPath = null;
            if (oSettings == null)
                return false;
            string strTmp = null;
            try
            {
                string strDir = FirstExistingDir(arrDirCandidates);
                if (strDir == null)
                    return false;
                string strPath = Path.Combine(strDir, SETTINGS_FILE_NAME);
                strTmp = strPath + ".tmp";   // временный файл — в том же каталоге

                List<string> lstLines = new List<string>();
                if (!string.IsNullOrEmpty(oSettings.TargetStrip))
                    lstLines.Add("TargetStrip=" + oSettings.TargetStrip);
                if (!string.IsNullOrEmpty(oSettings.Form))
                    lstLines.Add("Form=" + oSettings.Form);
                if (!string.IsNullOrEmpty(oSettings.SymbolLibrary))
                    lstLines.Add("SymbolLibrary=" + oSettings.SymbolLibrary);
                if (!string.IsNullOrEmpty(oSettings.SymbolName))
                    lstLines.Add("SymbolName=" + oSettings.SymbolName);
                lstLines.Add("VariantH=" + oSettings.VariantH.ToString(CultureInfo.InvariantCulture));
                lstLines.Add("VariantV=" + oSettings.VariantV.ToString(CultureInfo.InvariantCulture));
                // rev.16.2 (решение 01.10: «Формат блока — теперь не используется,
                // удалить»): блок BlockFormat1/2/Index/BlockCompareProp и вся
                // запись 20202[x] на символ кабеля удалены. Unknown-ключи старых
                // settings-файлов Load пропускает молча (default-ветка).
                lstLines.Add("OrientationMode=" + OrientationName(oSettings.OrientationMode));
                // rev.17 (спека 2026-10-01-ui-emc-profiles): 6 профилей .emc —
                // строковые поля, пишутся только непустые ("file|name"). Пусто
                // (null или "") — строки нет; после Load поле остаётся null.
                if (!string.IsNullOrEmpty(oSettings.EmcStripH))
                    lstLines.Add("EmcStripH=" + oSettings.EmcStripH);
                if (!string.IsNullOrEmpty(oSettings.EmcStripV))
                    lstLines.Add("EmcStripV=" + oSettings.EmcStripV);
                if (!string.IsNullOrEmpty(oSettings.EmcDeviceH))
                    lstLines.Add("EmcDeviceH=" + oSettings.EmcDeviceH);
                if (!string.IsNullOrEmpty(oSettings.EmcDeviceV))
                    lstLines.Add("EmcDeviceV=" + oSettings.EmcDeviceV);
                if (!string.IsNullOrEmpty(oSettings.EmcLinkH))
                    lstLines.Add("EmcLinkH=" + oSettings.EmcLinkH);
                if (!string.IsNullOrEmpty(oSettings.EmcLinkV))
                    lstLines.Add("EmcLinkV=" + oSettings.EmcLinkV);
                foreach (KeyValuePair<string, double> oKv in oSettings._dicGridPitch)
                    lstLines.Add(GRID_PITCH_PREFIX + oKv.Key + "=" +
                        oKv.Value.ToString("R", CultureInfo.InvariantCulture));

                File.WriteAllText(strTmp, string.Join("\r\n", lstLines.ToArray()) + "\r\n");
                try
                {
                    File.Replace(strTmp, strPath, null);
                }
                catch (IOException)
                {
                    // старого файла ещё нет (FileNotFoundException) либо замена
                    // недоступна — fallback: копирование поверх (атомарность
                    // Save гарантирует: сбой Copy оставит на диске либо старый,
                    // либо новый файл целиком — settings не потеряются)
                    File.Copy(strTmp, strPath, true);
                    try { File.Delete(strTmp); }
                    catch { }
                }
                strUsedPath = strPath;
                return true;
            }
            catch
            {
                if (!string.IsNullOrEmpty(strTmp))
                {
                    try { File.Delete(strTmp); }
                    catch { }   // cleanup временного файла — сбой удаления игнорируем
                }
                return false;
            }
        }

        /// <summary>Первый существующий каталог из кандидатов (null — ни один;
        /// сами каталоги не создаются).</summary>
        private static string FirstExistingDir(string[] arrDirCandidates)
        {
            if (arrDirCandidates == null)
                return null;
            foreach (string strCandidate in arrDirCandidates)
            {
                if (string.IsNullOrEmpty(strCandidate))
                    continue;
                try
                {
                    if (Directory.Exists(strCandidate))
                        return strCandidate;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Имя режима ориентации для файла (roundtrip с разбором в Load).</summary>
        private static string OrientationName(SettingsOrientation eMode)
        {
            switch (eMode)
            {
                case SettingsOrientation.Horizontal: return "Horizontal";
                case SettingsOrientation.Vertical: return "Vertical";
                default: return "Auto";
            }
        }

        /// <summary>Применение пары key=value: известный ключ — поле,
        /// GridPitch.&lt;форма&gt; — кэш шага; непарсable значения, известный
        /// ключ с пустым значением и неизвестные ключи — пропуск (поле остаётся
        /// прежним; ИСКЛЮЧЕНИЕ rev.16.0: BlockFormat1/2 — пустое записывается
        /// как "").</summary>
        private static void ApplyValue(AddInSettings oSettings, string strKey, string strValue)
        {
            switch (strKey)
            {
                case "TargetStrip":
                    if (strValue.Length == 0) break;   // пустое значение — дефолт сохраняется
                    oSettings.TargetStrip = strValue;
                    break;
                case "Form":
                    if (strValue.Length == 0) break;   // пустое значение — дефолт сохраняется
                    oSettings.Form = strValue;
                    break;
                case "SymbolLibrary":
                    if (strValue.Length == 0) break;   // пустое значение — дефолт сохраняется
                    oSettings.SymbolLibrary = strValue;
                    break;
                case "SymbolName":
                    if (strValue.Length == 0) break;   // пустое значение — дефолт сохраняется
                    oSettings.SymbolName = strValue;
                    break;
                case "VariantH":
                    {
                        int iValue;
                        if (int.TryParse(strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out iValue))
                            oSettings.VariantH = iValue;
                        break;
                    }
                case "VariantV":
                    {
                        int iValue;
                        if (int.TryParse(strValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out iValue))
                            oSettings.VariantV = iValue;
                        break;
                    }
                // rev.16.2 (решение 01.10): ключи BlockFormat*/BlockCompareProp
                // удалены — приходят из старых настроек → silently ignore
                // (default-ветка «неизвестный ключ — пропуск»).
                case "OrientationMode":
                    if (string.Equals(strValue, "Horizontal", StringComparison.OrdinalIgnoreCase))
                        oSettings.OrientationMode = SettingsOrientation.Horizontal;
                    else if (string.Equals(strValue, "Vertical", StringComparison.OrdinalIgnoreCase))
                        oSettings.OrientationMode = SettingsOrientation.Vertical;
                    else if (string.Equals(strValue, "Auto", StringComparison.OrdinalIgnoreCase))
                        oSettings.OrientationMode = SettingsOrientation.Auto;
                    break;
                // rev.17 (спека 2026-10-01-ui-emc-profiles): 6 профилей .emc.
                // Пустое значение — пропуск (поле остаётся null, дефолт).
                case "EmcStripH":
                    if (strValue.Length == 0) break;
                    oSettings.EmcStripH = strValue;
                    break;
                case "EmcStripV":
                    if (strValue.Length == 0) break;
                    oSettings.EmcStripV = strValue;
                    break;
                case "EmcDeviceH":
                    if (strValue.Length == 0) break;
                    oSettings.EmcDeviceH = strValue;
                    break;
                case "EmcDeviceV":
                    if (strValue.Length == 0) break;
                    oSettings.EmcDeviceV = strValue;
                    break;
                case "EmcLinkH":
                    if (strValue.Length == 0) break;
                    oSettings.EmcLinkH = strValue;
                    break;
                case "EmcLinkV":
                    if (strValue.Length == 0) break;
                    oSettings.EmcLinkV = strValue;
                    break;
                default:
                    if (strKey.StartsWith(GRID_PITCH_PREFIX, StringComparison.Ordinal))
                    {
                        string strForm = strKey.Substring(GRID_PITCH_PREFIX.Length);
                        double dPitch;
                        if (strForm.Length > 0 &&
                            double.TryParse(strValue, NumberStyles.Float, CultureInfo.InvariantCulture, out dPitch))
                            oSettings.SetGridPitch(strForm, dPitch);
                    }
                    break;   // неизвестный ключ — пропуск
            }
        }
    }
}
