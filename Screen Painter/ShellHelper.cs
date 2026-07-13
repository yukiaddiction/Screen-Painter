using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Screen_Painter;

public static class ShellHelper
{
    public static Task DisplayAlert(string title, string message, string cancel)
        => Shell.Current?.DisplayAlert(title, message, cancel) ?? Task.CompletedTask;

    public static Task<bool> DisplayAlert(string title, string message, string accept, string cancel)
        => Shell.Current?.DisplayAlert(title, message, accept, cancel) ?? Task.FromResult(false);

    public static Task<string?> DisplayPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string? placeholder = null, Keyboard? keyboard = null)
        => Shell.Current?.DisplayPromptAsync(title, message, accept, cancel, placeholder, AppConstants.PromptMaxLength, keyboard, null) ?? Task.FromResult<string?>(string.Empty);

    public static Task<string?> DisplayActionSheet(string title, string cancel, string? destruction, params string[] buttons)
        => Shell.Current?.DisplayActionSheet(title, cancel, destruction, buttons) ?? Task.FromResult<string?>(null);

    public static Task GoToAsync(string route)
        => Shell.Current?.GoToAsync(route) ?? Task.CompletedTask;

    public static Task GoToAsync(string route, IDictionary<string, object> parameters)
        => Shell.Current?.GoToAsync(route, parameters) ?? Task.CompletedTask;
}
