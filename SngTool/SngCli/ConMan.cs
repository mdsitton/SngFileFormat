using System;
using System.Diagnostics;
using System.Threading;

namespace SngCli
{
    public static class ConMan
    {
        public static int ProgressItems { get; set; }
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
            ProgressItems = totalItems;
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
            ProgressItems = 0;
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
                var firstNewLine = message.IndexOf('\n');

                // Deal with line wrapping
                if (firstNewLine != -1 || message.Length > width)
                {
                    // always handle first line with all spaces
                    string prepOutput = new string(' ', width);

                    int lineCount = 0;
                    int messagePos = firstNewLine < width ? firstNewLine + 1 : 0;

                    while (messagePos < message.Length)
                    {
                        var nextNewLine = message.IndexOf('\n', messagePos);

                        // no more new lines to deal with calculate the remaining lines
                        if (nextNewLine == -1)
                        {
                            lineCount += (int)Math.Ceiling((float)(message.Length - messagePos) / width);
                            prepOutput += new string('\n', lineCount);
                            messagePos = message.Length;
                            break;
                        }
                        else
                        {
                            lineCount++;
                            messagePos = nextNewLine + 1;
                        }
                    }
                    Console.Write(prepOutput);
                    Console.CursorTop -= lineCount;
                }
                else
                {
                    // Simple case where there are no new lines, and the message
                    // is shorter than the width of the console
                    Console.WriteLine();
                    Console.CursorTop -= 1;
                    var linePadding = width - message.Length;
                    if (linePadding > 0)
                    {
                        message += new string(' ', linePadding);
                    }
                }

                Console.WriteLine(message);
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

            float percent = (float)progress / ProgressItems;

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
