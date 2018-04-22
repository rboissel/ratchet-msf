using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CheckMSF
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Ratchet.IO.Format.MSF.Stream[] streams = Ratchet.IO.Format.MSF.Open(System.IO.File.Open(args[0], System.IO.FileMode.Open));
                Console.WriteLine("Found " + streams.Length + " streams: ");
                for (int n = 0; n < streams.Length; n++)
                {
                    Console.WriteLine(" * stream " + n.ToString() + ": " + streams[n].Length + " bytes");
                }
            }
            catch
            {
                Console.WriteLine("Invalid MSF file");
            }
        }
    }
}
