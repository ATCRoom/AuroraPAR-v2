using System.Windows;
using System.Windows.Input;

namespace AuroraPAR
{
    /// <summary>
    /// Gives the keyboard back to the window after a text field (APT, ICAO, DH, GP...): the keys P, A, T, L, C and the
    /// arrows are read by the window. <see cref="Keyboard.ClearFocus"/> alone leaves the logical focus on the text box,
    /// and a later click on a part that takes no focus gave the cursor back to it.
    /// </summary>
    internal static class FocusHelper
    {
        public static void ReleaseKeyboard(DependencyObject from)
        {
            Window? window = Window.GetWindow(from);
            if (window == null)
            {
                Keyboard.ClearFocus();
                return;
            }
            FocusManager.SetFocusedElement(window, window);
            Keyboard.Focus(window);
        }
    }
}
