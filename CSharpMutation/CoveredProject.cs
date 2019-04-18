using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Interops;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Emit;

namespace CSharpMutation
{
    public class CoveredProject
    {
        public delegate void OnMutant(ExportedMutantInfo info);
        static CoveredProject()
        {   
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .SingleOrDefault(asm => asm.FullName == e.Name);
            };
        }
        private CSharpCompilation projectCompiler;
        private CSharpCompilation testCompiler;
        private byte[] testAssembly;
        private byte[] interopsAssembly;
        private byte[][] dependencies;
        private string outputPath;
        private string appConfigPath;
        private Dictionary<int, List<string>> testCaseCoverageByLineID;

        private OnMutant _onMutantKilled;
        private OnMutant _onMutantLived;

        private object _locker = new object();

        public CoveredProject(Project myProject, Project testProject, OnMutant onMutantKilled, OnMutant onMutantLived)
        {
            this._onMutantKilled = onMutantKilled;
            this._onMutantLived = onMutantLived;
            outputPath = Path.GetDirectoryName(testProject.FilePath);
            appConfigPath = Path.Combine(outputPath, "App.config");
            var interopsLocation = typeof(AssemblyLoader).Assembly.Location;
            try
            {
                File.Copy(appConfigPath, interopsLocation + ".config", true);
            }
            catch (FileNotFoundException e)
            {
                // no config file; fine
                Debug.Print(e.ToString());
            }
            //Directory.SetCurrentDirectory(outputPath);
            Directory.SetCurrentDirectory(Path.GetDirectoryName(testProject.FilePath));


            testCompiler = GetLibraryCompilation(testProject);
            projectCompiler = GetLibraryCompilation(myProject);

            var paths = testCompiler.References.Where(r => r is PortableExecutableReference).Select(per => ((PortableExecutableReference)per).FilePath).ToList();
            paths.AddRange(projectCompiler.References.Where(r => r is PortableExecutableReference).Select(per => ((PortableExecutableReference)per).FilePath));
            paths = paths.Distinct().ToList();
            dependencies = paths.Select(File.ReadAllBytes).ToArray();


            interopsAssembly = new WebClient().DownloadData(interopsLocation);
            var interopsMetadata = AssemblyMetadata.CreateFromImage(interopsAssembly);

            projectCompiler = FixProject(myProject.Documents, projectCompiler, interopsMetadata);
            var instrumentedProjectCompiler = InstrumentProject(projectCompiler, interopsMetadata, myProject.Solution.Workspace);

            var instrumentedAssembly = CompileAssembly(instrumentedProjectCompiler);

            testCompiler = testCompiler.ReplaceReference(testCompiler.References.Single(r => r is CompilationReference && ((CompilationReference)r).Compilation.AssemblyName == projectCompiler.AssemblyName), projectCompiler.ToMetadataReference());
            testAssembly = CompileAssembly(testCompiler);
            
            CollectCoverage(instrumentedAssembly);
        }

        private void CollectCoverage(byte[] instrumentedAssembly)
        {
            AppDomain domain = AppDomain.CreateDomain("Instrumentation", null, new AppDomainSetup()
            {
                ConfigurationFile = appConfigPath,
                ApplicationBase = outputPath
            });

            AssemblyLoader handler = (AssemblyLoader)domain.CreateInstanceFromAndUnwrap(typeof(AssemblyLoader).Assembly.Location,
                            typeof(AssemblyLoader).FullName);
            handler.Setup(dependencies, instrumentedAssembly, testAssembly);

            MSTestRunner test =
                (MSTestRunner) domain.CreateInstanceFromAndUnwrap(typeof(MSTestRunner).Assembly.Location, typeof(MSTestRunner).FullName);

            test.SetupCoverage(CoverageData.GetInstance().LineLocatorIDs, CoverageData.GetInstance().reverseLineLocatorIDs);

            Dictionary<string, Dictionary<int, long>> lineCountsByTests = test.RunTestsForCoverage(testAssembly);
            testCaseCoverageByLineID = new Dictionary<int, List<string>>();
            foreach (var pair in lineCountsByTests)
            {
                foreach (var pair2 in pair.Value)
                {
                    int lineID = pair2.Key;
                    if (!testCaseCoverageByLineID.ContainsKey(lineID))
                    {
                        testCaseCoverageByLineID[lineID] = new List<string>();
                    }
                    testCaseCoverageByLineID[lineID].Add(pair.Key);
                }
            }
            AppDomain.Unload(domain);
        }

        public MutationResult MutateAndTestProject()
        {
            // FIXME: could trivally parallelize this, but concurrent tests are competing for access to files...
            return projectCompiler.SyntaxTrees
                .AsParallel()
                .WithDegreeOfParallelism(8)
                .Select(MutateDocumentAndTestProject)
                .Aggregate(MutationResult.MergeResults);
        }

        private MutationResult MutateDocumentAndTestProject(SyntaxTree tree)
        {
            List<ExportedMutantInfo> killedMutants = new List<ExportedMutantInfo>();
            List<ExportedMutantInfo> liveMutants = new List<ExportedMutantInfo>();
            Task<SyntaxNode> getTree = tree.GetRootAsync();
            int appdomainid = 0;
            getTree.ContinueWith((Task<SyntaxNode> task) =>
            {
                // TODO: create a Walker that calls a Decorator yield chain to produce mutants.
                SyntaxNode root = task.Result;
                MutatingWalker walker = new MutatingWalker(new BaseMutatorImpl(projectCompiler),
                    (mutantInfo) =>
                    {
                        // TODO: need to separate OnMutant into per-test-runner classes
                        SyntaxNode original = mutantInfo.original;
                        SyntaxNode mutant = mutantInfo.mutant;
                        var newRoot = root.ReplaceNode(original, mutant);

                        byte[] mutatedAssembly = null;
                        try
                        {
                            var mutantCompiler = projectCompiler
                                    .ReplaceSyntaxTree(root.SyntaxTree, newRoot.SyntaxTree);
                            mutatedAssembly = CompileAssembly(mutantCompiler);
                        }
                        catch (Exception e)
                        {
                            // Compile errors are a really bad sign and are indicative of bugs in the mutation.
                            // ALWAYS debug these.
                            Debug.Print("Compile error trying to mutate "+original.ToString()+" to "+mutant.ToString()+" , reason: "+ e.ToString());
                            return false;
                        }
                        // build app domain
                        AppDomain mutantDomain = AppDomain.CreateDomain("Mutant " + appdomainid++, null,
                            new AppDomainSetup()
                            {
                                ConfigurationFile = appConfigPath,
                                ApplicationBase = outputPath
                            });
                        AssemblyLoader handler =
                            (AssemblyLoader)
                            mutantDomain.CreateInstanceFromAndUnwrap(typeof(AssemblyLoader).Assembly.Location,
                                typeof(AssemblyLoader).FullName);
                        handler.Setup(dependencies, mutatedAssembly, testAssembly);

                        // run mstestrunner in special abort mode
                        MSTestRunner mutantTest =
                            (MSTestRunner)
                            mutantDomain.CreateInstanceFromAndUnwrap(typeof(MSTestRunner).Assembly.Location,
                                typeof(MSTestRunner).FullName);

                        mutantTest.SetupCoverage(CoverageData.GetInstance().LineLocatorIDs,
                            CoverageData.GetInstance().reverseLineLocatorIDs);

                        bool allPassed = true;
                        // Ensure only one thread runs tests at a time.
                        lock (_locker)
                        {
                            try
                            {

                                // TODO: create a fixed number of worker threads and give each thread its own directory.
                                // Lock is needed because certain tests are writing files and are writing to the same locations.
                                allPassed = mutantTest.RunTests(testAssembly,
                                    testCaseCoverageByLineID[mutantInfo.lineID]);
                            }
                            catch (Exception e)
                            {
                                Debug.Print(e.ToString());
                                return true; // keep going to next mutation
                            }
                        }
                        // TODO: need to be collecting tons of data about tests or RIP
                        ExportedMutantInfo exportedInfo = new ExportedMutantInfo(mutantInfo);
                        if (allPassed)
                        {
                            Task.Run(() => _onMutantLived(exportedInfo));
                            liveMutants.Add(exportedInfo);
                        }
                        else
                        {
                            Task.Run(() => _onMutantKilled(exportedInfo));
                            killedMutants.Add(exportedInfo);
                        }
                        AppDomain.Unload(mutantDomain);
                        return allPassed;
                    }, CoverageData.GetInstance(), testCaseCoverageByLineID);
                walker.Visit(root);

            }).Wait();
            return new MutationResult(killedMutants.ToList(), liveMutants.ToList());
        }

        private static CSharpCompilation FixProject(IEnumerable<Document> documents, CSharpCompilation compiler, AssemblyMetadata interops)
        {
            compiler = compiler.AddReferences(interops.GetReference());
            var oldToNew = documents.AsParallel().WithDegreeOfParallelism(8).Select(FixDocument);
            
            foreach (KeyValuePair<SyntaxTree, SyntaxTree> oldAndNew in oldToNew)
            {
                compiler = compiler.ReplaceSyntaxTree(oldAndNew.Key, oldAndNew.Value);
                Debug.Assert(oldAndNew.Value.GetText().ToString() != "{}");
            }
            return compiler;
        }

        private static CSharpCompilation InstrumentProject(CSharpCompilation compiler, AssemblyMetadata interops, Workspace workspace)
        {
            compiler = compiler.AddReferences(interops.GetReference());
            var oldToNew = compiler.SyntaxTrees.AsParallel().WithDegreeOfParallelism(8).Where(IsNotGeneratedCode).Select(t => InstrumentTree(t, workspace));

            foreach (KeyValuePair<SyntaxTree, SyntaxTree> oldAndNew in oldToNew)
            {
                compiler = compiler.ReplaceSyntaxTree(oldAndNew.Key, oldAndNew.Value);
                Debug.Assert(oldAndNew.Value.GetText().ToString() != "{}");
            }
            return compiler;
        }

        private static bool IsNotGeneratedCode(SyntaxTree document)
        {
            return !document.GetRoot().DescendantNodesAndSelf().Any(s => s is ClassDeclarationSyntax && ((ClassDeclarationSyntax)s).AttributeLists.Any(syntax => syntax.ToString().Contains("GeneratedCodeAttribute")));
        }

        private static CSharpCompilation GetLibraryCompilation(Project myProject)
        {
            var options = myProject.CompilationOptions;
            Compilation compilation = myProject.GetCompilationAsync().Result;
            compilation = compilation.WithOptions(new CSharpCompilationOptions(
                    options.OutputKind, 
                    moduleName: options.ModuleName, 
                    mainTypeName:options.MainTypeName,
                    scriptClassName:options.ScriptClassName, 
                    platform: options.Platform,
                    assemblyIdentityComparer: options.AssemblyIdentityComparer,
                    concurrentBuild: options.ConcurrentBuild,
                    sourceReferenceResolver: options.SourceReferenceResolver,
                    metadataReferenceResolver: options.MetadataReferenceResolver,
                    optimizationLevel: options.OptimizationLevel,
                    allowUnsafe: true
                    // omit crypto intentionally so project is compatible with interops etc
                ));   
            return (CSharpCompilation)compilation;
        }

        public static KeyValuePair<SyntaxTree, SyntaxTree> FixDocument(Document document)
        {
            DocumentEditor curlyBraceEditor = DocumentEditor.CreateAsync(document).Result;
            SyntaxFixer fixer = new SyntaxFixer();
            SyntaxNode newRoot = fixer.Visit(curlyBraceEditor.OriginalRoot);
            curlyBraceEditor.ReplaceNode(curlyBraceEditor.OriginalRoot, newRoot);
            Document newDocument = curlyBraceEditor.GetChangedDocument();
            return new KeyValuePair<SyntaxTree, SyntaxTree>(document.GetSyntaxTreeAsync().Result, newDocument.GetSyntaxTreeAsync().Result);
        }

        public static KeyValuePair<SyntaxTree, SyntaxTree> InstrumentTree(SyntaxTree originalTree, Workspace workspace)
        {
            SyntaxEditor editor = new SyntaxEditor(originalTree.GetRoot(), workspace);

            CodeCoverageInstrumenter rewriter = new CodeCoverageInstrumenter(editor);
            rewriter.Visit(editor.OriginalRoot);
            Debug.WriteLine("Instrumented " + Path.GetFileName(originalTree.FilePath));
            SyntaxTree fixedTree = editor.GetChangedRoot().SyntaxTree;
            return new KeyValuePair<SyntaxTree, SyntaxTree>(originalTree, fixedTree);
        }

        private static byte[] CompileAssembly(CSharpCompilation compiler)
        {
            byte[] instrumentedAssembly;
            MemoryStream stream = new MemoryStream();

            EmitResult result = compiler.Emit(stream);

            if (IsMarginalSuccess(result))
            {
                instrumentedAssembly = stream.ToArray();
            }
            else
            {
                    string errors = "";
                    foreach (var resultDiagnostic in result.Diagnostics)
                    {
                        errors += resultDiagnostic.ToString();
                        errors += "\r\n";
                    }
                    throw new Exception("Failed to instrument " + compiler.AssemblyName + ": " + errors);
                
            }
            return instrumentedAssembly;
        }

        private static bool IsMarginalSuccess(EmitResult result)
        {
            if (result.Success) return true;

            if (result.Diagnostics.Select(d => d.WarningLevel == 0).Any()) return false;

            return true;

        }
    }
    
}