using System;

namespace MyEplanActions
{
    /// <summary>Чистый распознаватель Esc-отмены (B1, Track B 01.10.2026):
    /// Esc-отмена интеракции TSA_INSERT_POINT. EscCancelFilter (WinForms
    /// IMessageFilter) ставится/снимается хуком вокруг цикла ожидания;
    /// Application.AddMessageFilter видит ВСЕ сообщения очереди ДО
    /// диспетчеризации — не зависит от фокуса GED, поэтому Esc ловится
    /// гарантированно, пока цикл ожидания качает очередь DoEvents.
    /// WM_SYSKEYDOWN (0x0104) сознательно НЕ считаем отменой — это Alt+Esc,
    /// системная комбинация. Файл компилируется и в тест-раннер
    /// (tests\build_tests.bat, ловушка п.66) — поэтому БЕЗ WinForms/EPLAN
    /// usings, вся логика — чистое сравнение констант (KB: WM_KEYDOWN=0x0100,
    /// WM_KEYUP=0x0101, WM_SYSKEYDOWN=0x0104, VK_ESCAPE=0x1B).</summary>
    public static class EscKey
    {
        public const int WM_KEYDOWN = 0x0100;
        public const int VK_ESCAPE = 0x1B;

        /// <summary>true — пара (сообщение, wParam) = «нажат Esc»: только
        /// WM_KEYDOWN с wParam==VK_ESCAPE. Все остальные сообщения/клавиши —
        /// false (в т.ч. отпускание 0x0101 и системный WM_SYSKEYDOWN 0x0104).</summary>
        public static bool IsCancelKeyDown(int iMsg, IntPtr pWParam)
        {
            return iMsg == WM_KEYDOWN && pWParam == (IntPtr)VK_ESCAPE;
        }
    }
}
