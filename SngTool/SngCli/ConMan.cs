using System;
using System.Diagnostics;
using System.Threading;

namespace SngCli
{
    public static class ConMan
    {
        private static int progressItems;
        private static int progress;
        private static Stopwatch stopwatch;
        private static readonly object consoleLock = new object();
        private static bool progressActive = false;
        private static bool errorDisableOutput = false;
        private static int updateInterval = 80;
        private static Thread? updateThread;

        static ConMan()
        {
            progress = 0;
            AppDomain.CurrentDomain.ProcessExit += (s, ev) => DisableProgress();
            Console.CancelKeyPress += (s, ev) => DisableProgress();
            stopwatch = new Stopwatch();
        }

        public static void UpdateProgress(int value)
        {
            progress = value;
        }

        public static void EnableProgress(int totalItems)
        {
            progressItems = totalItems;
            Console.CursorVisible = false;
            progressActive = true;
            progress = 0;
            stopwatch.Start();
            updateThread = new Thread(() =>
            {
                var sleepTime = updateInterval / 2;
                while (progressActive)
                {
                    lock (consoleLock)
                    {
                        DrawProgressBar();
                    }
                    Thread.Sleep(16);
                }
            });
            updateThread.Start();

        }

        public static void DisableProgress(bool error = false)
        {
            if (!progressActive)
                return;
            DrawProgressBar();
            Console.WriteLine();
            progressActive = false;
            errorDisableOutput = error;
            progressItems = 0;
            Console.CursorVisible = true;
            stopwatch.Reset();
            updateThread = null;
        }

        public static void Out(string message)
        {
            if (!progressActive)
            {
                if (!errorDisableOutput)
                    Console.WriteLine(message);
                return;
            }

            lock (consoleLock)
            {
                var width = Console.BufferWidth;
                int linePadding;
                // Deal with line wrapping
                if (message.Length > width)
                {
                    int lineCount = (int)Math.Ceiling((float)message.Length / width);
                    Console.Write(new string('\n', lineCount));
                    Console.CursorTop -= lineCount;
                    // Line padding is only used for the first line and since we know
                    // that we are going to always fill it up it's not needed here.
                    linePadding = 0;
                }
                else
                {
                    Console.WriteLine();
                    Console.CursorTop -= 1;
                    linePadding = width - message.Length;
                }
                Console.WriteLine(message + new string(' ', linePadding));
                DrawProgressBar();
            }
        }

        private static TimeSpan lastSpinner = default;
        private static int spinIndex = 0;

        private static void DrawProgressBar()
        {

            if (!progressActive)
            {
                return;
            }

            var original = Console.GetCursorPosition();
            Console.CursorTop = Console.WindowTop + Console.WindowHeight - 1;


            float percent = (float)progress / progressItems;

            var width = Console.BufferWidth - 25;

            int progressBarFilledLength = (int)(width * percent);
            int progressBarRemain = (int)Math.Round((width * percent) - progressBarFilledLength);
            int progressBarEmptyLength = width - progressBarFilledLength - progressBarRemain;

            string progressBarFilled = new string('=', progressBarFilledLength);
            string progressHalf = new string('-', progressBarRemain);
            string progressBarEmpty = new string(' ', progressBarEmptyLength);

            var spinnerElapsed = stopwatch.Elapsed - lastSpinner;
            if (spinnerElapsed.Milliseconds > updateInterval)
            {
                spinIndex = (spinIndex + 1) % SpinnerChars.Length;
                lastSpinner = stopwatch.Elapsed;
            }

            var time = stopwatch.Elapsed;

            string elapsedTime = string.Format("{0:D2}:{1:D2}:{2:D2}", time.Hours, time.Minutes, time.Seconds);

            Console.Write($"[{progressBarFilled}{progressHalf}{progressBarEmpty}] {percent * 100:  0}% {SpinnerChars[spinIndex]} {elapsedTime}");

            Console.SetCursorPosition(original.Left, original.Top);
        }

        private static readonly char[] SpinnerChars = new[] { '|', '/', '-', '\\' };
    }
}
