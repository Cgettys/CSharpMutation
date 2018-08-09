using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Resources;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System.Windows.Forms;

namespace CSharpMutation
{
    public class Program
    {
        static void Main(string[] args)
        {
            //args = new string[] {"../../../CSharpMutation.sln"};
            //args = new string[] { "C:/tfs/DSE FedEx Return Mgr/Support/FedExSupportMgr/FedExReturnManager.sln" };
            args = new string[] { "C:/tfs/DSE FedEx Return Mgr/Business Objects/FDXRequest/FDXRequest.sln" };
            // The test solution is copied to the output directory when you build this sample.
            // TODO: integrate with active VisualStudioWorkspace,  https://joshvarty.com/2014/09/12/learn-roslyn-now-part-6-working-with-workspaces/
            MSBuildWorkspace workspace = MSBuildWorkspace.Create();
            // Open the solution within the workspace.
            Solution originalSolution = workspace.OpenSolutionAsync(args[0]).Result;

            Console.WriteLine("Select a project to mutate:");
            int projectIndex = 0;
            foreach (Project project in originalSolution.Projects)
            {
                Console.WriteLine(projectIndex++ + ": " + project.AssemblyName);
            }
            projectIndex = Int32.Parse(Console.ReadLine());


            // TODO: get tests to run from IDE
            Console.WriteLine("Now select a project with test cases:");
            var testProjects = originalSolution.Projects/*.Where(
                p => p.MetadataReferences.Any(pr => pr.Display.Contains("UnitTestFramework")));
            */;
            int testProjectIndex = 0;
            var testProjectArray = testProjects as Project[] ?? testProjects.ToArray();
            foreach (
                Project project in
                testProjectArray)
            {
                Console.WriteLine(testProjectIndex++ + ": " + project.AssemblyName);
            }
            testProjectIndex = Int32.Parse(Console.ReadLine());
            
            Project myProject = originalSolution.Projects.ElementAt(projectIndex); //originalSolution.Projects.Single(project => project.Name == "FakeApplication");
            Console.WriteLine("Mutation testing "+myProject.AssemblyName + "(this can take a while)");
            

            Project testProject = testProjectArray.ElementAt(testProjectIndex);
            
            CoveredProject coveredProject = new CoveredProject(myProject, testProject, (m) => {}, (m) => { });

            // TODO: limit only to cs files
            MutationResult result = coveredProject.MutateAndTestProject();

            foreach (var mutant in result.LiveMutants)
            {
                Console.WriteLine(mutant);
            }
            Console.WriteLine(result.LiveMutants.Count + " mutants survived");

            Console.WriteLine(result.KilledMutants.Count + " mutants killed");

        }
    }
}
