// program.cs tests an Elevator class
// AUTH:    Sprax
// DATE:    2012 Oct

using System;

namespace ElevatorAndSM
{
    // Utility class for console output
    class Sx
    {
        public static void puts(String str) { System.Console.WriteLine(str); }
        public static void format(String formats, params Object[] args)
        {
            System.Console.Write(String.Format(formats, args));
        }
    }

    // Utility class for sleeping
    class Threads
    {
        public static void tryToSleep(int millis)
        {
            System.Threading.Thread.Sleep(millis);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            ElevSM.unit_test(3, args);
            return;
        }
    }
}
