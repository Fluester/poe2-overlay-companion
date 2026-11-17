using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion.Tests;

public sealed class OverlayFocusTests
{
    private static void RunOnStaThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }

    [Fact]
    public void ShouldBlockTextFocus_BlocksOnlyTextBoxesAndPasswordBoxesWhenLocked()
    {
        RunOnStaThread(() =>
        {
            var tb = new TextBox();
            var rtb = new RichTextBox();
            var pb = new PasswordBox();
            var btn = new Button();
            var chk = new CheckBox();

            Assert.True(Win32OverlayHelper.ShouldBlockTextFocus(false, tb));
            Assert.True(Win32OverlayHelper.ShouldBlockTextFocus(false, rtb));
            Assert.True(Win32OverlayHelper.ShouldBlockTextFocus(false, pb));
            Assert.False(Win32OverlayHelper.ShouldBlockTextFocus(false, btn));
            Assert.False(Win32OverlayHelper.ShouldBlockTextFocus(false, chk));
            Assert.False(Win32OverlayHelper.ShouldBlockTextFocus(true, tb));
            Assert.False(Win32OverlayHelper.ShouldBlockTextFocus(true, pb));
        });
    }

    [Fact]
    public void OverlayTypingToggle_BlocksTextBoxWhenLocked_AllowsWhenEnabled_AndClearsFocusOnDisable()
    {
        RunOnStaThread(() =>
        {
            bool allowTyping = false;
            int buttonClicks = 0;

            var tb = new TextBox { Width = 120, Height = 24 };
            var pb = new PasswordBox { Width = 120, Height = 24 };
            var btn = new Button { Content = "Click", Width = 80, Height = 24 };
            btn.Click += (_, _) => buttonClicks++;

            var panel = new StackPanel();
            panel.Children.Add(tb);
            panel.Children.Add(pb);
            panel.Children.Add(btn);

            var win = new Window
            {
                Width = 220,
                Height = 160,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = panel
            };
            win.PreviewGotKeyboardFocus += (_, e) => Win32OverlayHelper.HandlePreviewGotTextFocus(allowTyping, e);

            try
            {
                win.Show();
                win.Activate();

                // 1. Locked: TextBox and PasswordBox cannot acquire keyboard focus, while Button still clicks
                Assert.False(tb.Focus());
                Assert.False(tb.IsKeyboardFocused);
                Assert.False(pb.Focus());
                Assert.False(pb.IsKeyboardFocused);

                Assert.True(btn.Focus());
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, buttonClicks);

                // 2. Enable Input ON: TextBox and PasswordBox can take keyboard focus
                allowTyping = true;
                Assert.True(tb.Focus());
                Assert.True(tb.IsKeyboardFocused);
                Assert.Same(tb, Keyboard.FocusedElement);

                Assert.True(pb.Focus());
                Assert.True(pb.IsKeyboardFocused);

                // 3. Disable Input: clears both WPF keyboard focus and FocusManager element, and re-blocks TextBox
                allowTyping = false;
                Win32OverlayHelper.ReleaseTypingFocus(win, returnFocusToGame: false);

                Assert.Null(Keyboard.FocusedElement);
                Assert.Null(FocusManager.GetFocusedElement(win));
                Assert.False(tb.IsKeyboardFocused);
                Assert.False(pb.IsKeyboardFocused);
                Assert.False(tb.Focus());
                Assert.False(tb.IsKeyboardFocused);
            }
            finally
            {
                win.Close();
            }
        });
    }
}
