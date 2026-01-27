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
            Console.Write("\r" + new string(' ', Console.WindowWidth) + "\r");
        }
        public static void Reset() 
        {
            Console.CursorVisible = true;
            Console.ResetColor();
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
            Console.CursorVisible = false;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
        }

        public static void Write(string message, ConsoleColor color = ConsoleColor.Yellow) 
        {
            Console.CursorVisible = false;
            Console.ForegroundColor = color;
            Console.Write(message);
        }

        public static void WriteProgress(string message, int position, int total, ConsoleColor color = ConsoleColor.Yellow)
        {
            ClearLine();
            Write($"\r{message} {YELLOW}{position.ToString("#,##0")} / {total.ToString("#,##0")} {CYAN}{position.GetPercentageCompleted(total)}",color);
        }

        
    }
}
