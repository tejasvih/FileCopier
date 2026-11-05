using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileCopier
{
    internal class ConsoleUtils
    {
        public static string NL = Environment.NewLine; // shortcut
        public static string NORMAL = Console.IsOutputRedirected ? "" : "\x1b[39m";
        public static string RED = Console.IsOutputRedirected ? "" : "\x1b[91m";
        public static string GREEN = Console.IsOutputRedirected ? "" : "\x1b[92m";
        public static string YELLOW = Console.IsOutputRedirected ? "" : "\x1b[93m";
        public static string BLUE = Console.IsOutputRedirected ? "" : "\x1b[94m";
        public static string MAGENTA = Console.IsOutputRedirected ? "" : "\x1b[95m";
        public static string CYAN = Console.IsOutputRedirected ? "" : "\x1b[96m";
        public static string GREY = Console.IsOutputRedirected ? "" : "\x1b[97m";
        public static string BOLD = Console.IsOutputRedirected ? "" : "\x1b[1m";
        public static string NOBOLD = Console.IsOutputRedirected ? "" : "\x1b[22m";
        public static string UNDERLINE = Console.IsOutputRedirected ? "" : "\x1b[4m";
        public static string NOUNDERLINE = Console.IsOutputRedirected ? "" : "\x1b[24m";
        public static string REVERSE = Console.IsOutputRedirected ? "" : "\x1b[7m";
        public static string NOREVERSE = Console.IsOutputRedirected ? "" : "\x1b[27m";

        public static void ClearLine()
        {
            if (Console.IsOutputRedirected) return;
            try
            {
                Console.Write("\r" + new string(' ', Math.Max(0, Console.WindowWidth - 1)) + "\r");
            }
            catch (IOException)
            {
                // A detached console cannot provide its window width.
            }
        }
        public static void Reset() 
        {
            if (Console.IsOutputRedirected) return;
            try
            {
                Console.CursorVisible = true;
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

        private static void SetAppearance(ConsoleColor color)
        {
            if (Console.IsOutputRedirected) return;
            try
            {
                Console.CursorVisible = false;
                Console.ForegroundColor = color;
            }
            catch (IOException)
            {
                // Console presentation must not affect transfer results.
            }
        }

        public static void WriteError(string message)
        {
            WriteLine(message, ConsoleColor.Red);
        }

        public static void WriteWarning(string message)
        {
            WriteLine(message, ConsoleColor.Yellow);
        }

        public static void WriteSuccess(string message)
        {
            WriteLine(message, ConsoleColor.Green);
        }

        public static void WriteLine(string message, ConsoleColor color = ConsoleColor.Yellow) 
        {
            SetAppearance(color);
            Console.WriteLine(message);
        }

        public static void Write(string message, ConsoleColor color = ConsoleColor.Yellow) 
        {
            SetAppearance(color);
            Console.Write(message);
        }

        public static void WriteProgress(string message, int position, int total, ConsoleColor color = ConsoleColor.Yellow)
        {
            ClearLine();
            Write($"\r{message} {YELLOW}{position.ToString("#,##0")} / {total.ToString("#,##0")} {CYAN}{position.GetPercentageCompleted(total)}",color);
        }

        
    }
}
