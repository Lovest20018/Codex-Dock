using System.Runtime.InteropServices;

namespace CodexConfig.Core;

public interface IUserEnvironment
{
    string? Read();
    void Write(string? value);
}

public sealed class WindowsUserEnvironment : IUserEnvironment
{
    public const string Variable = "CODEX_AUTHAPI_BASE_URL";
    public string? Read() => Environment.GetEnvironmentVariable(Variable, EnvironmentVariableTarget.User);
    public void Write(string? value)
    {
        // HKCU only. No machine-wide changes, no administrator rights required.
        Environment.SetEnvironmentVariable(Variable, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(Variable, value, EnvironmentVariableTarget.Process);
        _ = SendMessageTimeout(new IntPtr(0xffff), 0x001a, UIntPtr.Zero, "Environment", 0x0002, 2000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        string lParam, uint flags, uint timeout, out UIntPtr result);
}
