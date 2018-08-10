using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading.Tasks;

namespace Interops
{
    [Serializable]
    public class AssemblyLoader : MarshalByRefObject
    {
        private byte[][] instrumentedAssemblies;
        private LinkedList<Assembly> assemblies = null;
        private bool Initialized = false;
        private byte[][] _assemblies = null;
        

        public void Setup(byte[][] assemblies, params byte[][] instrumentedAssemblies)
        {
            this._assemblies = assemblies;
            this.instrumentedAssemblies = instrumentedAssemblies;

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        }

        public Assembly Resolve(object sender, ResolveEventArgs resolveEventArgs)
        {
            if (!Initialized)
            {
                foreach (byte[] assemblyBytes in instrumentedAssemblies)
                {
                    AppDomain.CurrentDomain.Load(assemblyBytes);
                }
                foreach (byte[] assemblyBytes in _assemblies)
                {
                    AppDomain.CurrentDomain.Load(assemblyBytes);
                }
                Initialized = true;
            }

            Assembly alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(asm => asm.FullName == resolveEventArgs.Name);

            if(alreadyLoaded != null) return alreadyLoaded;

            Debug.WriteLine("Loading "+resolveEventArgs.Name);
            
            //throw new MissingSatelliteAssemblyException("Could not find: " + resolveEventArgs.Name);
            return null;
        }
    }
}