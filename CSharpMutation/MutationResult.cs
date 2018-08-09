using System;
using System.Collections.Generic;

namespace CSharpMutation
{
    public class MutationResult
    {
        public List<ExportedMutantInfo> KilledMutants;
        public List<ExportedMutantInfo> LiveMutants;
        
        public MutationResult(List<ExportedMutantInfo> killedMutants, List<ExportedMutantInfo> liveMutants)
        {
            this.KilledMutants = killedMutants;
            this.LiveMutants = liveMutants;
        }
        public static MutationResult MergeResults(MutationResult arg1, MutationResult arg2)
        {
            List<ExportedMutantInfo> killedMutants = new List<ExportedMutantInfo>();
            killedMutants.AddRange(arg1.KilledMutants);
            killedMutants.AddRange(arg2.KilledMutants);
            List<ExportedMutantInfo> liveMutants = new List<ExportedMutantInfo>();
            liveMutants.AddRange(arg1.LiveMutants);
            liveMutants.AddRange(arg2.LiveMutants);
            return new MutationResult(killedMutants, liveMutants);
        }
    }
}