using System;
using System.Windows.Forms;

namespace MyEplanActions
{
    /// <summary>Фильтр Esc-отмены (B1, Track B 01.10.2026): ставится хуком
    /// (TryPickInsertPoint, AnalyzeAction) ДО запуска экшена
    /// XGedStartInteractionAction, снимается в finally цикла ожидания и на
    /// раннем выходе !bLaunched — окно = запуск экшена + цикл ожидания
    /// (итерация 2b: ФАКТ стенда — фильтр видит сообщения блокирующего насоса
    /// Execute: [IPING-ESC] через ~2 с после запуска при возврате Invoke
    /// через ~90 с). AddMessageFilter видит ВСЕ сообщения очереди до
    /// диспетчеризации — не зависит от фокуса GED (KB: WinForms
    /// IMessageFilter.PreFilterMessage). Сообщения НИКОГДА не глотаются
    /// (return false) — Esc доходит до EPLAN штатно. Диагноз — через шимм
    /// InsertPointInteraction.NoteEscFilter (Buf приватен) в буфер
    /// [IPING-DUMP]/файл пробы. НЕ для тест-раннера (тянет WinForms).
    /// Итерация 2 (01.10.2026): фильтр не только ставит флаг — вызывает
    /// TryMarkCancelled (Cancelled=true + ClearCursor + статус-строка): рамка
    /// гаснет мгновенно, пайплайн гарантированно уходит в тихую отмену;
    /// движковый abort доезжает позже (задержка ~14 с — факт стенда) и НЕ
    /// нужен для исхода.</summary>
    public class EscCancelFilter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message oMsg)
        {
            try
            {
                if (EscKey.IsCancelKeyDown(oMsg.Msg, oMsg.WParam))
                {
                    InsertPointInteraction.EscPressed = true;
                    InsertPointInteraction.NoteEscFilter("[IPING-ESC] WM_KEYDOWN VK_ESCAPE — отмена по Esc");
                    InsertPointInteraction.TryMarkCancelled();
                }
            }
            catch (Exception) { }
            return false;   // никогда не глотаем сообщения
        }
    }
}
