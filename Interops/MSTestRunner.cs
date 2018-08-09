using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;

namespace Interops
{
    public class MSTestRunner : MarshalByRefObject
    {
        private readonly Dictionary<String, Dictionary<int, long>> _lineCountsByTests = new Dictionary<String, Dictionary<int, long>>();
        private CoverageData coverageData;

        public Dictionary<String, Dictionary<int, long>> RunTestsForCoverage(byte[] testAssemblyBytes)
        {
            // Can't pass CoverageData directly because it goes over the proxy and gets messed up during deserialization? Weird...
            //CoverageData.GetInstance().LineCounts = lineCounts;
            //CoverageData.GetInstance().LineLocatorIDs = lineLocatorIDs;
            //CoverageData.GetInstance().reverseLineLocatorIDs = reverseLineLocatorIDs;

            Assembly assembly = AppDomain.CurrentDomain.Load(testAssemblyBytes);
            {
                foreach (Module module in assembly.Modules)
                {
                    var classes = module.FindTypes(
                        (type, filterCriteria) => type.GetCustomAttributes().Any(a => a.GetType().Name.Contains("TestClass")), null);
                    foreach (
                        Type clazz in classes
                        )
                    {
                        var testMethods = clazz.GetRuntimeMethods().Where(isTestMethod).ToArray();
                        RunTestsForClassWithCoverage(testMethods, clazz, true);
                    }
                }
            }

            return _lineCountsByTests;
        }

        public bool RunTests(byte[] testAssemblyBytes, List<string> tests)
        {
            Assembly assembly = AppDomain.CurrentDomain.Load(testAssemblyBytes);
            var pairs = tests.Select((test) => test.Split(':'));
            Dictionary<Type, List<MethodInfo>> testClassesToMethods = new Dictionary<Type, List<MethodInfo>>();
            foreach (String[] pair in pairs)
            {
                Type clazz = assembly.GetType(pair[0]);
                MethodInfo testCase = clazz.GetMethod(pair[1]);
                if (testCase == null)
                {
                    throw new NullReferenceException(pair[1] + " could not be found in "+pair[0]);
                }
                if (!testClassesToMethods.ContainsKey(clazz))
                {
                    testClassesToMethods[clazz] = new List<MethodInfo>();
                }
                testClassesToMethods[clazz].Add(testCase);
            }
            bool allPassed = true;
            foreach (var pair in testClassesToMethods)
            {
                allPassed &= RunTestsForClassWithCoverage(pair.Value.ToArray(), pair.Key, false);
                if (!allPassed) break;
            }
            return allPassed;
        }

        private bool RunTestsForClassWithCoverage(MethodInfo[] testMethods, Type clazz, bool forCoverage)
        {
            bool allPassed = true;
            var classInit = clazz.GetRuntimeMethods().Where(isClassInitialize).ToArray();
            var testInit = clazz.GetRuntimeMethods().Where(isTestInitialize).ToArray();
            var testCleanup = clazz.GetRuntimeMethods().Where(isTestCleanup).ToArray();
            var classCleanup = clazz.GetRuntimeMethods().Where(isClassCleanup).ToArray();
            // FIXME: refactor out into next step of pipeline
            // run test case
            var emptyargs = new object[] {};
            // FIXME: pass in a ClassContext and TestContext
            var nullArgs = new object[] {  null};
            var testInstance = clazz.GetConstructors().First().Invoke(emptyargs);
            foreach (var methodInfo in classInit)
            {
                try
                {
                    methodInfo.Invoke(testInstance, nullArgs);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.InnerException);
                    return false;
                }
            }

            Debug.WriteLine("Running "+ testMethods.Length+" tests.");
            foreach (var testMethod in testMethods)
            {
                foreach (var methodInfo in testInit)
                {
                    try
                    {
                        methodInfo.Invoke(testInstance, emptyargs);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.InnerException);
                        if (e.InnerException is TargetInvocationException
                            || e.InnerException is MissingSatelliteAssemblyException)
                        {
                            throw e.InnerException;
                        }
                        else if (e is MissingSatelliteAssemblyException)
                        {
                            throw e;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
                // test MUST pass for mutation
                Debug.WriteLine("Running test " + testMethod.Name);
                bool passed = false;
                try
                {
                    testMethod.Invoke(testInstance, emptyargs);
                    passed = true;
                }
                catch (TargetInvocationException e)
                {
                    Attribute expectsException = testMethod.GetCustomAttributes()
                        .SingleOrDefault(a => a.GetType().Name.Contains("ExpectedException"));
                    IList<CustomAttributeData> data = testMethod.GetCustomAttributesData();
                    if (expectsException != null)
                    {
                        foreach (CustomAttributeData d in data)
                        {
                            if (d.AttributeType == expectsException.GetType() &&
                                d.ConstructorArguments[0].Value.Equals(e.InnerException.GetType()))
                            {
                                Debug.WriteLine("Test " + testMethod.Name + " received expected exception");
                                passed = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Test " + testMethod.Name + " failed, reason: " + e.InnerException);
                    }

                }
                catch (MissingSatelliteAssemblyException e)
                {
                    // When dependency can't be resolved (ie I can't even find the DLL myself)
                    // That's really bad, but not sure how to fix if I don't have it.
                    Debug.WriteLine(e);
                    passed = false;
                }

                if (AssemblyLoader.FailedToLoad)
                {
                    // For some reason the MissingSatelliteAssemblyException is not bubbling.
                    // Skip the test.
                    passed = false;
                    AssemblyLoader.FailedToLoad = false;
                }

                Debug.WriteLine(testMethod.Name + " "+ (passed? "passed":"failed"));

                allPassed &= passed;

                foreach (var methodInfo in testCleanup)
                {
                    methodInfo.Invoke(testInstance, emptyargs);
                }

                // collect coverage
                if (forCoverage)
                {
                    // only keep if tests passed
                    if (allPassed)
                    {
                        // TODO: make test object to hold test, result, coverage data instead of massive dictionary
                        String testID = clazz.FullName + ":" + testMethod.Name;
                        _lineCountsByTests[testID] = coverageData.LineCounts;
                    }
                    else
                    {
                        Debug.WriteLine(
                            "WARNING: not all tests passed during code coverage. Skipping failing tests...");
                        allPassed = true;
                    }
                    // reset coverage
                    coverageData.ResetCoverageCounters();
                }
                else if(!allPassed) // FIXME: don't put this logic here
                {
                    return false;
                }
            }
            return true;
        }

        public void SetupCoverage(Dictionary<string, int> lineLocatorIDs, Dictionary<int, string> reverseLineLocatorIDs)
        {
            coverageData = CoverageData.GetInstance();
            coverageData.LineLocatorIDs = lineLocatorIDs;
            coverageData.reverseLineLocatorIDs = reverseLineLocatorIDs;
        }

        private bool isTestMethod(MethodInfo method)
        {
            return method.GetCustomAttributes().Any(a => a.GetType().Name.Contains("TestMethod"));
        }

        private bool isTestInitialize(MethodInfo method)
        {
            return method.GetCustomAttributes().Any(a => a.GetType().Name.Contains("TestInitialize"));
        }

        private bool isTestCleanup(MethodInfo method)
        {
            return method.GetCustomAttributes().Any(a => a.GetType().Name.Contains("TestCleanup")); ;
        }

        private bool isClassInitialize(MethodInfo method)
        {
            return method.GetCustomAttributes().Any(a => a.GetType().Name.Contains("ClassInitialize")); ;
        }

        private bool isClassCleanup(MethodInfo method)
        {
            return method.GetCustomAttributes().Any(a => a.GetType().Name.Contains("ClassCleanup")); ;
        }
    }

}
