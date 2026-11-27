using StillSaneExile.Desktop.Infrastructure;
using StillSaneExile.Desktop.Services;

namespace StillSaneExile.Desktop.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+Alt+C")]
    [InlineData("F3")]
    [InlineData("RightAlt")]
    [InlineData("Ctrl+Shift+D")]
    [InlineData("Mouse4")]
    [InlineData("Mouse5")]
    [InlineData("Ctrl+Mouse4")]
    [InlineData("Shift+Alt+Mouse5")]
    public void Bindings_print_back_as_they_were_stored(string text) => Assert.Equal(text, Hotkey.Parse(text)?.ToString());

    [Fact]
    public void Values_stored_before_side_buttons_parse_exactly_as_before()
    {
        Assert.Equal(new Hotkey(0x43, Ctrl: true, Shift: false, Alt: false), Hotkey.Parse("Ctrl+C"));
        Assert.Equal(new Hotkey(0x72, false, false, false), Hotkey.Parse("F3"));
        Assert.Equal(new Hotkey(0xA5, false, false, false), Hotkey.Parse("RightAlt"));
        Assert.Equal(new Hotkey(0x43, Ctrl: true, Shift: false, Alt: true), Hotkey.Parse("ctrl + alt + c"));
        Assert.True(Hotkey.Parse("Ctrl+C")!.IsNativeCopy);
    }

    [Fact]
    public void Side_buttons_use_the_Windows_virtual_key_codes_and_read_plainly()
    {
        var back = Hotkey.Parse("mouse4")!;
        var forward = Hotkey.Parse("Mouse5")!;

        Assert.Equal(0x05, back.Vk);
        Assert.Equal(0x06, forward.Vk);
        Assert.True(back.IsMouseButton);
        Assert.False(back.IsNativeCopy);
        Assert.Equal("Mouse 4 (back)", back.Label);
        Assert.Equal("Mouse 5 (forward)", forward.Label);
        Assert.Equal("Ctrl+Mouse 4 (back)", Hotkey.Parse("Ctrl+Mouse4")!.Label);
        Assert.Equal("Ctrl+C", Hotkey.Parse("Ctrl+C")!.Label);
        Assert.False(Hotkey.Parse("F3")!.IsMouseButton);
    }

    [Theory]
    [InlineData("Mouse1")]
    [InlineData("Mouse3")]
    [InlineData("LeftButton")]
    [InlineData("Ctrl")]
    [InlineData("")]
    public void The_main_mouse_buttons_and_nonsense_are_not_bindings(string text) => Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void A_side_button_binding_survives_a_restart()
    {
        using var dir = new TempDir();
        new Settings(new Store(dir.Sub("sse.db"))).PeekKey = Hotkey.Parse("Mouse5")!;

        Assert.Equal("Mouse5", new Store(dir.Sub("sse.db")).Get("keys.peek"));
        Assert.Equal(Hotkey.Parse("Mouse5"), new Settings(new Store(dir.Sub("sse.db"))).PeekKey);
    }
}
