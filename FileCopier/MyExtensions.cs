using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FileCopier
{
    public static class MyExtensions
    {
        const ulong KB = 1024;
        const ulong MB = 1024 * KB;
        const ulong GB = 1024 * MB;
        const ulong TB = 1024 * GB;
        const ulong PB = 1024 * TB;
        const ulong EB = 1024 * PB;
        //const ulong ZB = 1024 * EB;
        //const ulong YB = 1024 * ZB;

        public enum SizeUnits
        {
            Bytes, KB, MB, GB, TB, PB, EB, ZB, YB
        }

        public static string ToSize(this Int64 value, SizeUnits unit)
        {
            return (value / (double)Math.Pow(1024, (Int64)unit)).ToString("0.00") + unit.ToString();
        }
        public static string ToSize(this ulong value, SizeUnits unit)
        {
            return (value / (double)Math.Pow(1024, (ulong)unit)).ToString("0.00") + unit.ToString();
        }

        public static string ToSize(this ulong value)
        {
            SizeUnits unit = SizeUnits.Bytes;
            if (value < KB)
            {
                unit = SizeUnits.Bytes;
            }
            else if (value < MB)
            {
                unit = SizeUnits.KB;
            }
            else if (value < GB)
            {
                unit = SizeUnits.MB;
            }
            else if (value < TB)
            {
                unit = SizeUnits.GB;
            }
            else if (value < PB)
            {
                unit = SizeUnits.TB;
            }
            else if (value < EB)
            {
                unit = SizeUnits.PB;
            }
            /*else if (value < (ulong)(1024L * 1024 * 1024 * 1024 * 1024 * 1024))
            {
                unit = SizeUnits.EB;
            }
            else if (value < (ulong)(1024L * 1024 * 1024 * 1024 * 1024 * 1024))
            {
                unit = SizeUnits.ZB;
            }*/
            else
            {
                unit = SizeUnits.YB; //unchecked
            }


            return (value / (double)Math.Pow(1024, (ulong)unit)).ToString((unit == SizeUnits.Bytes) ? "#,##0" : "#,##0.##") + unit.ToString();
        }

        public static string GetPercentageCompleted(this ulong value,ulong total)
        {
            //int percentComplete = (int)Math.Round((double)(100L * value) / total);
            return ((double)(value) / total).ToString("0.##%");
        }
        public static string GetPercentageCompleted(this int value, int total)
        {
            //int percentComplete = (int)Math.Round((double)(100 * value) / total);
            return ((double)(value) / total).ToString("0.##%");
        }
    }
}
