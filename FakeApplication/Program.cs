using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FakeApplication
{
    public class Program
    {
        static void Main(string[] args)
        {
            
        }
        public int getMeows(int n)
        {
            if (n <= 0)
            {
                throw new Exception("No meows possible!");
            }
            else
            {
                for (int i = 0; i < n ;i++)
                {
                    Console.Write("Meow ");
                }
                Console.WriteLine();
                return n;
            }
        }

        public Boolean Meow()
        {
            Console.WriteLine("meow");
            return true;
        }
    }
}
