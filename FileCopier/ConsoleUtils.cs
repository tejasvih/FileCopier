namespace FileCopier
{
    /// <summary>
    /// Console output with one color per kind of line: headings and run information cyan, folder
    /// scanning blue, details and skipped files dark gray, copied or verified files green, warnings
    /// yellow, errors red. Each line restores the default
    /// color, so plain Console.WriteLine text is never tinted by the line before it.
    /// </summary>
    internal static class ConsoleUtils
    {
        public static void Reset()
        {
            if (Console.IsOutputRedirected) return;
            try
            {
                Console.ResetColor();
            }
            catch (IOException)
            {
                // Console presentation must not affect transfer results.
            }
        }

        public static bool IsQuitRequested()
        {
            if (Console.IsInputRedirected) return false;
            try
            {
                return Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Q;
            }
            catch (IOException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Lets the console show text in any language, such as a phone name in Japanese.</summary>
        public static void UseUnicodeOutput()
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (IOException)
            {
                // Console presentation must not affect transfer results.
            }
        }

        public static void WriteHeading(string message) => WriteLine(message, ConsoleColor.Cyan);
        public static void WriteStep(string message) => WriteLine(message, ConsoleColor.Blue);
        public static void WriteDetail(string message) => WriteLine(message, ConsoleColor.DarkGray);
        public static void WriteDone(string message) => WriteLine(message, ConsoleColor.Green);
        public static void WriteSkipped(string message) => WriteLine(message, ConsoleColor.DarkGray);
        public static void WriteWarning(string message) => WriteLine(message, ConsoleColor.Yellow);
        public static void WriteError(string message) => WriteLine(message, ConsoleColor.Red);
        public static void WriteSuccess(string message) => WriteLine(message, ConsoleColor.Green);

        /// <summary>Writes a line in the given color, or the default color when none is given.</summary>
        public static void WriteLine(string message, ConsoleColor? color = null)
        {
            if (color is not { } value || Console.IsOutputRedirected)
            {
                Console.WriteLine(message);
                return;
            }

            // Color codes travel in the same write as the text, so terminals that render the
            // console asynchronously (Windows Terminal, VS Code) cannot apply them to the wrong text.
            if (UseVirtualTerminal)
            {
                Console.WriteLine(Colorize(message, value));
                return;
            }

            var colored = TrySetColor(value);
            try
            {
                Console.WriteLine(message);
            }
            finally
            {
                if (colored) TryResetColor();
            }
        }

        /// <summary>
        /// Text wrapped in color codes when this console shows them, for text written through a
        /// TextWriter such as the menu; plain text otherwise.
        /// </summary>
        public static string Colorize(string text, ConsoleColor color) =>
            UseVirtualTerminal ? $"\x1b[{AnsiCode(color)}m{text}\x1b[39m" : text;

        private static readonly bool UseVirtualTerminal = EnableVirtualTerminal();

        // Turns on ANSI color codes for this console; Windows 10 and later support them.
        private static bool EnableVirtualTerminal()
        {
            const uint EnableVirtualTerminalProcessing = 0x0004;
            if (Console.IsOutputRedirected || !OperatingSystem.IsWindows()) return false;
            var output = GetStdHandle(-11);
            return GetConsoleMode(output, out var mode) &&
                ((mode & EnableVirtualTerminalProcessing) != 0 || SetConsoleMode(output, mode | EnableVirtualTerminalProcessing));
        }

        private static int AnsiCode(ConsoleColor color) => color switch
        {
            ConsoleColor.Black => 30,
            ConsoleColor.DarkRed => 31,
            ConsoleColor.DarkGreen => 32,
            ConsoleColor.DarkYellow => 33,
            ConsoleColor.DarkBlue => 34,
            ConsoleColor.DarkMagenta => 35,
            ConsoleColor.DarkCyan => 36,
            ConsoleColor.Gray => 37,
            ConsoleColor.DarkGray => 90,
            ConsoleColor.Red => 91,
            ConsoleColor.Green => 92,
            ConsoleColor.Yellow => 93,
            ConsoleColor.Blue => 94,
            ConsoleColor.Magenta => 95,
            ConsoleColor.Cyan => 96,
            _ => 97,
        };

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern nint GetStdHandle(int handle);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(nint handle, out uint mode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(nint handle, uint mode);

        private static bool TrySetColor(ConsoleColor color)
        {
            try
            {
                Console.ForegroundColor = color;
                return true;
            }
            catch (IOException)
            {
                // Console presentation must not affect transfer results.
                return false;
            }
        }

        private static void TryResetColor()
        {
            try
            {
                Console.ResetColor();
            }
            catch (IOException)
            {
                // Console presentation must not affect transfer results.
            }
        }
    }
}
