using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Interops
{
    public class CoverageData : MarshalByRefObject
    {
        public Dictionary<string, int> LineLocatorIDs = new Dictionary<string, int>();
        public Dictionary<int, string> reverseLineLocatorIDs = new Dictionary<int, string>();
        public Dictionary<int, long> LineCounts = new Dictionary<int, long>();
        internal int NextLineID = 0;
        private static CoverageData _instance; // FIXME: make process global, "RVA?" https://blogs.msdn.microsoft.com/cbrumme/2003/04/15/static-fields/
        private static readonly object _locker = new object();

        private CoverageData()
        {
        }

        public static CoverageData GetInstance()
        {
            lock (_locker)
            {
                if (_instance == null)
                {
                    _instance = new CoverageData();
                }
            }
            return _instance;
        }

        public static void SetInstance(CoverageData data)
        {
            lock (_locker)
            {
                _instance = data;
            }
        }

        // To communicate across proxy instance
        public void SetStaticInstance(CoverageData data)
        {
            SetInstance(data);
        }

        public int GetLineID(String lineLocator)
        {
            if (lineLocator == null)
            {
                throw new ArgumentNullException(nameof(lineLocator));
            }
            if (LineLocatorIDs.ContainsKey(lineLocator))
            {
                return LineLocatorIDs[lineLocator];
            }
            else
            {
                lock (_locker)
                {
                    int id = NextLineID++;
                    LineLocatorIDs[lineLocator] = id;
                    reverseLineLocatorIDs[id] = lineLocator;
                    return id;
                }
            }

        }

        public long IncrementLineCount(int id)
        {
            lock (_locker)
            {
                if (LineCounts.ContainsKey(id))
                {
                    LineCounts[id]++;
                }
                else
                {
                    LineCounts[id] = 1;
                }
                return LineCounts[id];
            }
        }

        public void ResetCoverageCounters()
        {
            lock (_locker)
            {
                LineCounts = new Dictionary<int, long>();
            }
        }
        
    }
}
